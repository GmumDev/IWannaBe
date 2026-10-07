using System;

namespace IWannabe.Rhythm
{
    public enum JudgeGrade
    {
        Perfect,
        /// <summary>판정 범위 안이지만 정확하지 않은 입력(리듬세상의 "아슬아슬").</summary>
        Barely,
        Miss,
    }

    public enum NotePhase
    {
        Press,
        Release,
    }

    /// <summary>판정 범위(초). 입력 시각과 목표 시각의 차이 절댓값으로 비교한다.</summary>
    [Serializable]
    public struct JudgeWindows
    {
        public double perfect;
        public double barely;
        public double releasePerfect;
        public double releaseBarely;

        public static JudgeWindows Default => new JudgeWindows
        {
            perfect = 0.05,
            barely = 0.1,
            releasePerfect = 0.07,
            releaseBarely = 0.14,
        };
    }

    public readonly struct NoteJudgement
    {
        public readonly TimelineNote Note;
        public readonly NotePhase Phase;
        public readonly JudgeGrade Grade;
        /// <summary>입력 시각 - 목표 시각(초). 음수면 빠름. 입력이 없었으면 NaN.</summary>
        public readonly double Delta;

        public NoteJudgement(TimelineNote note, NotePhase phase, JudgeGrade grade, double delta)
        {
            Note = note;
            Phase = phase;
            Grade = grade;
            Delta = delta;
        }

        public bool HasInput => !double.IsNaN(Delta);
    }
}
