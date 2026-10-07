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
    /// 스테이지 하나를 구성하는 규칙. 이름이 곧 폴더·파일·Addressables 그룹 이름이 된다.
    /// 예) AssetName "HitBack" → Music/HitBack.wav, SFX/HitBack/, Data/Stages/HitBack/HitBack_*.asset,
    /// Prefabs/Stages/HitBackStage.prefab, 그룹 Stage_HitBack, 주소 Stages/HitBack.
    /// </summary>
    abstract class StageRecipe
    {
        public abstract string StageId { get; }
        public abstract string DisplayName { get; }
        public abstract string AssetName { get; }
        public abstract Color Background { get; }
        public abstract Color HudInk { get; }

        public string GroupName => $"Stage_{AssetName}";
        public string Address => $"Stages/{AssetName}";
        public string MusicPath => $"{SetupPaths.Music}/{AssetName}.wav";
        public string SfxFolder => $"{SetupPaths.Sfx}/{AssetName}";
        public string DataFolder => $"{SetupPaths.StageData}/{AssetName}";
        public string PrefabPath => $"{SetupPaths.StagePrefabs}/{AssetName}Stage.prefab";
        public string AssetPath(string kind) => $"{DataFolder}/{AssetName}_{kind}.asset";

        /// <summary>패턴 라이브러리가 비어 있을 때 채울 기본 패턴.</summary>
        public abstract List<PatternDefinition> CreatePatterns();

        /// <summary>채보 생성 프로필을 처음 만들 때의 생성 설정.</summary>
        public virtual GeneratorSettings CreateGeneratorSettings() => new GeneratorSettings();

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
}
