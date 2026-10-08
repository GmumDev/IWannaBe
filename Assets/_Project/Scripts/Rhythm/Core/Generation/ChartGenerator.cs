using System;
using System.Collections.Generic;
using System.Text;

namespace IWannabe.Rhythm.Charting
{
    [Serializable]
    public sealed class GeneratorSettings
    {
        public int seed = 20261007;
        /// <summary>1~5. 이 값 이하 난이도의 패턴만 쓰고, 노트 밀도도 함께 오른다.</summary>
        public int difficulty = 3;
        /// <summary>패턴을 채우는 단위 구간 길이(마디).</summary>
        public int phraseBars = 2;
        /// <summary>첫 다운비트 이후 노트를 두지 않는 마디 수.</summary>
        public int leadInBars = 1;
        /// <summary>마지막 비트 직전에 비워 둘 박 수.</summary>
        public int tailBeats = 2;
        /// <summary>연속한 입력(누름·뗌) 사이 최소 간격. 판정 범위보다 넓어야 입력이 모호해지지 않는다.</summary>
        public double minNoteGapSeconds = 0.15;
        public double minHoldSeconds = 0.3;
        /// <summary>곡 시작 직후에는 큐를 두지 않는다.</summary>
        public double minCueTimeSeconds = 0.5;
        /// <summary>노트를 둘 위치의 최소 온셋 강도. 이보다 약한 곳(무음 등)은 크게 감점.</summary>
        public float minNoteStrength = 0.12f;
        /// <summary>구간 강도(0~2)별 목표 노트 밀도(박당 개수).</summary>
        public float[] notesPerBeatByLevel = { 0.35f, 0.6f, 0.85f };
        /// <summary>구간 강도별로 한 구절을 통째로 쉴 확률.</summary>
        public float[] restChanceByLevel = { 0.35f, 0.1f, 0f };
        /// <summary>구간 강도별로 쉼 없이 이어질 수 있는 최대 구절 수.</summary>
        public int[] maxActivePhrasesByLevel = { 2, 4, 6 };
        /// <summary>구절 평균 온셋 강도가 이보다 낮으면 쉰다(브레이크·무음).</summary>
        public float silenceThreshold = 0.04f;
        /// <summary>다음 패턴의 앵커를 찾는 범위(박).</summary>
        public double lookaheadBeats = 4;
        /// <summary>
        /// 리믹스에서 큐는 앞 구간에서 울리고 입력은 미니게임이 바뀐 뒤에 오는 패턴(이어받기)에 주는 가산점.
        /// 클수록 구간 경계마다 이어받기가 자주 나온다.
        /// </summary>
        public double carryOverBonus = 0.25;
        /// <summary>
        /// 앞 구간 패턴의 입력이 다음 구간 시작 박을 넘어가려면 적어도 이만큼(박) 뒤여야 한다. 그 사이에는 입력을 두지 않아,
        /// 전환이 입력 사이에 들어갈 자리와 바뀐 화면에서 날아오는 물건을 보고 입력할 시간을 남긴다.
        /// 이 간격 이상 뒤의 입력이 다음 미니게임이 이어받는 입력이다. 구간 시작 박에 딱 맞는 입력은 넘어간 것이 아니다.
        /// </summary>
        public double carryOverMinBeats = 0.5;

        /// <summary>
        /// 마지막 입력 박이 <paramref name="endBeat"/>인 패턴이, 다음 구간이 <paramref name="segmentEndBeat"/>에 시작할 때
        /// 다음 미니게임이 이어받는 패턴인지.
        /// </summary>
        public bool IsCarryOver(double endBeat, double segmentEndBeat) =>
            !double.IsPositiveInfinity(segmentEndBeat) && endBeat >= segmentEndBeat + carryOverMinBeats - 1e-6;
    }

