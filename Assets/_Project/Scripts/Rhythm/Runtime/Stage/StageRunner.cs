using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 플레이 씬의 진행자. <see cref="StageFlow"/>가 로드해 둔 스테이지로 미니게임 연출을 띄우고,
    /// 곡 시간에 맞춰 큐 효과음 예약·연출 이벤트·판정을 돌린다. 콘텐츠 로드·언로드는 하지 않는다.
    /// <para>
    /// 채보는 패턴 단위로 재생한다(<see cref="ChartPlayback"/>). 판을 시작할 때 시작 지점 이후에 시작하는 패턴을 정하고,
    /// 큐 소리 예약·오브젝트 준비·큐 알림·판정이 모두 그 목록 하나를 곡 시간 순서대로 따른다. 처음 시작도 시작 지점 0초인 판이다
    /// (<see cref="Restart"/>). 사건은 <see cref="AdvanceTo"/> 한 곳에서 시간 순서대로 처리한다(매 프레임, 입력, 일시정지).
    /// </para>
    /// <para>
    /// 곡 시간은 뒤로 가지 않는다. 일시정지는 그 순간까지의 사건을 처리하고 멈추며, 재개는 시계를 멈춘 시각에 세워 둔 채
    /// 카운트다운한 뒤 음악과 함께 그 시각에서 출발한다. 카운트다운 중에 다시 멈춰도 멈춘 시각은 같다.
    /// 일시정지로 꺼지는 것은 미리 예약한 큐 소리뿐이고, 재개할 때 멈춘 시각 이후 큐부터 다시 예약한다.
    /// </para>
    /// <para>
    /// 채보의 구간마다 그 미니게임의 연출로 바꾼다(리믹스). 연출은 미니게임마다 하나씩 처음에 모두 만들어 두고,
    /// 채보 진행과 상관없이 구간 시작 박에 바로 바꾸되, 그 박이 입력과 겹치면 입력 사이로 옮겨 바꾼다
    /// (<see cref="SegmentSwitch"/>). 앞 구간에서 입력이 남은 노트(날아오는 중, 누르고 있는 홀드)는
    /// 새 연출이 이어받아 보여 주고 판정을 받는다. 판정과 점수는 구간과 상관없이 이어진다.
    /// </para>
    /// </summary>
    public sealed class StageRunner : MonoBehaviour
    {
        public enum State { Loading, Playing, Paused, Finished, Failed }

        /// <summary>구간 하나의 실행 정보.</summary>
        sealed class Segment
        {
            public TimelineSegment Timeline;
            public MinigameDefinition Minigame;
            public StagePresenter Presenter;
        }

        /// <summary>자동 플레이 입력의 입력원 번호. 실제 장치 번호와 겹치지 않는다.</summary>
        const int AutoInputSource = -1;

        [SerializeField] RhythmSettings settings;
        [SerializeField] Conductor conductor;
        [SerializeField] SfxPlayer sfx;
        [SerializeField] RhythmInput input;
        [SerializeField] StageHud hud;
        [SerializeField] Camera stageCamera;
        [SerializeField] Transform stageRoot;

        [Tooltip("에디터에서 로비를 거치지 않고 이 씬을 바로 플레이할 때 불러올 스테이지.")]
        [SerializeField] StageReference fallbackStage;

        [Tooltip("개발용. 모든 노트를 정확한 시각에 자동으로 입력하고 실제 입력은 무시한다. 리믹스 전환·채보 점검에 쓴다.")]
        [SerializeField] bool autoPlay;

        State state = State.Loading;
        StageFlow flow;
        StageDefinition definition;
        ChartTimeline timeline;
        ChartPlayback playback;
        Judge judge;
        ScoreTracker score;
        readonly List<Segment> segments = new List<Segment>();
        /// <summary>재개 카운트다운 중에 눌러서 무시한 입력원. 그 입력원의 뗌도 무시한다.</summary>
        readonly HashSet<int> countInSources = new HashSet<int>();
        int current = -1;
        double endTime;
        double inputOffset;
        /// <summary>사건을 처리한 마지막 곡 시각.</summary>
        double processedTime;
        bool countingIn;
        int lastBeat;
        int nextAutoNote;
        TimelineNote autoHold;

        /// <summary>판이 시작될 때마다(처음 시작, 시작 지점부터 다시). 디버그 패널·점검 도구가 기록을 새로 시작한다.</summary>
        public event Action<StageRunner> Started;
        /// <summary>스테이지가 끝났을 때(결과 화면을 띄운 직후). 오류로 끝나면 점수가 null이다. 테스트 도구가 쓴다.</summary>
        public event Action<StageRunner, ScoreTracker> Ended;
        /// <summary>판정이 날 때마다(디버그 패널용).</summary>
        public event Action<NoteJudgement> NoteJudged;
        /// <summary>노트에 맞지 않은 입력(헛치기)마다. 인자는 판정 시각(디버그 패널용).</summary>
        public event Action<double> NoteWhiffed;

        public bool AutoPlay
        {
            get => autoPlay;
            set => autoPlay = value;
        }

        public StageDefinition Definition => definition;
        public State CurrentState => state;
        public bool HasFailed => state == State.Failed;
        /// <summary>플레이용 타임라인. 스테이지 준비가 끝나기 전에는 null. 아래 재생 목록·판정·점수도 마찬가지.</summary>
        public ChartTimeline Timeline => timeline;
        /// <summary>이번 판에 재생하는 채보와 진행 기록.</summary>
        public ChartPlayback Playback => playback;
        public Judge Judge => judge;
        public ScoreTracker Score => score;
        public JudgeWindows JudgeWindows => settings.JudgeWindows;
        /// <summary>이번 판의 시작 지점(곡 시각, 초).</summary>
        public double StartTime => playback != null ? playback.StartTime : 0;
        /// <summary>사건을 처리한 마지막 곡 시각. 일시정지 중이면 멈춘 시각이다.</summary>
        public double ProcessedTime => processedTime;
        /// <summary>마지막으로 일시정지한 곡 시각. 이번 판에 멈춘 적이 없으면 NaN.</summary>
        public double LastPauseTime { get; private set; } = double.NaN;
        /// <summary>지금 화면에 있는 구간의 인덱스. 시작 전에는 -1.</summary>
        public int CurrentSegment => current;
        public double InputOffsetSeconds => inputOffset;
        public int SegmentCount => segments.Count;
        /// <summary>이번 판에서 곡 중간에 연출을 바꾼 횟수.</summary>
        public int SwitchCount { get; private set; }
        /// <summary>구간이 바뀔 때 새 연출이 이어받은 노트 수(입력이 남아 있던 앞 구간 노트).</summary>
        public int CarriedNoteCount { get; private set; }
        /// <summary>구간 전환 시각과 가장 가까운 입력 사이의 최소 거리(초). 전환이 없으면 양의 무한대.</summary>
        public double MinSwitchClearance { get; private set; } = double.PositiveInfinity;
        /// <summary>
        /// 지금 구간이 아닌 구간의 패턴 준비·큐가 온 횟수. 패턴의 큐가 모두 자기 구간 안에 있다면 0이다.
        /// </summary>
        public int StrayEventCount { get; private set; }
        /// <summary>판이 끝났을 때 오브젝트·큐 알림·큐 소리·판정 중 빠진 것이 있던 패턴 수. 0이어야 한다.</summary>
        public int IncompletePatternCount { get; private set; }

        StagePresenter Presenter => segments[current].Presenter;
        MinigameDefinition Minigame => segments[current].Minigame;

        void OnEnable()
        {
            hud.PauseClicked += Pause;
            hud.ResumeClicked += Resume;
            hud.RetryClicked += Retry;
            hud.ExitClicked += ExitToLobby;
            input.PauseRequested += TogglePause;
            input.Pressed += OnPressed;
            input.Released += OnReleased;
        }

        void OnDisable()
        {
            hud.PauseClicked -= Pause;
            hud.ResumeClicked -= Resume;
            hud.RetryClicked -= Retry;
            hud.ExitClicked -= ExitToLobby;
            input.PauseRequested -= TogglePause;
            input.Pressed -= OnPressed;
            input.Released -= OnReleased;
        }

        void Start() => StartAsync(destroyCancellationToken).Forget();

        async UniTaskVoid StartAsync(CancellationToken cancellationToken)
        {
            input.GameplayEnabled = false;
            hud.ShowCenter(null);

            flow = StageFlow.Instance;
            if (flow == null)
            {
                Fail("StageFlow(AppRoot)가 씬에 없습니다.");
                return;
            }
            if (flow.CurrentStage == null)
            {
                if (fallbackStage == null || !fallbackStage.RuntimeKeyIsValid())
                {
                    Fail("불러올 스테이지가 지정되지 않았습니다.");
                    return;
                }
                await flow.LoadStageInPlaceAsync(fallbackStage, cancellationToken);
            }

            definition = flow.CurrentStage;
            if (definition == null)
            {
                Fail("스테이지를 불러오지 못했습니다.");
                return;
            }
            if (!Validate(definition, out string error))
            {
                Fail(error);
                return;
            }

            var tempoMap = definition.Song.CreateTempoMap();
            try
            {
                timeline = ChartTimeline.Build(definition.Chart.Patterns, definition.Chart.Segments, tempoMap);
            }
            catch (ArgumentException e)
            {
                Fail($"채보를 읽지 못했습니다. {e.Message}");
                return;
            }
            if (!PrepareSegments(tempoMap, out error) || !ValidateCueSounds(out error))
            {
                Fail(error);
                return;
            }

            inputOffset = PlayerCalibration.InputOffsetSeconds;
            conductor.Load(definition.Song.Clip, tempoMap, definition.MusicVolume);
            endTime = Math.Max(definition.Song.Clip.length, timeline.LastEventTime + settings.EndPaddingSeconds);
            hud.SetStageName(definition.DisplayName);
            PrepareRun(0);

            // 연출 준비가 끝났음을 알리고, 로딩 화면이 완전히 걷힌 뒤에 카운트를 시작한다.
            flow.ReportStagePrepared();
            await UniTask.WaitWhile(() => flow.IsTransitioning, cancellationToken: cancellationToken);

            Debug.Log($"[StageRunner] '{definition.DisplayName}' 시작: 노트 {timeline.Notes.Count}개, 구간 {segments.Count}개, " +
                      $"출력 지연 보정 {conductor.OutputLatency * 1000:0}ms, 입력 보정 {inputOffset * 1000:0}ms" +
                      (autoPlay ? ", 자동 플레이" : string.Empty));
            StartRun();
        }

        static bool Validate(StageDefinition stage, out string error)
        {
            error = null;
            if (stage.Song == null || stage.Song.Clip == null) error = "곡(SongData)이 없습니다.";
            else if (!stage.Song.HasBeatMap) error = "곡 분석 결과(비트 지도)가 없습니다. 채보 생성 프로필에서 분석을 실행하세요.";
            else if (stage.Chart == null) error = "채보(ChartData)가 없습니다.";
            else if (stage.Chart.Patterns.Count == 0) error = "채보가 비어 있습니다. 채보 생성 프로필에서 생성을 실행하세요.";
            else if (stage.Minigames.Count == 0) error = "스테이지에 미니게임이 없습니다.";
            else
            {
                foreach (var minigame in stage.Minigames)
                {
                    if (minigame == null) error = "스테이지의 미니게임 목록에 빈 칸이 있습니다.";
                    else if (minigame.PresenterPrefab == null) error = $"미니게임 '{minigame.DisplayName}'에 연출 프리팹이 없습니다.";
                    if (error != null) break;
                }
            }
            return error == null;
        }

        /// <summary>
        /// 구간마다 미니게임을 찾고, 미니게임마다 연출을 하나씩 만들어 숨겨 둔다(같은 미니게임이 다시 나오면 같은 연출을 다시 들인다).
        /// 전환 시각은 타임라인이 정한 시각(구간 시작 박, 입력과 겹치면 입력 사이)이다.
        /// </summary>
        bool PrepareSegments(TempoMap tempoMap, out string error)
        {
            error = null;
            var presenters = new Dictionary<MinigameDefinition, StagePresenter>();
            foreach (var segment in timeline.Segments)
            {
                var minigame = definition.FindMinigame(segment.MinigameId);
                if (minigame == null)
                {
                    error = $"구간 {segment.Index + 1}의 미니게임 '{segment.MinigameId}'이 스테이지 미니게임 목록에 없습니다.";
                    return false;
                }
                if (!presenters.TryGetValue(minigame, out var presenter))
                {
                    presenter = Instantiate(minigame.PresenterPrefab, stageRoot);
                    presenter.name = minigame.PresenterPrefab.name;
                    presenter.Bind(new StageContext(definition, minigame, timeline, tempoMap, conductor, sfx));
                    presenter.gameObject.SetActive(false);
                    presenters.Add(minigame, presenter);
                }

                segments.Add(new Segment { Timeline = segment, Minigame = minigame, Presenter = presenter });
                MinSwitchClearance = Math.Min(MinSwitchClearance, segment.SwitchClearance);
            }

            if (presenters.Count > 1)
                Debug.Log($"[StageRunner] 연출 {presenters.Count}개 생성, 전체 할당 {Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0):0}MB");
            return true;
        }

        /// <summary>모든 큐가 그 구간 미니게임에서 효과음을 찾는지 확인한다. 소리 없이 오브젝트만 나오는 큐가 없게 한다.</summary>
        bool ValidateCueSounds(out string error)
        {
            error = null;
            foreach (var cue in timeline.Cues)
            {
                var minigame = segments[cue.Segment].Minigame;
                if (minigame.TryGetCueSound(cue.CueId, out var sound) && sound.clip != null) continue;
                error = $"미니게임 '{minigame.DisplayName}'에 큐 '{cue.CueId}'의 효과음이 없습니다. 큐마다 효과음이 있어야 합니다.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 이번 판을 시작 지점 <paramref name="startTime"/>(곡 시각, 초)부터 다시 시작한다. 씬을 다시 읽지 않고 판정·점수·연출을
        /// 새로 시작하며, 시작 지점 이후에 시작하는 패턴만 재생한다. 시작 지점 앞으로 리드만큼 곡이 먼저 흐른다(채보는 나오지 않는다).
        /// 시작 지점이 0보다 크면 클리어 기록을 남기지 않는다. 디버그 패널과 점검 도구가 쓴다.
        /// </summary>
        public void Restart(double startTime)
        {
            if (state == State.Loading || state == State.Failed || timeline == null) return;
            if (flow != null && flow.IsTransitioning) return;
            startTime = Math.Max(0, Math.Min(startTime, definition.Song.Clip.length));
            PrepareRun(startTime);
            Debug.Log($"[StageRunner] {startTime:0.000}s부터 다시 시작: 패턴 {playback.Patterns.Count}/{timeline.Patterns.Count}개, " +
                      $"구간 {current + 1} '{Minigame.DisplayName}'");
            StartRun();
        }

        /// <summary>
        /// 판을 준비한다. 시작 지점 이후에 시작하는 패턴으로 재생 목록을 만들고, 판정·점수를 새로 만들고,
        /// 시작 지점의 구간 연출을 들인다. 시작 지점 앞 패턴은 소리·오브젝트·판정 모두 나오지 않는다.
        /// </summary>
        void PrepareRun(double startTime)
        {
            conductor.Stop();
            sfx.StopAll();
            input.GameplayEnabled = false;
            hud.ShowPause(false);
            hud.HideResult();
            hud.SetPauseButtonVisible(false);
            hud.ShowCenter(null);
            if (current >= 0) Presenter.ExitSegment();
            current = -1;

            playback = new ChartPlayback(timeline, startTime);
            if (judge != null)
            {
                judge.Judged -= OnJudged;
                judge.Whiffed -= OnWhiffed;
            }
            judge = new Judge(timeline.Notes, settings.JudgeWindows);
            judge.Judged += OnJudged;
            judge.Whiffed += OnWhiffed;
            playback.SkipExcludedNotes(judge);
            score = new ScoreTracker(playback.JudgementPartCount);

            SwitchCount = 0;
            CarriedNoteCount = 0;
            StrayEventCount = 0;
            IncompletePatternCount = 0;
            LastPauseTime = double.NaN;
            nextAutoNote = 0;
            autoHold = null;
            countInSources.Clear();
            processedTime = double.NegativeInfinity;
            hud.SetProgress((float)(startTime / endTime));
            EnterSegment(SegmentAt(startTime), startTime, false);
            state = State.Loading;
            Started?.Invoke(this);
        }

        /// <summary>준비한 판을 재생한다. 시계가 시작 지점보다 리드만큼 앞에서부터 흐른다.</summary>
        void StartRun()
        {
            conductor.Play(playback.StartTime, settings.StartLeadSeconds, hold: false);
            lastBeat = (int)Math.Floor(conductor.SongBeat);
            BeginCountIn("Ready?");
            input.GameplayEnabled = true;
            state = State.Playing;
        }

        void BeginCountIn(string text)
        {
            hud.ShowCenter(text);
            hud.SetPauseButtonVisible(true);
            countingIn = true;
        }

        /// <summary>곡 시각에 화면에 있어야 할 구간(전환 시각이 그 시각 이하인 마지막 구간).</summary>
        int SegmentAt(double time)
        {
            int index = 0;
            while (index + 1 < segments.Count && segments[index + 1].Timeline.SwitchTime <= time) index++;
            return index;
        }

        void Update()
        {
            if (state != State.Playing) return;

            // 플레이 중에는 패드 A·Enter가 선택된 UI 버튼을 누르지 않도록 선택을 비운다.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

            double time = conductor.SongTime;
            double beat = conductor.SongBeat;
            if (countingIn && !conductor.IsCountingIn)
            {
                hud.ShowCenter(null);
                countingIn = false;
            }

            playback.RecordTime(time);
            AdvanceTo(time);
            ScheduleCueSounds(time);
            DispatchBeats(beat);
            Presenter.Tick(time, beat);
            hud.SetProgress((float)(time / endTime));
            UpdateCameraSize();

            if (time >= endTime) Finish();
        }

        /// <summary>
        /// 곡 시각 <paramref name="time"/>까지의 사건을 시간 순서대로 처리한다: 구간 전환, 오브젝트 준비, 큐 알림, 자동 입력, 지난 노트의 Miss.
        /// 매 프레임·입력·일시정지가 모두 이 함수를 거친다. 이미 처리한 시각 이전이면 아무것도 하지 않는다.
        /// 전환 시각이 끼어 있으면 그 시각까지를 앞 연출에서 마저 처리한 뒤 바꿔, 프레임이 밀리거나 입력이 먼저 와도 사건이 시간 순서대로 일어난다.
        /// </summary>
        void AdvanceTo(double time)
        {
            if (time <= processedTime) return;
            while (current + 1 < segments.Count && time >= segments[current + 1].Timeline.SwitchTime)
            {
                double switchTime = segments[current + 1].Timeline.SwitchTime;
                ProcessUntil(switchTime);
                EnterSegment(current + 1, switchTime, true);
            }
            ProcessUntil(time);
            processedTime = time;
        }

        /// <summary>
        /// 곡 시각까지 패턴의 오브젝트를 준비시키고(첫 큐보다 조금 먼저), 큐를 연출에 알리고, 자동 입력과 시간이 지난 노트의 Miss를 처리한다.
        /// 다음 구간의 패턴·큐는 그 구간 연출이 들어온 뒤에 꺼낸다(다음 구간 첫 큐는 전환 시각 이후라 늦지 않다).
        /// </summary>
        void ProcessUntil(double time)
        {
            while (playback.TryTakePattern(time, settings.PatternSpawnLead, current, out var pattern))
            {
                CountIfStray(pattern.Segment);
                Presenter.SpawnPattern(pattern);
            }
            while (playback.TryTakeCue(time, current, out var cue))
            {
                CountIfStray(cue.Segment);
                Presenter.OnCue(cue);
            }
            if (autoPlay) AutoPlayInputs(time - inputOffset);
            judge.Update(time - inputOffset);
        }

        /// <summary>
        /// 앞 연출을 내보내고 다음 연출을 들인다. 앞 구간 노트 중 판정이 끝나지 않은 것(날아오는 중, 누르고 있는 홀드)은
        /// 새 연출이 이어받는다. 판정은 이 구간 노트까지 받는다. 화면 색·HUD·카메라도 바로 그 미니게임에 맞춘다.
        /// </summary>
        /// <param name="isSwitch">곡 중간의 전환인지. 판을 시작하며 들이는 것이면 false.</param>
        void EnterSegment(int index, double time, bool isSwitch)
        {
            if (current >= 0) Presenter.ExitSegment();
            current = index;
            judge.OpenSegment = index;
            var segment = segments[index];
            var carried = CollectCarriedNotes(index);
            segment.Presenter.EnterSegment(segment.Timeline, carried);
            stageCamera.backgroundColor = segment.Minigame.BackgroundColor;
            hud.ApplyTheme(segment.Minigame.HudInkColor);
            UpdateCameraSize();
            if (!isSwitch) return;

            SwitchCount++;
            CarriedNoteCount += carried.Count;
            var timelineSegment = segment.Timeline;
            Debug.Log($"[StageRunner] 구간 {index + 1}/{segments.Count} '{segment.Minigame.DisplayName}' 시작 " +
                      $"(곡 {time:0.00}s, 구간 시작 박 {timelineSegment.StartBeat:0.##}, 전환 박 {timelineSegment.SwitchBeat:0.##}, " +
                      $"가장 가까운 입력과 {timelineSegment.SwitchClearance * 1000:0}ms, 이어받은 노트 {carried.Count}개)");
        }

        /// <summary>
        /// 앞 구간 노트 중 오브젝트가 나와 있고 판정이 끝나지 않은 것. 재생하지 않는 패턴의 노트는 판정이 끝난 것으로 치므로 들어오지 않는다.
        /// </summary>
        List<CarriedNote> CollectCarriedNotes(int segmentIndex)
        {
            var carried = new List<CarriedNote>();
            foreach (var note in timeline.Notes)
            {
                if (note.Segment >= segmentIndex || judge.IsFinished(note)) continue;
                if (!playback.IsSpawned(timeline.Patterns[note.PatternIndex])) continue;
                carried.Add(new CarriedNote(note, judge.IsHoldingNote(note)));
            }
            return carried;
        }

        /// <summary>지금 구간이 아닌 구간의 패턴 준비·큐면 센다. 패턴의 큐가 자기 구간 안에 있다면 일어나지 않는다.</summary>
        void CountIfStray(int segment)
        {
            if (segment == current) return;
            StrayEventCount++;
            if (StrayEventCount == 1)
                Debug.LogWarning($"[StageRunner] 구간 {segment + 1}의 큐·패턴이 구간 {current + 1} 진행 중에 왔습니다. 채보의 구간 번호를 확인하세요.");
        }

        /// <summary>
        /// 큐 소리를 출력 지연과 예약 여유만큼 미리 오디오 시계에 예약한다. 곡 시간은 '들리는 시각' 기준이라 믹싱은 출력 지연만큼 앞서 일어난다.
        /// 예약이 늦었어도 빠뜨리지 않고 바로 울린다(큰 프레임 멈춤에서만 일어나며 <see cref="ChartPlayback.LateSoundCount"/>로 센다).
        /// </summary>
        void ScheduleCueSounds(double time)
        {
            double horizon = time + conductor.OutputLatency + settings.CueScheduleAhead;
            while (playback.TryTakeSound(horizon, out var cue))
            {
                // 큐 ID는 그 큐가 속한 구간의 미니게임 안에서 찾는다. 미니게임끼리 같은 ID를 써도 섞이지 않는다.
                // 효과음이 있는지는 시작할 때 확인했다(ValidateCueSounds).
                segments[cue.Segment].Minigame.TryGetCueSound(cue.CueId, out var sound);
                double dsp = conductor.SongTimeToDsp(cue.Time);
                double late = AudioSettings.dspTime - dsp;
                if (late > 0) playback.ReportLateSound(cue, late);
                sfx.PlayScheduled(sound.clip, dsp, sound.volume);
            }
        }

        void DispatchBeats(double beat)
        {
            int beatNow = (int)Math.Floor(beat);
            while (lastBeat < beatNow)
            {
                lastBeat++;
                if (lastBeat >= 0) Presenter.OnBeat(lastBeat);
            }
        }

        /// <summary>
        /// 판정 시각이 지난 노트를 그 노트의 정확한 시각으로 누르고 뗀다. 프레임이 밀려도 Perfect가 나도록
        /// 판정의 Miss 처리보다 먼저 돈다. 재생하지 않는 패턴의 노트는 누르지 않는다.
        /// </summary>
        void AutoPlayInputs(double judgeTime)
        {
            var notes = timeline.Notes;
            while (true)
            {
                while (nextAutoNote < notes.Count && judge.IsSkipped(notes[nextAutoNote])) nextAutoNote++;
                double releaseAt = autoHold != null ? autoHold.EndTime : double.MaxValue;
                double pressAt = nextAutoNote < notes.Count ? notes[nextAutoNote].Time : double.MaxValue;
                if (Math.Min(releaseAt, pressAt) > judgeTime) break;

                if (releaseAt <= pressAt)
                {
                    Presenter.OnInputReleased(releaseAt);
                    judge.Release(releaseAt, AutoInputSource);
                    autoHold = null;
                    continue;
                }

                var note = notes[nextAutoNote++];
                Presenter.OnInputPressed(pressAt);
                judge.Press(pressAt, AutoInputSource);
                if (note.Type == NoteType.Hold) autoHold = note;
                else Presenter.OnInputReleased(pressAt);
            }
        }

        void OnPressed(RhythmInputEvent e)
        {
            if (state != State.Playing || autoPlay) return;
            double songTime = conductor.RealtimeToSongTime(e.Time);
            // 재개 카운트다운 중(시계가 서 있을 때)의 입력은 받지 않는다.
            if (songTime < conductor.HoldSongTime)
            {
                countInSources.Add(e.Source);
                return;
            }
            // 입력이 들어온 순간까지의 사건(구간 전환 포함)을 먼저 처리해, 그 순간 화면에 있던 연출이 입력을 받게 한다.
            AdvanceTo(songTime);
            double time = songTime - inputOffset;
            Presenter.OnInputPressed(time);
            judge.Press(time, e.Source);
        }

        void OnReleased(RhythmInputEvent e)
        {
            if (countInSources.Remove(e.Source)) return;
            if (state != State.Playing || autoPlay) return;
            double songTime = conductor.RealtimeToSongTime(e.Time);
            AdvanceTo(songTime);
            double time = songTime - inputOffset;
            Presenter.OnInputReleased(time);
            judge.Release(time, e.Source);
        }

        /// <summary>판정은 지금 화면의 연출로 간다. 앞 구간에서 이어받은 노트도 마찬가지다.</summary>
        void OnJudged(NoteJudgement judgement)
        {
            playback.RecordJudgement(judgement);
            score.Add(judgement);
            Presenter.OnJudged(judgement);
            hud.ShowJudgement(judgement);
            NoteJudged?.Invoke(judgement);
        }

        void OnWhiffed(double time)
        {
            Presenter.OnWhiff(time);
            NoteWhiffed?.Invoke(time);
        }

        /// <summary>일시정지·재개를 바꾼다. ESC·패드 Start가 부르며, 점검 도구도 같은 처리를 쓰려고 이것을 부른다.</summary>
        public void TogglePause()
        {
            if (state == State.Playing) Pause();
            else if (state == State.Paused) Resume();
        }

        /// <summary>
        /// 멈춘 시각까지의 사건을 마저 처리하고 멈춘다. 그 뒤의 사건은 하나도 처리하지 않은 채로 남는다.
        /// 미리 예약한 큐 소리는 끄고 재개 때 다시 예약한다. 재개 카운트다운 중이면 시계가 멈춘 시각에 서 있으므로 멈춘 시각은 그대로다.
        /// </summary>
        void Pause()
        {
            if (state != State.Playing) return;
            conductor.Pause();
            double time = conductor.PausedSongTime;
            playback.RecordTime(time);
            AdvanceTo(time);
            sfx.StopAll();
            playback.RewindSounds(time);
            judge.ReleaseHolding(time - inputOffset);
            autoHold = null;
            LastPauseTime = time;
            input.GameplayEnabled = false;
            state = State.Paused;
            hud.SetPauseButtonVisible(false);
            Presenter.OnPaused(true);
            hud.ShowPause(true);
        }

        /// <summary>시계를 멈춘 시각에 세워 둔 채 카운트다운하고, 음악과 함께 그 시각에서 출발한다.</summary>
        void Resume()
        {
            if (state != State.Paused) return;
            hud.ShowPause(false);
            Presenter.OnPaused(false);
            conductor.Play(conductor.PausedSongTime, settings.ResumeLeadSeconds, hold: true);
            BeginCountIn("계속!");
            input.GameplayEnabled = true;
            state = State.Playing;
        }

        void Finish()
        {
            state = State.Finished;
            input.GameplayEnabled = false;
            judge.Update(endTime + 10);
            conductor.Stop();
            hud.SetPauseButtonVisible(false);
            hud.ShowCenter(null);
            Presenter.OnStageFinished(score);
            ReportPlayback();

            bool fullPlay = playback.IsFullPlay;
            bool cleared = StageProgress.IsClearingRank(score.Rank);
            // 자동 플레이는 점검용이고, 시작 지점부터 한 판은 곡 일부만 친 것이라 클리어 기록(해금)을 남기지 않는다.
            if (cleared && fullPlay && !autoPlay) StageProgress.MarkCleared(definition.StageId);
            hud.ShowResult(score, cleared, fullPlay);
            Ended?.Invoke(this, score);
        }

        /// <summary>판이 끝났을 때 모든 재생 패턴의 소리·오브젝트·판정이 빠짐없이 났는지 확인해 남긴다.</summary>
        void ReportPlayback()
        {
            var details = new List<string>();
            IncompletePatternCount = playback.CountIncomplete(double.PositiveInfinity, 0, details);
            string summary = $"[StageRunner] 끝: 패턴 {playback.Patterns.Count}/{timeline.Patterns.Count}개 재생" +
                             (playback.IsFullPlay ? string.Empty : $"({playback.StartTime:0.000}s부터)") +
                             $", 판정 {score.JudgedParts}/{score.TotalParts}, 빠진 것이 있는 패턴 {IncompletePatternCount}개, " +
                             $"묶음 위반 {playback.ViolationCount}건, 늦은 큐 소리 {playback.LateSoundCount}개";
            if (IncompletePatternCount == 0 && playback.ViolationCount == 0 && playback.LateSoundCount == 0)
            {
                Debug.Log(summary);
                return;
            }
            foreach (string violation in playback.Violations) details.Add(violation);
            if (playback.FirstLateSound != null) details.Add(playback.FirstLateSound);
            Debug.LogWarning(summary + "\n  " + string.Join("\n  ", details));
        }

        void Fail(string message)
        {
            state = State.Failed;
            input.GameplayEnabled = false;
            Debug.LogError($"[StageRunner] {message}");
            hud.ShowError(message);
            // 로딩 화면이 오류 메시지를 가리지 않도록 걷는다.
            if (flow != null) flow.ReportStagePrepared();
            Ended?.Invoke(this, null);
        }

        void Retry()
        {
            if (state == State.Loading || flow == null || flow.IsTransitioning) return;
            conductor.Stop();
            sfx.StopAll();
            input.GameplayEnabled = false;
            flow.RetryStage();
        }

        void ExitToLobby()
        {
            if (flow == null || flow.IsTransitioning) return;
            conductor.Stop();
            sfx.StopAll();
            input.GameplayEnabled = false;
            flow.ExitToLobby();
        }

        void UpdateCameraSize()
        {
            if (stageCamera == null || current < 0) return;
            var minigame = Minigame;
            float size = minigame.CameraSize;
            if (stageCamera.aspect > 0) size = Mathf.Max(size, minigame.MinVisibleHalfWidth / stageCamera.aspect);
            stageCamera.orthographicSize = size;
        }
    }
}
