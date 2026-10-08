using System;
using System.Collections.Generic;
using System.Text;
using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IWannabe.App
{
    /// <summary>
    /// 기기 점검 화면. 개발 빌드에서 첫 씬(DeviceCheck)으로 넣어 실기기의 오디오 지연, 터치 반응, 프레임을 잰다.
    /// 게임과 같은 경로로 재려고 곡 시계는 <see cref="Conductor"/>, 입력은 <see cref="RhythmInput"/>을 그대로 쓴다.
    /// <list type="bullet">
    /// <item>탭(소리): 75 BPM 클릭에 맞춰 누르게 해서, 입력이 곡 시계에서 몇 ms 어긋나 들어오는지 잰다. 게임에 필요한 입력 보정값이 이 값이다.</item>
    /// <item>탭(화면): 같은 박에 화면만 번쩍이게 한다. 소리 탭과의 차이로 오디오 출력 지연을 화면 지연과 떼어 볼 수 있다.</item>
    /// <item>루프백: 스피커(또는 이어폰을 마이크에 댄 채)로 낸 클릭을 마이크로 받아 왕복 지연을 잰다. 마이크 입력 지연이 섞이므로 설정끼리 비교용이다.</item>
    /// <item>터치: 입력 이벤트 시각과 처리 시각의 차, 터치 이동 이벤트 간격.</item>
    /// <item>프레임: 카탈로그의 스테이지를 자동 플레이로 돌며 프레임 시간을 잰다(<see cref="FrameCheckRunner"/>).</item>
    /// </list>
    /// 결과는 <see cref="DeviceCheckLog"/>에 쌓인다.
    /// </summary>
    public sealed partial class DeviceCheck : MonoBehaviour
    {
        enum Page { Info, AudioTap, VisualTap, Loopback, Touch, Frame, Log }

        static readonly string[] PageNames = { "정보", "탭(소리)", "탭(화면)", "루프백", "터치", "프레임", "결과" };
        static readonly string[] OutputNames = { "스피커", "유선", "블루투스" };
        static readonly int[] BufferSizes = { 256, 512, 1024 };
        /// <summary>프로젝트가 샘플레이트를 정하지 않으면(0) 기기가 고른다. S24는 24000Hz를 골라 12kHz 위 고음이 잘렸다.</summary>
        static readonly int[] SampleRates = { 24000, 48000 };

        const double TapBpm = 75;
        const int CountInBeats = 4;
        const int TapBeats = 24;
        /// <summary>가장 가까운 클릭에서 이보다(초) 먼 탭은 버린다. 75 BPM이면 박 간격 0.8초라 블루투스 지연(0.2~0.3초)도 들어온다.</summary>
        const double TapAcceptSeconds = 0.4;
        const float ReferenceSize = 720f;

        [SerializeField] StageCatalog catalog;
        [SerializeField] Conductor conductor;
        [SerializeField] RhythmInput input;
        [SerializeField] string lobbySceneName = "Lobby";

        static Page lastPage;
        static int lastOutput;
        static bool summaryLogged;

        Page page;
        int output;
        Vector2 scroll;
        GUIStyle label, button, title, flash;
        Texture2D white;

        bool tapRunning, tapVisual;
        double[] tapBeats;
        AudioClip tapClip;
        readonly List<double> tapOffsets = new List<double>();
        int tapPresses;
        string audioTapResult = "", visualTapResult = "";
        int suggestedOffsetMs = int.MinValue;

        public string OutputName => OutputNames[output];

        void Awake()
        {
            page = lastPage;
            output = lastOutput;
            input.GameplayEnabled = false;
            // 앱을 켤 때 한 번만 남긴다(로비·프레임 점검에서 돌아올 때는 남기지 않음).
            if (!summaryLogged) DeviceCheckLog.Add(DeviceSummary());
            summaryLogged = true;
        }

        void OnEnable()
        {
            input.Pressed += OnTapPressed;
            input.PauseRequested += OnBack;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            EnableTouchProbe();
        }

        void OnDisable()
        {
            input.Pressed -= OnTapPressed;
            input.PauseRequested -= OnBack;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            DisableTouchProbe();
            lastPage = page;
            lastOutput = output;
        }

        void OnDestroy()
        {
            if (white != null) Destroy(white);
            ReleaseClips();
        }

        /// <summary>
        /// 오디오 시스템이 다시 시작되면(DSP 버퍼 변경, 블루투스 연결·해제 등) 코드로 만든 클립(AudioClip.Create)은 데이터를 잃어 소리가 나지 않는다.
        /// 재던 것을 멈추고 클립을 버려, 다음 시작 때 새로 만들게 한다.
        /// </summary>
        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (tapRunning) StopTap("오디오 설정이 바뀌어 중지함");
            if (LoopbackRunning) StopLoopback("오디오 설정이 바뀌어 중지함");
            ReleaseClips();
            if (deviceWasChanged)
            {
                AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
                DeviceCheckLog.Add($"오디오 장치가 바뀜(블루투스 연결·해제 등): {AudioSettings.outputSampleRate}Hz 버퍼 {bufferLength}×{bufferCount}, " +
                                   $"추정 출력 지연 {conductor.OutputLatency * 1000:0.0}ms");
            }
        }

        void ReleaseClips()
        {
            if (tapClip != null) Destroy(tapClip);
            tapClip = null;
            DestroyLoopbackClips();
        }

        void Update()
        {
            if (tapRunning && conductor.SongTime > tapBeats[tapBeats.Length - 1] + 1.0) FinishTap();
            UpdateLoopback();
        }

        void OnBack()
        {
            if (tapRunning) StopTap("중지함");
            else if (LoopbackRunning) StopLoopback("중지함");
        }

        // ───────────────────────── 정보 ─────────────────────────

        string DeviceSummary()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            var config = AudioSettings.GetConfiguration();
            return $"기기 {SystemInfo.deviceModel}, {SystemInfo.operatingSystem}, CPU {SystemInfo.processorType} ×{SystemInfo.processorCount}, " +
                   $"메모리 {SystemInfo.systemMemorySize}MB, GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                   $"화면 {Screen.width}×{Screen.height} {Screen.currentResolution.refreshRateRatio.value:0.#}Hz, 목표 {Application.targetFrameRate}fps, " +
                   $"오디오 {config.sampleRate}Hz 버퍼 {bufferLength}×{bufferCount} (추정 출력 지연 {conductor.OutputLatency * 1000:0.0}ms), " +
                   $"입력 보정 {PlayerCalibration.InputOffsetMs}ms";
        }

        void SetBufferSize(int size) => ResetAudio($"DSP 버퍼 {size}", config => { config.dspBufferSize = size; return config; });

        void SetSampleRate(int rate) => ResetAudio($"샘플레이트 {rate}Hz", config => { config.sampleRate = rate; return config; });

        /// <summary>오디오 설정을 바꿔 오디오 시스템을 다시 시작한다. 코드로 만든 클립은 <see cref="OnAudioConfigurationChanged"/>에서 버린다.</summary>
        void ResetAudio(string request, Func<AudioConfiguration, AudioConfiguration> change)
        {
            if (!AudioSettings.Reset(change(AudioSettings.GetConfiguration())))
            {
                DeviceCheckLog.Add($"{request}로 바꾸지 못함");
                return;
            }
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            DeviceCheckLog.Add($"{request} 요청 → {AudioSettings.outputSampleRate}Hz 버퍼 {bufferLength}×{bufferCount}, " +
                               $"추정 출력 지연 {conductor.OutputLatency * 1000:0.0}ms");
        }

        // ───────────────────────── 탭 테스트 ─────────────────────────

        void StartTap(bool visual)
        {
            if (tapRunning || LoopbackRunning) return;
            if (tapClip == null) tapClip = CreateClickTrack("DeviceCheckTap", TapBpm, CountInBeats, TapBeats, 0.5, out tapBeats);
            tapVisual = visual;
            tapOffsets.Clear();
            tapPresses = 0;
            conductor.Load(tapClip, new TempoMap(tapBeats), visual ? 0f : 1f);
            conductor.Play(0, 0.5, false);
            input.GameplayEnabled = true;
            tapRunning = true;
        }

        void OnTapPressed(RhythmInputEvent e)
        {
            if (!tapRunning) return;
            tapPresses++;
            double time = conductor.RealtimeToSongTime(e.Time);
            double nearest = double.MaxValue;
            for (int i = CountInBeats; i < tapBeats.Length; i++)
                if (Math.Abs(time - tapBeats[i]) < Math.Abs(nearest)) nearest = time - tapBeats[i];
            if (Math.Abs(nearest) <= TapAcceptSeconds) tapOffsets.Add(nearest);
        }

        void FinishTap()
        {
            string kind = tapVisual ? "화면 탭" : $"소리 탭({OutputName})";
            StopTap(null);
            if (tapOffsets.Count < 4)
            {
                SetTapResult($"{kind}: 탭이 너무 적음({tapOffsets.Count}개)");
                return;
            }
            var sorted = tapOffsets.ToArray();
            Array.Sort(sorted);
            double median = sorted[sorted.Length / 2];
            double mean = 0, sq = 0;
            foreach (var o in sorted) mean += o;
            mean /= sorted.Length;
            foreach (var o in sorted) sq += (o - mean) * (o - mean);
            double sd = Math.Sqrt(sq / sorted.Length);
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            string result = $"{kind}: 중앙 {median * 1000:+0;-0}ms, 평균 {mean * 1000:+0;-0}ms, 표준편차 {sd * 1000:0}ms, " +
                            $"탭 {tapOffsets.Count}/{TapBeats}(누름 {tapPresses}), 버퍼 {bufferLength}×{bufferCount}";
            if (!tapVisual) suggestedOffsetMs = (int)Math.Round(median * 1000);
            SetTapResult(result);
            DeviceCheckLog.Add(result);
        }

        void StopTap(string reason)
        {
            tapRunning = false;
            input.GameplayEnabled = false;
            conductor.Stop();
            if (reason != null) SetTapResult(reason);
        }

        void SetTapResult(string text)
        {
            if (tapVisual) visualTapResult = text;
            else audioTapResult = text;
        }

        /// <summary>
        /// 클릭 트랙: <paramref name="lead"/>초 뒤부터 <paramref name="bpm"/> 간격으로 카운트인(높은 소리)과 본 클릭을 놓는다.
        /// <paramref name="beats"/>에 클릭 시각(초)을 돌려준다.
        /// </summary>
        static AudioClip CreateClickTrack(string name, double bpm, int countIn, int clicks, double lead, out double[] beats)
        {
            int rate = AudioSettings.outputSampleRate;
            double interval = 60.0 / bpm;
            beats = new double[countIn + clicks];
            for (int i = 0; i < beats.Length; i++) beats[i] = lead + i * interval;
            var data = new float[(int)((beats[beats.Length - 1] + 1.5) * rate)];
            for (int i = 0; i < beats.Length; i++) WriteClick(data, rate, beats[i], i < countIn ? 2400 : 1500, 0.8f);
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>시작이 또렷한 짧은 클릭(감쇠 8ms).</summary>
        static void WriteClick(float[] data, int rate, double time, double frequency, float amp)
        {
            int start = (int)Math.Round(time * rate), length = (int)(0.04 * rate);
            for (int n = 0; n < length && start + n < data.Length; n++)
            {
                double t = n / (double)rate;
                data[start + n] += (float)(amp * Math.Exp(-t / 0.008) * Math.Sin(2 * Math.PI * frequency * t));
            }
        }

        // ───────────────────────── 화면 ─────────────────────────

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Min(Screen.width, Screen.height) / ReferenceSize;
            var safe = Screen.safeArea;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var area = new Rect(safe.x / scale + 8, (Screen.height - safe.yMax) / scale + 8, safe.width / scale - 16, safe.height / scale - 16);

            if (tapRunning)
            {
                DrawTapRun(area);
                return;
            }

            GUILayout.BeginArea(area);
            GUILayout.Label("IWannabe 기기 점검 (개발 빌드)", title);
            page = (Page)GUILayout.SelectionGrid((int)page, PageNames, 7, button, GUILayout.Height(56));
            GUILayout.Space(8);
            scroll = GUILayout.BeginScrollView(scroll);
            switch (page)
            {
                case Page.Info: DrawInfo(); break;
                case Page.AudioTap: DrawTapPage(false); break;
                case Page.VisualTap: DrawTapPage(true); break;
                case Page.Loopback: DrawLoopbackPage(); break;
                case Page.Touch: DrawTouchPage(); break;
                case Page.Frame: DrawFramePage(); break;
                case Page.Log: DrawLog(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawInfo()
        {
            GUILayout.Label(DeviceSummary().Replace(", ", "\n"), label);
            GUILayout.Space(8);
            GUILayout.Label("지금 소리가 나오는 곳(결과에 붙는 이름). 블루투스·유선은 연결한 뒤 고른다.", label);
            output = GUILayout.SelectionGrid(output, OutputNames, 3, button, GUILayout.Height(56));
            GUILayout.Space(8);
            GUILayout.Label("DSP 버퍼 크기(작을수록 지연이 짧고 끊길 수 있다). 프로젝트 설정은 1024.", label);
            GUILayout.BeginHorizontal();
            foreach (var size in BufferSizes)
                if (GUILayout.Button(size.ToString(), button, GUILayout.Height(56))) SetBufferSize(size);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label($"출력 샘플레이트(지금 {AudioSettings.outputSampleRate}Hz). 프로젝트 설정은 0(기기가 고름).", label);
            GUILayout.BeginHorizontal();
            foreach (var rate in SampleRates)
                if (GUILayout.Button($"{rate}Hz", button, GUILayout.Height(56))) SetSampleRate(rate);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            if (GUILayout.Button("로비로(게임 그대로 해 보기, 오른쪽 위 버튼으로 돌아옴)", button, GUILayout.Height(56)))
            {
                DeviceCheckReturnButton.Show(lobbySceneName, SceneManager.GetActiveScene().name);
                SceneManager.LoadScene(lobbySceneName);
            }
        }

        void DrawTapPage(bool visual)
        {
            GUILayout.Label(visual
                ? "화면이 번쩍일 때마다 화면 아무 곳이나 누른다. 소리는 나지 않는다. 처음 4번은 박을 익히는 카운트인이다."
                : $"클릭 소리에 맞춰 화면 아무 곳이나 누른다({OutputName}). 처음 4번(높은 소리)은 카운트인이고, 다음 {TapBeats}번에 맞춰 누른다. 화면은 보지 말고 소리만 듣는다.",
                label);
            if (GUILayout.Button("시작", button, GUILayout.Height(72))) StartTap(visual);
            string result = visual ? visualTapResult : audioTapResult;
            if (result.Length > 0) GUILayout.Label(result, label);
            if (!visual && suggestedOffsetMs != int.MinValue)
            {
                GUILayout.Label($"게임의 입력 보정값으로 쓰면 {suggestedOffsetMs:+0;-0;0}ms (지금 {PlayerCalibration.InputOffsetMs}ms)", label);
                if (GUILayout.Button("이 값으로 입력 보정 설정", button, GUILayout.Height(56)))
                {
                    PlayerCalibration.InputOffsetMs = suggestedOffsetMs;
                    DeviceCheckLog.Add($"입력 보정을 {PlayerCalibration.InputOffsetMs}ms로 설정");
                }
            }
        }

        void DrawTapRun(Rect area)
        {
            bool lit = false;
            if (tapVisual)
            {
                double now = conductor.SongTime;
                foreach (var beat in tapBeats)
                    if (now >= beat && now < beat + 0.08) lit = true;
            }
            var full = new Rect(0, 0, Screen.width, Screen.height);
            var previous = GUI.color;
            GUI.color = lit ? Color.white : new Color(0.08f, 0.09f, 0.12f);
            GUI.DrawTexture(new Rect(full.x, full.y, full.width / GUI.matrix.m00, full.height / GUI.matrix.m11), white);
            GUI.color = previous;
            if (lit) return;
            GUILayout.BeginArea(area);
            GUILayout.Label(tapVisual ? "번쩍일 때 누르기" : "소리에 맞춰 누르기", flash);
            GUILayout.Label($"누름 {tapPresses} · 뒤로 가기로 중지", label);
            GUILayout.EndArea();
        }

        void DrawFramePage()
        {
            GUILayout.Label("카탈로그의 스테이지를 차례로 자동 플레이하며 프레임 시간을 잰다. 디버그 패널은 숨긴다. 끝나면 이 화면으로 돌아온다.", label);
            if (GUILayout.Button("스테이지 자동 플레이 시작", button, GUILayout.Height(72)))
                FrameCheckRunner.Begin(catalog, lobbySceneName, SceneManager.GetActiveScene().name);
            foreach (var line in FrameCheckRunner.Results) GUILayout.Label(line, label);
        }

        void DrawLog()
        {
            GUILayout.Label($"파일: {DeviceCheckLog.FilePath} ({DeviceCheckLog.Lines.Count}줄, 앱을 다시 켜도 남는다)", label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체 기록을 클립보드로 복사", button, GUILayout.Height(56)))
                GUIUtility.systemCopyBuffer = string.Join("\n", DeviceCheckLog.Lines);
            if (GUILayout.Button("기록 지우기", button, GUILayout.Height(56), GUILayout.Width(200))) DeviceCheckLog.Clear();
            GUILayout.EndHorizontal();
            for (int i = DeviceCheckLog.Lines.Count - 1; i >= 0; i--) GUILayout.Label(DeviceCheckLog.Lines[i], label);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            label = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            flash = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
        }

        static string Summarize(IReadOnlyList<double> values, string unit, double scale = 1000)
        {
            if (values.Count == 0) return "-";
            var sorted = new List<double>(values);
            sorted.Sort();
            double P(double q) => sorted[Math.Min(sorted.Count - 1, (int)Math.Round(q * (sorted.Count - 1)))] * scale;
            var sb = new StringBuilder();
            sb.Append($"중앙 {P(0.5):0.0}{unit}, 90% {P(0.9):0.0}{unit}, 최대 {P(1):0.0}{unit} ({sorted.Count}개)");
            return sb.ToString();
        }
    }
}