    /// <summary>
    /// 리믹스 구간 하나가 쓰는 패턴 목록. 이 목록의 패턴은 큐가 모두 [StartBeat, EndBeat) 안에 있어야 하고,
    /// 노트는 EndBeat(다음 구간 시작)를 넘어도 된다. 넘어간 노트는 다음 구간의 미니게임이 이어받는다.
    /// 따라 치기는 응답까지 범위 안에 둔다.
    /// </summary>
    public sealed class GeneratorPatternSet
    {
        public IReadOnlyList<PatternDefinition> Patterns;
        public double StartBeat = double.NegativeInfinity;
        public double EndBeat = double.PositiveInfinity;
        /// <summary>패턴 이름 앞에 붙이는 이름(미니게임 ID). 쿨다운·반복 감점·보고에서 미니게임별로 패턴을 구분한다.</summary>
        public string Label;
    }

    public sealed class GeneratorInput
    {
        public double[] BeatTimes;
        public int BeatsPerBar = 4;
        public int FirstDownbeatIndex;
        public int GridPerBeat = AudioAnalyzer.GridPerBeat;
        public float[] GridFull;
        public float[] GridLow;
        public float[] GridMid;
        public float[] GridHigh;
        public int[] BarLevel;
        /// <summary>미니게임 하나짜리 채보의 패턴 목록.</summary>
        public IReadOnlyList<PatternDefinition> Patterns;
        /// <summary>리믹스: 구간마다의 패턴 목록(시작 순). 채우면 <see cref="Patterns"/> 대신 쓰고, 결과 패턴의 segment는 이 목록의 인덱스가 된다.</summary>
        public IReadOnlyList<GeneratorPatternSet> PatternSets;
        public GeneratorSettings Settings;

        public static GeneratorInput FromAnalysis(AnalysisResult analysis, IReadOnlyList<PatternDefinition> patterns, GeneratorSettings settings)
        {
            return new GeneratorInput
            {
                BeatTimes = analysis.BeatTimes,
                BeatsPerBar = analysis.BeatsPerBar,
                FirstDownbeatIndex = analysis.FirstDownbeatIndex,
                GridPerBeat = analysis.GridPerBeat,
                GridFull = analysis.GridFull,
                GridLow = analysis.GridLow,
                GridMid = analysis.GridMid,
                GridHigh = analysis.GridHigh,
                BarLevel = analysis.BarLevel,
                Patterns = patterns,
                Settings = settings,
            };
        }

        public float[] GetGrid(OnsetBand band)
        {
            switch (band)
            {
                case OnsetBand.Low: return GridLow ?? GridFull;
                case OnsetBand.Mid: return GridMid ?? GridFull;
                case OnsetBand.High: return GridHigh ?? GridFull;
                default: return GridFull;
            }
        }
    }

    public sealed class GeneratorReport
    {
        public int PhraseCount;
        public int ActivePhraseCount;
        public int PatternCount;
        public int NoteCount;
        public readonly SortedDictionary<string, int> PatternCounts = new SortedDictionary<string, int>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"구절 {ActivePhraseCount}/{PhraseCount} 활성, 패턴 {PatternCount}개, 노트 {NoteCount}개");
            if (PatternCounts.Count > 0)
            {
                sb.Append(" (");
                bool first = true;
                foreach (var pair in PatternCounts)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(pair.Key).Append(' ').Append(pair.Value);
                    first = false;
                }
                sb.Append(')');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 곡 분석 결과와 미니게임의 패턴 목록으로 채보를 만든다.
    /// 곡을 2마디 구절로 나누고, 구절마다 쉴지 정한 뒤, 온셋 강도가 높은 자리에 패턴을 하나씩 골라 끼운다.
    /// 리믹스는 구간마다의 패턴 목록을 받아 곡 전체를 한 번에 채운다. 패턴은 큐가 있는 구간의 목록에서 고르므로,
    /// 구간 경계에서는 앞 미니게임의 큐로 시작해 다음 미니게임에서 입력하는 패턴이 생길 수 있다.
    /// 같은 시드면 항상 같은 결과가 나온다.
    /// </summary>
    public static class ChartGenerator
    {
        const double Epsilon = 1e-6;

