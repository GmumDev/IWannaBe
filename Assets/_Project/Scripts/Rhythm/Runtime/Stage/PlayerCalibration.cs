using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>기기별 입력 지연 보정값. 기기마다 오디오 출력·터치 인식 지연이 달라 플레이어가 맞춘다.</summary>
    public static class PlayerCalibration
    {
        const string InputOffsetKey = "IWannabe.InputOffsetMs";
        public const int MinOffsetMs = -300;
        public const int MaxOffsetMs = 300;

        /// <summary>양수면 입력이 늦게 인식되는 기기라는 뜻이며, 판정할 때 입력 시각에서 뺀다.</summary>
        public static int InputOffsetMs
        {
            get => PlayerPrefs.GetInt(InputOffsetKey, 0);
            set
            {
                PlayerPrefs.SetInt(InputOffsetKey, Mathf.Clamp(value, MinOffsetMs, MaxOffsetMs));
                PlayerPrefs.Save();
            }
        }

        public static double InputOffsetSeconds => InputOffsetMs / 1000.0;
    }
}
