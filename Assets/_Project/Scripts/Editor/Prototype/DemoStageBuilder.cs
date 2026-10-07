using System;
using System.Collections.Generic;
using System.IO;
using IWannabe.App;
using IWannabe.Rhythm;
using IWannabe.Rhythm.Charting;
using IWannabe.Rhythm.EditorTools;
using IWannabe.Stages.Demo;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 데모 스테이지 일체를 만든다: 도형 스프라이트, 오디오 임포트 설정, 곡 분석·채보 생성,
    /// 연출 프리팹, StageDefinition, Addressables 그룹, Lobby/StagePlay 씬.
    /// 새 스테이지를 만들 때 에셋들이 어떻게 연결되는지 보여 주는 견본이기도 하다.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoStageBuilder
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 자동 실행한다(Temp는 버전 관리 대상이 아님).
        const string TriggerFile = "Temp/IWannabe.BuildDemoStage";

        const string Root = "Assets/_Project";
        const string MusicPath = Root + "/Music/DemoSong.wav";
        const string SfxFolder = Root + "/SFX/Demo";
        const string ShapesFolder = Root + "/Textures/Shapes";
        const string DataFolder = Root + "/Data";
        const string StageDataFolder = DataFolder + "/Stages/Demo";
        const string PrefabFolder = Root + "/Prefabs/Stages";
        const string PrefabPath = PrefabFolder + "/DemoStage.prefab";
        const string ScenesFolder = Root + "/Scenes";
        const string LobbyScenePath = ScenesFolder + "/Lobby.unity";
        const string StageScenePath = ScenesFolder + "/StagePlay.unity";
        const string SpriteMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        const string SettingsPath = DataFolder + "/RhythmSettings.asset";
        const string CatalogPath = DataFolder + "/StageCatalog.asset";
        const string GroupName = "Stage_Demo";
        const string StageAddress = "Stages/Demo";
        const string StageLabel = "stage";

        static readonly Color Background = Hex("FDF0D5");

        static DemoStageBuilder()
        {
            if (!File.Exists(TriggerFile)) return;
            File.Delete(TriggerFile);
            EditorApplication.delayCall += () => Build(false);
        }

        [MenuItem("IWannabe/Prototype/Build Demo Stage")]
        static void BuildFromMenu() => Build(true);

        public static void Build(bool askFirst)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DemoStageBuilder] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }
            if (askFirst && !EditorUtility.DisplayDialog("데모 스테이지 구성",
                    "데모 스테이지의 분석·채보·프리팹·씬을 다시 만듭니다.\nDemoStage 프리팹과 Lobby/StagePlay 씬은 덮어씁니다.", "진행", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                Progress("폴더와 도형 준비", 0.05f);
                foreach (var folder in new[] { SfxFolder, ShapesFolder, StageDataFolder, PrefabFolder, ScenesFolder })
                    EnsureFolder(folder);
                var shapes = ShapeSprites.Ensure(ShapesFolder);
                var material = AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath);

                Progress("오디오 임포트 설정", 0.15f);
                ConfigureAudio();

                Progress("곡 분석", 0.3f);
                LoadOrCreate<RhythmSettings>(SettingsPath);
                var profile = PrepareChartProfile(out var song, out var chart);
                ChartPipeline.Analyze(profile);

                Progress("채보 생성", 0.5f);
                var report = ChartPipeline.Generate(profile);

                Progress("연출 프리팹", 0.6f);
                var presenter = BuildPresenterPrefab(shapes, material);

                Progress("스테이지 정의·Addressables", 0.7f);
                var stage = LoadOrCreate<StageDefinition>(StageDataFolder + "/Demo_StageDefinition.asset");
                stage.Setup("demo", "받아치기 (데모)", song, chart, presenter, CreateCueSounds(), Background);
                EditorUtility.SetDirty(stage);
                AssetDatabase.SaveAssets();
                var stageReference = RegisterAddressable(stage);
                UpdateCatalog(stage, stageReference);

                // 씬을 Single 모드로 바꾸면 참조 없는 에셋이 메모리에서 내려가므로,
                // 씬에 연결할 에셋은 씬을 만든 뒤 경로로 다시 불러온다.
                Progress("씬 구성", 0.85f);
                BuildStageScene(stageReference);
                BuildLobbyScene(); // 마지막에 만든 Lobby 씬이 열린 채로 남는다
                UpdateBuildSettings();
                AssetDatabase.SaveAssets();

                Debug.Log($"[DemoStageBuilder] 데모 스테이지 구성 완료 — {report}. Lobby 씬에서 플레이 버튼을 누르세요.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DemoStageBuilder] 구성 실패: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ───────────────────────── 에셋 ─────────────────────────

        static void ConfigureAudio()
        {
            ConfigureClip(MusicPath, AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, 0.7f, false);
            foreach (var file in Directory.GetFiles(SfxFolder, "*.wav"))
                ConfigureClip(file.Replace('\\', '/'), AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.ADPCM, 1f, true);
        }

        static void ConfigureClip(string path, AudioClipLoadType loadType, AudioCompressionFormat format, float quality, bool mono)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new FileNotFoundException("오디오 파일이 없습니다.", path);
            var sample = importer.defaultSampleSettings;
            sample.loadType = loadType;
            sample.compressionFormat = format;
            sample.quality = quality;
            sample.preloadAudioData = true;
            importer.defaultSampleSettings = sample;
            importer.forceToMono = mono;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        static ChartGenerationProfile PrepareChartProfile(out SongData song, out ChartData chart)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            if (clip == null) throw new FileNotFoundException("데모 곡이 없습니다.", MusicPath);

            song = LoadOrCreate<SongData>(StageDataFolder + "/Demo_SongData.asset");
            song.SetClip(clip);
            EditorUtility.SetDirty(song);

            var analysis = LoadOrCreate<SongAnalysis>(StageDataFolder + "/Demo_SongAnalysis.asset");
            var library = LoadOrCreate<PatternLibrary>(StageDataFolder + "/Demo_PatternLibrary.asset");
            if (library.Patterns.Count == 0)
            {
                library.SetPatterns(PatternPresets.CreateStarterSet());
                EditorUtility.SetDirty(library);
            }
            chart = LoadOrCreate<ChartData>(StageDataFolder + "/Demo_ChartData.asset");

            var profile = LoadOrCreate<ChartGenerationProfile>(StageDataFolder + "/Demo_ChartProfile.asset");
            profile.song = song;
            profile.analysis = analysis;
            profile.patterns = library;
            profile.output = chart;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static List<CueSound> CreateCueSounds()
        {
            return new List<CueSound>
            {
                new CueSound { cueId = PatternPresets.CueThrow, clip = LoadSfx("throw"), volume = 0.9f },
                new CueSound { cueId = PatternPresets.CueLob, clip = LoadSfx("lob"), volume = 0.9f },
                new CueSound { cueId = PatternPresets.CueCharge, clip = LoadSfx("charge"), volume = 0.8f },
                new CueSound { cueId = PatternPresets.CueTick, clip = LoadSfx("tick"), volume = 0.8f },
                new CueSound { cueId = PatternPresets.CueBell, clip = LoadSfx("bell"), volume = 0.9f },
            };
        }

        static AudioClip LoadSfx(string name)
        {
            string path = $"{SfxFolder}/{name}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new FileNotFoundException("효과음이 없습니다.", path);
            return clip;
        }

        static StageReference RegisterAddressable(StageDefinition stage)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup(GroupName)
                ?? settings.CreateGroup(GroupName, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stage));
            var entry = settings.CreateOrMoveEntry(guid, group, false, true);
            entry.address = StageAddress;
            entry.SetLabel(StageLabel, true, true, true);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return new StageReference(guid);
        }

        static void UpdateCatalog(StageDefinition stage, StageReference reference)
        {
            var catalog = LoadOrCreate<StageCatalog>(CatalogPath);
            var entries = new List<StageCatalogEntry>(catalog.Stages);
            entries.RemoveAll(e => e == null || e.stage == null || string.IsNullOrEmpty(e.stage.AssetGUID) || e.stage.AssetGUID == reference.AssetGUID);
            entries.Insert(0, new StageCatalogEntry { displayName = stage.DisplayName, stage = reference });
            catalog.SetStages(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── 연출 프리팹 ─────────────────────────

        static DemoStagePresenter BuildPresenterPrefab(ShapeSprites shapes, Material material)
        {
            var root = new GameObject("DemoStage");
            try
            {
                var presenter = root.AddComponent<DemoStagePresenter>();
                var t = root.transform;
                Shape(t, "Ground", shapes.Square, material, new Vector2(0f, -4.6f), new Vector2(40f, 4f), Hex("8AB17D"), 0);
                Shape(t, "GroundLine", shapes.Square, material, new Vector2(0f, -2.62f), new Vector2(40f, 0.08f), Hex("6A994E"), 1);

                // 캐릭터 피벗은 발밑이라 박마다 눌릴 때 바닥에 붙어 있다.
                var pitcher = Node(t, "Pitcher", new Vector2(-5.6f, -2.6f));
                Shape(pitcher, "Body", shapes.Circle, material, new Vector2(0f, 1f), new Vector2(2f, 2f), Hex("E76F51"), 5);
                Shape(pitcher, "EyeL", shapes.Circle, material, new Vector2(0.2f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
                Shape(pitcher, "EyeR", shapes.Circle, material, new Vector2(0.62f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
                var release = Node(pitcher, "ReleasePoint", new Vector2(1.1f, 1.6f));

                var batter = Node(t, "Batter", new Vector2(5.4f, -2.6f));
                Shape(batter, "Body", shapes.Circle, material, new Vector2(0f, 1f), new Vector2(2f, 2f), Hex("2A9D8F"), 5);
                Shape(batter, "EyeL", shapes.Circle, material, new Vector2(-0.62f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
                Shape(batter, "EyeR", shapes.Circle, material, new Vector2(-0.2f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
                var pivot = Node(batter, "PaddlePivot", new Vector2(-0.95f, 1f));
                Shape(pivot, "Paddle", shapes.Square, material, new Vector2(0f, 0.8f), new Vector2(0.28f, 1.6f), Hex("6D4C41"), 7);
                var hit = Node(batter, "HitPoint", new Vector2(-1.7f, 1.9f));

                var ball = Shape(t, "BallTemplate", shapes.Circle, material, Vector2.zero, Vector2.one, Color.white, 10);
                var flash = Shape(t, "FlashTemplate", shapes.Ring, material, Vector2.zero, Vector2.one, Color.white, 20);
                ball.gameObject.SetActive(false);
                flash.gameObject.SetActive(false);

                Assign(presenter,
                    ("pitcher", pitcher), ("batter", batter), ("paddlePivot", pivot),
                    ("releasePoint", release), ("hitPoint", hit),
                    ("ballTemplate", ball), ("flashTemplate", flash),
                    ("hitSound", LoadSfx("hit")), ("bigHitSound", LoadSfx("hit_big")),
                    ("barelySound", LoadSfx("hit_barely")), ("missSound", LoadSfx("miss")),
                    ("whiffSound", LoadSfx("whiff")));

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null) throw new IOException($"프리팹을 저장하지 못했습니다: {PrefabPath}");
                return prefab.GetComponent<DemoStagePresenter>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Transform Node(Transform parent, string name, Vector2 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            return node;
        }

        static SpriteRenderer Shape(Transform parent, string name, Sprite sprite, Material material, Vector2 position, Vector2 scale, Color color, int order)
        {
            var node = Node(parent, name, position);
            node.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = node.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (material != null) renderer.sharedMaterial = material;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        // ───────────────────────── 씬 ─────────────────────────

        static void BuildStageScene(StageReference stage)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var settings = LoadRequired<RhythmSettings>(SettingsPath);

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
            PrototypeUi.CreateEventSystem();

            var runner = systems.AddComponent<StageRunner>();
            Assign(runner,
                ("settings", settings), ("conductor", conductor), ("sfx", sfx), ("input", input),
                ("hud", hud), ("stageCamera", camera), ("stageRoot", stageRoot));
            var serialized = new SerializedObject(runner);
            var guid = serialized.FindProperty("fallbackStage.m_AssetGUID");
            if (guid == null) throw new InvalidOperationException("StageRunner.fallbackStage 필드를 찾지 못했습니다.");
            guid.stringValue = stage.AssetGUID;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, StageScenePath)) throw new IOException($"씬을 저장하지 못했습니다: {StageScenePath}");
        }

        static StageHud BuildHud()
        {
            var canvas = PrototypeUi.CreateCanvas("HUD", 10);
            var root = canvas.transform;
            var hud = canvas.gameObject.AddComponent<StageHud>();
            var ink = PrototypeUi.Ink;
            var top = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);
            var bottom = new Vector2(0.5f, 0f);

            var stageName = PrototypeUi.Label(PrototypeUi.Rect("StageName", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -36f), new Vector2(900f, 64f)),
                string.Empty, 40, TextAnchor.UpperLeft, ink, FontStyle.Bold);

            var progress = PrototypeUi.Rect("Progress", root, top, top, new Vector2(0f, -56f), new Vector2(720f, 16f));
            PrototypeUi.Panel(progress, new Color(0f, 0f, 0f, 0.15f), false);
            var fill = PrototypeUi.Stretch("Fill", progress);
            PrototypeUi.Panel(fill, Hex("2A9D8F"), false);
            fill.anchorMax = new Vector2(0f, 1f);

            var pause = PrototypeUi.MakeButton("PauseButton", root, "II", new Vector2(112f, 112f), 48);
            var pauseRect = (RectTransform)pause.transform;
            pauseRect.anchorMin = pauseRect.anchorMax = Vector2.one;
            pauseRect.pivot = Vector2.one;
            pauseRect.anchoredPosition = new Vector2(-36f, -28f);
            pause.gameObject.AddComponent<InputBlockArea>();
            var navigation = pause.navigation;
            navigation.mode = Navigation.Mode.None;
            pause.navigation = navigation;

            var center = PrototypeUi.Label(PrototypeUi.Rect("CenterText", root, middle, middle, new Vector2(0f, 140f), new Vector2(1400f, 160f)),
                string.Empty, 96, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var judgement = PrototypeUi.Label(PrototypeUi.Rect("JudgementText", root, bottom, bottom, new Vector2(0f, 150f), new Vector2(1000f, 80f)),
                string.Empty, 52, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            PrototypeUi.Label(PrototypeUi.Rect("ControlsHint", root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 28f), new Vector2(1400f, 44f)),
                "Space · F/J · 클릭 · 터치 · 패드 A   |   일시정지 Esc · Start", 28, TextAnchor.LowerLeft, new Color(ink.r, ink.g, ink.b, 0.6f));

            var pausePanel = PrototypeUi.Stretch("PausePanel", root);
            PrototypeUi.Panel(pausePanel, PrototypeUi.Dim, false);
            var pauseBox = PrototypeUi.Rect("Box", pausePanel, middle, middle, Vector2.zero, new Vector2(640f, 580f));
            PrototypeUi.Panel(pauseBox, Background, true);
            PrototypeUi.Label(PrototypeUi.Rect("Title", pauseBox, top, top, new Vector2(0f, -40f), new Vector2(600f, 90f)),
                "일시정지", 64, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var pauseButtons = PrototypeUi.Rect("Buttons", pauseBox, middle, middle, new Vector2(0f, -50f), new Vector2(480f, 380f));
            PrototypeUi.Column(pauseButtons, 24f);
            var resume = PrototypeUi.MakeButton("Resume", pauseButtons, "계속하기", new Vector2(440f, 100f), 44);
            var pauseRetry = PrototypeUi.MakeButton("Retry", pauseButtons, "다시하기", new Vector2(440f, 100f), 44);
            var pauseExit = PrototypeUi.MakeButton("Exit", pauseButtons, "나가기", new Vector2(440f, 100f), 44);

            var resultPanel = PrototypeUi.Stretch("ResultPanel", root);
            PrototypeUi.Panel(resultPanel, PrototypeUi.Dim, false);
            var resultBox = PrototypeUi.Rect("Box", resultPanel, middle, middle, Vector2.zero, new Vector2(860f, 640f));
            PrototypeUi.Panel(resultBox, Background, true);
            var rank = PrototypeUi.Label(PrototypeUi.Rect("Rank", resultBox, top, top, new Vector2(0f, -60f), new Vector2(800f, 140f)),
                string.Empty, 104, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var detail = PrototypeUi.Label(PrototypeUi.Rect("Detail", resultBox, middle, middle, new Vector2(0f, 30f), new Vector2(800f, 140f)),
                string.Empty, 40, TextAnchor.MiddleCenter, ink);
            var resultButtons = PrototypeUi.Rect("Buttons", resultBox, bottom, bottom, new Vector2(0f, 70f), new Vector2(780f, 110f));
            PrototypeUi.Row(resultButtons, 32f);
            var resultRetry = PrototypeUi.MakeButton("Retry", resultButtons, "다시하기", new Vector2(340f, 100f), 44);
            var resultExit = PrototypeUi.MakeButton("Exit", resultButtons, "로비로", new Vector2(340f, 100f), 44);

            pausePanel.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);

            Assign(hud,
                ("stageNameText", stageName), ("centerText", center), ("judgementText", judgement),
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
            var catalog = LoadRequired<StageCatalog>(CatalogPath);
            CreateCamera("MainCamera");

            var canvas = PrototypeUi.CreateCanvas("LobbyCanvas", 0);
            var root = canvas.transform;
            var lobby = canvas.gameObject.AddComponent<LobbyController>();
            var ink = PrototypeUi.Ink;
            var top = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);
            var bottom = new Vector2(0.5f, 0f);

            PrototypeUi.Label(PrototypeUi.Rect("Title", root, top, top, new Vector2(0f, -150f), new Vector2(1200f, 160f)),
                "IWannabe", 128, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            PrototypeUi.Label(PrototypeUi.Rect("Subtitle", root, top, top, new Vector2(0f, -270f), new Vector2(1200f, 70f)),
                "리듬 프로토타입 · 스테이지를 골라 시작하세요", 40, TextAnchor.MiddleCenter, ink);

            var list = PrototypeUi.Rect("StageList", root, middle, middle, new Vector2(0f, -20f), new Vector2(720f, 420f));
            PrototypeUi.Column(list, 24f);
            var template = PrototypeUi.MakeButton("StageButtonTemplate", list, "스테이지", new Vector2(640f, 120f), 52);

            var calibration = PrototypeUi.Rect("Calibration", root, bottom, bottom, new Vector2(0f, 180f), new Vector2(760f, 96f));
            PrototypeUi.Row(calibration, 20f);
            var minus = PrototypeUi.MakeButton("OffsetDown", calibration, "-", new Vector2(96f, 96f), 56);
            var offsetLabel = PrototypeUi.Label(PrototypeUi.Rect("OffsetLabel", calibration, middle, middle, Vector2.zero, new Vector2(460f, 96f)),
                "입력 보정 0 ms", 40, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var plus = PrototypeUi.MakeButton("OffsetUp", calibration, "+", new Vector2(96f, 96f), 56);
            PrototypeUi.Label(PrototypeUi.Rect("CalibrationHint", root, bottom, bottom, new Vector2(0f, 110f), new Vector2(1500f, 50f)),
                "판정이 늦게 느껴지는 기기(블루투스 이어폰 등)는 + 쪽으로 맞추세요", 28, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.7f));
            PrototypeUi.Label(PrototypeUi.Rect("ControlsHint", root, bottom, bottom, new Vector2(0f, 50f), new Vector2(1500f, 50f)),
                "조작: Space · F/J · 마우스 클릭 · 터치 · 패드 A   |   일시정지: Esc · Start", 28, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.7f));

            PrototypeUi.CreateEventSystem();

            Assign(lobby,
                ("catalog", catalog), ("stageButtonTemplate", template), ("stageButtonContainer", list),
                ("offsetLabel", offsetLabel), ("offsetDownButton", minus), ("offsetUpButton", plus));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, LobbyScenePath)) throw new IOException($"씬을 저장하지 못했습니다: {LobbyScenePath}");
        }

        static Camera CreateCamera(string name)
        {
            var go = new GameObject(name) { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 50f;
            go.AddComponent<AudioListener>();
            return camera;
        }

        static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == LobbyScenePath || s.path == StageScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(LobbyScenePath, true));
            scenes.Insert(1, new EditorBuildSettingsScene(StageScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ───────────────────────── 유틸 ─────────────────────────

        static void Assign(Object target, params (string field, Object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var property = serialized.FindProperty(field);
                if (property == null) throw new InvalidOperationException($"{target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException($"{typeof(T).Name} 에셋을 찾지 못했습니다.", path);
            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException($"잘못된 폴더 경로: {path}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Progress(string info, float t) => EditorUtility.DisplayProgressBar("IWannabe 데모 스테이지", info, t);

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var color) ? color : Color.magenta;
    }
}