        /// <summary>고를 수 있는 패턴 하나와 그 패턴이 속한 구간.</summary>
        sealed class Option
        {
            public PatternDefinition Definition;
            public GeneratorPatternSet Set;
            public int SetIndex;
            /// <summary>쿨다운·반복 감점·보고에 쓰는 이름. 구간 목록이 여럿이면 미니게임 이름이 붙는다.</summary>
            public string Key;
        }

        sealed class Candidate
        {
            public Option Option;
            public PatternInstance Instance;
            public double Score;
            public double EndBeat;
        }

        sealed class Placement
        {
            public const int RecentWindow = 4;
            public double CursorBeat = double.NegativeInfinity;
            public double LastInputTime = double.NegativeInfinity;
            /// <summary>마지막으로 놓은 패턴의 구간. 이보다 뒤 구간의 패턴은 그 구간의 첫 패턴이다.</summary>
            public int LastSetIndex = -1;
            public string LastKey;
            public readonly List<string> Recent = new List<string>();
            public readonly List<string> History = new List<string>();

            public bool IsCoolingDown(Option option)
            {
                for (int i = History.Count - 1; i >= 0 && i >= History.Count - option.Definition.cooldown; i--)
                    if (History[i] == option.Key) return true;
                return false;
            }
        }

        sealed class Context
        {
            public GeneratorInput Input;
            public GeneratorSettings Settings;
            public TempoMap Tempo;
            public double LimitBeat;

            public double Strength(OnsetBand band, double beat)
            {
                var grid = Input.GetGrid(band);
                if (grid == null) return 0;
                int index = (int)Math.Round(beat * Input.GridPerBeat);
                return index < 0 || index >= grid.Length ? 0 : grid[index];
            }

            public double MeanStrength(OnsetBand band, double from, double to, double step)
            {
                double sum = 0;
                int count = 0;
                for (double b = from; b < to - Epsilon; b += step)
                {
                    sum += Strength(band, b);
                    count++;
                }
                return count == 0 ? 0 : sum / count;
            }

            public double PositionInBar(double beat)
            {
                int bpb = Input.BeatsPerBar;
                double p = (beat - Input.FirstDownbeatIndex) % bpb;
                return p < 0 ? p + bpb : p;
            }
        }

        public static List<PatternInstance> Generate(GeneratorInput input, out GeneratorReport report)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.BeatTimes == null || input.BeatTimes.Length < 8) throw new ArgumentException("비트 정보가 부족합니다.");
            if (input.BarLevel == null) throw new ArgumentException("마디 강도 정보가 없습니다.");

            var s = input.Settings ?? new GeneratorSettings();
            var ctx = new Context
            {
                Input = input,
                Settings = s,
                Tempo = new TempoMap(input.BeatTimes),
                LimitBeat = input.BeatTimes.Length - 1 - Math.Max(0, s.tailBeats),
            };
            var options = CollectOptions(input, s);

            var rng = new Random(s.seed);
            var result = new List<PatternInstance>();
            var state = new Placement();
            report = new GeneratorReport();

            int bpb = input.BeatsPerBar;
            int phraseBars = Math.Max(1, s.phraseBars);
            int activeRun = 0;
            double densityScale = 0.6 + 0.1 * s.difficulty;

