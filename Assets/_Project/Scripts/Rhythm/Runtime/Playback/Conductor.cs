using System;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 오디오 시계(dspTime) 기준의 곡 시간을 제공한다.
    /// 음악은 PlayScheduled로 정확한 dsp 시각에 시작하고, 프레임·입력 시각(realtimeSinceStartup)은
    /// 추정한 dsp-realtime 오프셋으로 곡 시간으로 바꾼다.
    /// <para>
    /// 재생은 두 방식이다. 시작(처음, 시작 지점)은 시계가 리드만큼 앞에서부터 흘러 시작 시각에 닿는 프리롤이고,
    /// 재개는 시계가 멈춘 시각에 서 있다가 음악과 함께 그 시각에서 출발한다(<see cref="Play"/>). 그래서 일시정지와 재개를
    /// 몇 번 되풀이해도 곡 시간은 뒤로 가지 않는다.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class Conductor : MonoBehaviour
    {
        // 두 시계의 느린 드리프트를 따라가기 위해 오프셋 추정치를 초당 이만큼 낮춰 본다.
        const double OffsetDecayPerSecond = 0.002;
        const double ResyncThreshold = 0.1;
        /// <summary>음악을 예약할 dsp 시각을 지금보다 최소 이만큼(초) 뒤에 둔다. 더 가까우면 예약이 늦을 수 있다.</summary>
        const double ScheduleMargin = 0.1;

        [SerializeField] AudioSource musicSource;

        [Tooltip("dspTime은 소리가 믹싱되는 시각이라 실제로 들리는 시각보다 출력 버퍼만큼 이르다. " +
                 "켜 두면 DSP 버퍼 크기로 추정한 출력 지연을 빼서 곡 시간을 '들리는 시각' 기준으로 맞춘다.")]
        [SerializeField] bool compensateOutputLatency = true;

        double dspStartTime;
        double outputLatency;
        double dspOffset;
        double lastRealtime;
        double rawSongTime;
        double holdSongTime = double.NegativeInfinity;
        bool clockReady;
        bool running;

        public TempoMap TempoMap { get; private set; }
        /// <summary>지금 곡 시각(들리는 시각 기준). 재개 카운트다운 중에는 멈춘 시각에 서 있다.</summary>
        public double SongTime { get; private set; }
        public double SongBeat { get; private set; }
        public double PausedSongTime { get; private set; }
        public bool IsRunning => running;
        /// <summary>곡 시간 계산에서 빼는 출력 지연(초).</summary>
        public double OutputLatency => outputLatency;
        /// <summary>마지막 <see cref="Play"/>가 재생을 시작한 곡 시각.</summary>
        public double StartSongTime { get; private set; }
        /// <summary>재개 카운트다운 동안 시계가 서 있는 곡 시각. 이보다 앞선 입력은 카운트다운 중의 입력이다. 재개가 아니면 음의 무한대.</summary>
        public double HoldSongTime => holdSongTime;
        /// <summary>재생을 시작한 곡 시각에 아직 닿지 않았다(시작 프리롤, 재개 카운트다운).</summary>
        public bool IsCountingIn => running && rawSongTime < StartSongTime;

        /// <summary>음악 소스가 읽고 있는 곡 위치(초). 믹싱 위치라 들리는 위치보다 출력 지연만큼 앞선다. 재생 중이 아니면 NaN. 점검용.</summary>
        public double MusicPosition
        {
            get
            {
                var clip = musicSource.clip;
                if (clip == null || !musicSource.isPlaying) return double.NaN;
                return (double)musicSource.timeSamples / clip.frequency;
            }
        }

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
            holdSongTime = double.NegativeInfinity;
            StartSongTime = 0;
            SongTime = 0;
            rawSongTime = 0;
            SongBeat = tempoMap.TimeToBeat(0);
        }

        /// <summary>
        /// 곡의 <paramref name="fromSongTime"/> 위치가 <paramref name="leadSeconds"/> 뒤에 들리도록 재생한다.
        /// <paramref name="hold"/>가 false면 그동안 시계가 리드만큼 앞에서부터 흘러 그 위치에 닿고(시작 프리롤),
        /// true면 시계가 그 위치에 서 있다가 음악과 함께 출발한다(재개).
        /// 음악은 시계가 곡 시각 0 이상을 가리키면 그 위치를 재생한다. 프리롤에서도 들리고, 0초 전에 멈췄다가 재개하면 0초에 시작한다.
        /// </summary>
        public void Play(double fromSongTime, double leadSeconds, bool hold)
        {
            UpdateClock();
            double lead = Math.Max(0, leadSeconds);
            double now = AudioSettings.dspTime;
            double musicFrom;
            if (hold)
            {
                dspStartTime = now + Math.Max(ScheduleMargin, lead) - fromSongTime;
                musicFrom = Math.Max(0, fromSongTime);
            }
            else
            {
                dspStartTime = now + ScheduleMargin + lead - fromSongTime;
                musicFrom = Math.Max(0, fromSongTime - lead);
            }
            StartSongTime = fromSongTime;
            holdSongTime = hold ? fromSongTime : double.NegativeInfinity;

            musicSource.Stop();
            var clip = musicSource.clip;
            if (clip != null && musicFrom < clip.length)
            {
                musicSource.timeSamples = Mathf.Clamp((int)Math.Round(musicFrom * clip.frequency), 0, clip.samples - 1);
                musicSource.PlayScheduled(SongTimeToDsp(musicFrom));
            }
            running = true;
            Tick();
        }

        /// <summary>시계와 음악을 멈춘다. 멈춘 곡 시각은 <see cref="PausedSongTime"/>이다(카운트다운 중이면 서 있던 시각 그대로).</summary>
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

        /// <summary>입력 이벤트 시각(realtimeSinceStartup 기준)을 그 순간 들리던 곡 위치로 바꾼다. 카운트다운 중이면 서 있는 시각보다 앞이다.</summary>
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
            rawSongTime = RealtimeToSongTime(Time.realtimeSinceStartupAsDouble);
            SongTime = Math.Max(rawSongTime, holdSongTime);
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
