using System;
using System.Collections.Generic;
using System.Text;
using IWannabe.Rhythm;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 카탈로그의 스테이지를 자동 입력(<see cref="StageRunner.AutoPlay"/>)으로 차례로 플레이하고 결과를 로그로 남긴다.
    /// 플레이 모드에 들어가 로비에서 <see cref="StageFlow"/>로 스테이지를 하나씩 들어가고, 다 끝나면 플레이 모드를 빠져나온다.
    /// 스테이지마다 두 가지를 본다.
    /// <list type="number">
    /// <item>처음부터 끝까지 + 일시정지: 판정·점수 연결, 리믹스 구간 전환, 구간 경계 규칙(엉뚱한 구간 이벤트 0건)과 함께,
    /// 곡 중간에 ESC와 같은 처리(<see cref="StageRunner.TogglePause"/>)를 넣어 멈춘 시각과 재개 시각이 같은지 확인한다.
    /// "Ready?" 중(음수 시각), 한 번, 재개 카운트다운 중 0.05초 간격 10회 연타, 홀드 중, 구간 전환 직전에 멈춘다.</item>
    /// <item>시작 지점 재생(<see cref="StageRunner.Restart"/>): 큐와 노트 사이, 패턴 시작에 딱, 홀드 한가운데, 여러 큐 패턴 중간,
    /// 박 사이 아무 곳, 전환 직전·직후, 곡 끝 근처에서 시작해 시작 지점 이후에 시작하는 패턴만 나오는지 확인한다.</item>
    /// </list>
    /// 어느 쪽이든 재생한 패턴은 소리·오브젝트·판정이 모두 나야 하고(<see cref="ChartPlayback"/>의 위반 0건, 빠진 패턴 0개),
    /// 음악 재생 위치와 곡 시계의 차이가 일정해야 한다.
    /// 처음부터 친 판은 플레이 중 화면을 Temp/AutoPlayShots에 남긴다(연출·디버그 패널 확인용). 자동 플레이는 클리어 기록을 남기지 않는다.
    /// </summary>
    [InitializeOnLoad]
    static class StageAutoPlayTest
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 자동 실행한다(스테이지를 다시 구성하지 않고 점검만 할 때).
        const string TriggerFile = "Temp/IWannabe.AutoPlay";
        // 플레이 모드에 들어갈 때 도메인이 다시 로드되므로, 남은 스테이지 목록은 SessionState로 넘긴다.
        const string QueueKey = "IWannabe.AutoPlay.Queue";
        const double RunnerTimeoutSeconds = 60;
        const double StageTimeoutSeconds = 600;
        const double NextStageDelaySeconds = 1.5;
        const string ShotFolder = "Temp/AutoPlayShots";
        /// <summary>미니게임 하나짜리 스테이지를 찍는 곡 시각(초).</summary>
        const double ShotSongTime = 15;
        /// <summary>리믹스는 첫 구간 전환 직후, 이어받은 노트가 새 미니게임 모습으로 날아오는 중일 때 찍는다.</summary>
        const double ShotAfterSwitchSeconds = 0.04;
        /// <summary>구간 전환이 입력과 이보다(초) 가까우면 확인 필요로 본다. 빈틈이 좁아 한가운데로 옮긴 경우까지 허용한다.</summary>
        const double MinSwitchClearanceSeconds = SegmentSwitch.ClearanceSeconds / 2;
        /// <summary>디버그 패널 모드를 바꾸고 다시 그려질 때까지 기다리는 시간(초).</summary>
        const double PanelRedrawSeconds = 0.4;
        /// <summary>일시정지한 채로 두는 시간(실제 초).</summary>
        const double PauseHoldSeconds = 0.3;
        /// <summary>재개 카운트다운 중 ESC 연타 간격(실제 초)과 횟수(짝수라 마지막엔 재개 상태).</summary>
        const double RapidToggleInterval = 0.05;
        const int RapidToggleCount = 10;
        /// <summary>시작 지점 점검에서 시작 지점 뒤로 돌려 보는 곡 시간(초).</summary>
        const double SeekRunSeconds = 10;
        /// <summary>결과 화면을 잠깐 보여 준 뒤 시작 지점 점검으로 넘어가기까지(실제 초).</summary>
        const double SeekStartDelaySeconds = 0.5;
        /// <summary>
        /// 음악 재생 위치와 곡 시계의 차이(음악-시계 차)를 이어서 친 구간(판 시작 또는 재개부터 다음 일시정지까지)마다 재고, 그 중심이
        /// 스테이지 전체(처음부터 판의 재개 전후, 시작 지점 판들)에서 이 폭(초) 안에 모여야 한다. 재개·시작 지점에서 음악이 다른 위치로
        /// 시작하면 그 구간의 중심만 벌어진다. 측정해 보면 중심은 1ms 안에 모인다.
        /// </summary>
        const double MaxCenterSpread = 0.01;
        /// <summary>
        /// 한 구간 안에서 음악-시계 차가 흔들려도 되는 폭(초). 음악 위치는 오디오 버퍼 단위로 띄엄띄엄 갱신돼 50ms 남짓 흔들린다(측정값).
        /// 구간 중간에 음악이 튀는 큰 이상만 잡는다.
        /// </summary>
        const double MaxLegSpread = 0.1;
        /// <summary>음악 차이를 재기 시작하는 시각: 재생을 시작한 시각에서 이만큼(초) 지난 뒤.</summary>
        const double DriftSettleSeconds = 0.2;
        const double Epsilon = 1e-9;

        enum Step { Idle, WaitFlow, WaitRunner, Playing, BetweenStages }

        /// <summary>스테이지마다 처음부터 친 판(일시정지 포함) 다음에 시작 지점 판들을 친다.</summary>
        enum Phase { Full, SeekPending, Seek }

        /// <summary>스테이지마다 찍는 순서: 디버그 패널 자세히로 한 장 → 간단히로 바꿈 → 한 장.</summary>
        enum Shot { None, FullTaken, CompactRequested, Done }

        enum EscStep { Waiting, Paused, CountIn }

        /// <summary>처음부터 친 판에서 일시정지를 넣을 곳.</summary>
        sealed class EscPlan
        {
            public string Label;
            public double At;
            public int RapidToggles;
        }

        static readonly List<string> results = new List<string>();
        static Step step;
        static StageRunner runner;
        static Conductor conductor;
        static StageDebugPanel panel;
        static double stepStarted;
        static bool needsAttention;
        static Shot shot;
        static double shotStepAt;

        static Phase phase;
        static double phaseAt;
        /// <summary>지금 판(처음부터 판 또는 시작 지점 판 하나)에서 나온 문제.</summary>
        static readonly List<string> problems = new List<string>();
        /// <summary>
        /// 이어서 친 구간(판 시작 또는 재개부터 다음 일시정지까지)마다의 음악-시계 차 범위와 중심. 재개할 때 음악이 다른 위치로 시작하면
        /// 구간끼리 중심이 벌어진다.
        /// </summary>
        static double legMin, legMax;
        static string legLabel;
        static readonly List<(string label, double center)> legCenters = new List<(string, double)>();
        /// <summary>이 스테이지의 모든 판에서 잰 구간 중심의 범위.</summary>
        static double stageCenterMin, stageCenterMax;
        static double maxLegSpread;
        static bool musicMissingReported;

        static readonly List<EscPlan> escPlans = new List<EscPlan>();
        static int escIndex;
        static EscStep escStep;
        static double escStepAt;
        static double escOrigin;
        static int togglesDone;
        static bool inPauseCall;
        static int pauseCount;
        static int forcedReleases;
        static double maxResumeGap;

        static readonly List<(string label, double time)> seekPoints = new List<(string, double)>();
        static int seekIndex;
        static string seekLabel;
        static double seekTime;
        static int seekCutPatterns;

        static StageAutoPlayTest()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(TriggerFile)) return;
            System.IO.File.Delete(TriggerFile);
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("IWannabe/Test/Auto Play All Stages")]
        static void RunFromMenu() => Run(true);

        public static void Run(bool askToSave)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[AutoPlay] 플레이 모드에서는 시작할 수 없습니다.");
                return;
            }
            if (askToSave && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(SetupPaths.Catalog);
            var guids = new List<string>();
            if (catalog != null)
                foreach (var entry in catalog.Stages)
                    if (entry.stage != null && entry.stage.RuntimeKeyIsValid()) guids.Add(entry.stage.AssetGUID);
            if (guids.Count == 0)
            {
                Debug.LogError("[AutoPlay] 스테이지 카탈로그가 비어 있습니다. 먼저 스테이지를 구성하세요.");
                return;
            }

            SaveQueue(guids);
            EditorSceneManager.OpenScene(SetupPaths.LobbyScene);
            Debug.Log($"[AutoPlay] 스테이지 {guids.Count}개 자동 플레이 시작");
            EditorApplication.EnterPlaymode();
        }

        static List<string> LoadQueue() =>
            new List<string>(SessionState.GetString(QueueKey, string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

        static void SaveQueue(List<string> queue) => SessionState.SetString(QueueKey, string.Join(";", queue));

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && LoadQueue().Count > 0)
            {
                // 프로젝트 설정은 그대로 두고 이 플레이 세션만, 에디터가 뒤로 가도 멈추지 않게 한다.
                Application.runInBackground = true;
                results.Clear();
                needsAttention = false;
                runner = null;
                SetStep(Step.WaitFlow);
                EditorApplication.update += Pump;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && step != Step.Idle)
            {
                Debug.LogWarning("[AutoPlay] 플레이 모드가 끝나 자동 플레이를 중단합니다.");
                Stop();
            }
        }

        static void Pump()
        {
            if (!EditorApplication.isPlaying) return;
            var flow = StageFlow.Instance;
            double elapsed = EditorApplication.timeSinceStartup - stepStarted;
            switch (step)
            {
                case Step.WaitFlow:
                    if (flow != null && !flow.IsTransitioning) EnterNext(flow);
                    else if (elapsed > RunnerTimeoutSeconds) Abort("로비가 뜨지 않았습니다.");
                    break;

                case Step.WaitRunner:
                {
                    // 앞 스테이지 씬이 아직 남아 있을 수 있으므로 새로 생긴 진행자만 잡는다.
                    var found = Object.FindAnyObjectByType<StageRunner>();
                    if (found != null && found != runner)
                    {
                        runner = found;
                        runner.AutoPlay = true;
                        runner.Ended += OnEnded;
                        runner.NoteJudged += OnJudged;
                        conductor = Object.FindAnyObjectByType<Conductor>();
                        panel = Object.FindAnyObjectByType<StageDebugPanel>();
                        shot = Shot.None;
                        phase = Phase.Full;
                        escPlans.Clear();
                        stageCenterMin = double.PositiveInfinity;
                        stageCenterMax = double.NegativeInfinity;
                        BeginRun();
                        SetStep(Step.Playing);
                        if (runner.HasFailed) OnEnded(runner, null);
                    }
                    else if (elapsed > RunnerTimeoutSeconds) Abort("스테이지 씬이 열리지 않았습니다.");
                    break;
                }

                case Step.Playing:
                    if (runner == null) Abort("스테이지가 끝나기 전에 사라졌습니다.");
                    else if (elapsed > StageTimeoutSeconds) Abort("제한 시간 안에 끝나지 않았습니다.");
                    else if (runner.Timeline != null && conductor != null) UpdatePlaying();
                    break;

                case Step.BetweenStages:
                    if (elapsed < NextStageDelaySeconds || flow == null || flow.IsTransitioning) break;
                    if (LoadQueue().Count == 0) Complete();
                    else EnterNext(flow);
                    break;
            }
        }

        static void UpdatePlaying()
        {
            switch (phase)
            {
                case Phase.Full:
                    if (escPlans.Count == 0) PlanPauses();
                    SampleDrift();
                    UpdateEsc();
                    UpdateShots();
                    break;

                case Phase.SeekPending:
                    if (EditorApplication.timeSinceStartup - phaseAt >= SeekStartDelaySeconds) StartNextSeek();
                    break;

                case Phase.Seek:
                    SampleDrift();
                    if (runner.CurrentState == StageRunner.State.Playing && conductor.SongTime >= seekTime + SeekRunSeconds)
                    {
                        EvaluateSeek(false);
                        StartNextSeek();
                    }
                    break;
            }
        }

        /// <summary>판 하나(처음부터 판 또는 시작 지점 판)를 시작할 때 그 판의 기록을 비운다.</summary>
        static void BeginRun()
        {
            problems.Clear();
            legMin = double.PositiveInfinity;
            legMax = double.NegativeInfinity;
            legLabel = "시작";
            legCenters.Clear();
            maxLegSpread = 0;
            musicMissingReported = false;
        }

        static void Problem(string message)
        {
            if (problems.Count < 12) problems.Add(message);
        }

        // ───────── 음악 위치 ─────────

        /// <summary>
        /// 음악 소스의 재생 위치와 곡 시계의 차이를 모은다. 출력 지연만큼 일정해야 하고, 재개·시작 지점에서 어긋나면 폭이 커진다.
        /// 곡 시계는 프레임 시작 값(<see cref="Conductor.SongTime"/>)이 아니라 지금 시각으로 다시 구한다. 음악 위치는 읽는 순간의 값이라
        /// 프레임 값과 비교하면 프레임 길이만큼 오차가 섞인다. 음악 위치는 오디오 버퍼 단위(약 21ms)로 갱신되므로 그만큼은 흔들린다.
        /// </summary>
        static void SampleDrift()
        {
            if (runner.CurrentState != StageRunner.State.Playing || conductor.IsCountingIn) return;
            double time = conductor.RealtimeToSongTime(Time.realtimeSinceStartupAsDouble);
            if (time < Math.Max(0, conductor.StartSongTime) + DriftSettleSeconds) return;
            double length = runner.Definition.Song.Clip.length;
            if (time > length - 0.5) return;
            double music = conductor.MusicPosition;
            if (double.IsNaN(music))
            {
                if (!musicMissingReported) Problem($"곡 {time:0.000}s에 음악이 재생되지 않음");
                musicMissingReported = true;
                return;
            }
            double drift = music - time;
            legMin = Math.Min(legMin, drift);
            legMax = Math.Max(legMax, drift);
        }

        /// <summary>이어서 친 구간 하나를 마감하고 다음 구간 이름을 정한다(재개가 끝날 때, 판이 끝날 때).</summary>
        static void CloseLeg(string nextLabel)
        {
            if (!double.IsInfinity(legMin))
            {
                double center = (legMin + legMax) / 2;
                legCenters.Add((legLabel, center));
                maxLegSpread = Math.Max(maxLegSpread, legMax - legMin);
                stageCenterMin = Math.Min(stageCenterMin, center);
                stageCenterMax = Math.Max(stageCenterMax, center);
            }
            legMin = double.PositiveInfinity;
            legMax = double.NegativeInfinity;
            legLabel = nextLabel;
        }

        static string DriftLabel()
        {
            if (legCenters.Count == 0) return "음악-시계 차 측정 없음";
            var centers = new StringBuilder();
            foreach (var (name, center) in legCenters)
                centers.Append(centers.Length > 0 ? ", " : string.Empty).Append(legCenters.Count > 1 ? $"{name} " : string.Empty).Append($"{center * 1000:0.0}");
            return $"음악-시계 차 중심 {centers}ms(구간 안 흔들림 최대 {maxLegSpread * 1000:0}ms)";
        }

        /// <summary>판을 마감하며 음악 정렬을 확인한다. 구간 중심은 스테이지 전체에서 모여야 한다(재개·시작 지점에서 음악 위치가 맞다).</summary>
        static void CheckDrift()
        {
            CloseLeg(null);
            if (maxLegSpread > MaxLegSpread)
                Problem($"한 구간 안에서 음악-시계 차가 {maxLegSpread * 1000:0}ms 흔들림({MaxLegSpread * 1000:0}ms 넘음)");
            if (stageCenterMax - stageCenterMin > MaxCenterSpread)
                Problem($"음악-시계 차 중심이 스테이지 안에서 {stageCenterMin * 1000:0.0}~{stageCenterMax * 1000:0.0}ms로 벌어짐({MaxCenterSpread * 1000:0}ms 넘음)");
        }

        // ───────── 일시정지 ─────────

        /// <summary>처음부터 친 판에서 일시정지를 넣을 곳을 정한다.</summary>
        static void PlanPauses()
        {
            var timeline = runner.Timeline;
            double last = timeline.LastEventTime;
            escPlans.Add(new EscPlan { Label = "Ready? 중", At = -0.8 });
            escPlans.Add(new EscPlan { Label = "한 번", At = 0.3 * last });
            escPlans.Add(new EscPlan { Label = $"카운트다운 중 {RapidToggleCount}회 연타", At = 0.45 * last, RapidToggles = RapidToggleCount });
            var hold = FirstNoteAfter(timeline, 0.55 * last, NoteType.Hold);
            if (hold != null)
                escPlans.Add(new EscPlan { Label = "홀드 중", At = (hold.Time + hold.EndTime) / 2 + runner.InputOffsetSeconds });
            if (timeline.Segments.Count > 1)
            {
                var segment = SegmentNear(timeline, 0.8 * last, -1);
                escPlans.Add(new EscPlan { Label = "전환 직전", At = segment.SwitchTime - 0.03, RapidToggles = 2 });
            }
            else
            {
                var pattern = PatternNear(timeline, 0.8 * last, p => p.Notes[0].Cue.Time < p.Notes[0].Time);
                if (pattern != null)
                    escPlans.Add(new EscPlan { Label = "큐와 노트 사이", At = (pattern.Notes[0].Cue.Time + pattern.Notes[0].Time) / 2 });
            }
            escPlans.Sort((a, b) => a.At.CompareTo(b.At));
            escIndex = 0;
            escStep = EscStep.Waiting;
            pauseCount = 0;
            forcedReleases = 0;
            maxResumeGap = 0;
        }

        /// <summary>
        /// 계획한 곳에서 ESC와 같은 처리로 멈추고, 잠깐 뒤 재개한다(연타 계획이면 카운트다운 중에 더 누른다).
        /// 멈춘 시각, 멈춘 동안의 시계, 재개 카운트다운의 시계, 재개 후 시계가 모두 처음 멈춘 시각과 같아야 한다.
        /// </summary>
        static void UpdateEsc()
        {
            if (escIndex >= escPlans.Count) return;
            var plan = escPlans[escIndex];
            double now = EditorApplication.timeSinceStartup;
            switch (escStep)
            {
                case EscStep.Waiting:
                    if (runner.CurrentState != StageRunner.State.Playing || conductor.SongTime < plan.At) return;
                    PressEsc(plan, true);
                    escOrigin = conductor.PausedSongTime;
                    CheckPaused(plan);
                    togglesDone = 0;
                    escStep = EscStep.Paused;
                    escStepAt = now;
                    break;

                case EscStep.Paused:
                    if (now - escStepAt < PauseHoldSeconds) return;
                    Check(Same(conductor.SongTime, escOrigin), plan, $"멈춘 동안 시계가 {conductor.SongTime:0.000}s");
                    PressEsc(plan, false);
                    CheckCountIn(plan);
                    escStep = EscStep.CountIn;
                    escStepAt = now;
                    break;

                case EscStep.CountIn:
                    if (togglesDone < plan.RapidToggles)
                    {
                        if (now - escStepAt < RapidToggleInterval) return;
                        bool pausing = runner.CurrentState == StageRunner.State.Playing;
                        PressEsc(plan, pausing);
                        if (pausing) CheckPaused(plan);
                        else CheckCountIn(plan);
                        togglesDone++;
                        escStepAt = now;
                        return;
                    }
                    if (runner.CurrentState != StageRunner.State.Playing || conductor.IsCountingIn) return;
                    double gap = conductor.SongTime - escOrigin;
                    Check(gap >= -Epsilon && gap < 0.1, plan, $"재개 후 시계가 {conductor.SongTime:0.000}s(멈춘 시각 {escOrigin:0.000}s)");
                    maxResumeGap = Math.Max(maxResumeGap, gap);
                    CloseLeg($"{plan.Label} 뒤");
                    escIndex++;
                    escStep = EscStep.Waiting;
                    break;
            }
        }

        static void PressEsc(EscPlan plan, bool pausing)
        {
            inPauseCall = pausing;
            runner.TogglePause();
            inPauseCall = false;
            if (pausing) pauseCount++;
            var expected = pausing ? StageRunner.State.Paused : StageRunner.State.Playing;
            Check(runner.CurrentState == expected, plan, $"ESC 뒤 상태가 {runner.CurrentState}");
        }

        /// <summary>멈춘 직후: 멈춘 시각이 처음 멈춘 시각과 같고, 그 시각까지의 사건을 모두 처리했다.</summary>
        static void CheckPaused(EscPlan plan)
        {
            Check(Same(conductor.PausedSongTime, escOrigin), plan, $"멈춘 시각 {conductor.PausedSongTime:0.000}s가 처음 멈춘 시각 {escOrigin:0.000}s와 다름");
            Check(Same(runner.ProcessedTime, escOrigin), plan, $"멈출 때 처리한 시각 {runner.ProcessedTime:0.000}s가 멈춘 시각과 다름");
        }

        /// <summary>재개 직후: 시계가 멈춘 시각에 서서 카운트다운하고, 음악도 그 시각부터 시작한다.</summary>
        static void CheckCountIn(EscPlan plan)
        {
            Check(conductor.IsCountingIn, plan, "재개 뒤 카운트다운이 없음");
            Check(Same(conductor.SongTime, escOrigin) && Same(conductor.StartSongTime, escOrigin), plan,
                $"재개 카운트다운 시계 {conductor.SongTime:0.000}s, 시작 시각 {conductor.StartSongTime:0.000}s가 멈춘 시각 {escOrigin:0.000}s와 다름");
        }

        static bool Same(double a, double b) => Math.Abs(a - b) <= Epsilon;

        static void Check(bool ok, EscPlan plan, string message)
        {
            if (!ok) Problem($"[{plan.Label}] {message}");
        }

        /// <summary>
        /// 판정은 모두 Perfect여야 한다. 예외는 홀드를 누르는 중에 일시정지해서 그 시각에 강제로 뗀 판정 하나뿐이고,
        /// 그 판정의 오차는 멈춘 시각 - 뗄 시각이어야 한다.
        /// </summary>
        static void OnJudged(NoteJudgement judgement)
        {
            if (judgement.Grade == JudgeGrade.Perfect) return;
            if (inPauseCall && judgement.Phase == NotePhase.Release)
            {
                double expected = conductor.PausedSongTime - runner.InputOffsetSeconds - judgement.Note.EndTime;
                if (Math.Abs(judgement.Delta - expected) <= 1e-6)
                {
                    forcedReleases++;
                    return;
                }
            }
            Problem($"노트 #{judgement.Note.Id}({judgement.Note.Time:0.000}s) {judgement.Phase} {judgement.Grade}" +
                    (judgement.HasInput ? $" {judgement.Delta * 1000:+0;-0}ms" : string.Empty));
        }

        // ───────── 시작 지점 ─────────

        /// <summary>시작 지점 판들을 칠 곳. 마지막은 곡 끝까지 친다.</summary>
        static void PlanSeeks()
        {
            seekPoints.Clear();
            var timeline = runner.Timeline;
            double last = timeline.LastEventTime;

            // 예고 큐와 노트 사이에서 시작하면 그 패턴은 노트가 시작 지점 뒤에 있어도 통째로 빠진다.
            var cut = PatternNear(timeline, 0.2 * last, p => p.Notes[0].Cue.Time < p.Notes[0].Time);
            if (cut != null) seekPoints.Add(("큐와 노트 사이", (cut.StartTime + cut.Notes[0].Time) / 2));
            // 시작 지점에 딱 맞게 시작하는 패턴은 들어간다.
            var exact = PatternNear(timeline, 0.35 * last, p => true);
            if (exact != null) seekPoints.Add(("패턴 시작에 딱", exact.StartTime));
            var hold = FirstNoteAfter(timeline, 0.45 * last, NoteType.Hold) ?? FirstNoteAfter(timeline, 0, NoteType.Hold);
            if (hold != null) seekPoints.Add(("홀드 한가운데", (hold.Time + hold.EndTime) / 2));
            var multi = PatternNear(timeline, 0.55 * last, p => p.Cues.Count >= 2 && p.Cues[1].Time > p.Cues[0].Time);
            if (multi != null) seekPoints.Add(("여러 큐 패턴 중간", (multi.Cues[0].Time + multi.Cues[1].Time) / 2));
            seekPoints.Add(("박 사이 아무 곳", 0.62 * last + 0.123));
            if (timeline.Segments.Count > 1)
            {
                var before = SegmentNear(timeline, 0.7 * last, -1);
                seekPoints.Add(("전환 직전", before.SwitchTime - 0.3));
                var after = SegmentNear(timeline, 0.85 * last, before.Index);
                seekPoints.Add(("전환 직후", after.SwitchTime + 0.05));
            }
            var lastPattern = timeline.Patterns[timeline.Patterns.Count - 1];
            seekPoints.Add(("곡 끝까지", Math.Max(0, lastPattern.StartTime - 0.5)));
            seekIndex = -1;
        }

        /// <summary>
        /// 다음 시작 지점부터 판을 다시 시작하고, 곧바로 재생 목록을 확인한다: 시작 지점 이후에 시작하는 패턴과 정확히 같고,
        /// 빠진 패턴의 노트는 모두 판정에서 빠지고, 시작 지점의 구간 연출이 화면에 있어야 한다.
        /// </summary>
        static void StartNextSeek()
        {
            seekIndex++;
            if (seekIndex >= seekPoints.Count)
            {
                FinishStage();
                return;
            }
            (seekLabel, seekTime) = seekPoints[seekIndex];
            phase = Phase.Seek;
            BeginRun();
            runner.Restart(seekTime);

            var timeline = runner.Timeline;
            var playback = runner.Playback;
            if (!Same(runner.StartTime, seekTime)) Problem($"시작 지점이 {runner.StartTime:0.000}s");
            int expectedSegment = 0;
            while (expectedSegment + 1 < timeline.Segments.Count && timeline.Segments[expectedSegment + 1].SwitchTime <= seekTime) expectedSegment++;
            if (runner.CurrentSegment != expectedSegment) Problem($"시작 구간이 {runner.CurrentSegment + 1}(기대 {expectedSegment + 1})");

            int expectedIndex = 0;
            int expectedParts = 0;
            seekCutPatterns = 0;
            foreach (var pattern in timeline.Patterns)
            {
                bool expected = pattern.StartTime >= seekTime - 1e-6;
                if (expected)
                {
                    if (expectedIndex >= playback.Patterns.Count || playback.Patterns[expectedIndex] != pattern)
                        Problem($"패턴 '{pattern.PatternId}'({pattern.StartTime:0.000}s)이 재생 목록 {expectedIndex}번째에 없음");
                    expectedIndex++;
                    foreach (var note in pattern.Notes) expectedParts += note.Type == NoteType.Hold ? 2 : 1;
                    continue;
                }
                if (playback.Includes(pattern)) Problem($"시작 지점 앞 패턴 '{pattern.PatternId}'({pattern.StartTime:0.000}s)이 재생 목록에 있음");
                if (pattern.EndTime >= seekTime) seekCutPatterns++;
                foreach (var note in pattern.Notes)
                    if (!runner.Judge.IsSkipped(note)) Problem($"시작 지점 앞 패턴의 노트 #{note.Id}가 판정에서 빠지지 않음");
            }
            if (expectedIndex != playback.Patterns.Count) Problem($"재생 목록 패턴 {playback.Patterns.Count}개(기대 {expectedIndex}개)");
            if (runner.Score.TotalParts != expectedParts) Problem($"판정 단위 {runner.Score.TotalParts}개(기대 {expectedParts}개)");
        }

        /// <summary>시작 지점 판 하나를 마무리한다. 판정이 끝났어야 할 패턴은 소리·오브젝트·판정이 모두 났어야 한다.</summary>
        static void EvaluateSeek(bool ended)
        {
            var playback = runner.Playback;
            var score = runner.Score;
            var details = new List<string>();
            var windows = runner.JudgeWindows;
            double window = Math.Max(Math.Max(windows.barely, windows.releaseBarely), 0);
            int incomplete = ended
                ? runner.IncompletePatternCount
                : playback.CountIncomplete(runner.ProcessedTime - runner.InputOffsetSeconds, window, details);
            if (incomplete > 0) Problem($"빠진 것이 있는 패턴 {incomplete}개" + (details.Count > 0 ? $": {details[0]}" : string.Empty));
            CheckPlayback(playback);
            if (score.Perfect != score.JudgedParts) Problem($"판정 {score.JudgedParts}개 중 Perfect {score.Perfect}개");
            if (ended && score.JudgedParts != score.TotalParts) Problem($"끝났는데 판정 {score.JudgedParts}/{score.TotalParts}");
            if (playback.SpawnedPatternCount == 0) Problem("나온 패턴이 없음");
            CheckDrift();

            string name = runner.Definition.DisplayName;
            string line = $"{name} 시작 지점 '{seekLabel}' {seekTime:0.000}s부터{(ended ? " 끝까지" : $" {SeekRunSeconds:0}초")}: " +
                          $"패턴 {playback.Patterns.Count}/{runner.Timeline.Patterns.Count}개 재생(걸쳐서 빠진 패턴 {seekCutPatterns}개), " +
                          $"시작 구간 {CurrentSegmentLabel()}, 나온 패턴 {playback.SpawnedPatternCount}개 · 큐 소리 {playback.SoundedCueCount}개 · " +
                          $"판정 Perfect {score.Perfect}/{score.JudgedParts}, 묶음 위반 {playback.ViolationCount}건, 빠진 패턴 {incomplete}개, {DriftLabel()}";
            Report(line);
        }

        /// <summary>시작 지점 판에서 처음 들인 구간(지금은 이미 지나갔을 수 있으므로 시작 지점으로 다시 구한다).</summary>
        static string CurrentSegmentLabel()
        {
            var timeline = runner.Timeline;
            int index = 0;
            while (index + 1 < timeline.Segments.Count && timeline.Segments[index + 1].SwitchTime <= seekTime) index++;
            return $"{index + 1}/{timeline.Segments.Count}";
        }

        // ───────── 판이 끝났을 때 ─────────

        static void OnEnded(StageRunner ended, ScoreTracker score)
        {
            if (step != Step.Playing || ended != runner) return;

            if (score == null)
            {
                needsAttention = true;
                string name = ended.Definition != null ? ended.Definition.DisplayName : "?";
                Report($"{name}: 오류로 끝남(위 오류 로그 확인)", false);
                FinishStage();
                return;
            }

            if (phase == Phase.Full)
            {
                EvaluateFull(score);
                PlanSeeks();
                // 결과 화면을 잠깐 보여 준 뒤 다음 프레임 밖에서 시작 지점 판을 시작한다(끝나는 처리 도중에 다시 시작하지 않는다).
                phase = Phase.SeekPending;
                phaseAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (phase == Phase.Seek)
            {
                EvaluateSeek(true);
                phase = Phase.SeekPending;
                phaseAt = EditorApplication.timeSinceStartup;
            }
        }

        /// <summary>
        /// 처음부터 친 판(일시정지 포함). 자동 입력은 모든 노트를 정확한 시각에 누르므로 홀드 중 일시정지로 뗀 것 말고는 전부 Perfect여야 하고,
        /// 구간은 빠짐없이 바뀌어야 하며, 전환은 입력과 겹치지 않아야 한다. 일시정지를 넣은 곳을 모두 지나야 하고, 재생한 패턴은
        /// 소리·오브젝트·판정이 모두 났어야 한다.
        /// </summary>
        static void EvaluateFull(ScoreTracker score)
        {
            var playback = runner.Playback;
            if (!playback.IsFullPlay) Problem("처음부터 친 판인데 빠진 패턴이 있음");
            if (score.Perfect + forcedReleases != score.TotalParts)
                Problem($"Perfect {score.Perfect} + 일시정지로 뗀 홀드 {forcedReleases} ≠ 판정 단위 {score.TotalParts}");
            if (runner.IncompletePatternCount > 0) Problem($"빠진 것이 있는 패턴 {runner.IncompletePatternCount}개");
            CheckPlayback(playback);
            if (runner.StrayEventCount != 0) Problem($"엉뚱한 구간 이벤트 {runner.StrayEventCount}건");
            if (runner.SwitchCount != runner.SegmentCount - 1) Problem($"전환 {runner.SwitchCount}회(구간 {runner.SegmentCount}개)");
            if (runner.MinSwitchClearance < MinSwitchClearanceSeconds) Problem($"전환과 입력 최소 간격 {runner.MinSwitchClearance * 1000:0}ms");
            if (escIndex < escPlans.Count) Problem($"일시정지 계획 {escPlans.Count}곳 중 {escIndex}곳만 지남");
            CheckDrift();

            var labels = new StringBuilder();
            foreach (var plan in escPlans) labels.Append(labels.Length > 0 ? ", " : string.Empty).Append(plan.Label);
            string line = $"{runner.Definition.DisplayName} 처음부터+일시정지: {score.Rank} 정확도 {score.Accuracy * 100:0.#}% " +
                          $"(Perfect {score.Perfect}/{score.TotalParts}, 일시정지로 뗀 홀드 {forcedReleases}, Miss {score.Miss}), " +
                          $"구간 {runner.SegmentCount}개 · 전환 {runner.SwitchCount}회 · 이어받은 노트 {runner.CarriedNoteCount}개 · 엉뚱한 구간 이벤트 {runner.StrayEventCount}건";
            if (runner.SegmentCount > 1) line += $" · 전환과 입력 최소 간격 {runner.MinSwitchClearance * 1000:0}ms";
            line += $" · 일시정지 {pauseCount}회({labels}) · 재개 시각-멈춘 시각 최대 {maxResumeGap * 1000:0.0}ms · " +
                    $"패턴 {playback.Patterns.Count}개 · 큐 소리 {playback.SoundedCueCount}/{playback.Cues.Count} · 묶음 위반 {playback.ViolationCount}건 · " +
                    $"늦은 큐 소리 {playback.LateSoundCount}개 · {DriftLabel()}";
            Report(line);
        }

        static void CheckPlayback(ChartPlayback playback)
        {
            if (playback.ViolationCount > 0) Problem($"묶음 위반 {playback.ViolationCount}건: {playback.Violations[0]}");
            if (playback.LateSoundCount > 0) Problem($"늦은 큐 소리 {playback.LateSoundCount}개: {playback.FirstLateSound}");
        }

        static void Report(string line, bool includeProblems = true)
        {
            if (includeProblems && problems.Count > 0)
            {
                needsAttention = true;
                line += "  ← 확인 필요: " + string.Join(" / ", problems);
            }
            results.Add(line);
            Debug.Log($"[AutoPlay] {line}");
        }

        /// <summary>이 스테이지의 점검을 끝내고 다음 스테이지로 넘어간다.</summary>
        static void FinishStage()
        {
            if (runner != null)
            {
                runner.Ended -= OnEnded;
                runner.NoteJudged -= OnJudged;
            }
            SetStep(Step.BetweenStages);
        }

        // ───────── 채보에서 점검할 곳 찾기 ─────────

        /// <summary>곡 시각 <paramref name="time"/>에서 가장 가까이 시작하는, 조건에 맞는 패턴.</summary>
        static TimelinePattern PatternNear(ChartTimeline timeline, double time, Func<TimelinePattern, bool> match)
        {
            TimelinePattern best = null;
            foreach (var pattern in timeline.Patterns)
            {
                if (pattern.Notes.Count == 0 || !match(pattern)) continue;
                if (best == null || Math.Abs(pattern.StartTime - time) < Math.Abs(best.StartTime - time)) best = pattern;
            }
            return best;
        }

        static TimelineNote FirstNoteAfter(ChartTimeline timeline, double time, NoteType type)
        {
            foreach (var note in timeline.Notes)
                if (note.Type == type && note.Time >= time) return note;
            return null;
        }

        /// <summary>곡 시각에서 가장 가까운 구간 전환(첫 구간 제외, <paramref name="except"/> 구간 제외).</summary>
        static TimelineSegment SegmentNear(ChartTimeline timeline, double time, int except)
        {
            TimelineSegment best = null;
            for (int i = 1; i < timeline.Segments.Count; i++)
            {
                var segment = timeline.Segments[i];
                if (segment.Index == except) continue;
                if (best == null || Math.Abs(segment.SwitchTime - time) < Math.Abs(best.SwitchTime - time)) best = segment;
            }
            return best;
        }

        // ───────── 화면 ─────────

        static void UpdateShots()
        {
            if (shot == Shot.Done || runner.Timeline == null) return;
            double now = EditorApplication.timeSinceStartup;
            switch (shot)
            {
                case Shot.None:
                {
                    var segments = runner.Timeline.Segments;
                    double at = segments.Count > 1 ? segments[1].SwitchTime + ShotAfterSwitchSeconds : ShotSongTime;
                    if (runner.CurrentState != StageRunner.State.Playing || conductor.IsCountingIn || conductor.SongTime < at) return;
                    if (panel != null) panel.CurrentMode = StageDebugPanel.Mode.Full;
                    TakeShot("full");
                    shot = panel != null ? Shot.FullTaken : Shot.Done;
                    break;
                }
                case Shot.FullTaken:
                    if (now - shotStepAt < PanelRedrawSeconds) return;
                    panel.CurrentMode = StageDebugPanel.Mode.Compact;
                    shot = Shot.CompactRequested;
                    break;
                case Shot.CompactRequested:
                    if (now - shotStepAt < PanelRedrawSeconds) return;
                    TakeShot("compact");
                    shot = Shot.Done;
                    break;
            }
            shotStepAt = now;
        }

        static void TakeShot(string suffix)
        {
            System.IO.Directory.CreateDirectory(ShotFolder);
            string path = $"{ShotFolder}/{(runner.Definition != null ? runner.Definition.StageId : "stage")}_{suffix}.png";
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[AutoPlay] 화면 저장: {path} (곡 {conductor.SongTime:0.00}s)");
        }

        // ───────── 흐름 ─────────

        static void EnterNext(StageFlow flow)
        {
            var queue = LoadQueue();
            string guid = queue[0];
            queue.RemoveAt(0);
            SaveQueue(queue);
            flow.EnterStage(new StageReference(guid));
            SetStep(Step.WaitRunner);
        }

        static void Abort(string reason)
        {
            needsAttention = true;
            results.Add($"중단: {reason}" + (problems.Count > 0 ? " / " + string.Join(" / ", problems) : string.Empty));
            Complete();
        }

        static void Complete()
        {
            var report = new StringBuilder($"[AutoPlay] 자동 플레이 {(needsAttention ? "끝 - 확인 필요" : "통과")}");
            foreach (var line in results) report.Append("\n  ").Append(line);
            if (needsAttention) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
            Stop();
            EditorApplication.ExitPlaymode();
        }

        static void Stop()
        {
            EditorApplication.update -= Pump;
            SessionState.EraseString(QueueKey);
            if (runner != null)
            {
                runner.Ended -= OnEnded;
                runner.NoteJudged -= OnJudged;
            }
            runner = null;
            step = Step.Idle;
        }

        static void SetStep(Step next)
        {
            step = next;
            stepStarted = EditorApplication.timeSinceStartup;
        }
    }
}
