using System;
using IWannabe.Rhythm;
using UnityEngine;

namespace IWannabe.Stages
{
    /// <summary>
    /// 홀드 노트 하나의 진행을 링으로 보여 준다. 홀드 큐가 울리면 빈 링이 나타나고,
    /// 누름이 판정되면 누름 박부터 뗌 박까지 차오르며, 다 차면 깜빡여 "지금 떼라"를 알린다.
    /// 뗌이 판정되면 결과 색으로 퍼지며 사라진다. 진행은 곡 박 기준이라 실제 뗌 타이밍과 정확히 일치한다.
    /// </summary>
    public sealed class HoldRingDriver
    {
        enum Phase { Hidden, Waiting, Holding, Ending }

        const float EndSeconds = 0.3f;

        readonly ProgressRing ring;
        readonly Color fillColor;
        readonly Color trackColor;
        readonly Color barelyColor;
        readonly Color missColor;

        Phase phase = Phase.Hidden;
        TimelineNote note;
        double endTime;
        Color endColor;

        public HoldRingDriver(ProgressRing ring, Color fillColor, Color barelyColor, Color missColor)
        {
            this.ring = ring;
            this.fillColor = fillColor;
            this.barelyColor = barelyColor;
            this.missColor = missColor;
            trackColor = WithAlpha(fillColor, 0.25f);
            ring.Hide();
        }

        /// <summary>홀드 큐가 울릴 때 호출한다.</summary>
        public void Begin(TimelineNote holdNote)
        {
            note = holdNote;
            phase = Phase.Waiting;
            ring.Show(trackColor, fillColor);
        }

        public void OnJudged(NoteJudgement judgement, double songTime)
        {
            if (note == null || judgement.Note.Id != note.Id) return;
            if (judgement.Phase == NotePhase.Press)
            {
                if (judgement.Grade == JudgeGrade.Miss) End(missColor, songTime);
                else phase = Phase.Holding;
                return;
            }
            End(judgement.Grade == JudgeGrade.Perfect ? fillColor
                : judgement.Grade == JudgeGrade.Barely ? barelyColor
                : missColor, songTime);
        }

        public void Tick(double songTime, double songBeat)
        {
            switch (phase)
            {
                case Phase.Waiting:
                    ring.SetProgress(0f);
                    break;

                case Phase.Holding:
                {
                    float progress = (float)((songBeat - note.Beat) / Math.Max(1e-3, note.EndBeat - note.Beat));
                    ring.SetProgress(progress);
                    bool full = progress >= 1f;
                    ring.SetScale(full ? 1.1f + 0.08f * Mathf.Sin((float)(songTime * 40.0)) : 1f);
                    ring.SetColors(trackColor, full ? Color.white : fillColor);
                    break;
                }

                case Phase.Ending:
                {
                    float t = (float)((songTime - endTime) / EndSeconds);
                    if (t >= 1f || t < -1f)
                    {
                        ring.Hide();
                        phase = Phase.Hidden;
                        note = null;
                        break;
                    }
                    t = Mathf.Clamp01(t);
                    ring.SetProgress(1f);
                    ring.SetScale(1f + 0.6f * t);
                    ring.SetColors(WithAlpha(trackColor, trackColor.a * (1f - t)), WithAlpha(endColor, 1f - t));
                    break;
                }
            }
        }

        void End(Color color, double songTime)
        {
            phase = Phase.Ending;
            endTime = songTime;
            endColor = color;
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