            for (int bar = Math.Max(0, s.leadInBars); bar < input.BarLevel.Length; bar += phraseBars)
            {
                double p0 = input.FirstDownbeatIndex + bar * bpb;
                double p1 = Math.Min(p0 + phraseBars * bpb, ctx.LimitBeat);
                if (p1 - p0 < 1) break;
                report.PhraseCount++;

                int level = 0;
                for (int b = bar; b < Math.Min(input.BarLevel.Length, bar + phraseBars); b++)
                    level = Math.Max(level, input.BarLevel[b]);
                level = Math.Max(0, Math.Min(2, level));

                double energy = ctx.MeanStrength(OnsetBand.Full, p0, p1, 0.5);
                bool rest = energy < s.silenceThreshold
                    || activeRun >= At(s.maxActivePhrasesByLevel, level, 3)
                    || rng.NextDouble() < At(s.restChanceByLevel, level, 0.2f);
                if (rest)
                {
                    activeRun = 0;
                    continue;
                }
                activeRun++;
                report.ActivePhraseCount++;

                int target = Math.Max(1, (int)Math.Round(At(s.notesPerBeatByLevel, level, 0.4f) * densityScale * (p1 - p0)));
                int placed = 0;
                for (int guard = 0; guard < 16 && placed < target; guard++)
                {
                    var candidates = new List<Candidate>();
                    foreach (var option in options)
                    {
                        var def = option.Definition;
                        if (level < def.minLevel || level > def.maxLevel) continue;
                        if (state.IsCoolingDown(option)) continue;
                        if (def.kind == PatternKind.CallAndResponse)
                        {
                            if (placed == 0) TryCallAndResponse(option, p0, p1, level, state, ctx, candidates);
                        }
                        else
                        {
                            CollectFixed(option, p0, p1, level, state, ctx, candidates);
                        }
                    }
                    if (candidates.Count == 0) break;

                    var pick = Pick(candidates, rng);
                    Commit(pick, state, ctx, result, report);
                    placed += pick.Instance.notes.Count;
                }
            }

