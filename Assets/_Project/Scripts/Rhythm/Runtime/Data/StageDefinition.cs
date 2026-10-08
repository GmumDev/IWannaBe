using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 하나: 곡, 채보, 채보가 쓰는 미니게임 목록. 미니게임 스테이지는 미니게임이 하나이고,
    /// 리믹스는 채보의 구간마다 다른 미니게임을 쓴다.
    /// 스테이지마다 Addressables 그룹 하나에 들어가며, 이 에셋을 로드하면 곡·채보와 미니게임 번들이 함께 로드된다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Stage Definition", fileName = "StageDefinition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] string stageId;
        [SerializeField] string displayName;
        [SerializeField] SongData song;
        [SerializeField] ChartData chart;

        [Tooltip("채보 구간이 minigameId로 찾는 미니게임. 구간이 없는 채보는 첫 미니게임 하나로 곡 전체를 플레이한다.")]
        [SerializeField] List<MinigameDefinition> minigames = new List<MinigameDefinition>();

        [SerializeField, Range(0f, 1f)] float musicVolume = 0.9f;

        public string StageId => stageId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public SongData Song => song;
        public ChartData Chart => chart;
        public IReadOnlyList<MinigameDefinition> Minigames => minigames;
        public float MusicVolume => musicVolume;

        /// <summary>ID로 미니게임을 찾는다. ID가 비어 있으면(구간 없는 채보) 첫 미니게임. 없으면 null.</summary>
        public MinigameDefinition FindMinigame(string minigameId)
        {
            if (string.IsNullOrEmpty(minigameId)) return minigames.Count > 0 ? minigames[0] : null;
            foreach (var minigame in minigames)
                if (minigame != null && minigame.MinigameId == minigameId) return minigame;
            return null;
        }

        public void Setup(string id, string title, SongData songData, ChartData chartData, List<MinigameDefinition> games)
        {
            stageId = id;
            displayName = title;
            song = songData;
            chart = chartData;
            minigames = games ?? new List<MinigameDefinition>();
        }
    }
}
