namespace IWannabe.Rhythm
{
    public enum StageRank
    {
        TryAgain,
        Ok,
        Superb,
    }

    /// <summary>판정 결과를 집계한다. Barely는 절반 점수.</summary>
    public sealed class ScoreTracker
    {
        public const double SuperbThreshold = 0.8;
        public const double OkThreshold = 0.6;

        public int TotalParts { get; }
        public int Perfect { get; private set; }
        public int Barely { get; private set; }
        public int Miss { get; private set; }

        public ScoreTracker(int totalParts)
        {
            TotalParts = totalParts;
        }

        public int JudgedParts => Perfect + Barely + Miss;

        public double Accuracy => TotalParts == 0 ? 1.0 : (Perfect + 0.5 * Barely) / TotalParts;

        public StageRank Rank => Accuracy >= SuperbThreshold ? StageRank.Superb
            : Accuracy >= OkThreshold ? StageRank.Ok
            : StageRank.TryAgain;

        public void Add(NoteJudgement judgement)
        {
            switch (judgement.Grade)
            {
                case JudgeGrade.Perfect: Perfect++; break;
                case JudgeGrade.Barely: Barely++; break;
                default: Miss++; break;
            }
        }
    }
}
