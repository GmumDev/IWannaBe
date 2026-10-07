using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>스테이지 클리어 기록. 기기(PlayerPrefs)에 저장해 앱을 다시 켜도 유지된다.</summary>
    public static class StageProgress
    {
        const string ClearedKey = "IWannabe.ClearedStages";
        const string Separator = ",";

        static HashSet<string> cleared;

        static HashSet<string> Cleared => cleared ??= new HashSet<string>(
            PlayerPrefs.GetString(ClearedKey, string.Empty).Split(Separator.ToCharArray(), System.StringSplitOptions.RemoveEmptyEntries));

        public static bool IsCleared(string stageId) => !string.IsNullOrEmpty(stageId) && Cleared.Contains(stageId);

        public static void MarkCleared(string stageId)
        {
            if (string.IsNullOrEmpty(stageId) || !Cleared.Add(stageId)) return;
            PlayerPrefs.SetString(ClearedKey, string.Join(Separator, Cleared));
            PlayerPrefs.Save();
        }

        /// <summary>리듬세상처럼 "OK" 이상이면 클리어로 친다.</summary>
        public static bool IsClearingRank(StageRank rank) => rank != StageRank.TryAgain;

        // 도메인 리로드를 끈 에디터에서도 플레이할 때마다 저장된 기록을 새로 읽는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cleared = null;
    }
}
