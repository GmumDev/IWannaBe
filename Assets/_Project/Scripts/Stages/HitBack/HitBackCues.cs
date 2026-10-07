namespace IWannabe.Stages.HitBack
{
    /// <summary>
    /// 받아치기 스테이지의 큐 ID. 큐 종류만 듣고 입력 타이밍을 알 수 있도록 큐마다 간격이 고정돼 있다.
    /// </summary>
    public static class HitBackCues
    {
        /// <summary>1박 뒤에 친다.</summary>
        public const string Throw = "throw";
        /// <summary>2박 뒤에 친다(높이 뜬 큰 공).</summary>
        public const string Lob = "lob";
        /// <summary>1박 뒤에 눌러 잡고, tick 다음 박에 뗀다.</summary>
        public const string Charge = "charge";
        public const string Tick = "tick";
        /// <summary>같은 리듬을 4박 뒤에 따라 친다.</summary>
        public const string Bell = "bell";
    }
}
