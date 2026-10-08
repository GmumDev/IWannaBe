using System;
using System.Collections.Generic;
using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace IWannabe.App
{
    /// <summary>
    /// 프레임 점검: 로비를 거쳐(<see cref="StageFlow"/>가 로비 씬의 AppRoot에 있다) 카탈로그의 스테이지를 차례로 자동 플레이하고,
    /// 플레이 중 프레임 시간·GC·메모리를 잰 뒤 기기 점검 씬으로 돌아온다. 디버그 패널은 그리는 비용이 섞이지 않게 숨긴다.
    /// </summary>
    public sealed class FrameCheckRunner : MonoBehaviour
    {
        const float NextStageDelaySeconds = 1.5f;

        static readonly List<string> results = new List<string>();
        static FrameCheckRunner active;

        readonly List<StageCatalogEntry> stages = new List<StageCatalogEntry>();
        readonly List<float> frames = new List<float>(20000);
        string lobbyScene, returnScene;
        int index = -1;
        StageRunner runner;
        int gcAtStart;
        float nextAt = -1f;
        bool returning;

        public static IReadOnlyList<string> Results => results;

        public static void Begin(StageCatalog catalog, string lobbySceneName, string returnSceneName)
        {
            if (active != null || catalog == null) return;
            var go = new GameObject("FrameCheckRunner");
            DontDestroyOnLoad(go);
            active = go.AddComponent<FrameCheckRunner>();
            active.lobbyScene = lobbySceneName;
            active.returnScene = returnSceneName;
            active.stages.AddRange(catalog.Stages);
            results.Clear();
            DeviceCheckLog.Add($"프레임 점검 시작: 스테이지 {active.stages.Count}개, 목표 {Application.targetFrameRate}fps, vSync {QualitySettings.vSyncCount}, " +
                               $"화면 {Screen.currentResolution.refreshRateRatio.value:0.#}Hz");
            SceneManager.LoadScene(lobbySceneName);
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == lobbyScene)
            {
                if (returning) SceneManager.LoadScene(returnScene);
                else if (index < 0) nextAt = Time.realtimeSinceStartup + NextStageDelaySeconds;
                return;
            }

            var found = FindAnyObjectByType<StageRunner>();
            if (found == null) return;
            runner = found;
            runner.AutoPlay = true;
            runner.Started += OnStarted;
            runner.Ended += OnEnded;
            var panel = FindAnyObjectByType<StageDebugPanel>();
            if (panel != null) panel.CurrentMode = StageDebugPanel.Mode.Hidden;
        }

        void OnStarted(StageRunner _)
        {
            frames.Clear();
            gcAtStart = GC.CollectionCount(0);
        }

        void Update()
        {
            if (runner != null && runner.CurrentState == StageRunner.State.Playing) frames.Add(Time.unscaledDeltaTime);
            if (nextAt >= 0f && Time.realtimeSinceStartup >= nextAt && StageFlow.Instance != null && !StageFlow.Instance.IsTransitioning)
            {
                nextAt = -1f;
                EnterNext();
            }
        }

        void OnEnded(StageRunner ended, ScoreTracker score)
        {
            ended.Started -= OnStarted;
            ended.Ended -= OnEnded;
            string name = index < stages.Count ? stages[index].displayName : ended.name;
            string line = score == null ? $"{name}: 오류로 끝남" : $"{name}: {Describe(frames)}, GC {GC.CollectionCount(0) - gcAtStart}회, " +
                $"할당 {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0}MB, Mono {Profiler.GetMonoUsedSizeLong() / 1048576f:0}MB";
            results.Add(line);
            DeviceCheckLog.Add($"프레임 {line}");
            runner = null;
            nextAt = Time.realtimeSinceStartup + NextStageDelaySeconds;
        }

        void EnterNext()
        {
            index++;
            var flow = StageFlow.Instance;
            if (index < stages.Count && stages[index].stage != null && stages[index].stage.RuntimeKeyIsValid())
            {
                flow.EnterStage(stages[index].stage);
                return;
            }
            DeviceCheckLog.Add("프레임 점검 끝");
            returning = true;
            flow.ExitToLobby();
        }

        void OnDestroy()
        {
            if (active == this) active = null;
        }

        void LateUpdate()
        {
            // 기기 점검 씬으로 돌아오면 할 일이 끝난다.
            if (returning && SceneManager.GetActiveScene().name == returnScene) Destroy(gameObject);
        }

        /// <summary>평균 fps, 프레임 시간 중앙·99%·최대, 중앙값의 1.5배를 넘은 프레임(끊김) 수.</summary>
        static string Describe(List<float> frameTimes)
        {
            if (frameTimes.Count < 10) return "프레임 기록 부족";
            var sorted = frameTimes.ToArray();
            Array.Sort(sorted);
            double sum = 0;
            foreach (var f in sorted) sum += f;
            float median = sorted[sorted.Length / 2];
            float p99 = sorted[(int)(0.99 * (sorted.Length - 1))];
            int hitches = 0;
            foreach (var f in sorted)
                if (f > median * 1.5f) hitches++;
            return $"평균 {sorted.Length / sum:0.0}fps, 프레임 중앙 {median * 1000:0.0}ms·99% {p99 * 1000:0.0}ms·최대 {sorted[sorted.Length - 1] * 1000:0.0}ms, " +
                   $"끊김(중앙×1.5 초과) {hitches}/{sorted.Length}";
        }
    }
}
