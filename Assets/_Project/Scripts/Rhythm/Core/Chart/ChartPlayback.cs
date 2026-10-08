using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 한 판(처음부터, 또는 시작 지점부터)에 재생할 채보와 그 진행 기록.
    /// <para>
    /// 채보는 패턴 단위로 재생한다. 판을 시작할 때 시작 지점 이후에 시작하는(첫 큐·노트가 시작 지점 이후인) 패턴을 고르고,
    /// 이 목록은 판이 끝날 때까지 바뀌지 않는다. 큐 소리 예약, 오브젝트 준비, 큐 알림은 모두 이 목록을 곡 시간 순서대로 꺼내 쓰고,
    /// 목록에 없는 패턴의 노트는 판정하지 않는다(<see cref="SkipExcludedNotes"/>). 그래서 한 패턴의 소리·오브젝트·판정은 함께 나오거나
    /// 함께 빠진다. 어느 하나만 건너뛰는 길은 없다.
    /// </para>
    /// <para>
    /// 곡 시간은 뒤로 가지 않으므로(일시정지 후 재개는 멈춘 시각에서 이어간다) 꺼낸 위치를 되돌릴 일이 없다. 예외는 소리 하나다.
    /// 일시정지하면 미리 예약해 둔 소리가 꺼지므로, 멈춘 시각 이후 큐부터 다시 예약한다(<see cref="RewindSounds"/>).
    /// </para>
    /// 무엇을 했는지 노트·큐마다 기록해, 순서가 어긋나거나 같은 일을 두 번 하면 위반으로 세고(<see cref="ViolationCount"/>),
    /// 판정이 끝났어야 할 패턴에 빠진 것이 있는지 확인한다(<see cref="CountIncomplete"/>).
    /// </summary>
    public sealed class ChartPlayback
    {
        /// <summary>패턴 시작 시각과 시작 지점을 비교할 때의 허용 오차(초). 같은 박에서 계산한 두 시각은 같다고 본다.</summary>
        const double TimeEpsilon = 1e-6;
        const int MaxViolationMessages = 20;

        readonly ChartTimeline timeline;
        readonly bool[] included;
        readonly List<TimelinePattern> patterns = new List<TimelinePattern>();
        readonly List<TimelineCue> cues = new List<TimelineCue>();
        readonly bool[] patternSpawned;
        readonly bool[] cueShown;
        readonly bool[] cueSounded;
        readonly int[] judgedParts;
        readonly List<string> violations = new List<string>();
        int nextPattern;
        int nextCue;
        int nextSound;
        double lastTime = double.NegativeInfinity;

        public ChartPlayback(ChartTimeline timeline, double startTime)
        {
            this.timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            StartTime = startTime;
            included = new bool[timeline.Patterns.Count];
            foreach (var pattern in timeline.Patterns)
            {
                if (!StartsAtOrAfter(pattern, startTime)) continue;
                included[pattern.Index] = true;
                patterns.Add(pattern);
                foreach (var note in pattern.Notes) JudgementPartCount += PartCount(note);
            }
            foreach (var cue in timeline.Cues)
                if (included[cue.PatternIndex]) cues.Add(cue);

            patternSpawned = new bool[timeline.Patterns.Count];
            cueShown = new bool[timeline.Cues.Count];
            cueSounded = new bool[timeline.Cues.Count];
            judgedParts = new int[timeline.Notes.Count];
        }

        /// <summary>시작 지점(곡 시각, 초).</summary>
        public double StartTime { get; }
        /// <summary>재생하는 패턴(시작 시각 순). 판이 끝날 때까지 바뀌지 않는다.</summary>
        public IReadOnlyList<TimelinePattern> Patterns => patterns;
        /// <summary>재생하는 패턴의 큐(시간순).</summary>
        public IReadOnlyList<TimelineCue> Cues => cues;
        /// <summary>재생하는 패턴의 판정 단위 수. Tap은 1, Hold는 2.</summary>
        public int JudgementPartCount { get; }
        /// <summary>채보 전체를 재생하는지(시작 지점 앞에서 빠진 패턴이 없다).</summary>
        public bool IsFullPlay => patterns.Count == timeline.Patterns.Count;

        public int SpawnedPatternCount { get; private set; }
        public int ShownCueCount { get; private set; }
        /// <summary>지금 소리가 예약돼 있거나 이미 울린 큐 수. 일시정지로 꺼진 소리는 다시 예약할 때까지 빠진다.</summary>
        public int SoundedCueCount { get; private set; }
        public int JudgedPartCount { get; private set; }
        /// <summary>소리를 예약하려는데 이미 그 시각이 지나 있던 큐 수. 큰 프레임 멈춤에서만 생기며, 그래도 소리는 바로 울린다.</summary>
        public int LateSoundCount { get; private set; }
        /// <summary>처음 늦게 예약한 큐 소리 설명. 없으면 null.</summary>
        public string FirstLateSound { get; private set; }
        public int ViolationCount { get; private set; }
        /// <summary>위반 내용(앞의 몇 개만).</summary>
        public IReadOnlyList<string> Violations => violations;

        /// <summary>패턴이 시작 지점 이후에 시작하는지. 시작 지점에 딱 맞는 패턴도 들어간다.</summary>
        public static bool StartsAtOrAfter(TimelinePattern pattern, double startTime) => pattern.StartTime >= startTime - TimeEpsilon;

        public bool Includes(TimelinePattern pattern) => included[pattern.Index];
        public bool Includes(TimelineNote note) => included[note.PatternIndex];
        public bool IsSpawned(TimelinePattern pattern) => patternSpawned[pattern.Index];

        /// <summary>재생하지 않는 패턴의 노트를 판정에서 뺀다. 판을 시작할 때 한 번 부른다.</summary>
        public void SkipExcludedNotes(Judge judge)
        {
            foreach (var note in timeline.Notes)
                if (!included[note.PatternIndex]) judge.Skip(note);
        }

        /// <summary>
        /// 소리를 예약할 다음 큐. 곡 시각 <paramref name="horizon"/>까지의 큐를 시간 순서대로 하나씩 꺼낸다.
        /// 구간과 상관없이 꺼낸다. 다음 구간 큐 소리도 출력 지연보다 먼저 예약해야 제때 울리고, 그 큐의 오브젝트는 구간 전환 때 준비되는데
        /// 전환은 늘 그 큐보다 앞이다.
        /// </summary>
        public bool TryTakeSound(double horizon, out TimelineCue cue)
        {
            if (nextSound >= cues.Count || cues[nextSound].Time > horizon)
            {
                cue = null;
                return false;
            }
            cue = cues[nextSound++];
            if (cueSounded[cue.Id]) Violate($"큐 #{cue.Id} '{cue.CueId}'({cue.Time:0.000}s) 소리를 두 번 예약");
            cueSounded[cue.Id] = true;
            SoundedCueCount++;
            return true;
        }

        /// <summary>
        /// 일시정지로 예약해 둔 소리가 꺼졌다. 곡 시각 <paramref name="time"/> 이후 큐부터 다시 예약하게 한다.
        /// 그 시각까지의 큐 소리는 이미 울렸다.
        /// </summary>
        public void RewindSounds(double time)
        {
            int first = nextSound;
            while (first > 0 && cues[first - 1].Time > time) first--;
            for (int i = first; i < nextSound; i++)
            {
                cueSounded[cues[i].Id] = false;
                SoundedCueCount--;
            }
            nextSound = first;
        }

        /// <summary>소리 예약이 늦었다(이미 그 시각이 지나 바로 울렸다).</summary>
        public void ReportLateSound(TimelineCue cue, double lateSeconds)
        {
            LateSoundCount++;
            if (FirstLateSound == null) FirstLateSound = $"큐 #{cue.Id} '{cue.CueId}'({cue.Time:0.000}s) 소리를 {lateSeconds * 1000:0}ms 늦게 예약(바로 울림)";
        }

        /// <summary>
        /// 오브젝트를 준비할 다음 패턴. 첫 큐·노트보다 <paramref name="lead"/>초 먼저 꺼내되, 그 패턴 구간의 연출이 화면에 들어온 뒤에만
        /// (<paramref name="openSegment"/> 이하) 꺼낸다. 꺼낸 패턴의 노트는 모두 오브젝트가 생긴 것으로 기록한다.
        /// </summary>
        public bool TryTakePattern(double time, double lead, int openSegment, out TimelinePattern pattern)
        {
            if (nextPattern >= patterns.Count || patterns[nextPattern].Segment > openSegment || patterns[nextPattern].StartTime - lead > time)
            {
                pattern = null;
                return false;
            }
            pattern = patterns[nextPattern++];
            patternSpawned[pattern.Index] = true;
            SpawnedPatternCount++;
            return true;
        }

        /// <summary>연출에 알릴 다음 큐. 큐 시각이 된 큐를, 그 큐 구간의 연출이 화면에 들어온 뒤에 꺼낸다.</summary>
        public bool TryTakeCue(double time, int openSegment, out TimelineCue cue)
        {
            if (nextCue >= cues.Count || cues[nextCue].Segment > openSegment || cues[nextCue].Time > time)
            {
                cue = null;
                return false;
            }
            cue = cues[nextCue++];
            if (!patternSpawned[cue.PatternIndex]) Violate($"큐 #{cue.Id} '{cue.CueId}'({cue.Time:0.000}s)가 오브젝트 준비 전에 옴");
            cueShown[cue.Id] = true;
            ShownCueCount++;
            return true;
        }

        /// <summary>판정이 났다. 재생하지 않는 패턴의 노트이거나, 오브젝트가 없거나, 같은 판정 단위가 두 번 오면 위반이다.</summary>
        public void RecordJudgement(NoteJudgement judgement)
        {
            var note = judgement.Note;
            if (!included[note.PatternIndex]) Violate($"재생하지 않는 패턴의 노트 #{note.Id}({note.Time:0.000}s)가 판정됨");
            else if (!patternSpawned[note.PatternIndex]) Violate($"노트 #{note.Id}({note.Time:0.000}s)가 오브젝트 없이 판정됨");
            judgedParts[note.Id]++;
            JudgedPartCount++;
            if (judgedParts[note.Id] > PartCount(note)) Violate($"노트 #{note.Id}({note.Time:0.000}s)가 판정 단위보다 많이 판정됨");
        }

        /// <summary>진행한 곡 시각(프레임·일시정지). 앞 시각보다 이르면 곡 시간이 뒤로 간 것이라 위반이다.</summary>
        public void RecordTime(double time)
        {
            if (time < lastTime - TimeEpsilon) Violate($"곡 시각이 뒤로 감({lastTime:0.000}s → {time:0.000}s)");
            if (time > lastTime) lastTime = time;
        }

        /// <summary>
        /// 판정이 끝났어야 할 패턴(마지막 입력의 판정 범위가 판정 시각 <paramref name="judgeTime"/> 전에 끝난 패턴) 가운데 오브젝트·큐 알림·
        /// 큐 소리·판정 중 빠진 것이 있는 패턴 수. 판이 끝날 때는 양의 무한대로 불러 모든 패턴을 확인한다.
        /// </summary>
        /// <param name="window">가장 넓은 판정 범위(초).</param>
        /// <param name="details">빠진 내용을 적을 목록(선택).</param>
        public int CountIncomplete(double judgeTime, double window, List<string> details = null)
        {
            int count = 0;
            foreach (var pattern in patterns)
            {
                if (pattern.EndTime + window >= judgeTime) continue;
                int shown = 0, sounded = 0, judged = 0, parts = 0;
                foreach (var cue in pattern.Cues)
                {
                    if (cueShown[cue.Id]) shown++;
                    if (cueSounded[cue.Id]) sounded++;
                }
                foreach (var note in pattern.Notes)
                {
                    judged += judgedParts[note.Id];
                    parts += PartCount(note);
                }
                if (patternSpawned[pattern.Index] && shown == pattern.Cues.Count && sounded == pattern.Cues.Count && judged == parts) continue;
                count++;
                details?.Add($"패턴 '{pattern.PatternId}'({pattern.StartTime:0.000}s): 오브젝트 {(patternSpawned[pattern.Index] ? "있음" : "없음")}, " +
                             $"큐 알림 {shown}/{pattern.Cues.Count}, 큐 소리 {sounded}/{pattern.Cues.Count}, 판정 {judged}/{parts}");
            }
            return count;
        }

        static int PartCount(TimelineNote note) => note.Type == NoteType.Hold ? 2 : 1;

        void Violate(string message)
        {
            ViolationCount++;
            if (violations.Count < MaxViolationMessages) violations.Add(message);
        }
    }
}
