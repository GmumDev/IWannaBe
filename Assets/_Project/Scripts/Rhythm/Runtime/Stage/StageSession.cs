using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>로비에서 고른 스테이지를 플레이 씬으로 넘긴다.</summary>
    public static class StageSession
    {
        public static StageReference Requested { get; private set; }

        public static void Request(StageReference stage) => Requested = stage;

        public static void Clear() => Requested = null;

        // 도메인 리로드를 끈 에디터 설정에서도 이전 플레이 값이 남지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Requested = null;
    }
}
