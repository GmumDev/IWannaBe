using IWannabe.Rhythm.Charting;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>
    /// 곡 하나의 채보 파이프라인 설정. 인스펙터에서 "곡 분석" → "채보 생성" 순서로 실행한다.
    /// 외부 곡으로 바꾸려면 SongData의 오디오를 교체하고 두 단계를 다시 실행하면 된다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Chart Generation Profile", fileName = "ChartGenerationProfile")]
    public sealed class ChartGenerationProfile : ScriptableObject
    {
        public SongData song;
        public SongAnalysis analysis;
        public PatternLibrary patterns;
        public ChartData output;
        public AnalyzerSettings analyzer = new AnalyzerSettings();
        public GeneratorSettings generator = new GeneratorSettings();
    }
}
