using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 채보. 박 단위 패턴 배치 목록이며, 플레이 시 템포 맵으로 초 단위로 풀린다.
    /// 리믹스는 구간 목록을 갖고, 패턴마다 자기 구간 번호를 갖는다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Chart Data", fileName = "ChartData")]
    public sealed class ChartData : ScriptableObject
    {
        [SerializeField] List<PatternInstance> patterns = new List<PatternInstance>();

        [Tooltip("곡 중간에 미니게임이 바뀌는 구간 목록(리믹스). 비어 있으면 스테이지의 첫 미니게임 하나로 곡 전체를 플레이한다.")]
        [SerializeField] List<ChartSegment> segments = new List<ChartSegment>();

        [Tooltip("생성기가 남긴 정보(시드, 설정, 통계). 손으로 고쳤다면 메모로 써도 된다.")]
        [SerializeField, TextArea(2, 8)] string generationInfo;

        public IReadOnlyList<PatternInstance> Patterns => patterns;
        public IReadOnlyList<ChartSegment> Segments => segments;
        public string GenerationInfo => generationInfo;

        public void SetChart(List<PatternInstance> value, List<ChartSegment> segmentList, string info)
        {
            patterns = value ?? new List<PatternInstance>();
            segments = segmentList ?? new List<ChartSegment>();
            generationInfo = info;
        }
    }
}
