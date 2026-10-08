using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    public static partial class AudioAnalyzer
    {
        /// <summary>지역 템포를 재는 창 길이와 이동 간격(박).</summary>
        const int TempoWindowBeats = 8;
        const int TempoWindowHop = 4;
        /// <summary>온셋에 격자를 맞출 때 찾아보는 박 길이 범위(원래 비트 간격 중앙값 기준 비율).</summary>
        const double OnsetGridPeriodRange = 0.015;
        /// <summary>온셋 격자를 창마다 점검하는 창 길이(박).</summary>
        const int OnsetWindowBeats = 8;

        /// <summary>BPM 하나짜리 격자: 시각 = A + B × 박 번호.</summary>
        struct GridLine
        {
            public double A;
            public double B;
        }

        /// <summary>
        /// 동적 계획법으로 찾은 원래 비트(<paramref name="raw"/>, 다듬기 전)가 고정 격자와 지역 회귀에 얼마나 맞는지 잰다.
        /// 원래 비트와 따로, 곡의 온셋에 직접 맞춘 고정 격자(<paramref name="onsetGrid"/>)가 곡 전체에서 온셋에 맞는지도 잰다.
        /// 분석기는 이 결과로 고정 템포 여부를 정하고, 다듬은 뒤 <see cref="FinishStability"/>로 나머지를 잰다.
        /// </summary>
        static BeatStability MeasureTracking(double[] raw, double duration, float[] peaks, double frameRate,
            BeatStabilityCriteria criteria, out GridLine onsetGrid)
        {
            criteria = criteria ?? new BeatStabilityCriteria();
            var s = new BeatStability
            {
                TrackedBeats = raw.Length,
                Coverage = Math.Min(1, (raw[raw.Length - 1] - raw[0]) / duration),
            };

            double median = Median(Diff(raw));
            s.IntervalCv = IntervalCv(raw, median);

            // 고정 격자: 분석기가 고정 템포로 다듬을 때와 같은 직선 회귀.
            FitConstantGrid(raw, median, out double a, out double b, out int[] index);
            var gridError = new double[raw.Length];
            int inliers = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                gridError[i] = Math.Abs(raw[i] - (a + b * index[i]));
                if (gridError[i] < criteria.gridTolerance) inliers++;
            }
            s.GridInlierRatio = inliers / (double)raw.Length;
            s.GridErrorP90 = Percentile(gridError, 0.9);

            // 가변 템포: 분석기가 가변 템포로 다듬을 때와 같은 지역 회귀.
            double[] smooth = LocalRegression(raw, 4);
            var jitter = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++) jitter[i] = Math.Abs(raw[i] - smooth[i]);
            s.LocalJitter = Median(jitter);

            // 지역 템포(초당 박). 빠진 박은 격자 박 번호로 센다.
            var tempos = new List<double>();
            for (int i = 0; i + TempoWindowBeats < raw.Length; i += TempoWindowHop)
            {
                int j = i + TempoWindowBeats;
                double span = raw[j] - raw[i];
                if (span > 0) tempos.Add((index[j] - index[i]) / span);
            }
            if (tempos.Count >= 2)
            {
                var array = tempos.ToArray();
                double center = Median(array);
                s.TempoRange = center > 0 ? (Percentile(array, 0.95) - Percentile(array, 0.05)) / center : 0;
                for (int k = 1; k < array.Length; k++)
                    s.TempoJump = Math.Max(s.TempoJump, Math.Abs(array[k] / array[k - 1] - 1));
            }

            var source = new double[peaks.Length];
            for (int i = 0; i < peaks.Length; i++) source[i] = peaks[i];
            double[] fine = GaussianSmooth(source, 0.008 * frameRate);
            onsetGrid = FitGridToOnsets(raw, median, source, fine, frameRate, duration);
            MeasureOnsetGrid(onsetGrid, fine, frameRate, duration, criteria.onsetWindowTolerance, out s.OnsetGridRatio, out s.OnsetGridErrorP90);
            return s;
        }

        /// <summary>
        /// 원래 비트가 군데군데 엇박으로 미끄러지거나(온셋이 약한 인트로) 박을 하나 더 넣어도, 곡의 템포가 일정하면 격자 하나가 온셋에 맞는다.
        /// 원래 비트 간격 중앙값 근처(±1.5%)에서 박 길이와 위상을 온셋 엔벨로프 합이 가장 큰 쪽으로 찾는다.
        /// 굵게(곡 끝에서 10ms 단위) 찾은 뒤 그 주변을 곱게(1ms 단위) 찾고, 박과 엇박 중에서는 원래 비트 다수가 있는 쪽을 고른다.
        /// </summary>
        static GridLine FitGridToOnsets(double[] raw, double median, double[] peaks, double[] fine, double frameRate, double duration)
        {
            double[] coarse = GaussianSmooth(peaks, 0.03 * frameRate);
            int beats = Math.Max(1, (int)(duration / median));
            double periodStep = 0.01 / beats, phaseStep = 0.01;

            double best = double.NegativeInfinity, bestA = 0, bestB = median;
            for (double b = median * (1 - OnsetGridPeriodRange); b <= median * (1 + OnsetGridPeriodRange); b += periodStep)
            {
                for (double a = 0; a < b; a += phaseStep)
                {
                    double score = GridScore(coarse, a, b, duration, frameRate);
                    if (score > best)
                    {
                        best = score;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            double centerA = bestA, centerB = bestB;
            best = double.NegativeInfinity;
            for (double b = centerB - periodStep; b <= centerB + periodStep + 1e-12; b += periodStep / 10)
            {
                for (double a = centerA - phaseStep; a <= centerA + phaseStep + 1e-12; a += phaseStep / 10)
                {
                    double score = GridScore(fine, a, b, duration, frameRate);
                    if (score > best)
                    {
                        best = score;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (CountNear(raw, bestA + bestB / 2, bestB) > CountNear(raw, bestA, bestB)) bestA += bestB / 2;
            return new GridLine { A = bestA, B = bestB };
        }

        static double GridScore(double[] envelope, double a, double b, double duration, double frameRate)
        {
            double score = 0;
            int kStart = (int)Math.Ceiling(-a / b);
            for (int k = kStart; ; k++)
            {
                double t = a + b * k;
                if (t > duration) break;
                score += Interpolate(envelope, t * frameRate);
            }
            return score;
        }

        static double Interpolate(double[] x, double position)
        {
            if (position <= 0) return x[0];
            if (position >= x.Length - 1) return x[x.Length - 1];
            int i = (int)position;
            double f = position - i;
            return x[i] * (1 - f) + x[i + 1] * f;
        }

        /// <summary>격자 박에서 1/4박 안에 있는 원래 비트 수.</summary>
        static int CountNear(double[] raw, double a, double b)
        {
            int count = 0;
            foreach (var t in raw)
            {
                double k = Math.Round((t - a) / b);
                if (Math.Abs(t - (a + b * k)) <= b / 4) count++;
            }
            return count;
        }

        /// <summary>
        /// 격자를 8박 창마다 ±1/8박(최소 40ms) 밀어 보며 온셋에 가장 잘 맞는 어긋남을 찾는다.
        /// 템포가 일정하면 모든 창에서 어긋남이 0 근처고, 템포가 흔들리면 창마다 어긋남이 쌓인다.
        /// 온셋이 거의 없는 창(인트로·브레이크·아웃트로)은 뺀다: 창 구간의 평균 온셋 강도가 상위 25% 창의 0.35배보다 약한 창.
        /// 격자와 무관한 값으로 고르므로, 격자가 어긋나 맞춤 강도가 약한 창은 빠지지 않고 어긋난 창으로 남는다.
        /// </summary>
        static void MeasureOnsetGrid(GridLine line, double[] envelope, double frameRate, double duration, double tolerance,
            out double ratio, out double errorP90)
        {
            double a = line.A, b = line.B;
            double reach = Math.Max(0.04, b / 8);
            int kStart = (int)Math.Ceiling(-a / b), kEnd = (int)Math.Floor((duration - a) / b);
            var strengths = new List<double>();
            var offsets = new List<double>();
            for (int k0 = kStart; k0 + OnsetWindowBeats - 1 <= kEnd; k0 += OnsetWindowBeats)
            {
                double bestScore = double.NegativeInfinity, bestShift = 0;
                for (double shift = -reach; shift <= reach + 1e-9; shift += 0.001)
                {
                    double score = 0;
                    for (int k = k0; k < k0 + OnsetWindowBeats; k++) score += Interpolate(envelope, (a + b * k + shift) * frameRate);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestShift = shift;
                    }
                }
                int from = Math.Max(0, (int)((a + b * k0) * frameRate)), to = Math.Min(envelope.Length, (int)((a + b * (k0 + OnsetWindowBeats)) * frameRate));
                double activity = 0;
                for (int i = from; i < to; i++) activity += envelope[i];
                strengths.Add(to > from ? activity / (to - from) : 0);
                offsets.Add(Math.Abs(bestShift));
            }

            ratio = 0;
            errorP90 = 1;
            if (strengths.Count < 4) return;
            double floor = 0.35 * Percentile(strengths.ToArray(), 0.75);
            var used = new List<double>();
            for (int i = 0; i < strengths.Count; i++)
                if (strengths[i] >= floor) used.Add(offsets[i]);
            if (used.Count < 4) return;
            int fit = 0;
            foreach (var offset in used)
                if (offset <= tolerance) fit++;
            ratio = fit / (double)used.Count;
            errorP90 = Percentile(used.ToArray(), 0.9);
        }

        /// <summary>다듬은 격자에서 잰 온셋 강도와 다운비트 점수로 나머지 지표를 채우고 판정한다.</summary>
        static void FinishStability(BeatStability s, double pulseClarity, double[] downbeatStrength, int bpb, float[] gridFull,
            BeatStabilityCriteria criteria)
        {
            s.PulseClarity = pulseClarity;

            // 박 위치(0번 칸)가 박 안의 다른 칸보다 얼마나 두드러지는지.
            int beats = gridFull.Length / GridPerBeat;
            double onBeat = 0, all = 0;
            for (int i = 0; i < beats; i++)
            {
                onBeat += gridFull[i * GridPerBeat];
                for (int j = 0; j < GridPerBeat; j++) all += gridFull[i * GridPerBeat + j];
            }
            s.BeatContrast = all > 0 ? onBeat / beats / (all / (beats * GridPerBeat)) : 0;

            var phases = PhaseAverages(downbeatStrength, bpb);
            var sorted = (double[])phases.Clone();
            Array.Sort(sorted);
            double top = sorted[sorted.Length - 1];
            s.DownbeatConfidence = top > 0 ? (top - sorted[sorted.Length - 2]) / top : 0;
            s.TripleMeterScore = PhaseContrast(downbeatStrength, 3) / Math.Max(1e-6, PhaseContrast(downbeatStrength, 4));

            s.Judge(criteria);
        }

        /// <summary>마디 위상별 평균에서 가장 큰 위상이 평균보다 얼마나 큰지(비율). 그 박자로 나눴을 때 마디 첫 박이 뚜렷할수록 크다.</summary>
        static double PhaseContrast(double[] strength, int bpb)
        {
            var phases = PhaseAverages(strength, bpb);
            double mean = Mean(phases);
            double max = phases[ArgMax(phases)];
            return mean > 0 ? (max - mean) / mean : 0;
        }
    }
}
