using System;

namespace IWannabe.Rhythm.Charting
{
    public enum TempoMode
    {
        /// <summary>찾은 비트가 BPM 하나짜리 격자에 맞으면 고정 템포, 아니면 가변 템포로 처리(<see cref="BeatStability.FitsConstantGrid"/>).</summary>
        Auto,
        /// <summary>BPM 하나로 직선 회귀. 전자음악·녹음 템포가 일정한 곡.</summary>
        Constant,
        /// <summary>비트별 시각을 유지하고 지터만 완화. 라이브 녹음 등.</summary>
        Variable,
    }

    public enum OnsetBand
    {
        Full,
        /// <summary>약 30~150Hz. 킥·베이스.</summary>
        Low,
        /// <summary>약 150~2500Hz. 스네어·보컬·멜로디.</summary>
        Mid,
        /// <summary>약 2.5~16kHz. 하이햇·심벌.</summary>
        High,
    }

    [Serializable]
    public sealed class AnalyzerSettings
    {
        public int fftSize = 2048;
        public int hopSize = 256;
        public double minBpm = 70;
        public double maxBpm = 180;
        /// <summary>템포 후보가 여럿일 때 가까운 쪽을 선호하는 기준 BPM.</summary>
        public double preferredBpm = 120;
        public int beatsPerBar = 4;
        public TempoMode tempoMode = TempoMode.Auto;
        /// <summary>
        /// 검출된 비트 시각에 더하는 보정(초). 스펙트럼 창 때문에 온셋 피크가 실제 타격보다
        /// 약간 앞서 잡히는 경향을 상쇄한다. 기본값은 합성 테스트 곡으로 측정한 값(약 -2.8ms).
        /// </summary>
        public double timeBias = 0.003;
        /// <summary>0 이상이면 다운비트(마디 첫 박) 위상을 강제로 지정한다.</summary>
        public int downbeatOverride = -1;
        /// <summary>마디 에너지(0~1)를 강도 단계 0/1/2로 나누는 경계.</summary>
        public float midLevelThreshold = 0.35f;
        public float highLevelThreshold = 0.8f;
        /// <summary>박자가 안정적인지 판정하는 기준.</summary>
        public BeatStabilityCriteria stability = new BeatStabilityCriteria();
    }

    /// <summary>곡 분석 결과. 채보 생성기의 입력이 된다.</summary>
    public sealed class AnalysisResult
    {
        public int SampleRate;
        public double Duration;
        public double Bpm;
        public bool ConstantTempo;
        public double[] BeatTimes;
        public int BeatsPerBar;
        /// <summary>BeatTimes 안에서 첫 마디 첫 박의 인덱스.</summary>
        public int FirstDownbeatIndex;
        public int GridPerBeat;
        /// <summary>대역별 온셋 강도(0~1). 인덱스 = 비트 * GridPerBeat + 박 내부 칸.</summary>
        public float[] GridFull;
        public float[] GridLow;
        public float[] GridMid;
        public float[] GridHigh;
        /// <summary>FirstDownbeatIndex부터 센 마디별 에너지(0~1).</summary>
        public float[] BarEnergy;
        /// <summary>마디별 강도 단계(0 조용함 ~ 2 격렬함).</summary>
        public int[] BarLevel;
        public double[] OnsetTimes;
        /// <summary>박자 안정성 지표와 판정. 저장된 분석(<c>SongAnalysis</c>)에서 되살린 결과에는 없다.</summary>
        public BeatStability Stability;

        public float[] GetGrid(OnsetBand band)
        {
            switch (band)
            {
                case OnsetBand.Low: return GridLow;
                case OnsetBand.Mid: return GridMid;
                case OnsetBand.High: return GridHigh;
                default: return GridFull;
            }
        }
    }
}
