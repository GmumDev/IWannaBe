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
            /// <summary>
            /// 이 구간 연출로 바꾸는 곡 시각. 구간 시작 박이 기본이고, 입력과 겹치면 입력 사이로 옮겨진다
            /// (<see cref="TimelineSegment.SwitchTime"/>). 첫 구간은 재생 전에 들어온다.
            /// </summary>
            public double SwitchTime;
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
        Judge judge;
        ScoreTracker score;
        readonly List<Segment> segments = new List<Segment>();
        int current = -1;
        /// <summary>노트 Id별로 그 노트를 예고한 큐. 구간이 바뀔 때 이어받는 노트에 붙여 넘긴다.</summary>
        TimelineCue[] cueForNote;
        double endTime;
        double inputOffset;
        double readyUntil;
        int nextCueToSchedule;
        int nextCueToDispatch;
        int nextPatternToSpawn;
        int lastBeat;
        int nextAutoNote;
        TimelineNote autoHold;

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
        /// <summary>플레이용 타임라인. 스테이지 준비가 끝나기 전에는 null. 아래 판정·점수도 마찬가지.</summary>
        public ChartTimeline Timeline => timeline;
        public Judge Judge => judge;
        public ScoreTracker Score => score;
        /// <summary>지금 화면에 있는 구간의 인덱스. 시작 전에는 -1.</summary>
        public int CurrentSegment => current;
        public double InputOffsetSeconds => inputOffset;
        public int SegmentCount => segments.Count;
        /// <summary>곡 중간에 연출을 바꾼 횟수.</summary>
        public int SwitchCount { get; private set; }
        /// <summary>구간이 바뀔 때 새 연출이 이어받은 노트 수(입력이 남아 있던 앞 구간 노트).</summary>
        public int CarriedNoteCount { get; private set; }
        /// <summary>구간 전환 시각과 가장 가까운 입력 사이의 최소 거리(초). 전환이 없으면 양의 무한대.</summary>
        public double MinSwitchClearance { get; private set; } = double.PositiveInfinity;
        /// <summary>
        /// 지금 구간이 아닌 구간의 패턴 준비·큐가 온 횟수. 패턴의 큐가 모두 자기 구간 안에 있다면 0이다.
        /// </summary>
        public int StrayEventCount { get; private set; }

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
            if (!PrepareSegments(tempoMap, out error))
            {
                Fail(error);
                return;
            }
            cueForNote = new TimelineCue[timeline.Notes.Count];
            foreach (var cue in timeline.Cues)
                if (cue.TargetNoteId >= 0 && cueForNote[cue.TargetNoteId] == null) cueForNote[cue.TargetNoteId] = cue;

            judge = new Judge(timeline.Notes, settings.JudgeWindows);
            judge.Judged += OnJudged;
            judge.Whiffed += OnWhiffed;
            score = new ScoreTracker(timeline.JudgementPartCount);
            inputOffset = PlayerCalibration.InputOffsetSeconds;

            conductor.Load(definition.Song.Clip, tempoMap, definition.MusicVolume);
            endTime = Math.Max(definition.Song.Clip.length, timeline.LastEventTime + settings.EndPaddingSeconds);

            hud.SetStageName(definition.DisplayName);
            hud.SetProgress(0);
            EnterSegment(0, 0);

            // 연출 준비가 끝났음을 알리고, 로딩 화면이 완전히 걷힌 뒤에 카운트를 시작한다.
            flow.ReportStagePrepared();
            await UniTask.WaitWhile(() => flow.IsTransitioning, cancellationToken: cancellationToken);

            Debug.Log($"[StageRunner] '{definition.DisplayName}' 시작: 노트 {timeline.Notes.Count}개, 구간 {segments.Count}개, " +
                      $"출력 지연 보정 {conductor.OutputLatency * 1000:0}ms, 입력 보정 {inputOffset * 1000:0}ms" +
                      (autoPlay ? ", 자동 플레이" : string.Empty));
            StartPlayback(0, settings.StartLeadSeconds, "Ready?");
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

                segments.Add(new Segment { Timeline = segment, Minigame = minigame, Presenter = presenter, SwitchTime = segment.SwitchTime });
                MinSwitchClearance = Math.Min(MinSwitchClearance, segment.SwitchClearance);
            }

            if (presenters.Count > 1)
                Debug.Log($"[StageRunner] 연출 {presenters.Count}개 생성, 전체 할당 {Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0):0}MB");
            return true;
        }

        void StartPlayback(double fromTime, double lead, string readyText)
        {
            conductor.Play(fromTime, lead);
            nextCueToSchedule = FirstCueAtOrAfter(fromTime);
            lastBeat = (int)Math.Floor(conductor.SongBeat);
            readyUntil = fromTime;
            hud.ShowCenter(readyText);
            hud.SetPauseButtonVisible(true);
            input.GameplayEnabled = true;
            state = State.Playing;
        }

        void Update()
        {
            if (state != State.Playing) return;

            // 플레이 중에는 패드 A·Enter가 선택된 UI 버튼을 누르지 않도록 선택을 비운다.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

            double time = conductor.SongTime;
            double beat = conductor.SongBeat;
            if (readyUntil > double.MinValue && time >= readyUntil)
            {
                hud.ShowCenter(null);
                readyUntil = double.MinValue;
            }

            AdvanceSegments(time);
            ScheduleCueSounds(time);
            ProcessUntil(time);
            DispatchBeats(beat);
            Presenter.Tick(time, beat);
            hud.SetProgress((float)(time / endTime));
            UpdateCameraSize();

            if (time >= endTime) Finish();
        }

        /// <summary>
        /// 곡 시각 <paramref name="songTime"/>까지 지난 구간 전환을 처리한다. 바꾸기 전에 전환 시각까지의 큐·패턴 준비·판정을
        /// 앞 연출에서 마저 처리해, 프레임이 밀리거나 입력이 먼저 와도 사건이 시간 순서대로 일어나게 한다.
        /// </summary>
        void AdvanceSegments(double songTime)
        {
            while (current + 1 < segments.Count && songTime >= segments[current + 1].SwitchTime)
            {
                double switchTime = segments[current + 1].SwitchTime;
                ProcessUntil(switchTime);
                EnterSegment(current + 1, switchTime);
            }
        }

        /// <summary>곡 시각까지 큐를 연출에 알리고, 패턴을 준비시키고, 자동 입력과 시간이 지난 노트의 Miss를 처리한다.</summary>
        void ProcessUntil(double songTime)
        {
            DispatchCues(songTime);
            SpawnPatterns(songTime);
            if (autoPlay) AutoPlayInputs(songTime - inputOffset);
            judge.Update(songTime - inputOffset);
        }

        /// <summary>
        /// 앞 연출을 내보내고 다음 연출을 들인다. 앞 구간 노트 중 판정이 끝나지 않은 것(날아오는 중, 누르고 있는 홀드)은
        /// 새 연출이 이어받는다. 화면 색·HUD·카메라도 바로 그 미니게임에 맞춘다.
        /// </summary>
        void EnterSegment(int index, double time)
        {
            if (current >= 0) Presenter.ExitSegment();
            current = index;
            var segment = segments[index];
            var carried = CollectCarriedNotes(index);
            segment.Presenter.EnterSegment(segment.Timeline, carried);
            stageCamera.backgroundColor = segment.Minigame.BackgroundColor;
            hud.ApplyTheme(segment.Minigame.HudInkColor);
            UpdateCameraSize();
            if (index == 0) return;

            SwitchCount++;
            CarriedNoteCount += carried.Count;
            var timelineSegment = segment.Timeline;
            Debug.Log($"[StageRunner] 구간 {index + 1}/{segments.Count} '{segment.Minigame.DisplayName}' 시작 " +
                      $"(곡 {time:0.00}s, 구간 시작 박 {timelineSegment.StartBeat:0.##}, 전환 박 {timelineSegment.SwitchBeat:0.##}, " +
                      $"가장 가까운 입력과 {timelineSegment.SwitchClearance * 1000:0}ms, 이어받은 노트 {carried.Count}개)");
        }

        List<CarriedNote> CollectCarriedNotes(int segmentIndex)
        {
            var carried = new List<CarriedNote>();
            foreach (var note in timeline.Notes)
            {
                if (note.Segment >= segmentIndex || judge.IsFinished(note)) continue;
                carried.Add(new CarriedNote(note, cueForNote[note.Id], judge.IsHoldingNote(note)));
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

        void ScheduleCueSounds(double time)
        {
            // 곡 시간은 '들리는 시각' 기준이라, 믹싱은 출력 지연만큼 앞서 일어난다.
            double horizon = time + conductor.OutputLatency + settings.CueScheduleAhead;
            var cues = timeline.Cues;
            while (nextCueToSchedule < cues.Count && cues[nextCueToSchedule].Time <= horizon)
            {
                var cue = cues[nextCueToSchedule++];
                double dsp = conductor.SongTimeToDsp(cue.Time);
                if (dsp < AudioSettings.dspTime - 0.01) continue; // 이미 믹싱 시점이 지난 큐는 울리지 않는다
                // 큐 ID는 그 큐가 속한 구간의 미니게임 안에서 찾는다. 미니게임끼리 같은 ID를 써도 섞이지 않는다.
                if (segments[cue.Segment].Minigame.TryGetCueSound(cue.CueId, out var sound) && sound.clip != null)
                    sfx.PlayScheduled(sound.clip, dsp, sound.volume);
            }
        }

        /// <summary>큐 시각이 된 큐를 연출에 알린다. 다음 구간 큐(구간 시작 박에 딱 맞는 큐 등)는 그 구간 연출이 들어온 뒤에 알린다.</summary>
        void DispatchCues(double time)
        {
            var cues = timeline.Cues;
            while (nextCueToDispatch < cues.Count && cues[nextCueToDispatch].Segment <= current && cues[nextCueToDispatch].Time <= time)
            {
                var cue = cues[nextCueToDispatch++];
                CountIfStray(cue.Segment);
                Presenter.OnCue(cue);
            }
        }

        /// <summary>
        /// 패턴을 첫 큐보다 조금 먼저 준비시킨다. 다음 구간 패턴은 그 구간 연출이 들어온 뒤(구간 시작 박)에 준비시킨다.
        /// 다음 구간 패턴의 큐는 구간 시작 박 이후라 그때 준비해도 늦지 않다.
        /// </summary>
        void SpawnPatterns(double time)
        {
            var patterns = timeline.Patterns;
            while (nextPatternToSpawn < patterns.Count && patterns[nextPatternToSpawn].Segment <= current
                   && patterns[nextPatternToSpawn].StartTime - settings.PatternSpawnLead <= time)
            {
                var pattern = patterns[nextPatternToSpawn++];
                CountIfStray(pattern.Segment);
                Presenter.OnPatternSpawn(pattern);
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

        int FirstCueAtOrAfter(double time)
        {
            var cues = timeline.Cues;
            int i = 0;
            while (i < cues.Count && cues[i].Time < time) i++;
            return i;
        }

        /// <summary>
        /// 판정 시각이 지난 노트를 그 노트의 정확한 시각으로 누르고 뗀다. 프레임이 밀려도 Perfect가 나도록
        /// 판정의 Miss 처리보다 먼저 돈다.
        /// </summary>
        void AutoPlayInputs(double judgeTime)
        {
            var notes = timeline.Notes;
            while (true)
            {
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
            // 입력이 들어온 순간 화면에 있던 연출이 입력을 받도록, 그 시각까지의 구간 전환을 먼저 처리한다.
            double songTime = conductor.RealtimeToSongTime(e.Time);
            AdvanceSegments(songTime);
            double time = songTime - inputOffset;
            Presenter.OnInputPressed(time);
            judge.Press(time, e.Source);
        }

        void OnReleased(RhythmInputEvent e)
        {
            if (state != State.Playing || autoPlay) return;
            double songTime = conductor.RealtimeToSongTime(e.Time);
            AdvanceSegments(songTime);
            double time = songTime - inputOffset;
            Presenter.OnInputReleased(time);
            judge.Release(time, e.Source);
        }

        /// <summary>판정은 지금 화면의 연출로 간다. 앞 구간에서 이어받은 노트도 마찬가지다.</summary>
        void OnJudged(NoteJudgement judgement)
        {
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

        void TogglePause()
        {
            if (state == State.Playing) Pause();
            else if (state == State.Paused) Resume();
        }

        void Pause()
        {
            if (state != State.Playing) return;
            conductor.Pause();
            sfx.StopAll();
            judge.ReleaseHolding(conductor.PausedSongTime - inputOffset);
            autoHold = null;
            input.GameplayEnabled = false;
            state = State.Paused;
            hud.SetPauseButtonVisible(false);
            Presenter.OnPaused(true);
            hud.ShowPause(true);
        }

        void Resume()
        {
            if (state != State.Paused) return;
            hud.ShowPause(false);
            Presenter.OnPaused(false);
            StartPlayback(conductor.PausedSongTime, settings.ResumeLeadSeconds, "계속!");
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

            bool cleared = StageProgress.IsClearingRank(score.Rank);
            // 자동 플레이는 점검용이라 클리어 기록(해금)을 남기지 않는다.
            if (cleared && !autoPlay) StageProgress.MarkCleared(definition.StageId);
            hud.ShowResult(score, cleared);
            Ended?.Invoke(this, score);
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
