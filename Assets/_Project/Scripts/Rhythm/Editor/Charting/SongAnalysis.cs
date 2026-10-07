using System;
using IWannabe.Rhythm.Charting;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>
    /// 곡 분석 결과 원본. 채보 생성기의 입력이며 에디터에서만 쓴다(빌드에 들어가지 않음).
    /// 플레이에 필요한 비트 지도는 <see cref="SongData"/>에 따로 복사된다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Song Analysis", fileName = "SongAnalysis")]
    public sealed class SongAnalysis : ScriptableObject
    {
        [SerializeField] AudioClip sourceClip;
        [SerializeField] string analyzedAt;
        [SerializeField] int sampleRate;
        [SerializeField] double duration;
        [SerializeField] double bpm;
        [SerializeField] bool constantTempo;
        [SerializeField] double[] beatTimes = new double[0];
        [SerializeField] int beatsPerBar = 4;
        [SerializeField] int firstDownbeatIndex;
        [SerializeField] int gridPerBeat = AudioAnalyzer.GridPerBeat;
        [SerializeField, HideInInspector] float[] gridFull = new float[0];
        [SerializeField, HideInInspector] float[] gridLow = new float[0];
        [SerializeField, HideInInspector] float[] gridMid = new float[0];
        [SerializeField, HideInInspector] float[] gridHigh = new float[0];
        [SerializeField] float[] barEnergy = new float[0];
        [SerializeField] int[] barLevel = new int[0];
        [SerializeField, HideInInspector] double[] onsetTimes = new double[0];

        public AudioClip SourceClip => sourceClip;
        public string AnalyzedAt => analyzedAt;
        public double Bpm => bpm;
        public bool ConstantTempo => constantTempo;
        public int BeatCount => beatTimes?.Length ?? 0;
        public int FirstDownbeatIndex => firstDownbeatIndex;
        public int[] BarLevel => barLevel;
        public bool HasData => beatTimes != null && beatTimes.Length >= 8 && barLevel != null;

        public void Store(AudioClip clip, AnalysisResult result)
        {
            sourceClip = clip;
            analyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            sampleRate = result.SampleRate;
            duration = result.Duration;
            bpm = result.Bpm;
            constantTempo = result.ConstantTempo;
            beatTimes = result.BeatTimes;
            beatsPerBar = result.BeatsPerBar;
            firstDownbeatIndex = result.FirstDownbeatIndex;
            gridPerBeat = result.GridPerBeat;
            gridFull = result.GridFull;
            gridLow = result.GridLow;
            gridMid = result.GridMid;
            gridHigh = result.GridHigh;
            barEnergy = result.BarEnergy;
            barLevel = result.BarLevel;
            onsetTimes = result.OnsetTimes;
        }

        public AnalysisResult ToResult()
        {
            return new AnalysisResult
            {
                SampleRate = sampleRate,
                Duration = duration,
                Bpm = bpm,
                ConstantTempo = constantTempo,
                BeatTimes = beatTimes,
                BeatsPerBar = beatsPerBar,
                FirstDownbeatIndex = firstDownbeatIndex,
                GridPerBeat = gridPerBeat,
                GridFull = gridFull,
                GridLow = gridLow,
                GridMid = gridMid,
                GridHigh = gridHigh,
                BarEnergy = barEnergy,
                BarLevel = barLevel,
                OnsetTimes = onsetTimes,
            };
        }
    }
}
