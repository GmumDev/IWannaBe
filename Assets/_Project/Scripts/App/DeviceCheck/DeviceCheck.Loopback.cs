using System;
using System.Collections.Generic;
using IWannabe.Rhythm;
using UnityEngine;

namespace IWannabe.App
{
    /// <summary>
    /// 루프백 점검: 클릭을 내보내고 마이크로 받아 "믹싱 예약 시각 → 소리가 나서 → 마이크로 들어온 시각"의 왕복 지연을 잰다.
    /// 마이크 입력 지연이 섞여 출력 지연만 떼어 낼 수는 없지만, 같은 기기에서 DSP 버퍼 크기·출력 장치끼리 비교하는 데 쓴다.
    /// 블루투스는 이어폰 한쪽을 휴대폰 마이크(아래쪽)에 대고 잰다.
    /// 마이크 코드는 에디터·개발 빌드에서만 들어간다. 릴리스 빌드에 마이크 권한(RECORD_AUDIO)이 붙지 않게 하려는 것이다.
    /// </summary>
    public sealed partial class DeviceCheck
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const int LoopbackClicks = 12;
        const double LoopbackInterval = 0.75;
        const double LoopbackLead = 1.0;
        const int MicSeconds = 14;

        AudioClip loopbackClip;
        double[] loopbackBeats;
        AudioClip micClip;
        int micRate;
        bool micWaiting;
        bool loopbackPlaying;
        readonly List<(double dsp, int position)> micClock = new List<(double, int)>();
        string loopbackResult = "";

        bool LoopbackRunning => micWaiting || loopbackPlaying;

        void StartLoopback()
        {
            if (tapRunning || LoopbackRunning) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                loopbackResult = "마이크 권한을 허용한 뒤 다시 시작을 누른다.";
                return;
            }
#endif
            if (Microphone.devices.Length == 0)
            {
                loopbackResult = "마이크가 없다.";
                return;
            }
            if (loopbackClip == null)
                loopbackClip = CreateClickTrack("DeviceCheckLoopback", 60.0 / LoopbackInterval, 0, LoopbackClicks, LoopbackLead, out loopbackBeats);

