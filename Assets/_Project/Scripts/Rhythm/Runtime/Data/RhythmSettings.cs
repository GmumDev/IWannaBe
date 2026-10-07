using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>모든 스테이지가 공유하는 판정·재생 설정.</summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Rhythm Settings", fileName = "RhythmSettings")]
    public sealed class RhythmSettings : ScriptableObject
    {
        [SerializeField] JudgeWindows judgeWindows = JudgeWindows.Default;

        [Tooltip("스테이지 시작 시 재생 예약부터 곡 0초까지의 여유(초).")]
        [SerializeField] double startLeadSeconds = 1.5;

        [Tooltip("일시정지 해제 후 재생 재개까지의 여유(초).")]
        [SerializeField] double resumeLeadSeconds = 1.0;

        [Tooltip("큐 효과음을 오디오 시계에 미리 예약하는 시간(초). 프레임이 밀려도 정확한 시점에 울린다.")]
        [SerializeField] double cueScheduleAhead = 0.25;

        [Tooltip("패턴 연출을 첫 큐보다 먼저 준비시키는 시간(초).")]
        [SerializeField] double patternSpawnLead = 0.6;

        [Tooltip("마지막 노트 뒤로 결과 화면까지 기다리는 최소 시간(초).")]
        [SerializeField] double endPaddingSeconds = 1.0;

        public JudgeWindows JudgeWindows => judgeWindows;
        public double StartLeadSeconds => startLeadSeconds;
        public double ResumeLeadSeconds => resumeLeadSeconds;
        public double CueScheduleAhead => cueScheduleAhead;
        public double PatternSpawnLead => patternSpawnLead;
        public double EndPaddingSeconds => endPaddingSeconds;
    }
}
