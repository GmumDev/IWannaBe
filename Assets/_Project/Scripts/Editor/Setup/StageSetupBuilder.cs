using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IWannabe.App;
using IWannabe.Rhythm;
using IWannabe.Rhythm.EditorTools;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static IWannabe.EditorTools.SetupUtil;
using Object = UnityEngine.Object;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 모든 스테이지와 공용 씬을 구성한다. 스테이지마다 레시피를 따라 곡 분석·채보 생성·연출 프리팹·
    /// StageDefinition·Addressables 그룹(스테이지당 1개)을 맞추고, 진행 순서대로 카탈로그를 만든다.
    /// 이미 있는 콘텐츠(분석 결과, 채보, 패턴 라이브러리, 연출 프리팹)는 덮어쓰지 않는다.
    /// AppRoot 프리팹과 Lobby/StagePlay 씬은 매번 다시 만든다.
    /// </summary>
    [InitializeOnLoad]
    public static class StageSetupBuilder
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 자동 실행한다(Temp는 버전 관리 대상이 아님).
        const string TriggerFile = "Temp/IWannabe.BuildStages";

        /// <summary>진행 순서. 앞 스테이지를 클리어해야 다음 스테이지가 열린다.</summary>
        static readonly StageRecipe[] Recipes = { new HitBackStageRecipe(), new SliceStageRecipe() };

        static StageSetupBuilder()
        {
            if (!File.Exists(TriggerFile)) return;
            File.Delete(TriggerFile);
            EditorApplication.delayCall += () => Build(false);
        }

        [MenuItem("IWannabe/Setup/Build All Stages")]
        static void BuildFromMenu() => Build(true);

        public static void Build(bool askFirst)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[StageSetup] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }
            if (askFirst && !EditorUtility.DisplayDialog("스테이지 구성",
                    "모든 스테이지의 연결(에셋·Addressables·카탈로그)을 맞추고 AppRoot 프리팹과 Lobby/StagePlay 씬을 다시 만듭니다.\n" +
                    "기존 분석 결과·채보·패턴·연출 프리팹은 그대로 둡니다.", "진행", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                Progress("공용 에셋 준비", 0.02f);
                foreach (var folder in new[] { SetupPaths.Shapes, SetupPaths.StagePrefabs, SetupPaths.AppPrefabs, SetupPaths.Scenes, SetupPaths.StageData })
                    EnsureFolder(folder);
                var kit = new StagePrefabKit
                {
                    Shapes = ShapeSprites.Ensure(SetupPaths.Shapes),
                    SpriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SetupPaths.SpriteMaterial),
                };
                LoadOrCreate<RhythmSettings>(SetupPaths.Settings);

                var entries = new List<StageCatalogEntry>();
                var summary = new StringBuilder();
                for (int i = 0; i < Recipes.Length; i++)
                {
                    var recipe = Recipes[i];
                    Progress($"스테이지 구성: {recipe.DisplayName}", 0.05f + 0.7f * i / Recipes.Length);
                    var reference = BuildStage(recipe, kit, out string report);
                    entries.Add(new StageCatalogEntry { stageId = recipe.StageId, displayName = recipe.DisplayName, stage = reference });
                    summary.Append($"\n  {i + 1}. {recipe.DisplayName}: {report}");
                }

                var catalog = LoadOrCreate<StageCatalog>(SetupPaths.Catalog);
                catalog.SetStages(entries);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();

                Progress("AppRoot·로딩 화면", 0.8f);
                BuildAppRoot(kit.Shapes);

                // 씬을 Single 모드로 바꾸면 참조 없는 에셋이 메모리에서 내려가므로,
                // 씬에 연결할 에셋은 씬을 만든 뒤 경로로 다시 불러온다.
                Progress("씬 구성", 0.9f);
                BuildStageScene(entries[0].stage.AssetGUID);
                BuildLobbyScene(); // 마지막에 만든 Lobby 씬이 열린 채로 남는다
                UpdateBuildSettings();
                AssetDatabase.SaveAssets();

                Debug.Log($"[StageSetup] 스테이지 구성 완료{summary}\nLobby 씬에서 플레이 버튼을 누르세요.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[StageSetup] 구성 실패: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ───────────────────────── 스테이지 ─────────────────────────

        static StageReference BuildStage(StageRecipe recipe, StagePrefabKit kit, out string report)
        {
            EnsureFolder(recipe.DataFolder);
            ConfigureAudio(recipe);

            var clip = LoadRequired<AudioClip>(recipe.MusicPath);
            var song = LoadOrCreate<SongData>(recipe.AssetPath("SongData"));
            if (song.Clip != clip)
            {
                song.SetClip(clip);
                EditorUtility.SetDirty(song);
            }

            var analysis = LoadOrCreate<SongAnalysis>(recipe.AssetPath("SongAnalysis"));
            var library = LoadOrCreate<PatternLibrary>(recipe.AssetPath("PatternLibrary"));
            if (library.Patterns.Count == 0)
            {
                library.SetPatterns(recipe.CreatePatterns());
                EditorUtility.SetDirty(library);
            }
            var chart = LoadOrCreate<ChartData>(recipe.AssetPath("ChartData"));

            var profile = LoadOrCreate<ChartGenerationProfile>(recipe.AssetPath("ChartProfile"), out bool newProfile);
            if (newProfile) profile.generator = recipe.CreateGeneratorSettings();
            profile.song = song;
            profile.analysis = analysis;
            profile.patterns = library;
            profile.output = chart;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 곡이 바뀌었거나 분석이 없을 때만 분석하고, 분석이 새로 되면 채보도 다시 만든다.
            bool analyze = !analysis.HasData || analysis.SourceClip != clip || !song.HasBeatMap;
            bool generate = analyze || chart.Patterns.Count == 0;
            if (analyze) ChartPipeline.Analyze(profile);
            if (generate) ChartPipeline.Generate(profile);

            var presenter = recipe.EnsurePresenterPrefab(kit);

            var cues = new List<CueSound>();
            foreach (var (cueId, sfx, volume) in recipe.CueSounds)
                cues.Add(new CueSound { cueId = cueId, clip = recipe.LoadSfx(sfx), volume = volume });

            var stage = LoadOrCreate<StageDefinition>(recipe.AssetPath("StageDefinition"));
            stage.Setup(recipe.StageId, recipe.DisplayName, song, chart, presenter, cues, recipe.Background, recipe.HudInk);
            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();

            int notes = 0;
            foreach (var pattern in chart.Patterns) notes += pattern.notes.Count;
            report = $"{(generate ? "분석·채보 생성" : "기존 채보 유지")}, 노트 {notes}개, 그룹 {recipe.GroupName}";
            return RegisterAddressable(stage, recipe);
        }

        static void ConfigureAudio(StageRecipe recipe)
        {
            ConfigureClip(recipe.MusicPath, AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, 0.7f, false);
            foreach (var file in Directory.GetFiles(recipe.SfxFolder, "*.wav"))
                ConfigureClip(file.Replace('\\', '/'), AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.ADPCM, 1f, true);
        }

        static void ConfigureClip(string path, AudioClipLoadType loadType, AudioCompressionFormat format, float quality, bool mono)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new FileNotFoundException("오디오 파일이 없습니다.", path);
            var sample = importer.defaultSampleSettings;
            bool configured = sample.loadType == loadType && sample.compressionFormat == format
                && Mathf.Approximately(sample.quality, quality) && sample.preloadAudioData
                && importer.forceToMono == mono && !importer.loadInBackground;
            if (configured) return;

            sample.loadType = loadType;
            sample.compressionFormat = format;
            sample.quality = quality;
            sample.preloadAudioData = true;
            importer.defaultSampleSettings = sample;
            importer.forceToMono = mono;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        /// <summary>스테이지마다 Addressables 그룹 하나. StageDefinition만 주소를 갖고, 나머지는 의존성으로 같은 번들에 묶인다.</summary>
        static StageReference RegisterAddressable(StageDefinition stage, StageRecipe recipe)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup(recipe.GroupName)
                ?? settings.CreateGroup(recipe.GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stage));
            var entry = settings.CreateOrMoveEntry(guid, group, false, true);
            entry.address = recipe.Address;
            entry.SetLabel("stage", true, true, true);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return new StageReference(guid);
        }

        // ───────────────────────── AppRoot·로딩 화면 ─────────────────────────

        static void BuildAppRoot(ShapeSprites shapes)
        {
            var root = new GameObject("AppRoot");
            try
            {
                var flow = root.AddComponent<StageFlow>();
                var canvas = SetupUi.CreateCanvas("LoadingCanvas", 1000);
                canvas.transform.SetParent(root.transform, false);
                var group = canvas.gameObject.AddComponent<CanvasGroup>();
                var screen = canvas.gameObject.AddComponent<LoadingScreen>();
                var middle = new Vector2(0.5f, 0.5f);
                var cream = Hex("FDF0D5");

                SetupUi.Panel(SetupUi.Stretch("Background", canvas.transform), Hex("264653"), false);
                var spinner = SetupUi.Rect("Spinner", canvas.transform, middle, middle, new Vector2(0f, 80f), new Vector2(120f, 120f));
                var ring = spinner.gameObject.AddComponent<Image>();
                ring.sprite = shapes.Ring;
                ring.color = Hex("FDF0D5", 0.35f);
                ring.raycastTarget = false;
                var dot = SetupUi.Rect("Dot", spinner, new Vector2(0.5f, 1f), middle, new Vector2(0f, -9f), new Vector2(28f, 28f));
                var dotImage = dot.gameObject.AddComponent<Image>();
                dotImage.sprite = shapes.Circle;
                dotImage.color = Hex("E9C46A");
                dotImage.raycastTarget = false;

                var message = SetupUi.Label(SetupUi.Rect("Message", canvas.transform, middle, middle, new Vector2(0f, -40f), new Vector2(1200f, 70f)),
                    "불러오는 중", 44, TextAnchor.MiddleCenter, cream, FontStyle.Bold);
                var progress = SetupUi.Rect("Progress", canvas.transform, middle, middle, new Vector2(0f, -110f), new Vector2(600f, 12f));
                SetupUi.Panel(progress, Hex("FDF0D5", 0.2f), false);
                var fill = SetupUi.Stretch("Fill", progress);
                SetupUi.Panel(fill, Hex("E9C46A"), false);
                fill.anchorMax = new Vector2(0f, 1f);

                group.alpha = 0f;
                group.blocksRaycasts = false;
                Assign(screen, ("group", group), ("messageText", message), ("progressFill", fill), ("spinner", spinner));
                Assign(flow, ("loadingScreen", screen));

                if (PrefabUtility.SaveAsPrefabAsset(root, SetupPaths.AppRoot) == null)
                    throw new IOException($"프리팹을 저장하지 못했습니다: {SetupPaths.AppRoot}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ───────────────────────── 씬 ─────────────────────────

        static void BuildStageScene(string fallbackStageGuid)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var settings = LoadRequired<RhythmSettings>(SetupPaths.Settings);
            PrefabUtility.InstantiatePrefab(LoadRequired<GameObject>(SetupPaths.AppRoot));

            var camera = CreateCamera("StageCamera");
            var systems = new GameObject("Systems");
            var conductor = systems.AddComponent<Conductor>();
            var music = systems.AddComponent<AudioSource>();
            music.playOnAwake = false;
            Assign(conductor, ("musicSource", music));

            var input = systems.AddComponent<RhythmInput>();
            input.ResetBindingsToDefault();

            var sfx = new GameObject("Sfx").AddComponent<SfxPlayer>();
            sfx.transform.SetParent(systems.transform, false);
            var stageRoot = new GameObject("StageRoot").transform;
            var hud = BuildHud();
            SetupUi.CreateEventSystem();

            var runner = systems.AddComponent<StageRunner>();
            Assign(runner,
                ("settings", settings), ("conductor", conductor), ("sfx", sfx), ("input", input),
                ("hud", hud), ("stageCamera", camera), ("stageRoot", stageRoot));
            var serialized = new SerializedObject(runner);
            var guid = serialized.FindProperty("fallbackStage.m_AssetGUID");
            if (guid == null) throw new InvalidOperationException("StageRunner.fallbackStage 필드를 찾지 못했습니다.");
            guid.stringValue = fallbackStageGuid;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SetupPaths.StageScene)) throw new IOException($"씬을 저장하지 못했습니다: {SetupPaths.StageScene}");
        }

        static StageHud BuildHud()
        {
            var canvas = SetupUi.CreateCanvas("HUD", 10);
            var root = canvas.transform;
            var hud = canvas.gameObject.AddComponent<StageHud>();
            var ink = SetupUi.Ink;
            var cream = Hex("FDF0D5");
            var top = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);
            var bottom = new Vector2(0.5f, 0f);

            var stageName = SetupUi.Label(SetupUi.Rect("StageName", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -36f), new Vector2(900f, 64f)),
                string.Empty, 40, TextAnchor.UpperLeft, ink, FontStyle.Bold);

            var progress = SetupUi.Rect("Progress", root, top, top, new Vector2(0f, -56f), new Vector2(720f, 16f));
            SetupUi.Panel(progress, new Color(0.5f, 0.5f, 0.5f, 0.3f), false);
            var fill = SetupUi.Stretch("Fill", progress);
            SetupUi.Panel(fill, Hex("2A9D8F"), false);
            fill.anchorMax = new Vector2(0f, 1f);

            var pause = SetupUi.MakeButton("PauseButton", root, "II", new Vector2(112f, 112f), 48);
            var pauseRect = (RectTransform)pause.transform;
            pauseRect.anchorMin = pauseRect.anchorMax = Vector2.one;
            pauseRect.pivot = Vector2.one;
            pauseRect.anchoredPosition = new Vector2(-36f, -28f);
            pause.gameObject.AddComponent<InputBlockArea>();
            var navigation = pause.navigation;
            navigation.mode = Navigation.Mode.None;
            pause.navigation = navigation;

            var center = SetupUi.Label(SetupUi.Rect("CenterText", root, middle, middle, new Vector2(0f, 140f), new Vector2(1400f, 160f)),
                string.Empty, 96, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var judgement = SetupUi.Label(SetupUi.Rect("JudgementText", root, bottom, bottom, new Vector2(0f, 150f), new Vector2(1000f, 80f)),
                string.Empty, 52, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var hint = SetupUi.Label(SetupUi.Rect("ControlsHint", root, Vector2.zero, Vector2.zero, new Vector2(40f, 28f), new Vector2(1400f, 44f)),
                "Space · F/J · 클릭 · 터치 · 패드 A   |   일시정지 Esc · Start", 28, TextAnchor.LowerLeft, new Color(ink.r, ink.g, ink.b, 0.6f));

            var pausePanel = SetupUi.Stretch("PausePanel", root);
            SetupUi.Panel(pausePanel, SetupUi.Dim, false);
            var pauseBox = SetupUi.Rect("Box", pausePanel, middle, middle, Vector2.zero, new Vector2(640f, 580f));
            SetupUi.Panel(pauseBox, cream, true);
            SetupUi.Label(SetupUi.Rect("Title", pauseBox, top, top, new Vector2(0f, -40f), new Vector2(600f, 90f)),
                "일시정지", 64, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var pauseButtons = SetupUi.Rect("Buttons", pauseBox, middle, middle, new Vector2(0f, -50f), new Vector2(480f, 380f));
            SetupUi.Column(pauseButtons, 24f);
            var resume = SetupUi.MakeButton("Resume", pauseButtons, "계속하기", new Vector2(440f, 100f), 44);
            var pauseRetry = SetupUi.MakeButton("Retry", pauseButtons, "다시하기", new Vector2(440f, 100f), 44);
            var pauseExit = SetupUi.MakeButton("Exit", pauseButtons, "나가기", new Vector2(440f, 100f), 44);

            var resultPanel = SetupUi.Stretch("ResultPanel", root);
            SetupUi.Panel(resultPanel, SetupUi.Dim, false);
            var resultBox = SetupUi.Rect("Box", resultPanel, middle, middle, Vector2.zero, new Vector2(860f, 660f));
            SetupUi.Panel(resultBox, cream, true);
            var rank = SetupUi.Label(SetupUi.Rect("Rank", resultBox, top, top, new Vector2(0f, -60f), new Vector2(800f, 140f)),
                string.Empty, 104, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var detail = SetupUi.Label(SetupUi.Rect("Detail", resultBox, middle, middle, new Vector2(0f, 20f), new Vector2(800f, 180f)),
                string.Empty, 40, TextAnchor.MiddleCenter, ink);
            var resultButtons = SetupUi.Rect("Buttons", resultBox, bottom, bottom, new Vector2(0f, 70f), new Vector2(780f, 110f));
            SetupUi.Row(resultButtons, 32f);
            var resultRetry = SetupUi.MakeButton("Retry", resultButtons, "다시하기", new Vector2(340f, 100f), 44);
            var resultExit = SetupUi.MakeButton("Exit", resultButtons, "로비로", new Vector2(340f, 100f), 44);

            pausePanel.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);

            Assign(hud,
                ("stageNameText", stageName), ("centerText", center), ("judgementText", judgement), ("hintText", hint),
                ("progressFill", fill), ("pauseButton", pause),
                ("pausePanel", pausePanel.gameObject), ("resumeButton", resume),
                ("pauseRetryButton", pauseRetry), ("pauseExitButton", pauseExit),
                ("resultPanel", resultPanel.gameObject), ("resultRankText", rank), ("resultDetailText", detail),
                ("resultRetryButton", resultRetry), ("resultExitButton", resultExit));
            return hud;
        }

        static void BuildLobbyScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = LoadRequired<StageCatalog>(SetupPaths.Catalog);
            PrefabUtility.InstantiatePrefab(LoadRequired<GameObject>(SetupPaths.AppRoot));
            CreateCamera("MainCamera");

            var canvas = SetupUi.CreateCanvas("LobbyCanvas", 0);
            var root = canvas.transform;
            var lobby = canvas.gameObject.AddComponent<LobbyController>();
            var ink = SetupUi.Ink;
            var top = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);
            var bottom = new Vector2(0.5f, 0f);

            SetupUi.Label(SetupUi.Rect("Title", root, top, top, new Vector2(0f, -140f), new Vector2(1200f, 160f)),
                "IWannabe", 128, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            SetupUi.Label(SetupUi.Rect("Subtitle", root, top, top, new Vector2(0f, -255f), new Vector2(1200f, 70f)),
                "스테이지를 골라 시작하세요 · 앞 스테이지를 클리어하면 다음 스테이지가 열려요", 36, TextAnchor.MiddleCenter, ink);

            var list = SetupUi.Rect("StageList", root, middle, middle, new Vector2(0f, -10f), new Vector2(720f, 360f));
            SetupUi.Column(list, 24f);
            var template = BuildStageButtonTemplate(list);

            var calibration = SetupUi.Rect("Calibration", root, bottom, bottom, new Vector2(0f, 180f), new Vector2(760f, 96f));
            SetupUi.Row(calibration, 20f);
            var minus = SetupUi.MakeButton("OffsetDown", calibration, "-", new Vector2(96f, 96f), 56);
            var offsetLabel = SetupUi.Label(SetupUi.Rect("OffsetLabel", calibration, middle, middle, Vector2.zero, new Vector2(460f, 96f)),
                "입력 보정 0 ms", 40, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var plus = SetupUi.MakeButton("OffsetUp", calibration, "+", new Vector2(96f, 96f), 56);
            SetupUi.Label(SetupUi.Rect("CalibrationHint", root, bottom, bottom, new Vector2(0f, 110f), new Vector2(1500f, 50f)),
                "판정이 늦게 느껴지는 기기(블루투스 이어폰 등)는 + 쪽으로 맞추세요", 28, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.7f));
            SetupUi.Label(SetupUi.Rect("ControlsHint", root, bottom, bottom, new Vector2(0f, 50f), new Vector2(1500f, 50f)),
                "조작: Space · F/J · 마우스 클릭 · 터치 · 패드 A   |   일시정지: Esc · Start", 28, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.7f));

            SetupUi.CreateEventSystem();

            Assign(lobby,
                ("catalog", catalog), ("stageButtonTemplate", template), ("stageButtonContainer", list),
                ("offsetLabel", offsetLabel), ("offsetDownButton", minus), ("offsetUpButton", plus));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SetupPaths.LobbyScene)) throw new IOException($"씬을 저장하지 못했습니다: {SetupPaths.LobbyScene}");
        }

        static StageButtonView BuildStageButtonTemplate(Transform parent)
        {
            var button = SetupUi.MakeButton("StageButtonTemplate", parent, "스테이지", new Vector2(640f, 150f), 54);
            var titleRect = (RectTransform)button.transform.Find("Label");
            titleRect.anchorMin = new Vector2(0f, 0.4f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            var statusRect = SetupUi.Stretch("Status", button.transform);
            statusRect.anchorMax = new Vector2(1f, 0.45f);
            var ink = SetupUi.Ink;
            var status = SetupUi.Label(statusRect, "도전 가능", 30, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.75f));

            var view = button.gameObject.AddComponent<StageButtonView>();
            Assign(view, ("button", button), ("titleText", titleRect.GetComponent<Text>()), ("statusText", status));
            return view;
        }

        static Camera CreateCamera(string name)
        {
            var go = new GameObject(name) { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("FDF0D5");
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 50f;
            go.AddComponent<AudioListener>();
            return camera;
        }

        static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == SetupPaths.LobbyScene || s.path == SetupPaths.StageScene);
            scenes.Insert(0, new EditorBuildSettingsScene(SetupPaths.LobbyScene, true));
            scenes.Insert(1, new EditorBuildSettingsScene(SetupPaths.StageScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void Progress(string info, float t) => EditorUtility.DisplayProgressBar("IWannabe 스테이지 구성", info, t);
    }
}
