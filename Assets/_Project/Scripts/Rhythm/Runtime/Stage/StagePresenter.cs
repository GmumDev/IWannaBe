using System;
using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>스테이지 연출이 코어에서 받는 공용 정보.</summary>
    public sealed class StageContext
    {
        public StageDefinition Definition { get; }
        /// <summary>이 연출이 맡은 미니게임.</summary>
        public MinigameDefinition Minigame { get; }
        public ChartTimeline Timeline { get; }
        public TempoMap TempoMap { get; }
        public Conductor Conductor { get; }
        public SfxPlayer Sfx { get; }

        public StageContext(StageDefinition definition, MinigameDefinition minigame, ChartTimeline timeline, TempoMap tempoMap,
            Conductor conductor, SfxPlayer sfx)
        {
            Definition = definition;
            Minigame = minigame;
            Timeline = timeline;
            TempoMap = tempoMap;
            Conductor = conductor;
            Sfx = sfx;
        }
    }

    /// <summary>
    /// 구간이 바뀔 때 앞 구간 미니게임에서 넘어온, 입력이 아직 끝나지 않은 노트.
    /// 새 연출은 이 노트를 자기 모습으로, 예고부터 입력까지 남은 만큼 진행된 상태로 보여 줘야 한다.
    /// </summary>
    public readonly struct CarriedNote
    {
        public readonly TimelineNote Note;
        /// <summary>이 노트를 예고한 앞 미니게임의 큐. 예고 큐가 없는 노트면 null.</summary>
        public readonly TimelineCue Cue;
        /// <summary>홀드를 이미 누르고 있다(누름 판정이 끝나고 뗌만 남았다).</summary>
        public readonly bool Holding;

        public CarriedNote(TimelineNote note, TimelineCue cue, bool holding)
        {
            Note = note;
            Cue = cue;
            Holding = holding;
        }

        /// <summary>예고가 시작된 박. 예고 큐가 없으면 입력 1박 전으로 본다.</summary>
        public double LaunchBeat => Cue != null ? Cue.Beat : Note.Beat - 1;
        /// <summary>예고부터 입력까지의 박 수. 새 연출이 어떤 모습(1박 물건, 2박 물건 등)으로 보여 줄지 고를 때 쓴다.</summary>
        public double LeadBeats => Note.Beat - LaunchBeat;
    }

    /// <summary>
    /// 미니게임 연출의 기반 클래스. 판정·타이밍은 코어가 맡고, 연출은 이벤트에 반응만 한다.
    /// 애니메이션은 프레임 시간이 아니라 곡 시간/박(Tick 인자)으로 계산해야 싱크가 어긋나지 않는다.
    /// <para>
    /// 연출은 곡 중간에 들어오고 나갈 수 있어야 한다(리믹스·연습). 코어는 구간 시작 박에 바로 <see cref="ExitSegment"/>,
    /// <see cref="EnterSegment"/> 순서로 부르고, 앞 구간에서 입력이 남은 노트를 새 연출에 넘긴다(<see cref="OnCarryNote"/>).
    /// 들어온 뒤로는 그 구간의 패턴·큐와, 이어받은 노트를 포함한 모든 판정·입력이 이 연출로 온다.
    /// 규칙 전체는 Docs/Design/05_MinigameRules.md에 있다.
    /// </para>
    /// </summary>
    public abstract class StagePresenter : MonoBehaviour
    {
        protected StageContext Context { get; private set; }
        /// <summary>지금 맡은 구간. 처음 들어오기 전에는 null.</summary>
        protected TimelineSegment Segment { get; private set; }

        /// <summary>시작 전에 미리 로드해 둘 연출용 오디오(타격음 등).</summary>
        public virtual IEnumerable<AudioClip> AudioClips => Array.Empty<AudioClip>();

        public void Bind(StageContext context)
        {
            Context = context;
            OnBind();
        }

        protected virtual void OnBind() { }

        /// <summary>
        /// 구간에 들어온다. 화면에 나타나 처음 모습으로 돌아간 뒤, 앞 구간에서 넘어온 노트를 이어받는다.
        /// 이 구간의 첫 패턴 준비보다 먼저 불린다.
        /// </summary>
        public void EnterSegment(TimelineSegment segment, IReadOnlyList<CarriedNote> carried)
        {
            Segment = segment;
            gameObject.SetActive(true);
            OnSegmentEnter(segment);
            foreach (var note in carried) OnCarryNote(note);
        }

        /// <summary>구간을 떠난다. 화면에서 사라지고, 다시 들어올 때까지 Tick·박·입력을 받지 않는다.</summary>
        public void ExitSegment()
        {
            OnSegmentExit();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 구간에 들어올 때 상태를 초기화한다. 앞 구간(같은 미니게임의 앞 구간 포함)에서 남은 오브젝트·효과·홀드 표시를
        /// 모두 치우고, 캐릭터 자세와 시간 기록을 처음 값으로 돌린다. 곡 처음 시작할 때도 불린다.
        /// </summary>
        protected abstract void OnSegmentEnter(TimelineSegment segment);

        /// <summary>
        /// 앞 구간 미니게임에서 넘어온 노트를 이 미니게임의 모습으로 이어받는다. 예고부터 입력까지 남은 만큼 진행된 상태로
        /// 보여 줘서, 플레이어가 화면이 바뀐 순간에도 언제 입력할지 알 수 있어야 한다(<see cref="CarriedNote.LeadBeats"/>로
        /// 1박·2박 물건 등을 고르고, 홀드는 <see cref="CarriedNote.Holding"/>이면 이미 누른 모습으로).
        /// 이후 이 노트의 판정은 이 연출로 온다.
        /// </summary>
        protected abstract void OnCarryNote(CarriedNote carried);

        /// <summary>구간을 떠나기 직전. 남은 오브젝트는 다음에 들어올 때 치우면 되므로 보통 비워 둔다.</summary>
        protected virtual void OnSegmentExit() { }

        /// <summary>패턴의 첫 큐보다 조금 먼저 호출된다. 오브젝트를 미리 준비할 때 쓴다.</summary>
        public virtual void OnPatternSpawn(TimelinePattern pattern) { }

        /// <summary>큐 시각에 호출된다. 효과음은 코어가 이미 예약해 두었다. 소리가 없어도 알아볼 시각 신호를 여기서(또는 그 전에) 낸다.</summary>
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
