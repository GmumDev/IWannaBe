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
    /// 카탈로그의 스테이지를 자동 입력(<see cref="StageRunner.AutoPlay"/>)으로 처음부터 끝까지 차례로 플레이하고 결과를 로그로 남긴다.
    /// 리믹스 구간 전환, 판정·점수 연결, 구간 경계 규칙(엉뚱한 구간으로 간 이벤트 0건)을 사람 손 없이 확인할 때 쓴다.
    /// 플레이 모드에 들어가 로비에서 <see cref="StageFlow"/>로 스테이지를 하나씩 들어가고, 다 끝나면 플레이 모드를 빠져나온다.
    /// 스테이지마다 플레이 중 화면을 한 장씩 Temp/AutoPlayShots에 남긴다(연출·디버그 패널 확인용).
    /// 자동 플레이는 클리어 기록을 남기지 않는다.
    /// </summary>
    [InitializeOnLoad]
    static class StageAutoPlayTest
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 자동 실행한다(스테이지를 다시 구성하지 않고 점검만 할 때).
        const string TriggerFile = "Temp/IWannabe.AutoPlay";
        // 플레이 모드에 들어갈 때 도메인이 다시 로드되므로, 남은 스테이지 목록은 SessionState로 넘긴다.
        const string QueueKey = "IWannabe.AutoPlay.Queue";
        const double RunnerTimeoutSeconds = 60;
        const double StageTimeoutSeconds = 300;
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

        enum Step { Idle, WaitFlow, WaitRunner, Playing, BetweenStages }

        /// <summary>스테이지마다 찍는 순서: 디버그 패널 자세히로 한 장 → 간단히로 바꿈 → 한 장.</summary>
        enum Shot { None, FullTaken, CompactRequested, Done }

        static readonly List<string> results = new List<string>();
        static Step step;
        static StageRunner runner;
        static Conductor conductor;
        static StageDebugPanel panel;
        static double stepStarted;
        static bool needsAttention;
        static Shot shot;
        static double shotStepAt;

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
                        conductor = Object.FindAnyObjectByType<Conductor>();
                        panel = Object.FindAnyObjectByType<StageDebugPanel>();
                        shot = Shot.None;
                        SetStep(Step.Playing);
                        if (runner.HasFailed) OnEnded(runner, null);
                    }
                    else if (elapsed > RunnerTimeoutSeconds) Abort("스테이지 씬이 열리지 않았습니다.");
                    break;
                }

                case Step.Playing:
                    if (runner == null) Abort("스테이지가 끝나기 전에 사라졌습니다.");
                    else if (elapsed > StageTimeoutSeconds) Abort("제한 시간 안에 끝나지 않았습니다.");
                    else UpdateShots();
                    break;

                case Step.BetweenStages:
                    if (elapsed < NextStageDelaySeconds || flow == null || flow.IsTransitioning) break;
                    if (LoadQueue().Count == 0) Complete();
                    else EnterNext(flow);
                    break;
            }
        }

        static void UpdateShots()
        {
            if (shot == Shot.Done || conductor == null || runner.Timeline == null) return;
            double now = EditorApplication.timeSinceStartup;
            switch (shot)
            {
                case Shot.None:
                {
                    var segments = runner.Timeline.Segments;
                    double at = segments.Count > 1 ? segments[1].SwitchTime + ShotAfterSwitchSeconds : ShotSongTime;
                    if (conductor.SongTime < at) return;
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

        static void EnterNext(StageFlow flow)
        {
            var queue = LoadQueue();
            string guid = queue[0];
            queue.RemoveAt(0);
            SaveQueue(queue);
            flow.EnterStage(new StageReference(guid));
            SetStep(Step.WaitRunner);
        }

        static void OnEnded(StageRunner ended, ScoreTracker score)
        {
            ended.Ended -= OnEnded;
            if (step != Step.Playing) return;

            string name = ended.Definition != null ? ended.Definition.DisplayName : "?";
            string line;
            if (score == null)
            {
                needsAttention = true;
                line = $"{name}: 오류로 끝남(위 오류 로그 확인)";
            }
            else
            {
                // 자동 입력은 모든 노트를 정확한 시각에 누르므로 전부 Perfect여야 하고, 구간은 빠짐없이 바뀌어야 하며,
                // 전환은 입력과 겹치지 않아야 한다.
                bool clear = ended.MinSwitchClearance >= MinSwitchClearanceSeconds;
                bool ok = score.Perfect == score.TotalParts && ended.StrayEventCount == 0 && ended.SwitchCount == ended.SegmentCount - 1 && clear;
                needsAttention |= !ok;
                line = $"{name}: {score.Rank} 정확도 {score.Accuracy * 100:0.#}% (Perfect {score.Perfect}/{score.TotalParts}, " +
                       $"아슬아슬 {score.Barely}, Miss {score.Miss}), 구간 {ended.SegmentCount}개 · 전환 {ended.SwitchCount}회 · " +
                       $"이어받은 노트 {ended.CarriedNoteCount}개 · 엉뚱한 구간 이벤트 {ended.StrayEventCount}건";
                if (ended.SegmentCount > 1) line += $" · 전환과 입력 최소 간격 {ended.MinSwitchClearance * 1000:0}ms";
                if (!ok) line += "  ← 확인 필요";
            }
            results.Add(line);
            Debug.Log($"[AutoPlay] {line}");
            SetStep(Step.BetweenStages);
        }

        static void Abort(string reason)
        {
            needsAttention = true;
            results.Add($"중단: {reason}");
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
            if (runner != null) runner.Ended -= OnEnded;
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
