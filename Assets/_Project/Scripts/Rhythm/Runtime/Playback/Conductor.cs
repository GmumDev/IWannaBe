using System;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 오디오 시계(dspTime) 기준의 곡 시간을 제공한다.
    /// 음악은 PlayScheduled로 정확한 dsp 시각에 시작하고, 프레임·입력 시각(realtimeSinceStartup)은
    /// 추정한 dsp-realtime 오프셋으로 곡 시간으로 바꾼다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class Conductor : MonoBehaviour
    {
        // 두 시계의 느린 드리프트를 따라가기 위해 오프셋 추정치를 초당 이만큼 낮춰 본다.
        const double OffsetDecayPerSecond = 0.002;
        const double ResyncThreshold = 0.1;

        [SerializeField] AudioSource musicSource;

        [Tooltip("dspTime은 소리가 믹싱되는 시각이라 실제로 들리는 시각보다 출력 버퍼만큼 이르다. " +
                 "켜 두면 DSP 버퍼 크기로 추정한 출력 지연을 빼서 곡 시간을 '들리는 시각' 기준으로 맞춘다.")]
        [SerializeField] bool compensateOutputLatency = true;

        double dspStartTime;
        double outputLatency;
        double dspOffset;
        double lastRealtime;
        bool clockReady;
        bool running;

        public TempoMap TempoMap { get; private set; }
        public double SongTime { get; private set; }
        public double SongBeat { get; private set; }
        public double PausedSongTime { get; private set; }
        public bool IsRunning => running;
        /// <summary>곡 시간 계산에서 빼는 출력 지연(초).</summary>
        public double OutputLatency => outputLatency;

        void Awake()
        {
            if (musicSource == null) musicSource = GetComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = false;
            RefreshOutputLatency();
        }

        void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        void OnDisable() => AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            RefreshOutputLatency();
            clockReady = false;
        }

        void RefreshOutputLatency()
        {
            outputLatency = 0;
            if (!compensateOutputLatency) return;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate > 0) outputLatency = (double)bufferLength * bufferCount / sampleRate;
        }

        public void Load(AudioClip clip, TempoMap tempoMap, float volume)
        {
            musicSource.Stop();
            musicSource.clip = clip;
            musicSource.volume = volume;
            TempoMap = tempoMap;
            running = false;
            SongTime = 0;
            SongBeat = tempoMap.TimeToBeat(0);
        }

        /// <summary>곡의 <paramref name="fromSongTime"/> 위치부터 <paramref name="leadSeconds"/> 뒤에 재생한다.</summary>
        public void Play(double fromSongTime, double leadSeconds)
        {
            UpdateClock();
            double startDsp = AudioSettings.dspTime + Math.Max(0.05, leadSeconds);
            dspStartTime = startDsp - fromSongTime;

            musicSource.Stop();
            var clip = musicSource.clip;
            if (clip != null && fromSongTime >= 0 && fromSongTime < clip.length)
            {
                musicSource.timeSamples = Mathf.Clamp((int)(fromSongTime * clip.frequency), 0, clip.samples - 1);
                musicSource.PlayScheduled(startDsp);
            }
            running = true;
            Tick();
        }

        public void Pause()
        {
            if (!running) return;
            UpdateClock();
            Tick();
            PausedSongTime = SongTime;
            musicSource.Stop();
            running = false;
        }

        public void Stop()
        {
            musicSource.Stop();
            running = false;
        }

        /// <summary>입력 이벤트 시각(realtimeSinceStartup 기준)을 그 순간 들리던 곡 위치로 바꾼다.</summary>
        public double RealtimeToSongTime(double realtime) => realtime + dspOffset - outputLatency - dspStartTime;

        /// <summary>곡 위치를 예약 재생용 dsp 시각으로 바꾼다. 음악과 같은 dsp 축이라 출력 지연은 양쪽에 똑같이 붙는다.</summary>
        public double SongTimeToDsp(double songTime) => dspStartTime + songTime;

        void Update()
        {
            UpdateClock();
            if (running) Tick();
        }

        void Tick()
        {
            SongTime = RealtimeToSongTime(Time.realtimeSinceStartupAsDouble);
            if (TempoMap != null) SongBeat = TempoMap.TimeToBeat(SongTime);
        }

        /// <summary>
        /// dspTime은 오디오 버퍼 단위로 끊겨 갱신되므로 (dsp - realtime) 표본은 톱니 모양이 된다.
        /// 표본의 상한선이 버퍼가 막 갱신된 순간이므로 그 선을 따라가 끊김 없는 시계를 만든다.
        /// </summary>
        void UpdateClock()
        {
            double realtime = Time.realtimeSinceStartupAsDouble;
            double sample = AudioSettings.dspTime - realtime;
            if (!clockReady)
            {
                dspOffset = sample;
                lastRealtime = realtime;
                clockReady = true;
                return;
            }

            double elapsed = Math.Max(0, realtime - lastRealtime);
            lastRealtime = realtime;
            if (Math.Abs(sample - dspOffset) > ResyncThreshold)
                dspOffset = sample; // 오디오 장치 재시작·백그라운드 복귀 등
            else
                dspOffset = Math.Max(sample, dspOffset - elapsed * OffsetDecayPerSecond);
        }
    }
}
