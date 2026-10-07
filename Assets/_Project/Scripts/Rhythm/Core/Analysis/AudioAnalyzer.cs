using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    /// <summary>
    /// 오프라인 곡 분석기.
    /// 스펙트럴 플럭스(대역별) → 자기상관 템포 추정 → 동적 계획법 비트 추적(Ellis 2007)
    /// → 템포 정규화 → 다운비트 추정 → 그리드 온셋 강도·마디 에너지 순으로 처리한다.
    /// </summary>
    public static class AudioAnalyzer
    {
        public const int GridPerBeat = 12; // 1/2, 1/3, 1/4, 1/6박을 모두 정확히 표현

        sealed class Features
        {
            public int FrameCount;
            public float[] Full;
            public float[] Low;
            public float[] Mid;
            public float[] High;
            public float[] Rms;
            public float[][] Chroma;
        }

        public static AnalysisResult Analyze(float[] mono, int sampleRate, AnalyzerSettings settings)
        {
            if (mono == null) throw new ArgumentNullException(nameof(mono));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (mono.Length < sampleRate * 4) throw new ArgumentException("최소 4초 이상의 오디오가 필요합니다.", nameof(mono));
            settings = settings ?? new AnalyzerSettings();

            int bpb = Math.Max(2, settings.beatsPerBar);
            double duration = mono.Length / (double)sampleRate;
            double frameRate = sampleRate / (double)settings.hopSize;
            var features = ComputeFeatures(mono, sampleRate, settings.fftSize, settings.hopSize);

            double period = EstimatePeriod(features.Full, frameRate, settings);
            int[] beatFrames = TrackBeats(features.Full, period);
            if (beatFrames.Length < 8)
                throw new InvalidOperationException("비트를 충분히 찾지 못했습니다. 리듬이 뚜렷한 곡인지 확인하세요.");

            var raw = new double[beatFrames.Length];
            for (int i = 0; i < raw.Length; i++) raw[i] = beatFrames[i] / frameRate;

            double[] beats = Regularize(raw, duration, settings.tempoMode, features.Full, frameRate, out bool constant);
            for (int i = 0; i < beats.Length; i++) beats[i] += settings.timeBias;
            beats = TrimToDuration(beats, duration);

            int downbeat = settings.downbeatOverride >= 0
                ? settings.downbeatOverride % bpb
                : EstimateDownbeat(beats, features, frameRate, bpb);

            var result = new AnalysisResult
            {
                SampleRate = sampleRate,
                Duration = duration,
                ConstantTempo = constant,
                BeatTimes = beats,
                BeatsPerBar = bpb,
                FirstDownbeatIndex = downbeat,
                GridPerBeat = GridPerBeat,
            };
            result.Bpm = 60.0 / Median(Diff(beats));

            // 온셋 피크는 실제 타격보다 앞서므로 그리드도 같은 보정만큼 당겨서 샘플링한다.
            double bias = settings.timeBias;
            result.GridFull = SampleGrid(Peakiness(features.Full, frameRate), beats, frameRate, bias);
            result.GridLow = SampleGrid(Peakiness(features.Low, frameRate), beats, frameRate, bias);
            result.GridMid = SampleGrid(Peakiness(features.Mid, frameRate), beats, frameRate, bias);
            result.GridHigh = SampleGrid(Peakiness(features.High, frameRate), beats, frameRate, bias);

            ComputeBarLevels(features.Rms, beats, downbeat, bpb, frameRate, settings, out result.BarEnergy, out result.BarLevel);
            result.OnsetTimes = PickOnsets(Peakiness(features.Full, frameRate), frameRate, bias);
            return result;
        }

        // ───────────────────────── 특징 추출 ─────────────────────────

        static Features ComputeFeatures(float[] x, int sampleRate, int n, int hop)
        {
            int frames = x.Length / hop + 1;
            var f = new Features
            {
                FrameCount = frames,
                Full = new float[frames],
                Low = new float[frames],
                Mid = new float[frames],
                High = new float[frames],
                Rms = new float[frames],
                Chroma = new float[frames][],
            };

            var window = new float[n];
            for (int i = 0; i < n; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / n));

            int bins = n / 2 + 1;
            double binHz = sampleRate / (double)n;
            int Bin(double hz) => Math.Min(bins - 1, Math.Max(1, (int)Math.Round(hz / binHz)));
            int lowStart = Bin(30), lowEnd = Bin(150), midEnd = Bin(2500), highEnd = Bin(16000);

            // 크로마(12음) 매핑: 화성 변화로 마디 경계를 찾는 데 쓴다.
            var pitchClass = new int[bins];
            for (int b = 0; b < bins; b++)
            {
                double hz = b * binHz;
                pitchClass[b] = hz >= 60 && hz <= 2000
                    ? (((int)Math.Round(12 * Math.Log(hz / 440.0, 2)) + 9) % 12 + 12) % 12
                    : -1;
            }

            const int lag = 2; // 약 12ms 전 프레임과 비교
            var history = new float[lag + 1][];
            for (int i = 0; i <= lag; i++) history[i] = new float[bins];

            var fft = new Fft(n);
            var re = new float[n];
            var im = new float[n];
            float magScale = 4f / n; // 풀스케일 사인파 ≈ 1

            for (int fr = 0; fr < frames; fr++)
            {
                int center = fr * hop;
                int start = center - n / 2;
                for (int k = 0; k < n; k++)
                {
                    int idx = start + k;
                    re[k] = idx >= 0 && idx < x.Length ? x[idx] * window[k] : 0f;
                    im[k] = 0f;
                }

                double sumSq = 0;
                int count = 0;
                for (int idx = Math.Max(0, center - hop / 2); idx < Math.Min(x.Length, center + hop / 2); idx++)
                {
                    sumSq += x[idx] * x[idx];
                    count++;
                }
                f.Rms[fr] = (float)Math.Sqrt(sumSq / Math.Max(1, count));

                fft.Forward(re, im);

                var current = history[fr % (lag + 1)];
                var previous = history[(fr + 1) % (lag + 1)];
                var chroma = new float[12];
                double full = 0, low = 0, mid = 0, high = 0;
                for (int b = 1; b <= highEnd; b++)
                {
                    float mag = (float)Math.Sqrt(re[b] * re[b] + im[b] * im[b]) * magScale;
                    float level = (float)Math.Log(1 + 100 * mag);
                    if (pitchClass[b] >= 0) chroma[pitchClass[b]] += mag;

                    if (fr >= lag && b >= lowStart)
                    {
                        float rise = level - previous[b];
                        if (rise > 0)
                        {
                            full += rise;
                            if (b <= lowEnd) low += rise;
                            else if (b <= midEnd) mid += rise;
                            else high += rise;
                        }
                    }
                    current[b] = level;
                }

                f.Full[fr] = (float)full;
                f.Low[fr] = (float)low;
                f.Mid[fr] = (float)mid;
                f.High[fr] = (float)high;
                f.Chroma[fr] = chroma;
            }
            return f;
        }

        // ───────────────────────── 템포 추정 ─────────────────────────

        static double EstimatePeriod(float[] envelope, double frameRate, AnalyzerSettings s)
        {
            double[] o = RemoveTrend(envelope, (int)(frameRate * 0.5));
            int n = o.Length;
            int minLag = Math.Max(2, (int)Math.Floor(60 * frameRate / s.maxBpm));
            int maxLag = (int)Math.Ceiling(60 * frameRate / s.minBpm);
            int acLength = Math.Min(n - 1, 2 * maxLag + 2);

            var ac = new double[acLength];
            for (int lagFrames = 1; lagFrames < acLength; lagFrames++)
            {
                double sum = 0;
                for (int i = 0; i + lagFrames < n; i++) sum += o[i] * o[i + lagFrames];
                ac[lagFrames] = sum / (n - lagFrames);
            }

            int best = minLag;
            double bestScore = double.NegativeInfinity;
            for (int l = minLag; l <= maxLag && l < acLength; l++)
            {
                double bpm = 60 * frameRate / l;
                double octaves = Math.Log(bpm / s.preferredBpm, 2);
                double prior = Math.Exp(-0.5 * octaves * octaves);
                double harmonic = 2 * l < acLength ? 0.5 * ac[2 * l] : 0;
                double score = (ac[l] + harmonic) * prior;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = l;
                }
            }

            double period = best;
            if (best > 1 && best < acLength - 1)
            {
                double a = ac[best - 1], b = ac[best], c = ac[best + 1];
                double denom = a - 2 * b + c;
                if (denom < 0) period += 0.5 * (a - c) / denom;
            }
            return period;
        }

        // ───────────────────────── 비트 추적 ─────────────────────────

        static int[] TrackBeats(float[] envelope, double period)
        {
            int n = envelope.Length;
            double std = StdDev(envelope);
            if (std <= 0) return Array.Empty<int>();

            var normalized = new double[n];
            for (int i = 0; i < n; i++) normalized[i] = envelope[i] / std;
            double[] local = GaussianSmooth(normalized, period / 32.0);

            const double tightness = 100;
            int minBack = Math.Max(1, (int)Math.Round(period / 2));
            int maxBack = (int)Math.Round(2 * period);
            var penalty = new double[maxBack + 1];
            for (int d = minBack; d <= maxBack; d++)
            {
                double r = Math.Log(d / period);
                penalty[d] = -tightness * r * r;
            }

            double threshold = 0.01 * Max(local);
            var cum = new double[n];
            var back = new int[n];
            bool started = false;
            for (int t = 0; t < n; t++)
            {
                double bestValue = double.NegativeInfinity;
                int bestTau = -1;
                for (int d = minBack; d <= maxBack; d++)
                {
                    int tau = t - d;
                    if (tau < 0) break;
                    double v = cum[tau] + penalty[d];
                    if (v > bestValue)
                    {
                        bestValue = v;
                        bestTau = tau;
                    }
                }

                if (bestTau < 0)
                {
                    cum[t] = local[t];
                    back[t] = -1;
                }
                else
                {
                    cum[t] = local[t] + bestValue;
                    back[t] = started ? bestTau : -1;
                }
                if (local[t] >= threshold) started = true;
            }

            // 누적 점수의 국소 최댓값 중 충분히 큰 마지막 지점에서 역추적한다.
            var peaks = new List<double>();
            for (int t = 1; t < n - 1; t++)
                if (cum[t] > cum[t - 1] && cum[t] >= cum[t + 1]) peaks.Add(cum[t]);
            if (peaks.Count == 0) return Array.Empty<int>();
            double medianPeak = Median(peaks.ToArray());

            int last = -1;
            for (int t = n - 2; t >= 1; t--)
            {
                if (cum[t] > cum[t - 1] && cum[t] >= cum[t + 1] && cum[t] >= 0.5 * medianPeak)
                {
                    last = t;
                    break;
                }
            }
            if (last < 0) return Array.Empty<int>();

            var beats = new List<int>();
            for (int t = last; t >= 0; t = back[t]) beats.Add(t);
            beats.Reverse();
            return TrimWeakBeats(beats, local);
        }

        static int[] TrimWeakBeats(List<int> beats, double[] local)
        {
            if (beats.Count < 3) return beats.ToArray();
            var values = new double[beats.Count];
            for (int i = 0; i < beats.Count; i++) values[i] = local[beats[i]];

            // 5탭 Hann 평활 후 RMS의 절반보다 약한 앞뒤 비트를 잘라낸다.
            double[] w = { 0.25, 0.75, 1.0, 0.75, 0.25 };
            var smooth = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double s = 0;
                for (int k = -2; k <= 2; k++)
                {
                    int j = i + k;
                    if (j >= 0 && j < values.Length) s += values[j] * w[k + 2];
                }
                smooth[i] = s;
            }
            double meanSq = 0;
            foreach (var v in smooth) meanSq += v * v;
            double threshold = 0.5 * Math.Sqrt(meanSq / smooth.Length);

            int first = 0, end = beats.Count - 1;
            while (first < end && smooth[first] <= threshold) first++;
            while (end > first && smooth[end] <= threshold) end--;
            return beats.GetRange(first, end - first + 1).ToArray();
        }

        // ───────────────────────── 템포 정규화 ─────────────────────────

        static double[] Regularize(double[] raw, double duration, TempoMode mode, float[] envelope, double frameRate, out bool constant)
        {
            double[] intervals = Diff(raw);
            double median = Median(intervals);

            double sum = 0, sumSq = 0;
            int count = 0;
            foreach (var d in intervals)
            {
                if (d < 0.8 * median || d > 1.2 * median) continue;
                sum += d;
                sumSq += d * d;
                count++;
            }
            double mean = count > 0 ? sum / count : median;
            double cv = count > 1 ? Math.Sqrt(Math.Max(0, sumSq / count - mean * mean)) / mean : 1;
            constant = mode == TempoMode.Constant || (mode == TempoMode.Auto && cv < 0.05);

            if (constant)
            {
                // 빠진 박을 고려해 인덱스를 붙인 뒤 직선 회귀(이상치 2회 제거)
                var index = new int[raw.Length];
                for (int i = 1; i < raw.Length; i++)
                    index[i] = index[i - 1] + Math.Max(1, (int)Math.Round((raw[i] - raw[i - 1]) / median));

                var use = new bool[raw.Length];
                for (int i = 0; i < use.Length; i++) use[i] = true;
                double a = raw[0], b = median;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    FitLine(index, raw, use, out a, out b);
                    for (int i = 0; i < raw.Length; i++)
                        use[i] = Math.Abs(raw[i] - (a + b * index[i])) < 0.03;
                }

                a = RefinePhase(a, b, duration, envelope, frameRate);

                int kStart = (int)Math.Ceiling((0 - a) / b - 1e-9);
                int kEnd = (int)Math.Floor((duration - a) / b + 1e-9);
                var grid = new double[kEnd - kStart + 1];
                for (int k = kStart; k <= kEnd; k++) grid[k - kStart] = a + b * k;
                return grid;
            }

            // 가변 템포: ±4박 지역 회귀로 지터만 걷어내고 양끝을 확장한다.
            var smooth = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                int lo = Math.Max(0, i - 4), hi = Math.Min(raw.Length - 1, i + 4);
                double sx = 0, sy = 0, sxx = 0, sxy = 0;
                int m = hi - lo + 1;
                for (int j = lo; j <= hi; j++)
                {
                    sx += j;
                    sy += raw[j];
                    sxx += (double)j * j;
                    sxy += j * raw[j];
                }
                double slope = (m * sxy - sx * sy) / (m * sxx - sx * sx);
                double intercept = (sy - slope * sx) / m;
                smooth[i] = intercept + slope * i;
            }

            var list = new List<double>(smooth);
            double head = smooth[1] - smooth[0];
            while (list[0] - head >= 0) list.Insert(0, list[0] - head);
            double tail = smooth[smooth.Length - 1] - smooth[smooth.Length - 2];
            while (list[list.Count - 1] + tail <= duration) list.Add(list[list.Count - 1] + tail);
            return list.ToArray();
        }

        static void FitLine(int[] x, double[] y, bool[] use, out double intercept, out double slope)
        {
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            int m = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (!use[i]) continue;
                sx += x[i];
                sy += y[i];
                sxx += (double)x[i] * x[i];
                sxy += x[i] * y[i];
                m++;
            }
            if (m < 2)
            {
                intercept = y[0];
                slope = x.Length > 1 ? (y[y.Length - 1] - y[0]) / Math.Max(1, x[x.Length - 1]) : 0.5;
                return;
            }
            slope = (m * sxy - sx * sy) / (m * sxx - sx * sx);
            intercept = (sy - slope * sx) / m;
        }

        /// <summary>비트 격자를 ±20ms 범위에서 밀어 보며 온셋 엔벨로프와 가장 잘 맞는 위상을 찾는다.</summary>
        static double RefinePhase(double a, double b, double duration, float[] envelope, double frameRate)
        {
            double bestShift = 0, bestScore = double.NegativeInfinity;
            for (double shift = -0.02; shift <= 0.02 + 1e-9; shift += 0.0005)
            {
                double score = 0;
                double start = a + shift;
                int k0 = (int)Math.Ceiling(-start / b);
                for (int k = k0; ; k++)
                {
                    double t = start + b * k;
                    if (t > duration) break;
                    score += Interpolate(envelope, t * frameRate);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestShift = shift;
                }
            }
            return a + bestShift;
        }

        static double[] TrimToDuration(double[] beats, double duration)
        {
            var list = new List<double>(beats.Length);
            foreach (var t in beats)
                if (t >= 0 && t <= duration) list.Add(t);
            return list.ToArray();
        }

        // ───────────────────────── 다운비트 ─────────────────────────

        /// <summary>
        /// 박마다 저음 온셋(킥)과 화성 변화량(크로마 차이)을 구해 마디 위상별로 합산한다.
        /// 코드는 보통 마디 첫 박에서 바뀌고 킥도 첫 박에 놓이는 경우가 많다.
        /// </summary>
        static int EstimateDownbeat(double[] beats, Features f, double frameRate, int bpb)
        {
            int nb = beats.Length;
            var low = new double[nb];
            var novelty = new double[nb];
            for (int i = 0; i < nb; i++)
                low[i] = MaxAround(f.Low, beats[i] * frameRate, 0.03 * frameRate);

            for (int i = 1; i < nb - 1; i++)
            {
                var before = AverageChroma(f.Chroma, beats[i - 1] * frameRate, beats[i] * frameRate);
                var after = AverageChroma(f.Chroma, beats[i] * frameRate, beats[i + 1] * frameRate);
                novelty[i] = 1 - Cosine(before, after);
            }

            double lowMean = Math.Max(1e-9, Mean(low));
            double novMean = Math.Max(1e-9, Mean(novelty));
            var score = new double[bpb];
            var count = new int[bpb];
            for (int i = 0; i < nb; i++)
            {
                score[i % bpb] += low[i] / lowMean + 1.5 * novelty[i] / novMean;
                count[i % bpb]++;
            }

            int best = 0;
            for (int p = 1; p < bpb; p++)
                if (score[p] / Math.Max(1, count[p]) > score[best] / Math.Max(1, count[best])) best = p;
            return best;
        }

        static double[] AverageChroma(float[][] chroma, double fromFrame, double toFrame)
        {
            var sum = new double[12];
            int a = Math.Max(0, (int)fromFrame), b = Math.Min(chroma.Length, (int)toFrame);
            for (int i = a; i < b; i++)
                for (int k = 0; k < 12; k++) sum[k] += chroma[i][k];
            return sum;
        }

        static double Cosine(double[] a, double[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return na <= 0 || nb <= 0 ? 1 : dot / Math.Sqrt(na * nb);
        }

        // ───────────────────────── 그리드·마디 ─────────────────────────

        /// <summary>국소 평균을 뺀 뒤 상위 분위수로 정규화해, 주변보다 튀는 온셋만 0~1로 남긴다.</summary>
        static float[] Peakiness(float[] envelope, double frameRate)
        {
            double[] e = RemoveTrend(envelope, (int)(frameRate * 0.1));
            var positives = new List<double>();
            foreach (var v in e)
                if (v > 0) positives.Add(v);
            double scale = positives.Count > 0 ? Percentile(positives.ToArray(), 0.98) : 1;
            if (scale <= 0) scale = 1;

            var result = new float[e.Length];
            for (int i = 0; i < e.Length; i++) result[i] = (float)Math.Min(1.0, e[i] / scale);
            return result;
        }

        static float[] SampleGrid(float[] peaks, double[] beats, double frameRate, double bias)
        {
            int nb = beats.Length;
            var grid = new float[nb * GridPerBeat];
            double tolerance = 0.015 * frameRate;
            for (int i = 0; i < nb; i++)
            {
                double interval = i + 1 < nb ? beats[i + 1] - beats[i] : beats[i] - beats[i - 1];
                for (int j = 0; j < GridPerBeat; j++)
                {
                    double t = beats[i] + interval * j / GridPerBeat - bias;
                    grid[i * GridPerBeat + j] = (float)MaxAround(peaks, t * frameRate, tolerance);
                }
            }
            return grid;
        }

        static void ComputeBarLevels(float[] rms, double[] beats, int downbeat, int bpb, double frameRate,
            AnalyzerSettings s, out float[] energy, out int[] level)
        {
            int barCount = Math.Max(0, (beats.Length - 1 - downbeat) / bpb);
            var db = new double[barCount];
            for (int bar = 0; bar < barCount; bar++)
            {
                int from = (int)(beats[downbeat + bar * bpb] * frameRate);
                int to = (int)(beats[downbeat + (bar + 1) * bpb] * frameRate);
                double sum = 0;
                int count = 0;
                for (int i = Math.Max(0, from); i < Math.Min(rms.Length, to); i++)
                {
                    sum += rms[i];
                    count++;
                }
                db[bar] = 20 * Math.Log10(sum / Math.Max(1, count) + 1e-9);
            }

            energy = new float[barCount];
            level = new int[barCount];
            if (barCount == 0) return;

            double lo = Percentile(db, 0.1), hi = Percentile(db, 0.95);
            double range = Math.Max(1e-6, hi - lo);
            for (int bar = 0; bar < barCount; bar++)
                energy[bar] = (float)Math.Max(0, Math.Min(1, (db[bar] - lo) / range));

            // 4마디 블록 단위로 강도를 정해 악구 중간에 단계가 바뀌지 않게 한다.
            for (int block = 0; block < barCount; block += 4)
            {
                int end = Math.Min(barCount, block + 4);
                double mean = 0;
                for (int bar = block; bar < end; bar++) mean += energy[bar];
                mean /= end - block;
                int value = mean >= s.highLevelThreshold ? 2 : mean >= s.midLevelThreshold ? 1 : 0;
                for (int bar = block; bar < end; bar++) level[bar] = value;
            }
        }

        static double[] PickOnsets(float[] peaks, double frameRate, double bias)
        {
            var onsets = new List<double>();
            int minDistance = Math.Max(1, (int)(0.05 * frameRate));
            int lastPick = -minDistance;
            for (int i = 1; i < peaks.Length - 1; i++)
            {
                if (peaks[i] < 0.3f || peaks[i] < peaks[i - 1] || peaks[i] < peaks[i + 1]) continue;
                if (i - lastPick < minDistance) continue;
                onsets.Add(i / frameRate + bias);
                lastPick = i;
            }
            return onsets.ToArray();
        }

        // ───────────────────────── 유틸 ─────────────────────────

        static double[] RemoveTrend(float[] x, int radius)
        {
            radius = Math.Max(1, radius);
            int n = x.Length;
            var prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + x[i];
            var result = new double[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - radius), b = Math.Min(n, i + radius + 1);
                double mean = (prefix[b] - prefix[a]) / (b - a);
                result[i] = Math.Max(0, x[i] - mean);
            }
            return result;
        }

        static double[] GaussianSmooth(double[] x, double sigma)
        {
            sigma = Math.Max(0.5, sigma);
            int radius = (int)Math.Ceiling(3 * sigma);
            var kernel = new double[2 * radius + 1];
            for (int k = -radius; k <= radius; k++) kernel[k + radius] = Math.Exp(-0.5 * k * k / (sigma * sigma));

            var result = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int j = i + k;
                    if (j >= 0 && j < x.Length) sum += x[j] * kernel[k + radius];
                }
                result[i] = sum;
            }
            return result;
        }

        static double Interpolate(float[] x, double position)
        {
            if (position <= 0) return x[0];
            if (position >= x.Length - 1) return x[x.Length - 1];
            int i = (int)position;
            double f = position - i;
            return x[i] * (1 - f) + x[i + 1] * f;
        }

        static double MaxAround(float[] x, double center, double radius)
        {
            int a = Math.Max(0, (int)Math.Floor(center - radius));
            int b = Math.Min(x.Length - 1, (int)Math.Ceiling(center + radius));
            double max = 0;
            for (int i = a; i <= b; i++)
                if (x[i] > max) max = x[i];
            return max;
        }

        static double[] Diff(double[] x)
        {
            var d = new double[Math.Max(0, x.Length - 1)];
            for (int i = 0; i < d.Length; i++) d[i] = x[i + 1] - x[i];
            return d;
        }

        static double Median(double[] x) => Percentile(x, 0.5);

        static double Percentile(double[] x, double q)
        {
            if (x.Length == 0) return 0;
            var copy = (double[])x.Clone();
            Array.Sort(copy);
            double position = q * (copy.Length - 1);
            int i = (int)Math.Floor(position);
            int j = Math.Min(copy.Length - 1, i + 1);
            return copy[i] + (copy[j] - copy[i]) * (position - i);
        }

        static double Mean(double[] x)
        {
            if (x.Length == 0) return 0;
            double sum = 0;
            foreach (var v in x) sum += v;
            return sum / x.Length;
        }

        static double Max(double[] x)
        {
            double max = double.NegativeInfinity;
            foreach (var v in x)
                if (v > max) max = v;
            return max;
        }

        static double StdDev(float[] x)
        {
            double sum = 0, sumSq = 0;
            foreach (var v in x)
            {
                sum += v;
                sumSq += (double)v * v;
            }
            double mean = sum / x.Length;
            return Math.Sqrt(Math.Max(0, sumSq / x.Length - mean * mean));
        }
    }
}
