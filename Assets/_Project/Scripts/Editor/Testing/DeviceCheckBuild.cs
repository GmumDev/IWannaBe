using System;
using System.Diagnostics;
using System.IO;
using IWannabe.App;
using IWannabe.Rhythm;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 기기 점검(<see cref="DeviceCheck"/>) 씬 만들기와 안드로이드 개발 빌드.
    /// 기기 점검 씬은 빌드 설정(EditorBuildSettings)에 넣지 않고 이 빌드에서만 첫 씬으로 넣는다. 릴리스 빌드에는 들어가지 않는다.
    /// 빌드 대상이 안드로이드가 아니면 먼저 바꾸고(에셋을 다시 임포트한다), 스크립트가 다시 로드된 뒤 이어서 빌드한다.
    /// 결과는 [DeviceCheckBuild] 로그로 남는다.
    /// </summary>
    [InitializeOnLoad]
    static class DeviceCheckBuild
    {
        // 토큰: scene(씬 다시 만들기), smoke(에디터에서 잠깐 돌려 보기), build(APK 빌드), install(빌드 후 연결된 기기에 설치·실행).
        const string TriggerFile = "Temp/IWannabe.DeviceCheck";
        const string ScenePath = SetupPaths.Scenes + "/DeviceCheck.unity";
        const string ApkPath = "Builds/Android/IWannabe-DeviceCheck.apk";
        // 플레이 모드에 들어가면 도메인이 다시 로드되므로 스모크 진행 여부는 SessionState로 넘긴다.
        // 빌드 대상을 바꾼 뒤 이어서 빌드할지도 SessionState로 넘긴다(바꾸는 도중에 Temp의 파일이 지워진 적이 있다).
        const string SmokeKey = "IWannabe.DeviceCheck.Smoke";
        const double SmokePageSeconds = 0.6;
        const string ContinueBuildKey = "IWannabe.DeviceCheck.ContinueBuild";

        static double smokeStarted;

        static DeviceCheckBuild()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string tokens = SessionState.GetString(ContinueBuildKey, "");
            SessionState.EraseString(ContinueBuildKey);
            if (File.Exists(TriggerFile))
            {
                tokens += " " + File.ReadAllText(TriggerFile);
                File.Delete(TriggerFile);
            }
            if (tokens.Trim().Length == 0) return;
            EditorApplication.delayCall += () =>
            {
                if (tokens.Contains("scene")) CreateScene();
                if (tokens.Contains("smoke")) Smoke();
                else if (tokens.Contains("build")) Build(tokens.Contains("install"));
            };
        }

        [MenuItem("IWannabe/Device Check/Smoke Test In Editor")]
        static void SmokeFromMenu() => Smoke();

        /// <summary>
        /// 지금 열린 씬은 그대로 두고 플레이 시작 씬만 기기 점검 씬으로 바꿔 플레이 모드에 들어간다. 탭을 하나씩 그려 본 뒤 빠져나온다.
        /// 오류가 나면 로그에 남고, 끝에 "[DeviceCheckBuild] 스모크 끝" 줄을 남긴다.
        /// </summary>
        static void Smoke()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            SessionState.SetBool(SmokeKey, true);
            Debug.Log("[DeviceCheckBuild] 스모크 시작");
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(SmokeKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                smokeStarted = EditorApplication.timeSinceStartup;
                EditorApplication.update += SmokeTick;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseBool(SmokeKey);
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("[DeviceCheckBuild] 스모크 끝");
            }
        }

        /// <summary>탭을 차례로 바꿔 그려 보고(비공개 필드라 리플렉션), 다 돌면 플레이 모드를 빠져나온다.</summary>
        static void SmokeTick()
        {
            var check = Object.FindAnyObjectByType<DeviceCheck>();
            var field = typeof(DeviceCheck).GetField("page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int pages = field != null ? Enum.GetValues(field.FieldType).Length : 0;
            int step = (int)((EditorApplication.timeSinceStartup - smokeStarted) / SmokePageSeconds);
            if (check != null && field != null && step < pages)
            {
                field.SetValue(check, Enum.ToObject(field.FieldType, step));
                return;
            }
            EditorApplication.update -= SmokeTick;
            Debug.Log($"[DeviceCheckBuild] 스모크: 기기 점검 {(check != null ? "있음" : "없음")}, 탭 {pages}개 그림");
            EditorApplication.isPlaying = false;
        }

        [MenuItem("IWannabe/Device Check/Create Scene")]
        static void CreateSceneFromMenu() => CreateScene();

        [MenuItem("IWannabe/Device Check/Build Android (Development)")]
        static void BuildFromMenu() => Build(false);

        [MenuItem("IWannabe/Device Check/Build Android and Install")]
        static void BuildAndInstallFromMenu() => Build(true);

        /// <summary>지금 열린 씬은 그대로 두고, 기기 점검 씬을 추가로 열어 만든 뒤 저장하고 닫는다.</summary>
        static void CreateScene()
        {
            var catalog = SetupUtil.LoadRequired<StageCatalog>(SetupPaths.Catalog);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SetupUtil.Hex("14171F");
            cameraObject.AddComponent<AudioListener>();

            var root = new GameObject("DeviceCheck");
            root.AddComponent<AudioSource>();
            var conductor = root.AddComponent<Conductor>();
            var input = root.AddComponent<RhythmInput>();
            input.ResetBindingsToDefault();
            var check = root.AddComponent<DeviceCheck>();
            SetupUtil.Assign(check, ("catalog", catalog), ("conductor", conductor), ("input", input));

            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            SceneManager.MoveGameObjectToScene(root, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log($"[DeviceCheckBuild] 씬 만듦: {ScenePath}");
        }

        static void Build(bool install)
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    Debug.Log("[DeviceCheckBuild] 빌드 대상을 Android로 바꾼다(에셋 다시 임포트). 끝나면 이어서 빌드한다.");
                    SessionState.SetString(ContinueBuildKey, install ? "build install" : "build");
                    if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    {
                        SessionState.EraseString(ContinueBuildKey);
                        Debug.LogError("[DeviceCheckBuild] 끝 - 빌드 대상을 바꾸지 못함(Android 모듈 확인)");
                    }
                    return;
                }
                if (!File.Exists(ScenePath)) CreateScene();

                EditorUserBuildSettings.buildAppBundle = false;
                AddressableAssetSettings.BuildPlayerContent(out var content);
                if (!string.IsNullOrEmpty(content.Error))
                {
                    Debug.LogError($"[DeviceCheckBuild] 끝 - Addressables 빌드 실패: {content.Error}");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath, SetupPaths.LobbyScene, SetupPaths.StageScene },
                    locationPathName = ApkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.Development,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[DeviceCheckBuild] 끝 - 빌드 실패: {summary.result}, 오류 {summary.totalErrors}개");
                    return;
                }
                Debug.Log($"[DeviceCheckBuild] 빌드 성공: {Path.GetFullPath(ApkPath)} ({summary.totalSize / 1048576f:0.0}MB, {summary.totalTime.TotalSeconds:0}초)");
                if (install) Install();
                Debug.Log("[DeviceCheckBuild] 끝 - 성공");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[DeviceCheckBuild] 끝 - 오류");
            }
        }

        /// <summary>연결된 기기(USB 디버깅)에 설치하고 실행한다.</summary>
        static void Install()
        {
            string adb = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
            string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string devices = Run(adb, "devices");
            if (!devices.Contains("\tdevice"))
            {
                Debug.LogWarning($"[DeviceCheckBuild] 연결된 기기가 없어 설치하지 않음(USB 디버깅 확인). adb devices:\n{devices}");
                return;
            }
            Debug.Log($"[DeviceCheckBuild] 설치: {Run(adb, $"install -r \"{Path.GetFullPath(ApkPath)}\"")}");
            Run(adb, $"shell monkey -p {package} -c android.intent.category.LAUNCHER 1");
            Debug.Log($"[DeviceCheckBuild] 실행: {package}");
        }

        static string Run(string file, string arguments)
        {
            var start = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(120000);
                return output.Trim();
            }
        }
    }
}
