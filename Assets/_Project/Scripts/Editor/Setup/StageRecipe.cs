using System.Collections.Generic;
using IWannabe.Rhythm;
using IWannabe.Rhythm.Charting;
using UnityEditor;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>연출 프리팹을 만들 때 레시피에 넘겨주는 공용 재료.</summary>
    sealed class StagePrefabKit
    {
        public ShapeSprites Shapes;
        /// <summary>플레이어 캐릭터의 기본 파츠.</summary>
        public OtamatonSprites Otamaton;
        public Material SpriteMaterial;
    }

    /// <summary>
    /// 스테이지 하나(곡·채보·Addressables 그룹)를 구성하는 규칙. 이름이 곧 폴더·파일·그룹 이름이 된다.
    /// 예) AssetName "HitBack" → Data/Stages/HitBack/HitBack_*.asset, 그룹 Stage_HitBack, 주소 Stages/HitBack.
    /// </summary>
    abstract class StageRecipe
    {
        public abstract string StageId { get; }
        public abstract string DisplayName { get; }
        public abstract string AssetName { get; }

        public string GroupName => $"Stage_{AssetName}";
        public string Address => $"Stages/{AssetName}";
        public virtual string MusicPath => $"{SetupPaths.Music}/{AssetName}.wav";
        public string DataFolder => $"{SetupPaths.StageData}/{AssetName}";
        public string AssetPath(string kind) => $"{DataFolder}/{AssetName}_{kind}.asset";

        /// <summary>채보 생성 프로필을 처음 만들 때의 생성 설정.</summary>
        public virtual GeneratorSettings CreateGeneratorSettings() => new GeneratorSettings();
    }

    /// <summary>
    /// 미니게임 하나와 그 미니게임 전용 곡으로 된 스테이지. 미니게임(연출 프리팹·큐 효과음·패턴 라이브러리)도 이 레시피가 만들며,
    /// 리믹스는 이 레시피가 만든 미니게임을 가져다 쓴다.
    /// 예) Music/HitBack.wav, SFX/HitBack/, Prefabs/Stages/HitBackStage.prefab, Data/Stages/HitBack/HitBack_Minigame.asset,
    /// 미니게임 그룹 Minigame_HitBack, 주소 Minigames/HitBack.
    /// </summary>
    abstract class MinigameStageRecipe : StageRecipe
    {
        public virtual string MinigameId => StageId;
        public abstract Color Background { get; }
        public abstract Color HudInk { get; }

        public string MinigameGroupName => $"Minigame_{AssetName}";
        public string MinigameAddress => $"Minigames/{AssetName}";
        public string SfxFolder => $"{SetupPaths.Sfx}/{AssetName}";
        public string PrefabPath => $"{SetupPaths.StagePrefabs}/{AssetName}Stage.prefab";

        /// <summary>패턴 라이브러리가 비어 있을 때 채울 기본 패턴.</summary>
        public abstract List<PatternDefinition> CreatePatterns();

        /// <summary>(큐 ID, SFX 폴더 안 파일 이름, 볼륨)</summary>
        public abstract IEnumerable<(string cueId, string sfx, float volume)> CueSounds { get; }

        /// <summary>연출 프리팹의 계층을 만든다. 프리팹이 없거나 다시 만들기를 요청했을 때만 쓰인다.</summary>
        public abstract GameObject BuildPresenterRoot(StagePrefabKit kit);

        public AudioClip LoadSfx(string name) => SetupUtil.LoadRequired<AudioClip>($"{SfxFolder}/{name}.wav");

        /// <summary>
        /// 연출 프리팹을 돌려준다. 이미 있으면 그대로 쓰고, <paramref name="rebuild"/>면 레시피대로 덮어쓴다
        /// (GUID는 유지되지만 내부 오브젝트가 새로 만들어지므로, 손으로 고친 내용은 사라진다).
        /// </summary>
        public StagePresenter EnsurePresenterPrefab(StagePrefabKit kit, bool rebuild)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && !rebuild)
            {
                var presenter = existing.GetComponent<StagePresenter>();
                if (presenter != null) return presenter;
            }

            var root = BuildPresenterRoot(kit);
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null) throw new System.IO.IOException($"프리팹을 저장하지 못했습니다: {PrefabPath}");
                return prefab.GetComponent<StagePresenter>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }

    /// <summary>
    /// 미니게임 여러 개가 한 곡 안에서 번갈아 나오는 리믹스 스테이지. 구간마다 그 미니게임의 패턴 라이브러리로 채보를 만든다.
    /// 구간 목록은 채보 생성 프로필을 처음 만들 때만 채우므로, 이후에는 프로필에서 손으로 고쳐도 된다.
    /// </summary>
    abstract class RemixStageRecipe : StageRecipe
    {
        /// <summary>(미니게임 스테이지 레시피, 시작 마디). 첫 구간은 0마디이고, 미니게임은 이 레시피보다 먼저 구성돼 있어야 한다.</summary>
        public abstract IReadOnlyList<(MinigameStageRecipe minigame, int startBar)> Segments { get; }
    }
}
