using System;
using UnityEngine;

namespace IWannabe.Otamaton
{
    /// <summary>
    /// 플레이어가 꾸미기에서 고른 오타마톤 모습. 앱(꾸미기)이 정하고, 로비·스테이지의 캐릭터가 읽는다.
    /// 정해지지 않았으면 각 캐릭터가 가진 기본 모습을 쓴다.
    /// </summary>
    public static class OtamatonAppearance
    {
        public static OtamatonBody Body { get; private set; }
        public static OtamatonEyes Eyes { get; private set; }

        public static event Action Changed;

        public static void Set(OtamatonBody body, OtamatonEyes eyes)
        {
            Body = body;
            Eyes = eyes;
            Changed?.Invoke();
        }

        // 도메인 리로드를 끈 에디터에서도 플레이할 때마다 새로 시작한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Body = null;
            Eyes = null;
            Changed = null;
        }
    }
}
