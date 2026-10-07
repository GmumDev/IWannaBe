using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace IWannabe.Rhythm
{
    [Serializable]
    public sealed class StageReference : AssetReferenceT<StageDefinition>
    {
        public StageReference(string guid) : base(guid) { }
    }

    [Serializable]
    public sealed class StageCatalogEntry
    {
        public string displayName;
        public StageReference stage;
    }

    /// <summary>
    /// 로비에 보여 줄 스테이지 목록. 표시 이름은 여기 두어 스테이지 본체를 로드하지 않고도 버튼을 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [SerializeField] List<StageCatalogEntry> stages = new List<StageCatalogEntry>();

        public IReadOnlyList<StageCatalogEntry> Stages => stages;

        public void SetStages(List<StageCatalogEntry> entries) => stages = entries ?? new List<StageCatalogEntry>();
    }
}
