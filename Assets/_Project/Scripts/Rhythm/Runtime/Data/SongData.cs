using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>곡 오디오와 비트 지도. 곡 분석기가 채우고, 필요하면 사람이 오프셋을 손본다.</summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Song Data", fileName = "SongData")]
    public sealed class SongData : ScriptableObject
    {
        [SerializeField] AudioClip clip;

        [Tooltip("곡 분석기가 채운 비트별 시각(초).")]
        [SerializeField] double[] beatTimes = new double[0];

        [SerializeField, Min(2)] int beatsPerBar = 4;

        [Tooltip("beatTimes 안에서 첫 마디 첫 박의 인덱스.")]
        [SerializeField, Min(0)] int firstDownbeatIndex;

        [SerializeField] float bpm;

        [Tooltip("모든 비트 시각에 더하는 수동 보정(초). 분석 결과가 전체적으로 어긋날 때 쓴다.")]
        [SerializeField] double offsetSeconds;

        public AudioClip Clip => clip;
        public IReadOnlyList<double> BeatTimes => beatTimes;
        public int BeatsPerBar => beatsPerBar;
        public int FirstDownbeatIndex => firstDownbeatIndex;
        public float Bpm => bpm;
        public double OffsetSeconds => offsetSeconds;
        public bool HasBeatMap => beatTimes != null && beatTimes.Length >= 2;

        public TempoMap CreateTempoMap()
        {
            var times = new double[beatTimes.Length];
            for (int i = 0; i < times.Length; i++) times[i] = beatTimes[i] + offsetSeconds;
            return new TempoMap(times);
        }

        public void SetClip(AudioClip value) => clip = value;

        public void SetBeatMap(double[] times, int barLength, int downbeatIndex, float estimatedBpm)
        {
            beatTimes = times ?? new double[0];
            beatsPerBar = Mathf.Max(2, barLength);
            firstDownbeatIndex = Mathf.Max(0, downbeatIndex);
            bpm = estimatedBpm;
        }
    }
}
