using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 플레이 씬의 진행자. <see cref="StageFlow"/>가 로드해 둔 스테이지로 연출 프리팹을 띄우고,
    /// 곡 시간에 맞춰 큐 효과음 예약·연출 이벤트·판정을 돌린다. 콘텐츠 로드·언로드는 하지 않는다.
    /// </summary>
    public sealed class StageRunner : MonoBehaviour
    {
        enum State { Loading, Playing, Paused, Finished, Failed }

        [SerializeField] RhythmSettings settings;
        [SerializeField] Conductor conductor;
        [SerializeField] SfxPlayer sfx;
        [SerializeField] RhythmInput input;
        [SerializeField] StageHud hud;
        [SerializeField] Camera stageCamera;
        [SerializeField] Transform stageRoot;

        [Tooltip("에디터에서 로비를 거치지 않고 이 씬을 바로 플레이할 때 불러올 스테이지.")]
        [SerializeField] StageReference fallbackStage;

        State state = State.Loading;
        StageFlow flow;
        StageDefinition definition;
        StagePresenter presenter;
        ChartTimeline timeline;
        Judge judge;
        ScoreTracker score;
        double endTime;
        double inputOffset;
        double readyUntil;
        int nextCueToSchedule;
        int nextCueToDispatch;
        int nextPatternToSpawn;
        int lastBeat;

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
            timeline = ChartTimeline.Build(definition.Chart.Patterns, tempoMap);
            judge = new Judge(timeline.Notes, settings.JudgeWindows);
            judge.Judged += OnJudged;
            judge.Whiffed += OnWhiffed;
            score = new ScoreTracker(timeline.JudgementPartCount);
            inputOffset = PlayerCalibration.InputOffsetSeconds;

            conductor.Load(definition.Song.Clip, tempoMap, definition.MusicVolume);
            endTime = Math.Max(definition.Song.Clip.length, timeline.LastEventTime + settings.EndPaddingSeconds);

            stageCamera.backgroundColor = definition.BackgroundColor;
            UpdateCameraSize();
            presenter = Instantiate(definition.PresenterPrefab, stageRoot);
            presenter.Bind(new StageContext(definition, timeline, tempoMap, conductor, sfx));

            hud.SetStageName(definition.DisplayName);
            hud.ApplyTheme(definition.HudInkColor);
            hud.SetProgress(0);

            // 연출 준비가 끝났음을 알리고, 로딩 화면이 완전히 걷힌 뒤에 카운트를 시작한다.
            flow.ReportStagePrepared();
            await UniTask.WaitWhile(() => flow.IsTransitioning, cancellationToken: cancellationToken);

            Debug.Log($"[StageRunner] '{definition.DisplayName}' 시작: 노트 {timeline.Notes.Count}개, " +
                      $"출력 지연 보정 {conductor.OutputLatency * 1000:0}ms, 입력 보정 {inputOffset * 1000:0}ms");
            StartPlayback(0, settings.StartLeadSeconds, "Ready?");
        }

        static bool Validate(StageDefinition stage, out string error)
        {
            error = null;
            if (stage.Song == null || stage.Song.Clip == null) error = "곡(SongData)이 없습니다.";
            else if (!stage.Song.HasBeatMap) error = "곡 분석 결과(비트 지도)가 없습니다. 채보 생성 프로필에서 분석을 실행하세요.";
            else if (stage.Chart == null) error = "채보(ChartData)가 없습니다.";
            else if (stage.Chart.Patterns.Count == 0) error = "채보가 비어 있습니다. 채보 생성 프로필에서 생성을 실행하세요.";
            else if (stage.PresenterPrefab == null) error = "연출 프리팹이 없습니다.";
            return error == null;
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

            ScheduleCueSounds(time);
            DispatchCues(time);
            SpawnPatterns(time);
            judge.Update(time - inputOffset);
            DispatchBeats(beat);
            presenter.Tick(time, beat);
            hud.SetProgress((float)(time / endTime));
            UpdateCameraSize();

            if (time >= endTime) Finish();
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
                if (definition.TryGetCueSound(cue.CueId, out var sound) && sound.clip != null)
                    sfx.PlayScheduled(sound.clip, dsp, sound.volume);
            }
        }

        void DispatchCues(double time)
        {
            var cues = timeline.Cues;
            while (nextCueToDispatch < cues.Count && cues[nextCueToDispatch].Time <= time)
                presenter.OnCue(cues[nextCueToDispatch++]);
        }

        void SpawnPatterns(double time)
        {
            var patterns = timeline.Patterns;
            while (nextPatternToSpawn < patterns.Count && patterns[nextPatternToSpawn].StartTime - settings.PatternSpawnLead <= time)
                presenter.OnPatternSpawn(patterns[nextPatternToSpawn++]);
        }

        void DispatchBeats(double beat)
        {
            int current = (int)Math.Floor(beat);
            while (lastBeat < current)
            {
                lastBeat++;
                if (lastBeat >= 0) presenter.OnBeat(lastBeat);
            }
        }

        int FirstCueAtOrAfter(double time)
        {
            var cues = timeline.Cues;
            int i = 0;
            while (i < cues.Count && cues[i].Time < time) i++;
            return i;
        }

        void OnPressed(RhythmInputEvent e)
        {
            if (state != State.Playing) return;
            double time = conductor.RealtimeToSongTime(e.Time) - inputOffset;
            presenter.OnInputPressed(time);
            judge.Press(time, e.Source);
        }

        void OnReleased(RhythmInputEvent e)
        {
            if (state != State.Playing) return;
            double time = conductor.RealtimeToSongTime(e.Time) - inputOffset;
            presenter.OnInputReleased(time);
            judge.Release(time, e.Source);
        }

        void OnJudged(NoteJudgement judgement)
        {
            score.Add(judgement);
            presenter.OnJudged(judgement);
            hud.ShowJudgement(judgement);
        }

        void OnWhiffed(double time) => presenter.OnWhiff(time);

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
            input.GameplayEnabled = false;
            state = State.Paused;
            hud.SetPauseButtonVisible(false);
            presenter.OnPaused(true);
            hud.ShowPause(true);
        }

        void Resume()
        {
            if (state != State.Paused) return;
            hud.ShowPause(false);
            presenter.OnPaused(false);
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
            presenter.OnStageFinished(score);

            bool cleared = StageProgress.IsClearingRank(score.Rank);
            if (cleared) StageProgress.MarkCleared(definition.StageId);
            hud.ShowResult(score, cleared);
        }

        void Fail(string message)
        {
            state = State.Failed;
            input.GameplayEnabled = false;
            Debug.LogError($"[StageRunner] {message}");
            hud.ShowError(message);
            // 로딩 화면이 오류 메시지를 가리지 않도록 걷는다.
            if (flow != null) flow.ReportStagePrepared();
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
            if (stageCamera == null || definition == null) return;
            float size = definition.CameraSize;
            if (stageCamera.aspect > 0) size = Mathf.Max(size, definition.MinVisibleHalfWidth / stageCamera.aspect);
            stageCamera.orthographicSize = size;
        }
    }
}
