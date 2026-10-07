using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IWannabe.App;
using IWannabe.Rhythm;
using IWannabe.Rhythm.EditorTools;
using UnityEditor;
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
    /// 여러 번들이 함께 쓰는 에셋은 공용 그룹에 명시적으로 넣는다: 곡은 Music(곡마다 번들 하나),
    /// 도형은 Shared, 꾸미기 카탈로그·오타마톤 파츠는 Customization.
    /// 이미 있는 콘텐츠(분석 결과, 채보, 패턴 라이브러리, 연출 프리팹, 꾸미기 항목)는 덮어쓰지 않는다.
    /// AppRoot 프리팹과 Lobby/StagePlay 씬은 매번 다시 만든다.
    /// </summary>
    [InitializeOnLoad]
    public static class StageSetupBuilder
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 자동 실행한다(Temp는 버전 관리 대상이 아님).
        // 내용에 RebuildPresentersToken이 있으면 연출 프리팹도 레시피대로 다시 만들고,
        // CheckDuplicatesToken이 있으면 끝난 뒤 Addressables 중복 검사를 돌린다.
        const string TriggerFile = "Temp/IWannabe.BuildStages";
        const string RebuildPresentersToken = "rebuild-presenters";
        const string CheckDuplicatesToken = "check-duplicates";

        /// <summary>진행 순서. 앞 스테이지를 클리어해야 다음 스테이지가 열린다.</summary>
        static readonly StageRecipe[] Recipes = { new HitBackStageRecipe(), new SliceStageRecipe() };

        static StageSetupBuilder()
        {
            if (!File.Exists(TriggerFile)) return;
            string tokens = File.ReadAllText(TriggerFile);
            File.Delete(TriggerFile);
            EditorApplication.delayCall += () => Build(false, tokens.Contains(RebuildPresentersToken), tokens.Contains(CheckDuplicatesToken));
        }

        [MenuItem("IWannabe/Setup/Build All Stages")]
        static void BuildFromMenu() => Build(true);

        [MenuItem("IWannabe/Setup/Rebuild Stage Presenter Prefabs")]
        static void RebuildPresentersFromMenu() => Build(true, true);

        public static void Build(bool askFirst, bool rebuildPresenters = false, bool checkDuplicates = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[StageSetup] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }
            string message = rebuildPresenters
                ? "모든 스테이지의 연출 프리팹을 레시피대로 다시 만듭니다. 프리팹을 손으로 고친 내용은 사라집니다.\n" +
                  "나머지(분석 결과·채보·패턴)는 그대로 두고, AppRoot와 Lobby/StagePlay 씬은 다시 만듭니다."
                : "모든 스테이지의 연결(에셋·Addressables·카탈로그)을 맞추고 AppRoot 프리팹과 Lobby/StagePlay 씬을 다시 만듭니다.\n" +
                  "기존 분석 결과·채보·패턴·연출 프리팹은 그대로 둡니다.";
            if (askFirst && !EditorUtility.DisplayDialog("스테이지 구성", message, "진행", "취소"))
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
                    Otamaton = OtamatonSprites.Ensure($"{SetupPaths.Otamaton}/Default", "default"),
                    SpriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(SetupPaths.SpriteMaterial),
                };
                LoadOrCreate<RhythmSettings>(SetupPaths.Settings);

                var entries = new List<StageCatalogEntry>();
                var summary = new StringBuilder();
                for (int i = 0; i < Recipes.Length; i++)
                {
                    var recipe = Recipes[i];
                    Progress($"스테이지 구성: {recipe.DisplayName}", 0.05f + 0.7f * i / Recipes.Length);
                    var reference = BuildStage(recipe, kit, rebuildPresenters, out string report);
                    entries.Add(new StageCatalogEntry { stageId = recipe.StageId, displayName = recipe.DisplayName, stage = reference });
                    summary.Append($"\n  {i + 1}. {recipe.DisplayName}: {report}");
                }

                Progress("공용 에셋 그룹·꾸미기 카탈로그", 0.76f);
                RegisterSharedAssets();
                string customizationGuid = CustomizationSetup.Build(Recipes);

                var catalog = LoadOrCreate<StageCatalog>(SetupPaths.Catalog);
                catalog.SetStages(entries);
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();

                Progress("AppRoot·로딩 화면", 0.8f);
                BuildAppRoot(kit.Shapes, customizationGuid);

                // 씬을 Single 모드로 바꾸면 참조 없는 에셋이 메모리에서 내려가므로,
                // 씬에 연결할 에셋은 씬을 만든 뒤 경로로 다시 불러온다.
                Progress("씬 구성", 0.9f);
                BuildStageScene(entries[0].stage.AssetGUID);
                BuildLobbyScene(); // 마지막에 만든 Lobby 씬이 열린 채로 남는다
                UpdateBuildSettings();
                AssetDatabase.SaveAssets();

                Debug.Log($"[StageSetup] 스테이지 구성 완료{summary}\nLobby 씬에서 플레이 버튼을 누르세요.");
                if (checkDuplicates) AddressablesSetup.CheckDuplicates();
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

        static StageReference BuildStage(StageRecipe recipe, StagePrefabKit kit, bool rebuildPresenter, out string report)
        {
            EnsureFolder(recipe.DataFolder);
            ConfigureAudio(recipe);

            var clip = LoadRequired<AudioClip>(recipe.MusicPath);
            // 곡은 로비 BGM으로도 쓰이므로 스테이지 번들에 묶지 않고 곡마다 번들 하나로 둔다.
            AddressablesSetup.Register(recipe.MusicPath,
                AddressablesSetup.Group(AddressablesSetup.MusicGroup, BundledAssetGroupSchema.BundlePackingMode.PackSeparately),
                $"Music/{recipe.AssetName}");
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

            // 프리팹을 다시 만들면 컴포넌트 fileID가 바뀌므로 아래 StageDefinition.Setup에서 참조를 다시 건다.
            var presenter = recipe.EnsurePresenterPrefab(kit, rebuildPresenter);

            var cues = new List<CueSound>();
            foreach (var (cueId, sfx, volume) in recipe.CueSounds)
                cues.Add(new CueSound { cueId = cueId, clip = recipe.LoadSfx(sfx), volume = volume });

            var stage = LoadOrCreate<StageDefinition>(recipe.AssetPath("StageDefinition"));
            stage.Setup(recipe.StageId, recipe.DisplayName, song, chart, presenter, cues, recipe.Background, recipe.HudInk);
            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();

            int notes = 0;
            foreach (var pattern in chart.Patterns) notes += pattern.notes.Count;
            report = $"{(generate ? "분석·채보 생성" : "기존 채보 유지")}, 노트 {notes}개, " +
                     $"연출 프리팹 {(rebuildPresenter ? "다시 만듦" : "유지")}, 그룹 {recipe.GroupName}";
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
            var entry = AddressablesSetup.Register(AssetDatabase.GetAssetPath(stage), AddressablesSetup.Group(recipe.GroupName), recipe.Address);
            entry.SetLabel("stage", true, true, true);
            AssetDatabase.SaveAssets();
            return new StageReference(entry.guid);
        }

        /// <summary>
        /// 모든 스테이지 연출이 쓰는 도형 스프라이트와 스프라이트 머티리얼(셰이더 포함)을 Shared 그룹에 명시적으로 넣어
        /// 스테이지 번들마다 복사되지 않게 한다. 로딩 화면(AppRoot)은 번들 없이 바로 떠야 해서 도형을 씬에서 직접 참조하므로,
        /// 그쪽 한 벌은 앱 데이터에 따로 남는다.
        /// </summary>
        static void RegisterSharedAssets()
        {
            var group = AddressablesSetup.Group(AddressablesSetup.SharedGroup);
            foreach (var path in Directory.GetFiles(SetupPaths.Shapes, "*.png"))
            {
                string assetPath = path.Replace('\\', '/');
                AddressablesSetup.Register(assetPath, group, $"Shared/Shapes/{Path.GetFileNameWithoutExtension(assetPath)}");
            }
            AddressablesSetup.Register(SetupPaths.SpriteMaterial, group, "Shared/SpriteMaterial");
            AssetDatabase.SaveAssets();
        }

        // ───────────────────────── AppRoot·로딩 화면 ─────────────────────────

        static void BuildAppRoot(ShapeSprites shapes, string customizationCatalogGuid)
        {
            var root = new GameObject("AppRoot");
            try
            {
                var flow = root.AddComponent<StageFlow>();
                // 꾸미기 카탈로그는 주소로만 가리킨다. 직접 참조하면 파츠 이미지가 앱 데이터와 번들에 두 벌 들어간다.
                var wardrobe = new SerializedObject(root.AddComponent<Wardrobe>());
                var catalogGuid = wardrobe.FindProperty("catalog.m_AssetGUID");
                if (catalogGuid == null) throw new InvalidOperationException("Wardrobe.catalog 필드를 찾지 못했습니다.");
                catalogGuid.stringValue = customizationCatalogGuid;
                wardrobe.ApplyModifiedPropertiesWithoutUndo();
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
            var lobby = canvas.gameObject.AddComponent<LobbyController>();
            var ink = SetupUi.Ink;
            var faded = new Color(ink.r, ink.g, ink.b, 0.7f);
            var top = new Vector2(0.5f, 1f);
            var middle = new Vector2(0.5f, 0.5f);

            // 꾸미기 팝업이 열리면 이 화면 전체(배경 포함)가 왼쪽으로 밀린다.
            var screen = SetupUi.Stretch("Screen", canvas.transform);
            BuildLobbyBackground(screen);

            SetupUi.Label(SetupUi.Rect("Title", screen, top, top, new Vector2(0f, -100f), new Vector2(1200f, 140f)),
                "IWannabe", 110, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            SetupUi.Label(SetupUi.Rect("Subtitle", screen, top, top, new Vector2(0f, -195f), new Vector2(1600f, 50f)),
                "스테이지를 골라 시작하세요 · 앞 스테이지를 클리어하면 다음 스테이지가 열려요", 32, TextAnchor.MiddleCenter, ink);
            SetupUi.Label(SetupUi.Rect("ControlsHint", screen, top, top, new Vector2(0f, -243f), new Vector2(1600f, 40f)),
                "조작: Space · F/J · 마우스 클릭 · 터치 · 패드 A   |   일시정지: Esc · Start", 26, TextAnchor.MiddleCenter, faded);

            // 캐릭터는 화면 가운데. 팝업이 열리면 팝업을 뺀 영역의 가운데로 밀려난다.
            var character = SetupUi.Rect("Character", screen, middle, middle, new Vector2(0f, 30f), new Vector2(380f, 380f));
            Assign(character.gameObject.AddComponent<LobbyCharacter>(), ("view", SetupUi.OtamatonUi(character, true)));

            var list = SetupUi.Rect("StageList", screen, middle, middle, new Vector2(0f, -275f), new Vector2(1400f, 140f));
            SetupUi.Row(list, 32f);
            var template = BuildStageButtonTemplate(list);

            var corner = Vector2.zero;
            var calibration = SetupUi.Rect("Calibration", screen, corner, corner, new Vector2(40f, 84f), new Vector2(600f, 80f));
            SetupUi.Row(calibration, 16f).childAlignment = TextAnchor.MiddleLeft;
            var minus = SetupUi.MakeButton("OffsetDown", calibration, "-", new Vector2(80f, 80f), 48);
            var offsetLabel = SetupUi.Label(SetupUi.Rect("OffsetLabel", calibration, middle, middle, Vector2.zero, new Vector2(380f, 80f)),
                "입력 보정 0 ms", 34, TextAnchor.MiddleCenter, ink, FontStyle.Bold);
            var plus = SetupUi.MakeButton("OffsetUp", calibration, "+", new Vector2(80f, 80f), 48);
            SetupUi.Label(SetupUi.Rect("CalibrationHint", screen, corner, corner, new Vector2(44f, 36f), new Vector2(1000f, 40f)),
                "판정이 늦게 느껴지는 기기(블루투스 이어폰 등)는 + 쪽으로 맞추세요", 24, TextAnchor.MiddleLeft, faded);

            var customize = SetupUi.MakeButton("CustomizeButton", screen, "꾸미기", new Vector2(150f, 150f), 36);
            var customizeRect = (RectTransform)customize.transform;
            customizeRect.anchorMin = customizeRect.anchorMax = customizeRect.pivot = new Vector2(1f, 0f);
            customizeRect.anchoredPosition = new Vector2(-40f, 40f);

            BuildCustomizationPopup(canvas.transform, screen, customize, catalog);

            var bgm = new GameObject("LobbyBgm");
            var source = bgm.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            Assign(bgm.AddComponent<LobbyBgm>(), ("source", source));

            SetupUi.CreateEventSystem();

            Assign(lobby,
                ("catalog", catalog), ("stageButtonTemplate", template), ("stageButtonContainer", list),
                ("offsetLabel", offsetLabel), ("offsetDownButton", minus), ("offsetUpButton", plus));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SetupPaths.LobbyScene)) throw new IOException($"씬을 저장하지 못했습니다: {SetupPaths.LobbyScene}");
        }

        /// <summary>
        /// 장착한 배경을 그리는 판. 화면이 왼쪽으로 밀려도 오른쪽에 빈틈이 없도록 오른쪽으로 넉넉히 더 깔고,
        /// 이미지는 비율을 지키며 판을 덮는다(단색이면 비율은 상관없다).
        /// </summary>
        static void BuildLobbyBackground(RectTransform screen)
        {
            const float overscan = 1000f; // 팝업 너비보다 넉넉히(팝업이 열릴 때 미는 거리는 그 절반 정도)
            var frame = SetupUi.Stretch("Background", screen);
            frame.offsetMax = new Vector2(overscan, 0f);
            var imageRect = SetupUi.Rect("Image", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = imageRect.gameObject.AddComponent<Image>();
            image.color = Hex("FDF0D5");
            image.raycastTarget = false;
            var fitter = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 1f;
            Assign(frame.gameObject.AddComponent<LobbyBackground>(), ("image", image), ("fitter", fitter));
        }

        /// <summary>
        /// 화면 오른쪽의 꾸미기 팝업(화면 높이의 86%, 너비:높이 = 3:4). 왼쪽은 탭, 오른쪽은 아이콘 그리드이며 둘 다 세로로 스크롤된다.
        /// 바깥은 어둡게 하지 않고 투명한 막만 깔아, 누르면 닫히게 한다.
        /// </summary>
        static void BuildCustomizationPopup(Transform canvas, RectTransform screen, Button openButton, StageCatalog stageCatalog)
        {
            const float margin = 40f;
            const float padding = 24f;
            const float tabWidth = 180f;
            var cream = Hex("FDF0D5");

            var root = SetupUi.Stretch("Customization", canvas);
            var popup = root.gameObject.AddComponent<CustomizationPopup>();

            var outside = SetupUi.Stretch("OutsideArea", root);
            var outsideImage = outside.gameObject.AddComponent<Image>();
            outsideImage.color = Color.clear;
            var outsideButton = outside.gameObject.AddComponent<Button>();
            outsideButton.targetGraphic = outsideImage;
            outsideButton.transition = Selectable.Transition.None;
            outsideButton.navigation = new Navigation { mode = Navigation.Mode.None };

            var panel = SetupUi.Rect("Panel", root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-margin, 0f), new Vector2(697f, 0f));
            panel.anchorMin = new Vector2(1f, 0.07f);
            panel.anchorMax = new Vector2(1f, 0.93f);
            var panelFitter = panel.gameObject.AddComponent<AspectRatioFitter>();
            panelFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            panelFitter.aspectRatio = 3f / 4f;
            SetupUi.Panel(panel, SetupUi.Ink, true);
            var panelGroup = panel.gameObject.AddComponent<CanvasGroup>();

            var tabs = SetupUi.Stretch("Tabs", panel);
            tabs.anchorMax = new Vector2(0f, 1f);
            tabs.offsetMin = new Vector2(padding, padding);
            tabs.offsetMax = new Vector2(padding + tabWidth, -padding);
            SetupUi.VerticalScroll(tabs, out var tabContent);
            SetupUi.Stack(tabContent, 12f);
            var tabTemplate = SetupUi.MakeButton("TabTemplate", tabContent, "탭", new Vector2(tabWidth, 96f), 30);

            var items = SetupUi.Stretch("Items", panel);
            items.offsetMin = new Vector2(padding + tabWidth + 20f, padding);
            items.offsetMax = new Vector2(-padding, -padding);
            var itemScroll = SetupUi.VerticalScroll(items, out var itemContent);
            SetupUi.Stack(itemContent, 28f).padding = new RectOffset(0, 0, 0, 16);
            var section = BuildWardrobeSectionTemplate(itemContent, cream);
            var cell = BuildWardrobeCellTemplate(itemContent);

            Assign(popup,
                ("screen", screen), ("openButton", openButton), ("outsideArea", outsideButton),
                ("panel", panel), ("panelGroup", panelGroup),
                ("tabTemplate", tabTemplate), ("itemScroll", itemScroll),
                ("sectionTemplate", section), ("cellTemplate", cell), ("stageCatalog", stageCatalog));
            var serialized = new SerializedObject(popup);
            serialized.FindProperty("rightMargin").floatValue = margin;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            outside.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
        }

        /// <summary>제목과 아이콘 그리드로 된 칸 묶음(플레이어 탭의 "몸통"·"눈" 등). 칸 수는 너비에 맞춰 정해진다.</summary>
        static RectTransform BuildWardrobeSectionTemplate(Transform parent, Color headingColor)
        {
            var section = SetupUi.Rect("SectionTemplate", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            SetupUi.Stack(section, 12f);

            var heading = SetupUi.Rect("Heading", section, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 44f));
            SetupUi.Label(heading, "제목", 30, TextAnchor.MiddleLeft, headingColor, FontStyle.Bold);
            heading.gameObject.AddComponent<LayoutElement>().minHeight = 44f;

            var cells = SetupUi.Rect("Cells", section, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var grid = cells.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132f, 180f);
            grid.spacing = new Vector2(14f, 14f);
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            section.gameObject.SetActive(false);
            return section;
        }

        /// <summary>
        /// 아이콘 칸 하나. 바깥 테두리(장착 표시) 안에 버튼 면이 있고, 그 위에 아이콘(오타마톤·그림·색·글자 중 하나)과 이름을 놓는다.
        /// 잠긴 항목은 내용 전체를 흐리게 하고 이름 자리에 해금 조건을 쓴다.
        /// </summary>
        static WardrobeCellView BuildWardrobeCellTemplate(Transform parent)
        {
            var ink = SetupUi.Ink;
            var cell = SetupUi.Rect("CellTemplate", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(132f, 180f));
            var view = cell.gameObject.AddComponent<WardrobeCellView>();

            var frame = SetupUi.Stretch("EquippedFrame", cell);
            SetupUi.Panel(frame, Hex("E9C46A"), true).raycastTarget = false;

            var face = SetupUi.Stretch("Face", cell);
            face.offsetMin = new Vector2(6f, 6f);
            face.offsetMax = new Vector2(-6f, -6f);
            var faceImage = SetupUi.Panel(face, Color.white, true);
            var button = face.gameObject.AddComponent<Button>();
            button.targetGraphic = faceImage;
            SetupUi.ApplyButtonColors(button);

            var content = SetupUi.Stretch("Content", face);
            var contentGroup = content.gameObject.AddComponent<CanvasGroup>();
            var iconArea = SetupUi.Rect("IconArea", content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(104f, 104f));
            var otamaton = SetupUi.OtamatonUi(SetupUi.Stretch("Otamaton", iconArea), false);
            var icon = SetupUi.Stretch("Icon", iconArea).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var swatch = SetupUi.Panel(SetupUi.Stretch("Swatch", iconArea), Color.white, true);
            swatch.raycastTarget = false;
            var glyph = SetupUi.Label(SetupUi.Stretch("Glyph", iconArea), "♪", 64, TextAnchor.MiddleCenter, ink, FontStyle.Bold);

            var name = SetupUi.Label(SetupUi.Rect("Name", content, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(112f, 48f)),
                "이름", 22, TextAnchor.MiddleCenter, ink);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 14;
            name.resizeTextMaxSize = 22;

            Assign(view,
                ("button", button), ("equippedFrame", frame.gameObject), ("content", contentGroup),
                ("otamatonIcon", otamaton), ("icon", icon), ("swatch", swatch), ("glyph", glyph), ("nameText", name));
            frame.gameObject.SetActive(false);
            cell.gameObject.SetActive(false);
            return view;
        }

        static StageButtonView BuildStageButtonTemplate(Transform parent)
        {
            var button = SetupUi.MakeButton("StageButtonTemplate", parent, "스테이지", new Vector2(460f, 140f), 46);
            var titleRect = (RectTransform)button.transform.Find("Label");
            titleRect.anchorMin = new Vector2(0f, 0.4f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            var statusRect = SetupUi.Stretch("Status", button.transform);
            statusRect.anchorMax = new Vector2(1f, 0.45f);
            var ink = SetupUi.Ink;
            var status = SetupUi.Label(statusRect, "도전 가능", 26, TextAnchor.MiddleCenter, new Color(ink.r, ink.g, ink.b, 0.75f));

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
