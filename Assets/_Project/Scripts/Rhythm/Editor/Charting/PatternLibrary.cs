using System.Collections.Generic;
using IWannabe.Rhythm.Charting;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>스테이지가 채보 생성에 쓰는 패턴 목록. cueId는 StageDefinition의 큐 효과음과 연출이 해석한다.</summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Pattern Library", fileName = "PatternLibrary")]
    public sealed class PatternLibrary : ScriptableObject
    {
        [SerializeField] List<PatternDefinition> patterns = new List<PatternDefinition>();

        public IReadOnlyList<PatternDefinition> Patterns => patterns;

        public void SetPatterns(List<PatternDefinition> value) => patterns = value ?? new List<PatternDefinition>();

        [ContextMenu("기본 패턴 세트로 채우기")]
        void FillWithStarterSet()
        {
            patterns = PatternPresets.CreateStarterSet();
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
}
