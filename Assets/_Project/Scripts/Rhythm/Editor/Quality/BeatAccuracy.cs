using System;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>분석기가 찾은 비트를 정답 비트와 비교한 결과.</summary>
    public sealed class BeatAccuracy
    {
        /// <summary>찾은 BPM / 실제 BPM. 0.5나 2 근처면 절반·두 배 템포로 잡은 것이다.</summary>
        public double BpmRatio;
        /// <summary>실제 박 중 ±20ms / ±50ms 안에 찾은 비트가 있는 비율.</summary>
        public double Within20ms;
        public double Within50ms;
        /// <summary>찾은 비트 중 ±50ms 안에 실제 박이 있는 비율. 두 배 템포·엇박으로 잡으면 낮다.</summary>
        public double Precision50ms;
        /// <summary>±50ms 안에서 짝지은 박의 오차(찾은 시각 - 실제 시각) 중앙값과 절댓값 평균(초).</summary>
        public double MedianError;
        public double MeanAbsError;
        /// <summary>실제 마디 첫 박 중, 짝지은 비트가 분석 결과에서도 마디 첫 박인 비율.</summary>
        public double DownbeatMatch;

        public static BeatAccuracy Measure(double[] detected, int detectedDownbeat, int detectedBpb, double[] truth, int truthDownbeat, int truthBpb)
        {
            var result = new BeatAccuracy();
            if (detected == null || detected.Length < 2 || truth == null || truth.Length < 2) return result;

            result.BpmRatio = MedianInterval(truth) / MedianInterval(detected);

            int hit20 = 0, hit50 = 0, downbeats = 0, downbeatHits = 0;
            var errors = new double[truth.Length];
            int matched = 0;
            double absSum = 0;
            for (int k = 0; k < truth.Length; k++)
            {
                int nearest = Nearest(detected, truth[k]);
                double error = detected[nearest] - truth[k];
                bool isDownbeat = k >= truthDownbeat && (k - truthDownbeat) % truthBpb == 0;
                if (isDownbeat) downbeats++;
                if (Math.Abs(error) <= 0.02) hit20++;
                if (Math.Abs(error) <= 0.05)
                {
                    hit50++;
                    errors[matched++] = error;
                    absSum += Math.Abs(error);
                    if (isDownbeat && nearest >= detectedDownbeat && (nearest - detectedDownbeat) % detectedBpb == 0) downbeatHits++;
                }
            }

            int precise = 0;
            foreach (var t in detected)
                if (Math.Abs(truth[Nearest(truth, t)] - t) <= 0.05) precise++;

            result.Within20ms = hit20 / (double)truth.Length;
            result.Within50ms = hit50 / (double)truth.Length;
            result.Precision50ms = precise / (double)detected.Length;
            result.DownbeatMatch = downbeats > 0 ? downbeatHits / (double)downbeats : 0;
            if (matched > 0)
            {
                var used = new double[matched];
                Array.Copy(errors, used, matched);
                Array.Sort(used);
                result.MedianError = used[matched / 2];
                result.MeanAbsError = absSum / matched;
            }
            return result;
        }

        static double MedianInterval(double[] times)
        {
            var d = new double[times.Length - 1];
            for (int i = 0; i < d.Length; i++) d[i] = times[i + 1] - times[i];
            Array.Sort(d);
            return d[d.Length / 2];
        }

        static int Nearest(double[] sorted, double t)
        {
            int index = Array.BinarySearch(sorted, t);
            if (index >= 0) return index;
            index = ~index;
            if (index == 0) return 0;
            if (index >= sorted.Length) return sorted.Length - 1;
            return t - sorted[index - 1] <= sorted[index] - t ? index - 1 : index;
        }

        public override string ToString() =>
            $"BPM 비 {BpmRatio:0.00}, ±20ms {Within20ms:P0}, ±50ms {Within50ms:P0}, 정밀도 {Precision50ms:P0}, " +
            $"오차 중앙 {MedianError * 1000:+0.0;-0.0}ms·평균 {MeanAbsError * 1000:0.0}ms, 다운비트 {DownbeatMatch:P0}";
    }
}