            Microphone.GetDeviceCaps(null, out int minRate, out int maxRate);
            micRate = maxRate == 0 ? 48000 : Mathf.Clamp(48000, minRate, maxRate);
            micClip = Microphone.Start(null, false, MicSeconds, micRate);
            micClock.Clear();
            micWaiting = true;
            loopbackResult = "마이크를 켜는 중";
        }

        void UpdateLoopback()
        {
            if (!LoopbackRunning) return;
            int position = Microphone.GetPosition(null);
            if (micWaiting)
            {
                if (position <= 0) return;
                micWaiting = false;
                conductor.Load(loopbackClip, new TempoMap(loopbackBeats), 1f);
                conductor.Play(0, 0.3, false);
                loopbackPlaying = true;
                loopbackResult = "재는 중(조용히)";
            }

            // 마이크에 들어온 샘플 수와 그때의 dsp 시각. Conductor가 다듬은 시계로 잰다(AudioSettings.dspTime은 버퍼 단위로 끊긴다).
            double dspNow = conductor.SongTimeToDsp(conductor.RealtimeToSongTime(Time.realtimeSinceStartupAsDouble)) + conductor.OutputLatency;
            if (position > 0) micClock.Add((dspNow, position));

            if (conductor.SongTime > loopbackBeats[loopbackBeats.Length - 1] + 1.0) FinishLoopback();
        }

        void FinishLoopback()
        {
            int recorded = Microphone.GetPosition(null);
            loopbackPlaying = false;
            conductor.Stop();
            var data = new float[micClip.samples * micClip.channels];
            micClip.GetData(data, 0);
            Microphone.End(null);

            int channels = micClip.channels;
            int length = Math.Min(recorded > 0 ? recorded : micClip.samples, micClip.samples);
            var envelope = new float[length];
            float previous = 0;
            for (int i = 0; i < length; i++)
            {
                float sample = data[i * channels];
                envelope[i] = Math.Abs(sample - previous); // 1차 차분: 저역 소음을 줄이고 클릭의 시작을 살린다
                previous = sample;
            }

            // 샘플 p가 앱에 들어온 dsp 시각 ≈ base + p / rate. 들어오자마자 잰 순간이 가장 작은 값이다.
            double baseDsp = double.MaxValue;
            foreach (var (dsp, position) in micClock) baseDsp = Math.Min(baseDsp, dsp - position / (double)micRate);

            var trips = new List<double>();
            double firstExpected = conductor.SongTimeToDsp(loopbackBeats[0]);
            int noiseEnd = Mathf.Clamp((int)((firstExpected - baseDsp - 0.1) * micRate), 0, length);
            float noise = Percentile(envelope, 0, noiseEnd, 0.95f);
            foreach (var beat in loopbackBeats)
            {
                double scheduled = conductor.SongTimeToDsp(beat);
                // 왕복 지연은 0보다 크므로 예약 시각 전에 잡힌 소리(잡음)는 보지 않는다.
                int from = Mathf.Clamp((int)((scheduled - baseDsp) * micRate), 0, length);
                int to = Mathf.Clamp((int)((scheduled - baseDsp + LoopbackInterval - 0.05) * micRate), 0, length);
                if (to <= from) continue;
                float peak = 0;
                for (int i = from; i < to; i++) peak = Math.Max(peak, envelope[i]);
                float threshold = Math.Max(noise * 6, peak * 0.3f);
                if (peak < noise * 4) continue; // 클릭이 안 들림
                for (int i = from; i < to; i++)
                {
                    if (envelope[i] < threshold) continue;
                    trips.Add(baseDsp + i / (double)micRate - scheduled);
                    break;
                }
            }

            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            loopbackResult = trips.Count < 4
                ? $"루프백({OutputName}): 클릭을 {trips.Count}/{LoopbackClicks}개만 찾음. 볼륨을 올리거나 조용한 곳에서 다시."
                : $"루프백({OutputName}): 왕복 {Summarize(trips, "ms")}, 최소 {Min(trips) * 1000:0}ms, 버퍼 {bufferLength}×{bufferCount}, 마이크 {micRate}Hz";
            DeviceCheckLog.Add(loopbackResult);
        }

        void StopLoopback(string reason)
        {
            micWaiting = false;
            loopbackPlaying = false;
            conductor.Stop();
            Microphone.End(null);
            loopbackResult = reason;
        }

        void DestroyLoopbackClips()
        {
            if (loopbackClip != null) Destroy(loopbackClip);
            loopbackClip = null;
        }

        void DrawLoopbackPage()
        {
            GUILayout.Label($"클릭 {LoopbackClicks}번을 내고 마이크로 받아 왕복 지연을 잰다({OutputName}). 조용한 곳에서 볼륨을 크게. " +
                            "블루투스는 이어폰 한쪽을 휴대폰 아래쪽 마이크에 댄다. 정보 탭에서 DSP 버퍼를 바꿔 가며 비교한다.", label);
            if (GUILayout.Button(LoopbackRunning ? "재는 중" : "시작", button, GUILayout.Height(72))) StartLoopback();
            if (loopbackResult.Length > 0) GUILayout.Label(loopbackResult, label);
        }

        static float Percentile(float[] values, int from, int to, float q)
        {
            if (to <= from) return 0;
            var copy = new float[to - from];
            Array.Copy(values, from, copy, 0, copy.Length);
            Array.Sort(copy);
            return copy[Math.Min(copy.Length - 1, (int)(q * (copy.Length - 1)))];
        }

        static double Min(List<double> values)
        {
            double min = double.MaxValue;
            foreach (var v in values) min = Math.Min(min, v);
            return min;
        }
#else
        bool LoopbackRunning => false;
        void UpdateLoopback() { }
        void StopLoopback(string reason) { }
        void DestroyLoopbackClips() { }
        void DrawLoopbackPage() => GUILayout.Label("루프백은 개발 빌드에서만 쓸 수 있다.", label);
#endif
    }
}
