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
        [Tooltip("StageDefinition의 stageId와 같아야 한다. 클리어 기록의 키로 쓰인다.")]
        public string stageId;
        public string displayName;
        public StageReference stage;
    }

    /// <summary>
    /// 로비에 보여 줄 스테이지 목록(진행 순서). 표시 이름·ID는 여기 두어 스테이지 본체를 로드하지 않고도
    /// 버튼과 해금 상태를 만든다. 앞 스테이지를 클리어해야 다음 스테이지가 열린다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [SerializeField] List<StageCatalogEntry> stages = new List<StageCatalogEntry>();

        public IReadOnlyList<StageCatalogEntry> Stages => stages;

        public void SetStages(List<StageCatalogEntry> entries) => stages = entries ?? new List<StageCatalogEntry>();

        public bool IsUnlocked(int index)
        {
            if (index <= 0) return true;
            return index < stages.Count && StageProgress.IsCleared(stages[index - 1].stageId);
        }
    }
}
