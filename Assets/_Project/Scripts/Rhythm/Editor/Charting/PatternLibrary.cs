using System.Collections.Generic;
using IWannabe.Rhythm.Charting;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>
    /// 미니게임 하나가 채보 생성에 쓰는 패턴 목록. cueId는 그 미니게임(MinigameDefinition)의 큐 효과음과 연출이 해석하므로,
    /// 미니게임 스테이지와 그 미니게임이 나오는 리믹스 구간이 같은 라이브러리를 쓴다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Pattern Library", fileName = "PatternLibrary")]
    public sealed class PatternLibrary : ScriptableObject
    {
        [SerializeField] List<PatternDefinition> patterns = new List<PatternDefinition>();

        public IReadOnlyList<PatternDefinition> Patterns => patterns;

        public void SetPatterns(List<PatternDefinition> value) => patterns = value ?? new List<PatternDefinition>();
    }
}
