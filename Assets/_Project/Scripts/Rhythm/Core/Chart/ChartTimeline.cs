using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    public sealed class TimelineNote
    {
        public int Id;
        public int PatternIndex;
        public int IndexInPattern;
        /// <summary>속한 구간의 <see cref="TimelineSegment.Index"/>.</summary>
        public int Segment;
        public NoteType Type;
        public double Beat;
        public double Time;
        /// <summary>Hold의 뗌 시점. Tap이면 누름 시점과 같다.</summary>
        public double EndBeat;
        public double EndTime;
    }

    public sealed class TimelineCue
    {
        public int Id;
        public int PatternIndex;
        /// <summary>속한 구간의 <see cref="TimelineSegment.Index"/>. 큐 ID는 이 구간의 미니게임 안에서 해석한다.</summary>
        public int Segment;
        public string CueId;
        public double Beat;
        public double Time;
        /// <summary>예고 대상 노트의 <see cref="TimelineNote.Id"/>. 없으면 -1.</summary>
        public int TargetNoteId = -1;
    }

    public sealed class TimelinePattern
    {
        public int Index;
        public string PatternId;
        public int Segment;
        public double AnchorBeat;
        public readonly List<TimelineNote> Notes = new List<TimelineNote>();
        public readonly List<TimelineCue> Cues = new List<TimelineCue>();
        /// <summary>가장 이른 큐/노트 시각.</summary>
        public double StartTime;
        /// <summary>가장 늦은 큐/노트(뗌 포함) 시각.</summary>
        public double EndTime;
    }

    /// <summary>
    /// 곡의 한 구간과 그 구간을 맡는 미니게임. 구간이 없는 채보도 곡 전체를 덮는 구간 하나를 갖는다.
    /// 패턴은 큐가 있는 구간에 속하고, 노트는 다음 구간으로 넘어갈 수 있다(다음 미니게임이 이어받는다).
    /// </summary>
    public sealed class TimelineSegment
    {
        public int Index;
        /// <summary>구간을 맡는 미니게임 ID. 구간이 없는 채보면 null(스테이지의 첫 미니게임).</summary>
        public string MinigameId;
        /// <summary>구간 시작. 첫 구간은 곡 처음부터라 음의 무한대.</summary>
        public double StartBeat;
        public double StartTime;
        /// <summary>다음 구간 시작. 마지막 구간은 양의 무한대.</summary>
        public double EndBeat;
        public double EndTime;
        /// <summary>
        /// 이 구간 연출로 바꾸는 시각. 구간 시작이 입력과 겹치면 입력 사이로 옮겨진다(<see cref="SegmentSwitch"/>).
        /// 첫 구간은 곡 처음부터라 음의 무한대.
        /// </summary>
        public double SwitchTime;
        public double SwitchBeat;
        /// <summary>전환 시각에서 가장 가까운 입력까지의 거리(초). 첫 구간은 양의 무한대.</summary>
        public double SwitchClearance;
    }

    /// <summary>
    /// 박 단위로 저장된 채보를 템포 맵으로 초 단위까지 풀어 놓은 플레이용 타임라인.
    /// 노트와 큐는 각각 시간순으로 정렬돼 있다.
    /// </summary>
    public sealed class ChartTimeline
    {
        public IReadOnlyList<TimelinePattern> Patterns => patterns;
        public IReadOnlyList<TimelineNote> Notes => notes;
        public IReadOnlyList<TimelineCue> Cues => cues;
        /// <summary>구간 목록(시작 순). 항상 하나 이상이다.</summary>
        public IReadOnlyList<TimelineSegment> Segments => segments;
        public double LastEventTime { get; private set; }

        /// <summary>판정 단위 수. Tap은 1, Hold는 누름·뗌으로 2.</summary>
        public int JudgementPartCount { get; private set; }

        readonly List<TimelinePattern> patterns = new List<TimelinePattern>();
        readonly List<TimelineNote> notes = new List<TimelineNote>();
        readonly List<TimelineCue> cues = new List<TimelineCue>();
        readonly List<TimelineSegment> segments = new List<TimelineSegment>();

        ChartTimeline() { }

        public static ChartTimeline Build(IReadOnlyList<PatternInstance> instances, TempoMap tempoMap) => Build(instances, null, tempoMap);

        /// <param name="chartSegments">비어 있거나 null이면 곡 전체를 덮는 구간 하나로 본다.</param>
        public static ChartTimeline Build(IReadOnlyList<PatternInstance> instances, IReadOnlyList<ChartSegment> chartSegments, TempoMap tempoMap)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (tempoMap == null) throw new ArgumentNullException(nameof(tempoMap));

            var timeline = new ChartTimeline();
            timeline.BuildSegments(chartSegments, tempoMap);
            var ordered = new List<PatternInstance>(instances);
            ordered.Sort((a, b) => a.anchorBeat.CompareTo(b.anchorBeat));

            for (int p = 0; p < ordered.Count; p++)
            {
                var src = ordered[p];
                if (src.segment < 0 || src.segment >= timeline.segments.Count)
                    throw new ArgumentException($"패턴 '{src.patternId}'(앵커 {src.anchorBeat})의 구간 번호 {src.segment}가 구간 수 {timeline.segments.Count}를 벗어납니다.");

                var pattern = new TimelinePattern
                {
                    Index = p,
                    PatternId = src.patternId,
                    Segment = src.segment,
                    AnchorBeat = src.anchorBeat,
                    StartTime = double.MaxValue,
                    EndTime = double.MinValue,
                };

                for (int n = 0; n < src.notes.Count; n++)
                {
                    var noteSrc = src.notes[n];
                    double beat = src.anchorBeat + noteSrc.offset;
                    double endBeat = noteSrc.type == NoteType.Hold ? beat + Math.Max(0, noteSrc.holdBeats) : beat;
                    var note = new TimelineNote
                    {
                        PatternIndex = p,
                        IndexInPattern = n,
                        Segment = src.segment,
                        Type = noteSrc.type,
                        Beat = beat,
                        Time = tempoMap.BeatToTime(beat),
                        EndBeat = endBeat,
                        EndTime = tempoMap.BeatToTime(endBeat),
                    };
                    pattern.Notes.Add(note);
                    timeline.notes.Add(note);
                    pattern.StartTime = Math.Min(pattern.StartTime, note.Time);
                    pattern.EndTime = Math.Max(pattern.EndTime, note.EndTime);
                    timeline.JudgementPartCount += note.Type == NoteType.Hold ? 2 : 1;
                }

                foreach (var cueSrc in src.cues)
                {
                    double beat = src.anchorBeat + cueSrc.offset;
                    var cue = new TimelineCue
                    {
                        PatternIndex = p,
                        Segment = src.segment,
                        CueId = cueSrc.cueId,
                        Beat = beat,
                        Time = tempoMap.BeatToTime(beat),
                    };
                    if (cueSrc.targetNote >= 0 && cueSrc.targetNote < pattern.Notes.Count)
                        cue.TargetNoteId = -2 - cueSrc.targetNote; // 노트 Id 확정 후 치환
                    pattern.Cues.Add(cue);
                    timeline.cues.Add(cue);
                    pattern.StartTime = Math.Min(pattern.StartTime, cue.Time);
                    pattern.EndTime = Math.Max(pattern.EndTime, cue.Time);
                }

                timeline.patterns.Add(pattern);
            }

            timeline.notes.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (int i = 0; i < timeline.notes.Count; i++)
                timeline.notes[i].Id = i;

            timeline.cues.Sort((a, b) => a.Time.CompareTo(b.Time));
            for (int i = 0; i < timeline.cues.Count; i++)
            {
                var cue = timeline.cues[i];
                cue.Id = i;
                if (cue.TargetNoteId <= -2)
                    cue.TargetNoteId = timeline.patterns[cue.PatternIndex].Notes[-2 - cue.TargetNoteId].Id;
            }

            // 연출 쪽이 미리 준비할 수 있도록 패턴은 시작 시각 순으로 둔다.
            timeline.patterns.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            double last = 0;
            for (int i = 0; i < timeline.patterns.Count; i++)
            {
                var pattern = timeline.patterns[i];
                pattern.Index = i;
                foreach (var note in pattern.Notes) note.PatternIndex = i;
                foreach (var cue in pattern.Cues) cue.PatternIndex = i;
                last = Math.Max(last, pattern.EndTime);
            }
            timeline.LastEventTime = last;
            timeline.PlaceSwitches(tempoMap);
            return timeline;
        }

        /// <summary>구간마다 연출을 바꿀 시각을 정한다. 앞 구간 큐가 다 지난 뒤, 다음 구간 첫 사건 전, 입력과 겹치지 않게.</summary>
        void PlaceSwitches(TempoMap tempoMap)
        {
            var inputs = new List<double>();
            foreach (var note in notes)
            {
                inputs.Add(note.Time);
                if (note.Type == NoteType.Hold) inputs.Add(note.EndTime);
            }
            inputs.Sort();

            segments[0].SwitchTime = double.NegativeInfinity;
            segments[0].SwitchBeat = double.NegativeInfinity;
            segments[0].SwitchClearance = double.PositiveInfinity;
            for (int k = 1; k < segments.Count; k++)
            {
                double earliest = segments[k - 1].SwitchTime;
                foreach (var cue in cues)
                    if (cue.Segment < k) earliest = Math.Max(earliest, cue.Time);
                double latest = double.PositiveInfinity;
                foreach (var pattern in patterns)
                    if (pattern.Segment >= k) latest = Math.Min(latest, pattern.StartTime);

                var segment = segments[k];
                segment.SwitchTime = SegmentSwitch.Find(segment.StartTime, earliest, latest, inputs);
                segment.SwitchBeat = tempoMap.TimeToBeat(segment.SwitchTime);
                segment.SwitchClearance = SegmentSwitch.Clearance(segment.SwitchTime, inputs);
            }
        }

        void BuildSegments(IReadOnlyList<ChartSegment> chartSegments, TempoMap tempoMap)
        {
            int count = chartSegments == null || chartSegments.Count == 0 ? 1 : chartSegments.Count;
            for (int i = 0; i < count; i++)
            {
                var src = chartSegments == null || chartSegments.Count == 0 ? null : chartSegments[i];
                if (chartSegments != null && chartSegments.Count > 0 && src == null)
                    throw new ArgumentException($"구간 {i + 1}이 비어 있습니다.");
                double start = i == 0 ? double.NegativeInfinity : src.startBeat;
                if (i >= 2 && start <= segments[i - 1].StartBeat)
                    throw new ArgumentException($"구간 시작 비트는 증가해야 합니다. (구간 {i + 1})");
                segments.Add(new TimelineSegment
                {
                    Index = i,
                    MinigameId = src?.minigameId,
                    StartBeat = start,
                    StartTime = double.IsInfinity(start) ? start : tempoMap.BeatToTime(start),
                });
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                bool last = i == segments.Count - 1;
                segment.EndBeat = last ? double.PositiveInfinity : segments[i + 1].StartBeat;
                segment.EndTime = last ? double.PositiveInfinity : segments[i + 1].StartTime;
            }
        }
    }
}
