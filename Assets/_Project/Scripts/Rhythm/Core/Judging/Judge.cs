using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 버튼 하나짜리 입력을 노트에 매칭해 판정한다.
    /// 모든 입력이 같은 버튼이므로 "판정 범위 안에서 아직 판정되지 않은 가장 이른 노트"에 매칭한다.
    /// 시간은 모두 곡 시간(초) 기준이며, 호출하는 쪽에서 지연 보정을 적용해 넘긴다.
    /// </summary>
    public sealed class Judge
    {
        enum State : byte { Pending, Holding, Done }

        public event Action<NoteJudgement> Judged;
        /// <summary>매칭되는 노트가 없는 입력(헛치기). 인자는 입력 시각.</summary>
        public event Action<double> Whiffed;

        readonly IReadOnlyList<TimelineNote> notes;
        readonly State[] states;
        readonly JudgeWindows windows;
        int scan;
        int holdingIndex = -1;
        int holdingSource;

        public Judge(IReadOnlyList<TimelineNote> notes, JudgeWindows windows)
        {
            this.notes = notes ?? throw new ArgumentNullException(nameof(notes));
            this.windows = windows;
            states = new State[notes.Count];
        }

        public bool IsHolding => holdingIndex >= 0;

        /// <summary>노트 판정이 모두 끝났는지(홀드는 뗌까지). 노트 목록은 <see cref="TimelineNote.Id"/> 순이어야 한다.</summary>
        public bool IsFinished(TimelineNote note) => states[note.Id] == State.Done;

        /// <summary>이 홀드 노트의 누름 판정이 끝나 뗌을 기다리는 중인지.</summary>
        public bool IsHoldingNote(TimelineNote note) => holdingIndex == note.Id;

        public void Press(double time, int source)
        {
            if (holdingIndex >= 0)
            {
                Whiffed?.Invoke(time);
                return;
            }

            int hit = -1;
            for (int i = scan; i < notes.Count; i++)
            {
                var candidate = notes[i];
                if (candidate.Time - windows.barely > time) break;
                if (states[i] != State.Pending) continue;
                if (Math.Abs(time - candidate.Time) <= windows.barely)
                {
                    hit = i;
                    break;
                }
            }

            if (hit < 0)
            {
                Whiffed?.Invoke(time);
                return;
            }

            var note = notes[hit];
            double delta = time - note.Time;
            var grade = Math.Abs(delta) <= windows.perfect ? JudgeGrade.Perfect : JudgeGrade.Barely;
            if (note.Type == NoteType.Hold)
            {
                states[hit] = State.Holding;
                holdingIndex = hit;
                holdingSource = source;
            }
            else
            {
                states[hit] = State.Done;
            }
            Judged?.Invoke(new NoteJudgement(note, NotePhase.Press, grade, delta));
            AdvanceScan();
        }

        public void Release(double time, int source)
        {
            if (holdingIndex < 0 || source != holdingSource) return;

            var note = notes[holdingIndex];
            double delta = time - note.EndTime;
            double abs = Math.Abs(delta);
            var grade = abs <= windows.releasePerfect ? JudgeGrade.Perfect
                : abs <= windows.releaseBarely ? JudgeGrade.Barely
                : JudgeGrade.Miss;

            states[holdingIndex] = State.Done;
            holdingIndex = -1;
            Judged?.Invoke(new NoteJudgement(note, NotePhase.Release, grade, delta));
            AdvanceScan();
        }

        /// <summary>현재 홀드를 강제로 뗀다(일시정지 등).</summary>
        public void ReleaseHolding(double time)
        {
            if (holdingIndex >= 0) Release(time, holdingSource);
        }

        /// <summary>매 프레임 호출해 시간이 지난 노트를 Miss 처리한다.</summary>
        public void Update(double time)
        {
            for (int i = scan; i < notes.Count; i++)
            {
                var note = notes[i];
                if (note.Time + windows.barely >= time) break;
                if (states[i] != State.Pending) continue;

                states[i] = State.Done;
                Judged?.Invoke(new NoteJudgement(note, NotePhase.Press, JudgeGrade.Miss, double.NaN));
                if (note.Type == NoteType.Hold)
                    Judged?.Invoke(new NoteJudgement(note, NotePhase.Release, JudgeGrade.Miss, double.NaN));
            }

            if (holdingIndex >= 0)
            {
                var note = notes[holdingIndex];
                if (time > note.EndTime + windows.releaseBarely)
                {
                    states[holdingIndex] = State.Done;
                    holdingIndex = -1;
                    Judged?.Invoke(new NoteJudgement(note, NotePhase.Release, JudgeGrade.Miss, double.NaN));
                }
            }

            AdvanceScan();
        }

        void AdvanceScan()
        {
            while (scan < notes.Count && states[scan] == State.Done) scan++;
        }
    }
}
