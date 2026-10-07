using System;
using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>스테이지 연출이 코어에서 받는 공용 정보.</summary>
    public sealed class StageContext
    {
        public StageDefinition Definition { get; }
        public ChartTimeline Timeline { get; }
        public TempoMap TempoMap { get; }
        public Conductor Conductor { get; }
        public SfxPlayer Sfx { get; }

        public StageContext(StageDefinition definition, ChartTimeline timeline, TempoMap tempoMap, Conductor conductor, SfxPlayer sfx)
        {
            Definition = definition;
            Timeline = timeline;
            TempoMap = tempoMap;
            Conductor = conductor;
            Sfx = sfx;
        }
    }

    /// <summary>
    /// 스테이지별 연출의 기반 클래스. 판정·타이밍은 코어가 맡고, 연출은 이벤트에 반응만 한다.
    /// 애니메이션은 프레임 시간이 아니라 곡 시간/박(Tick 인자)으로 계산해야 싱크가 어긋나지 않는다.
    /// </summary>
    public abstract class StagePresenter : MonoBehaviour
    {
        protected StageContext Context { get; private set; }

        /// <summary>시작 전에 미리 로드해 둘 연출용 오디오(타격음 등).</summary>
        public virtual IEnumerable<AudioClip> AudioClips => Array.Empty<AudioClip>();

        public void Bind(StageContext context)
        {
            Context = context;
            OnBind();
        }

        protected virtual void OnBind() { }

        /// <summary>패턴의 첫 큐보다 조금 먼저 호출된다. 오브젝트를 미리 준비할 때 쓴다.</summary>
        public virtual void OnPatternSpawn(TimelinePattern pattern) { }

        /// <summary>큐 시각에 호출된다. 효과음은 코어가 이미 예약해 두었다.</summary>
        public virtual void OnCue(TimelineCue cue) { }

        public virtual void OnInputPressed(double songTime) { }
        public virtual void OnInputReleased(double songTime) { }
        public virtual void OnJudged(NoteJudgement judgement) { }
        public virtual void OnWhiff(double songTime) { }
        public virtual void OnBeat(int beat) { }
        public virtual void Tick(double songTime, double songBeat) { }
        public virtual void OnPaused(bool paused) { }
        public virtual void OnStageFinished(ScoreTracker score) { }
    }
}