            report.PatternCount = result.Count;
            return result;
        }

        /// <summary>구간마다 난이도 이하의 쓸 수 있는 패턴을 모은다. 쓸 패턴이 하나도 없는 구간이 있으면 예외.</summary>
        static List<Option> CollectOptions(GeneratorInput input, GeneratorSettings s)
        {
            bool remix = input.PatternSets != null && input.PatternSets.Count > 0;
            var sets = remix ? input.PatternSets : new[] { new GeneratorPatternSet { Patterns = input.Patterns } };
            var options = new List<Option>();
            for (int k = 0; k < sets.Count; k++)
            {
                var set = sets[k];
                string where = remix ? $"구간 {k + 1}: " : string.Empty;
                if (set?.Patterns == null || set.Patterns.Count == 0) throw new ArgumentException($"{where}패턴이 없습니다.");
                if (k > 0 && set.StartBeat < sets[k - 1].EndBeat - Epsilon) throw new ArgumentException($"{where}앞 구간과 범위가 겹칩니다.");

                int before = options.Count;
                foreach (var p in set.Patterns)
                {
                    if (p == null || p.difficulty > s.difficulty) continue;
                    if (p.kind == PatternKind.Fixed && (p.notes == null || p.notes.Count == 0)) continue;
                    string key = string.IsNullOrEmpty(set.Label) ? p.id : $"{set.Label}/{p.id}";
                    options.Add(new Option { Definition = p, Set = set, SetIndex = k, Key = key });
                }
                if (options.Count == before) throw new ArgumentException($"{where}난이도 {s.difficulty} 이하인 패턴이 없습니다.");
            }
            return options;
        }

        static void CollectFixed(Option option, double p0, double p1, int level, Placement state, Context ctx, List<Candidate> output)
        {
            var def = option.Definition;
            double minCue = 0, minNote = double.MaxValue, maxEnd = double.MinValue;
            foreach (var cue in def.cues) minCue = Math.Min(minCue, cue.offset);
            foreach (var note in def.notes)
            {
                minNote = Math.Min(minNote, note.offset);
                maxEnd = Math.Max(maxEnd, note.offset + (note.type == NoteType.Hold ? note.holdBeats : 0));
            }

            double step = def.anchorStep > 0 ? def.anchorStep : 1;
            double earliest = Math.Max(p0 - minNote, state.CursorBeat - minCue);
            double first = Math.Ceiling(earliest / step - Epsilon) * step;
            // 직전 입력 뒤 1박을 넘는 공백은 감점. 리드가 긴 패턴은 그만큼 손해를 본다.
            double waitFrom = Math.Max(state.CursorBeat, p0 - 1);

            for (double anchor = first; anchor + maxEnd <= p1 + Epsilon && anchor <= first + ctx.Settings.lookaheadBeats + Epsilon; anchor += step)
            {
                var instance = Instantiate(def, anchor);
                if (!Fits(instance, option, false, state, ctx, out double endBeat)) continue;

                double score = ScoreFixed(def, instance, level, ctx);
                score -= 0.06 * Math.Max(0, anchor + minNote - waitFrom - 1);
                score += RepeatPenalty(option, state);
                score += CarryOverBonus(option.Set, endBeat, ctx.Settings);
                output.Add(new Candidate { Option = option, Instance = instance, Score = score, EndBeat = endBeat });
            }
        }

        static double ScoreFixed(PatternDefinition def, PatternInstance instance, int level, Context ctx)
        {
            double sum = 0, min = double.MaxValue;
            foreach (var note in instance.notes)
            {
                double v = ctx.Strength(def.band, instance.anchorBeat + note.offset);
                sum += v;
                min = Math.Min(min, v);
            }
            double score = sum / instance.notes.Count;
            if (min < ctx.Settings.minNoteStrength) score -= 0.5;

            double position = ctx.PositionInBar(instance.anchorBeat);
            if (Math.Abs(position) < Epsilon) score += 0.15;
            else if (Math.Abs(position - 2) < Epsilon) score += 0.08;
            else if (Math.Abs(position - Math.Round(position)) < Epsilon) score += 0.03;

            // 홀드는 누르는 동안 새 타격이 적을수록(음이 이어질수록) 자연스럽고, 바쁜 구간에서는 어색하다.
            foreach (var note in instance.notes)
            {
                if (note.type != NoteType.Hold) continue;
                double from = instance.anchorBeat + note.offset + 0.5;
                double to = instance.anchorBeat + note.offset + note.holdBeats;
                double inside = ctx.MeanStrength(def.band, from, to, 0.5);
                score += 0.3 * (0.5 - inside);
            }

            score += WeightBonus(def);
            score += 0.08 * (def.difficulty - 2) * (level - 1);
            return score;
        }

        static void TryCallAndResponse(Option option, double p0, double p1, int level, Placement state, Context ctx, List<Candidate> output)
        {
            var def = option.Definition;
            int callBeats = Math.Max(1, def.callBeats);
            if (p0 + 2 * callBeats > p1 + Epsilon) return;
            if (state.CursorBeat > p0 + Epsilon) return;

            double anchor = p0 + callBeats;
            double step = 1.0 / Math.Max(1, def.responseGrid);
            int slots = (int)Math.Round(callBeats / step);
            var response = new double[slots];
            var call = new double[slots];
            for (int k = 0; k < slots; k++)
            {
                response[k] = ctx.Strength(def.band, anchor + k * step);
                call[k] = ctx.Strength(def.band, anchor - callBeats + k * step);
            }

            int wanted = Math.Max(def.minResponseNotes, Math.Min(def.maxResponseNotes, def.minResponseNotes + level));
            var order = new List<int>();
            for (int k = 0; k < slots; k++) order.Add(k);
            order.Sort((a, b) => response[b].CompareTo(response[a]));

            var chosen = new List<int>();
            foreach (int k in order)
            {
                if (chosen.Count >= wanted) break;
                if (response[k] < ctx.Settings.minNoteStrength) break;
                double t = ctx.Tempo.BeatToTime(anchor + k * step);
                bool spaced = true;
                foreach (int c in chosen)
                {
                    if (Math.Abs(ctx.Tempo.BeatToTime(anchor + c * step) - t) < ctx.Settings.minNoteGapSeconds)
                    {
                        spaced = false;
                        break;
                    }
                }
                if (spaced) chosen.Add(k);
            }
            if (chosen.Count < def.minResponseNotes) return;
            chosen.Sort();

            var instance = new PatternInstance { patternId = def.id, anchorBeat = anchor };
            double strengthSum = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                double offset = chosen[i] * step;
                instance.notes.Add(new ChartNote { offset = offset, type = NoteType.Tap });
                instance.cues.Add(new ChartCue { offset = offset - callBeats, cueId = def.callCueId, targetNote = i });
                strengthSum += response[chosen[i]];
            }
            // 따라 치기는 선반에 줄선 모습이 곧 응답 리듬이라, 응답까지 그 미니게임 구간 안에 둔다.
            if (!Fits(instance, option, true, state, ctx, out double endBeat)) return;

            // 음악이 앞뒤 구간에서 같은 리듬을 반복할수록 따라 치기가 자연스럽다.
            double similarity = Math.Max(0, Correlation(call, response));
            double score = strengthSum / chosen.Count + 0.2 * similarity;
            score += WeightBonus(def);
            score += 0.08 * (def.difficulty - 2) * (level - 1);
            score += RepeatPenalty(option, state);
            output.Add(new Candidate { Option = option, Instance = instance, Score = score, EndBeat = endBeat });
        }

        static PatternInstance Instantiate(PatternDefinition def, double anchor)
        {
            var instance = new PatternInstance { patternId = def.id, anchorBeat = anchor };
            foreach (var cue in def.cues)
                instance.cues.Add(new ChartCue { offset = cue.offset, cueId = cue.cueId, targetNote = cue.targetNote });
            foreach (var note in def.notes)
                instance.notes.Add(new ChartNote { offset = note.offset, type = note.type, holdBeats = note.holdBeats });
            return instance;
        }

        /// <summary>
        /// 큐가 앞 패턴과 겹치지 않고 그 패턴 목록의 구간 안에 있으며, 입력 간격이 충분하고, 곡 범위 안에 있는지 확인한다.
        /// <paramref name="keepNotesInSet"/>면 노트(뗌 포함)도 구간 안에 있어야 한다.
        /// 리믹스 구간 경계에서는 전환이 입력 사이에 들어갈 자리(<see cref="SegmentSwitch"/>)를 남긴다.
        /// </summary>
        static bool Fits(PatternInstance instance, Option option, bool keepNotesInSet, Placement state, Context ctx, out double endBeat)
        {
            var s = ctx.Settings;
            var set = option.Set;
            endBeat = double.MinValue;
            double firstBeat = double.MaxValue;
            foreach (var cue in instance.cues)
            {
                double beat = instance.anchorBeat + cue.offset;
                if (beat < 0 || beat < state.CursorBeat - Epsilon) return false;
                if (beat < set.StartBeat - Epsilon || beat >= set.EndBeat - Epsilon) return false;
                if (ctx.Tempo.BeatToTime(beat) < s.minCueTimeSeconds) return false;
                firstBeat = Math.Min(firstBeat, beat);
            }

            var inputs = new List<double>();
            foreach (var note in instance.notes)
            {
                double beat = instance.anchorBeat + note.offset;
                if (InCarryGap(beat, set, s)) return false;
                double press = ctx.Tempo.BeatToTime(beat);
                inputs.Add(press);
                firstBeat = Math.Min(firstBeat, beat);
                double end = beat;
                if (note.type == NoteType.Hold)
                {
                    end = beat + note.holdBeats;
                    if (InCarryGap(end, set, s)) return false;
                    double release = ctx.Tempo.BeatToTime(end);
                    if (release - press < s.minHoldSeconds) return false;
                    inputs.Add(release);
                }
                endBeat = Math.Max(endBeat, end);
            }
            if (endBeat > ctx.LimitBeat + Epsilon) return false;
            if (keepNotesInSet && endBeat >= set.EndBeat - Epsilon) return false;

            // 새 구간의 첫 패턴은 앞 입력과 전환 간격 이상 떨어져 시작한다. 그 사이가 연출을 바꿀 자리다.
            if (option.SetIndex > 0 && option.SetIndex > state.LastSetIndex
                && ctx.Tempo.BeatToTime(firstBeat) - state.LastInputTime < SegmentSwitch.ClearanceSeconds)
                return false;

            inputs.Sort();
            if (inputs[0] - state.LastInputTime < s.minNoteGapSeconds) return false;
            for (int i = 1; i < inputs.Count; i++)
                if (inputs[i] - inputs[i - 1] < s.minNoteGapSeconds) return false;
            return true;
        }

        /// <summary>
        /// 다음 구간 시작 박 바로 뒤(이어받기 최소 간격 안)인지. 앞 구간 패턴의 입력을 여기 두면 전환이 입력 사이에 들어갈 자리가 모자란다.
        /// </summary>
        static bool InCarryGap(double beat, GeneratorPatternSet set, GeneratorSettings s) =>
            !double.IsPositiveInfinity(set.EndBeat)
            && beat > set.EndBeat + Epsilon && beat < set.EndBeat + s.carryOverMinBeats - Epsilon;

        /// <summary>가중치 1이 기준. 홀드처럼 가끔 나와야 하는 패턴은 가중치를 낮춰 둔다.</summary>
        static double WeightBonus(PatternDefinition def) => 0.4 * (def.weight - 1);

        /// <summary>최근 몇 패턴 안에서 자주 쓴 패턴일수록 감점해 한 패턴만 반복되지 않게 한다.</summary>
        static double RepeatPenalty(Option option, Placement state)
        {
            int uses = 0;
            foreach (var key in state.Recent)
                if (key == option.Key) uses++;
            double penalty = -0.1 * uses;
            if (option.Key == state.LastKey) penalty -= 0.05;
            return penalty;
        }

        /// <summary>큐는 이 구간에, 입력은 미니게임이 바뀌고 조금 뒤에 오는 패턴(이어받기)이면 가산점.</summary>
        static double CarryOverBonus(GeneratorPatternSet set, double endBeat, GeneratorSettings s) =>
            s.IsCarryOver(endBeat, set.EndBeat) ? s.carryOverBonus : 0;

        /// <summary>최고점 근처 후보 중에서 점수 가중 무작위로 고른다. 매번 같은 패턴만 나오는 것을 막는다.</summary>
        static Candidate Pick(List<Candidate> candidates, Random rng)
        {
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            double best = candidates[0].Score;
            int count = 0;
            while (count < candidates.Count && count < 3 && candidates[count].Score >= best - 0.15) count++;

            var weights = new double[count];
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                weights[i] = Math.Exp((candidates[i].Score - best) / 0.08);
                total += weights[i];
            }
            double roll = rng.NextDouble() * total;
            for (int i = 0; i < count; i++)
            {
                roll -= weights[i];
                if (roll <= 0) return candidates[i];
            }
            return candidates[0];
        }

        static void Commit(Candidate pick, Placement state, Context ctx, List<PatternInstance> result, GeneratorReport report)
        {
            var option = pick.Option;
            pick.Instance.segment = option.SetIndex;
            result.Add(pick.Instance);
            state.CursorBeat = pick.EndBeat;
            state.LastInputTime = ctx.Tempo.BeatToTime(pick.EndBeat);
            state.LastSetIndex = option.SetIndex;
            state.LastKey = option.Key;
            state.Recent.Add(option.Key);
            if (state.Recent.Count > Placement.RecentWindow) state.Recent.RemoveAt(0);
            state.History.Add(option.Key);

            report.NoteCount += pick.Instance.notes.Count;
            report.PatternCounts.TryGetValue(option.Key, out int n);
            report.PatternCounts[option.Key] = n + 1;
        }

        static double Correlation(double[] a, double[] b)
        {
            int n = a.Length;
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++)
            {
                ma += a[i];
                mb += b[i];
            }
            ma /= n;
            mb /= n;
            double cov = 0, va = 0, vb = 0;
            for (int i = 0; i < n; i++)
            {
                cov += (a[i] - ma) * (b[i] - mb);
                va += (a[i] - ma) * (a[i] - ma);
                vb += (b[i] - mb) * (b[i] - mb);
            }
            return va <= 0 || vb <= 0 ? 0 : cov / Math.Sqrt(va * vb);
        }

        static T At<T>(T[] values, int index, T fallback)
        {
            if (values == null || values.Length == 0) return fallback;
            return values[Math.Max(0, Math.Min(values.Length - 1, index))];
        }
    }
}
