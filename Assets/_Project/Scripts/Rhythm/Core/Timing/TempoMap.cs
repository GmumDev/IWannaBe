using System;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 비트 번호와 곡 시간(초)을 서로 변환한다.
    /// 비트 시각 배열 사이는 선형 보간하고, 범위 밖은 양 끝 구간의 템포로 외삽한다.
    /// 녹음된 곡은 템포가 미세하게 흔들리므로 BPM 하나 대신 비트별 시각을 기준으로 삼는다.
    /// </summary>
    public sealed class TempoMap
    {
        readonly double[] beatTimes;

        public TempoMap(double[] beatTimes)
        {
            if (beatTimes == null || beatTimes.Length < 2)
                throw new ArgumentException("비트 시각이 최소 2개 필요합니다.", nameof(beatTimes));
            for (int i = 1; i < beatTimes.Length; i++)
            {
                if (beatTimes[i] <= beatTimes[i - 1])
                    throw new ArgumentException($"비트 시각은 증가해야 합니다. (index {i})", nameof(beatTimes));
            }
            this.beatTimes = (double[])beatTimes.Clone();
        }

        public int BeatCount => beatTimes.Length;
        public double FirstBeatTime => beatTimes[0];
        public double LastBeatTime => beatTimes[beatTimes.Length - 1];

        public double BeatToTime(double beat)
        {
            int last = beatTimes.Length - 1;
            if (beat <= 0)
                return beatTimes[0] + beat * (beatTimes[1] - beatTimes[0]);
            if (beat >= last)
                return beatTimes[last] + (beat - last) * (beatTimes[last] - beatTimes[last - 1]);

            int i = (int)Math.Floor(beat);
            double f = beat - i;
            return beatTimes[i] + f * (beatTimes[i + 1] - beatTimes[i]);
        }

        public double TimeToBeat(double time)
        {
            int last = beatTimes.Length - 1;
            if (time <= beatTimes[0])
                return (time - beatTimes[0]) / (beatTimes[1] - beatTimes[0]);
            if (time >= beatTimes[last])
                return last + (time - beatTimes[last]) / (beatTimes[last] - beatTimes[last - 1]);

            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (beatTimes[mid] <= time) lo = mid;
                else hi = mid;
            }
            return lo + (time - beatTimes[lo]) / (beatTimes[lo + 1] - beatTimes[lo]);
        }

        /// <summary>해당 비트 위치의 한 박 길이(초).</summary>
        public double SecondsPerBeatAt(double beat)
        {
            int last = beatTimes.Length - 1;
            int i = (int)Math.Floor(beat);
            if (i < 0) i = 0;
            if (i >= last) i = last - 1;
            return beatTimes[i + 1] - beatTimes[i];
        }
    }
}
