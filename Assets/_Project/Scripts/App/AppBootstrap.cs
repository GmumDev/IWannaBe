using UnityEngine;

namespace IWannabe.App
{
    static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            // 모바일 기본값(30fps)이면 연출이 끊겨 보인다. 화면 주사율에 맞춘다.
            int refreshRate = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = Mathf.Max(60, refreshRate);
        }
    }
}
