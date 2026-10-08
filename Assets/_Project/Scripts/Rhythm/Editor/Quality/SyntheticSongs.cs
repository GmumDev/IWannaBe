using System;
using System.Collections.Generic;
using IWannabe.Rhythm.Charting;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>정답 비트를 아는 합성 곡 하나.</summary>
    public sealed class SyntheticSong
    {
        public string Id;
        public string Description;
        public int SampleRate;
        public float[] Samples;
        /// <summary>실제 박 시각(초). 박이 없는 곡은 비어 있다.</summary>
        public double[] BeatTimes = Array.Empty<double>();
        public int BeatsPerBar = 4;
        /// <summary>BeatTimes 안에서 첫 마디 첫 박의 인덱스.</summary>
        public int FirstDownbeatIndex;
        /// <summary>박자 안정성 판정으로 기대하는 값.</summary>
        public BeatStabilityVerdict Expected;
        public bool ExpectTripleMeter;
    }

    /// <summary>
    /// 분석기 점검용 합성 곡. 드럼·베이스·코드를 직접 합성해 박 시각을 정확히 안다.
    /// 템포가 일정한 곡, 천천히 흔들리는 곡, 바뀌는 곡, 박자가 자유로운 곡, 3박자 곡을 만든다.
    /// </summary>
    public static class SyntheticSongs
    {
        enum Groove { Pop, House, Swing, Breakbeat, Waltz, Sparse }

        sealed class Arrangement
        {
            public Groove Groove = Groove.Pop;
            /// <summary>타악기마다 더하는 시각 흔들림의 표준편차(초).</summary>
            public double Jitter;
            /// <summary>드럼을 빼는 마디(코드·베이스만 남는다).</summary>
            public Func<int, bool> DrumsOff = _ => false;
        }

        static readonly double[][] Chords =
        {
            new[] { 261.63, 329.63, 392.00 }, // C
            new[] { 196.00, 246.94, 293.66 }, // G
            new[] { 220.00, 261.63, 329.63 }, // Am
            new[] { 174.61, 220.00, 261.63 }, // F
        };
        static readonly double[] Roots = { 65.41, 98.00, 110.00, 87.31 };

        public static List<SyntheticSong> CreateAll(int sampleRate = 44100)
        {
            var songs = new List<SyntheticSong>
            {
                Band("const120_pop", "120 BPM 팝(킥 1·3, 스네어 2·4, 8분 하이햇)", sampleRate, Constant(120, 160), 4,
                    new Arrangement(), BeatStabilityVerdict.Stable),
                Band("const128_house", "128 BPM 하우스(4박 킥, 엇박 하이햇·베이스)", sampleRate, Constant(128, 160), 4,
                    new Arrangement { Groove = Groove.House }, BeatStabilityVerdict.Stable),
                Band("const96_swing", "96 BPM 스윙(셋잇단 하이햇)", sampleRate, Constant(96, 128), 4,
                    new Arrangement { Groove = Groove.Swing }, BeatStabilityVerdict.Stable),
                Band("const174_breakbeat", "174 BPM 브레이크비트(빠른 곡, 절반 템포로 잡히는지)", sampleRate, Constant(174, 232), 4,
                    new Arrangement { Groove = Groove.Breakbeat }, BeatStabilityVerdict.Stable),
                Band("const110_human", "110 BPM, 타악기마다 ±12ms 사람 손 흔들림", sampleRate, Constant(110, 148), 4,
                    new Arrangement { Jitter = 0.012 }, BeatStabilityVerdict.Stable),
                Band("const120_breaks", "120 BPM, 인트로 8마디·중간 8마디는 드럼 없이 코드만", sampleRate, Constant(120, 192), 4,
                    new Arrangement { DrumsOff = bar => bar < 8 || (bar >= 24 && bar < 32) }, BeatStabilityVerdict.Stable),
                Band("const84_sparse", "84 BPM, 드럼 없이 코드·베이스만(박이 약한 정박 곡)", sampleRate, Constant(84, 112), 4,
                    new Arrangement { Groove = Groove.Sparse }, BeatStabilityVerdict.Stable),
                Band("waltz150", "150 BPM 3/4박자 왈츠", sampleRate, Constant(150, 192), 3,
                    new Arrangement { Groove = Groove.Waltz }, BeatStabilityVerdict.Stable, expectTriple: true),
                Band("drift118_live", "118 BPM 라이브 연주(템포가 ±2.5% 천천히 흔들림, ±8ms 손 흔들림)", sampleRate,
                    Tempo(k => 118 * (1 + 0.018 * Math.Sin(2 * Math.PI * k / 90) + 0.008 * Math.Sin(2 * Math.PI * k / 23 + 1)), 160), 4,
                    new Arrangement { Jitter = 0.008 }, BeatStabilityVerdict.Variable),
                Band("ramp120to126", "120→126 BPM으로 아주 천천히 빨라짐", sampleRate, Tempo(k => 120 + 6.0 * k / 160, 160), 4,
                    new Arrangement(), BeatStabilityVerdict.Variable),
                Band("ramp90to140", "90→140 BPM으로 빨라짐(아첼레란도)", sampleRate, Tempo(k => 90 + 50.0 * k / 176, 176), 4,
                    new Arrangement(), BeatStabilityVerdict.Unstable),
                Band("step100to132", "100 BPM 24마디 뒤 132 BPM으로 바뀜", sampleRate, Tempo(k => k < 96 ? 100 : 132, 192), 4,
                    new Arrangement(), BeatStabilityVerdict.Unstable),
                RubatoPiano(sampleRate),
                Ambient(sampleRate),
            };
            return songs;
        }

        // ───────────────────────── 템포 ─────────────────────────

        static double[] Constant(double bpm, int beats) => Tempo(_ => bpm, beats);

        /// <summary>박 번호별 BPM으로 박 시각을 만든다. 첫 박은 0.5초.</summary>
        static double[] Tempo(Func<int, double> bpmAtBeat, int beats)
        {
            var times = new double[beats];
            times[0] = 0.5;
            for (int k = 1; k < beats; k++) times[k] = times[k - 1] + 60.0 / bpmAtBeat(k - 1);
            return times;
        }

        static double At(double[] beats, double position)
        {
            int k = (int)Math.Floor(position);
            double f = position - k;
            if (k >= beats.Length - 1) return beats[beats.Length - 1] + (position - (beats.Length - 1)) * (beats[beats.Length - 1] - beats[beats.Length - 2]);
            return beats[k] + f * (beats[k + 1] - beats[k]);
        }

        // ───────────────────────── 곡 ─────────────────────────

        static SyntheticSong Band(string id, string description, int sampleRate, double[] beats, int bpb, Arrangement arrangement,
            BeatStabilityVerdict expected, bool expectTriple = false)
        {
            double duration = beats[beats.Length - 1] + 2.0;
            int seed = StableSeed(id);
            var m = new Mixer(sampleRate, duration, seed);
            var rng = new Random(seed ^ 0x5eed);
            double J() => arrangement.Jitter > 0 ? Gaussian(rng) * arrangement.Jitter : 0;

            int bars = beats.Length / bpb;
            for (int bar = 0; bar < bars; bar++)
            {
                int first = bar * bpb;
                int chord = bar % Chords.Length;
                double barStart = beats[first];
                double barEnd = first + bpb < beats.Length ? beats[first + bpb] : barStart + bpb * (beats[first] - beats[first - 1]);
                m.Pad(barStart, barEnd - barStart, Chords[chord], 0.05f);

                bool drums = !arrangement.DrumsOff(bar) && arrangement.Groove != Groove.Sparse;
                for (int b = 0; b < bpb; b++)
                {
                    int k = first + b;
                    if (k >= beats.Length) break;
                    double t = beats[k];
                    double half = At(beats, k + 0.5);
                    double beatLength = At(beats, k + 1) - t;

                    switch (arrangement.Groove)
                    {
                        case Groove.Pop:
                            if (drums)
                            {
                                if (b == 0 || b == 2) m.Kick(t + J(), 0.9f);
                                if (b == 1 || b == 3) m.Snare(t + J(), 0.6f);
                                if (b == 2 && bar % 2 == 1) m.Kick(half + J(), 0.6f);
                                m.Hat(t + J(), 0.22f);
                                m.Hat(half + J(), 0.14f);
                            }
                            if (b == 0 || b == 2) m.Tone(t, beatLength * 1.8, Roots[chord], 0.22f, 0.005, 0.5, 3);
                            break;

                        case Groove.House:
                            if (drums)
                            {
                                m.Kick(t + J(), 0.9f);
                                if (b == 1 || b == 3) m.Snare(t + J(), 0.4f);
                                m.Hat(half + J(), 0.25f, open: true);
                            }
                            m.Tone(half, beatLength * 0.45, Roots[chord] * 2, 0.2f, 0.004, 0.15, 3);
                            break;

                        case Groove.Swing:
                            if (drums)
                            {
                                if (b == 0 || b == 2) m.Kick(t + J(), 0.85f);
                                if (b == 1 || b == 3) m.Snare(t + J(), 0.5f);
                                m.Hat(t + J(), 0.22f);
                                m.Hat(At(beats, k + 2.0 / 3) + J(), 0.13f);
                            }
                            m.Tone(t, beatLength * 0.9, Roots[chord] * (b % 2 == 0 ? 1 : 1.5), 0.2f, 0.005, 0.4, 3);
                            break;

                        case Groove.Breakbeat:
                            if (drums)
                            {
                                if (b == 0) m.Kick(t + J(), 0.9f);
                                if (b == 2) m.Kick(half + J(), 0.75f);
                                if (b == 1 || b == 3) m.Snare(t + J(), 0.65f);
                                m.Hat(t + J(), 0.18f);
                                m.Hat(half + J(), 0.12f);
                            }
                            if (b == 0) m.Tone(t, beatLength * 3.5, Roots[chord], 0.22f, 0.005, 0.9, 3);
                            break;

                        case Groove.Waltz:
                            if (drums)
                            {
                                if (b == 0) m.Kick(t + J(), 0.9f);
                                else m.Snare(t + J(), 0.3f);
                                m.Hat(t + J(), 0.15f);
                            }
                            if (b == 0) m.Tone(t, beatLength * 2.8, Roots[chord], 0.25f, 0.005, 0.8, 3);
                            else foreach (var f in Chords[chord]) m.Tone(t, beatLength * 0.8, f, 0.06f, 0.004, 0.25, 2);
                            break;

                        case Groove.Sparse:
                            if (b == 0) m.Tone(t, beatLength * 3.8, Roots[chord], 0.28f, 0.01, 1.2, 3);
                            if (b == 2) m.Tone(t, beatLength * 1.8, Roots[chord] * 1.5, 0.16f, 0.01, 0.8, 3);
                            foreach (var f in Chords[chord]) m.Tone(t, beatLength * 0.9, f, b == 0 ? 0.08f : 0.05f, 0.006, 0.5, 2);
                            break;
                    }
                }
            }

            return new SyntheticSong
            {
                Id = id,
                Description = description,
                SampleRate = sampleRate,
                Samples = m.Finish(),
                BeatTimes = beats,
                BeatsPerBar = bpb,
                FirstDownbeatIndex = 0,
                Expected = expected,
                ExpectTripleMeter = expectTriple,
            };
        }

        /// <summary>박자가 자유로운 피아노: 4박 악구마다 템포가 ±25% 바뀌고 악구 끝에서 느려진다. 드럼 없음.</summary>
        static SyntheticSong RubatoPiano(int sampleRate)
        {
            const string id = "rubato_piano";
            var rng = new Random(7);
            var m = new Mixer(sampleRate, 92, 7);
            double[] scale = { 261.63, 293.66, 329.63, 349.23, 392.00, 440.00, 493.88, 523.25, 587.33, 659.25 };
            double t = 0.5;
            int phrase = 0;
            int degree = 4;
            while (t < 88)
            {
                double beat = 60.0 / 76 * (0.75 + 0.55 * rng.NextDouble());
                int chord = phrase % Chords.Length;
                m.Tone(t, beat * 3, Roots[chord], 0.18f, 0.004, 1.5, 4);
                foreach (var f in Chords[chord]) m.Tone(t + 0.01 * rng.NextDouble(), beat * 2, f, 0.05f, 0.004, 1.0, 3);

                double inPhrase = 0;
                while (inPhrase < 4)
                {
                    double[] lengths = { 0.5, 1, 1, 1.5, 0.75 };
                    double length = lengths[rng.Next(lengths.Length)];
                    double stretch = 1 + 0.6 * Math.Pow(inPhrase / 4, 2); // 악구 끝 리타르단도
                    degree = Math.Max(0, Math.Min(scale.Length - 1, degree + rng.Next(-2, 3)));
                    m.Tone(t, beat * length * stretch * 1.5, scale[degree], 0.16f, 0.003, 0.9, 4);
                    t += beat * length * stretch + 0.03 * Gaussian(rng);
                    inPhrase += length;
                }
                t += beat * rng.NextDouble(); // 악구 사이 숨
                phrase++;
            }

            return new SyntheticSong
            {
                Id = id,
                Description = "루바토 피아노(악구마다 템포 ±25%, 악구 끝 리타르단도, 드럼 없음)",
                SampleRate = sampleRate,
                Samples = m.Finish(),
                Expected = BeatStabilityVerdict.Unstable,
            };
        }

        /// <summary>박이 없는 앰비언트: 천천히 부풀었다 줄어드는 패드와 띄엄띄엄 울리는 종소리.</summary>
        static SyntheticSong Ambient(int sampleRate)
        {
            const string id = "ambient_drone";
            var rng = new Random(11);
            var m = new Mixer(sampleRate, 90, 11);
            double[] drone = { 130.81, 196.00, 261.63, 329.63, 392.00 };
            foreach (var f in drone) m.Swell(0, 90, f * (1 + 0.002 * Gaussian(rng)), 0.05f, 0.03 + 0.1 * rng.NextDouble(), rng.NextDouble() * 6);
            double t = 1;
            while (t < 88)
            {
                m.Tone(t, 3, drone[rng.Next(drone.Length)] * 2, 0.08f, 0.02, 2.5, 3);
                t += -Math.Log(1 - rng.NextDouble()) * 2.5 + 0.3; // 평균 2.8초 간격의 무작위 시각
            }

            return new SyntheticSong
            {
                Id = id,
                Description = "앰비언트(박 없음, 패드와 무작위 시각의 종소리)",
                SampleRate = sampleRate,
                Samples = m.Finish(),
                Expected = BeatStabilityVerdict.Unstable,
            };
        }

        /// <summary>실행 환경과 관계없이 같은 값을 내는 문자열 해시(FNV-1a). string.GetHashCode는 .NET에서 실행마다 바뀐다.</summary>
        static int StableSeed(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text) hash = (hash ^ c) * 16777619;
                return (int)hash;
            }
        }

        static double Gaussian(Random rng)
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        // ───────────────────────── 합성 ─────────────────────────

        sealed class Mixer
        {
            readonly int rate;
            readonly float[] buffer;
            readonly Random noise;

            public Mixer(int sampleRate, double seconds, int seed)
            {
                rate = sampleRate;
                buffer = new float[(int)(seconds * sampleRate)];
                noise = new Random(seed);
            }

            int Index(double t) => (int)Math.Round(t * rate);

            void Add(int i, double value)
            {
                if (i >= 0 && i < buffer.Length) buffer[i] += (float)value;
            }

            /// <summary>음정이 150Hz에서 45Hz로 떨어지는 킥.</summary>
            public void Kick(double t, float amp)
            {
                int start = Index(t), length = (int)(0.35 * rate);
                double phase = 0;
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    phase += 2 * Math.PI * (45 + 105 * Math.Exp(-s / 0.03)) / rate;
                    double env = Math.Min(1, s / 0.001) * Math.Exp(-s / 0.13);
                    Add(start + n, amp * env * Math.Sin(phase));
                }
            }

            /// <summary>고역 노이즈와 190Hz 몸통으로 된 스네어.</summary>
            public void Snare(double t, float amp)
            {
                int start = Index(t), length = (int)(0.25 * rate);
                double previous = 0;
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    double white = noise.NextDouble() * 2 - 1;
                    double high = white - previous;
                    previous = white;
                    double attack = Math.Min(1, s / 0.0007);
                    Add(start + n, amp * attack * (0.5 * high * Math.Exp(-s / 0.07) + 0.6 * Math.Sin(2 * Math.PI * 190 * s) * Math.Exp(-s / 0.04)));
                }
            }

            public void Hat(double t, float amp, bool open = false)
            {
                int start = Index(t), length = (int)((open ? 0.25 : 0.08) * rate);
                double decay = open ? 0.08 : 0.022;
                double p1 = 0, p2 = 0;
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    double white = noise.NextDouble() * 2 - 1;
                    double high = white - 2 * p1 + p2; // 2차 차분: 고역만 남김
                    p2 = p1;
                    p1 = white;
                    Add(start + n, amp * 0.35 * high * Math.Min(1, s / 0.0005) * Math.Exp(-s / decay));
                }
            }

            /// <summary>배음 <paramref name="harmonics"/>개짜리 감쇠음(베이스·피아노·종).</summary>
            public void Tone(double t, double seconds, double frequency, float amp, double attack, double decay, int harmonics)
            {
                int start = Index(t), length = (int)((seconds + 0.05) * rate);
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    double env = Math.Min(1, s / attack) * Math.Exp(-s / decay);
                    if (s > seconds) env *= Math.Max(0, 1 - (s - seconds) / 0.05);
                    double value = 0;
                    for (int h = 1; h <= harmonics; h++) value += Math.Sin(2 * Math.PI * frequency * h * s) / h;
                    Add(start + n, amp * env * value);
                }
            }

            /// <summary>천천히 들어오고 나가는 화음 패드. 마디마다 바뀌어 화성 변화로 마디 첫 박을 알려 준다.</summary>
            public void Pad(double t, double seconds, double[] frequencies, float amp)
            {
                int start = Index(t), length = (int)((seconds + 0.15) * rate);
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    double env = Math.Min(1, s / 0.04) * (s > seconds ? Math.Max(0, 1 - (s - seconds) / 0.15) : 1);
                    double value = 0;
                    foreach (var f in frequencies) value += Math.Sin(2 * Math.PI * f * s) + 0.3 * Math.Sin(4 * Math.PI * f * s);
                    Add(start + n, amp * env * value);
                }
            }

            /// <summary>진폭이 <paramref name="rateHz"/>로 천천히 부풀었다 줄어드는 지속음.</summary>
            public void Swell(double t, double seconds, double frequency, float amp, double rateHz, double phase)
            {
                int start = Index(t), length = (int)(seconds * rate);
                for (int n = 0; n < length; n++)
                {
                    double s = n / (double)rate;
                    double env = 0.5 + 0.5 * Math.Sin(2 * Math.PI * rateHz * s + phase);
                    Add(start + n, amp * env * (Math.Sin(2 * Math.PI * frequency * s) + 0.2 * Math.Sin(6 * Math.PI * frequency * s)));
                }
            }

            /// <summary>아주 작은 노이즈를 깔고 최대 0.9로 맞춘다.</summary>
            public float[] Finish()
            {
                float peak = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] += (float)((noise.NextDouble() * 2 - 1) * 0.0005);
                    peak = Math.Max(peak, Math.Abs(buffer[i]));
                }
                if (peak > 0)
                {
                    float scale = 0.9f / peak;
                    for (int i = 0; i < buffer.Length; i++) buffer[i] *= scale;
                }
                return buffer;
            }
        }
    }
}
