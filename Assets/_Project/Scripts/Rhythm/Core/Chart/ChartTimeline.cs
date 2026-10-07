using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    public sealed class TimelineNote
    {
        public int Id;
        public int PatternIndex;
        public int IndexInPattern;
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
        public double AnchorBeat;
        public readonly List<TimelineNote> Notes = new List<TimelineNote>();
        public readonly List<TimelineCue> Cues = new List<TimelineCue>();
        /// <summary>가장 이른 큐/노트 시각.</summary>
        public double StartTime;
        /// <summary>가장 늦은 큐/노트(뗌 포함) 시각.</summary>
        public double EndTime;
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
        public double LastEventTime { get; private set; }

        /// <summary>판정 단위 수. Tap은 1, Hold는 누름·뗌으로 2.</summary>
        public int JudgementPartCount { get; private set; }

        readonly List<TimelinePattern> patterns = new List<TimelinePattern>();
        readonly List<TimelineNote> notes = new List<TimelineNote>();
        readonly List<TimelineCue> cues = new List<TimelineCue>();

        ChartTimeline() { }

        public static ChartTimeline Build(IReadOnlyList<PatternInstance> instances, TempoMap tempoMap)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (tempoMap == null) throw new ArgumentNullException(nameof(tempoMap));

            var timeline = new ChartTimeline();
            var ordered = new List<PatternInstance>(instances);
            ordered.Sort((a, b) => a.anchorBeat.CompareTo(b.anchorBeat));

            for (int p = 0; p < ordered.Count; p++)
            {
                var src = ordered[p];
                var pattern = new TimelinePattern
                {
                    Index = p,
                    PatternId = src.patternId,
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
            return timeline;
        }
    }
}
