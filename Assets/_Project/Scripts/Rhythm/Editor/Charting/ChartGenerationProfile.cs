using System;
using System.Collections.Generic;
using IWannabe.Rhythm.Charting;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>
    /// 곡 하나의 채보 파이프라인 설정. 인스펙터에서 "곡 분석" → "채보 생성" 순서로 실행한다.
    /// 외부 곡으로 바꾸려면 SongData의 오디오를 교체하고 두 단계를 다시 실행하면 된다.
    /// 리믹스는 구간 목록을 채우면 곡 전체를 한 번에 생성하되, 패턴은 큐가 들어가는 구간의 미니게임 패턴 라이브러리에서 고른다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Chart Generation Profile", fileName = "ChartGenerationProfile")]
    public sealed class ChartGenerationProfile : ScriptableObject
    {
        /// <summary>리믹스 구간 하나: 어느 마디부터 어떤 미니게임을 어떤 패턴으로 채울지.</summary>
        [Serializable]
        public sealed class SegmentPlan
        {
            [Tooltip("이 구간을 맡는 미니게임.")]
            public MinigameDefinition minigame;
            [Tooltip("이 미니게임의 패턴 라이브러리. 그 미니게임 연출이 해석하는 cueId만 들어 있어야 한다.")]
            public PatternLibrary patterns;
            [Tooltip("구간이 시작하는 마디(첫 다운비트가 0). 첫 구간은 0이고, 뒤로 갈수록 커야 한다.")]
            [Min(0)] public int startBar;
        }

        public SongData song;
        public SongAnalysis analysis;
        [Tooltip("미니게임 하나짜리 스테이지의 패턴. 구간 목록을 채우면 쓰지 않는다.")]
        public PatternLibrary patterns;
        [Tooltip("리믹스처럼 곡 중간에 미니게임이 바뀔 때 채운다. 비우면 위 패턴 하나로 곡 전체를 만든다.")]
        public List<SegmentPlan> segments = new List<SegmentPlan>();
        public ChartData output;
        public AnalyzerSettings analyzer = new AnalyzerSettings();
        public GeneratorSettings generator = new GeneratorSettings();

        public bool HasPatterns => segments.Count > 0 || patterns != null;
    }
}
