namespace IWannabe.Stages.Slice
{
    /// <summary>베기 스테이지의 큐 ID. 큐마다 입력까지의 간격이 고정돼 있다.</summary>
    public static class SliceCues
    {
        /// <summary>던진 과일을 1박 뒤에 벤다.</summary>
        public const string Toss = "toss";
        /// <summary>높이 던진 대나무를 2박 뒤에 벤다.</summary>
        public const string High = "high";
        /// <summary>1박 뒤 링 안에 멈춘 통나무를 누른 채 난도질하고, tick 다음 박에 떼면 조각나 흩어진다.</summary>
        public const string Draw = "draw";
        public const string Tick = "tick";
        /// <summary>스승이 징으로 들려준 리듬을 4박 뒤에 따라 벤다.</summary>
        public const string Gong = "gong";
    }
}
