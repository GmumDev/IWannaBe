using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 클리어 기록. 아직 저장·불러오기는 없어 앱을 끄면 초기화된다.
    /// 저장이 생기면 이 클래스의 읽기/쓰기만 저장소로 바꾸면 된다.
    /// </summary>
    public static class StageProgress
    {
        static readonly HashSet<string> Cleared = new HashSet<string>();

        public static bool IsCleared(string stageId) => !string.IsNullOrEmpty(stageId) && Cleared.Contains(stageId);

        public static void MarkCleared(string stageId)
        {
            if (!string.IsNullOrEmpty(stageId)) Cleared.Add(stageId);
        }

        /// <summary>리듬세상처럼 "OK" 이상이면 클리어로 친다.</summary>
        public static bool IsClearingRank(StageRank rank) => rank != StageRank.TryAgain;

        // 도메인 리로드를 끈 에디터에서도 플레이할 때마다 새로 시작한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Cleared.Clear();
    }
}
