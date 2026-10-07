using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>스테이지 채보. 박 단위 패턴 배치 목록이며, 플레이 시 템포 맵으로 초 단위로 풀린다.</summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Chart Data", fileName = "ChartData")]
    public sealed class ChartData : ScriptableObject
    {
        [SerializeField] List<PatternInstance> patterns = new List<PatternInstance>();

        [Tooltip("생성기가 남긴 정보(시드, 설정, 통계). 손으로 고쳤다면 메모로 써도 된다.")]
        [SerializeField, TextArea(2, 6)] string generationInfo;

        public IReadOnlyList<PatternInstance> Patterns => patterns;
        public string GenerationInfo => generationInfo;

        public void SetPatterns(List<PatternInstance> value, string info)
        {
            patterns = value ?? new List<PatternInstance>();
            generationInfo = info;
        }
    }
}
