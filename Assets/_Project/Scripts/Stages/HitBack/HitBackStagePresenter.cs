using System;
using System.Collections.Generic;
using IWannabe.Otamaton;
using IWannabe.Rhythm;
using UnityEngine;

namespace IWannabe.Stages.HitBack
{
    /// <summary>
    /// 스테이지 1 "받아치기". 왼쪽 투수가 공을 던지면 오른쪽 타자가 박에 맞춰 받아친다.
    /// throw는 1박, lob은 2박 뒤에 도착하고, charge 공은 잡아서 버티다가 tick 다음 박에 놓아 날린다.
    /// bell이 울리면 같은 리듬을 4박 뒤에 따라 친다.
    /// 모든 움직임은 곡 시간/박으로 계산해 프레임이 흔들려도 박과 어긋나지 않는다.
    /// </summary>
    public sealed class HitBackStagePresenter : StagePresenter
    {
        enum BallState { Waiting, Flying, Shelved, Held, Launched, Dropped }

        sealed class Ball
        {
            public SpriteRenderer Sprite;
            /// <summary>공이 나타나 움직이기 시작하는 박(예고 큐의 박).</summary>
            public double LaunchBeat;
            public TimelineNote Note;
            public BallState State;
            /// <summary>따라 치기 공: 종이 울리면 선반에 줄섰다가 응답 박에 떨어진다.</summary>
            public bool Echo;
            public Vector3 Slot;
            public float Size;
            public float ArcHeight;
            public Vector3 StartPosition;
            public Vector3 Velocity;
            public double StateTime;
            public float Spin;
        }

        sealed class Flash
        {
            public SpriteRenderer Sprite;
            public double StartTime;
            public float Duration;
            public float FromScale;
            public float ToScale;
            public Color Color;
        }

        const float SwingSeconds = 0.16f;
        const float Gravity = -22f;
        const float FadeSeconds = 1.2f;

        [Header("Rig")]
        [SerializeField] Transform pitcher;
        [SerializeField] Transform batter;
        [Tooltip("타자 역할의 플레이어 캐릭터. 입력 중엔 입을 벌리고, miss가 나면 잠시 Hit 눈이 된다.")]
        [SerializeField] OtamatonView otamaton;
        [SerializeField] Transform paddlePivot;
        [SerializeField] Transform releasePoint;
        [SerializeField] Transform hitPoint;
        [SerializeField] SpriteRenderer ballTemplate;
        [SerializeField] SpriteRenderer flashTemplate;
        [Tooltip("홀드(charge) 동안 뗄 때까지 차오르는 링. 타격 지점에 둔다.")]
        [SerializeField] ProgressRing holdRing;
        [Tooltip("따라 치기 공이 응답 리듬 모양대로 줄서는 선반의 양 끝.")]
        [SerializeField] Transform echoShelfLeft;
        [SerializeField] Transform echoShelfRight;

        [Header("Look")]
        [SerializeField] Color throwColor = new Color(0.24f, 0.35f, 0.5f);
        [SerializeField] Color lobColor = new Color(0.93f, 0.42f, 0.3f);
        [SerializeField] Color chargeColor = new Color(0.61f, 0.36f, 0.9f);
        [SerializeField] Color bellColor = new Color(0.95f, 0.36f, 0.71f);
        [SerializeField] Color perfectFlashColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField] Color barelyFlashColor = new Color(0.6f, 0.6f, 0.6f);
        [SerializeField] float paddleRestAngle = -25f;
        [SerializeField] float paddleSwingAngle = 70f;
        [SerializeField] float paddleHoldAngle = 35f;

        [Header("Feedback Sounds")]
        [SerializeField] AudioClip hitSound;
        [SerializeField] AudioClip bigHitSound;
        [SerializeField] AudioClip barelySound;
        [SerializeField] AudioClip missSound;
        [SerializeField] AudioClip whiffSound;

        readonly List<Ball> balls = new List<Ball>();
        readonly Dictionary<int, Ball> ballByNote = new Dictionary<int, Ball>();
        readonly Stack<SpriteRenderer> ballPool = new Stack<SpriteRenderer>();
        readonly List<Flash> flashes = new List<Flash>();
        readonly Stack<SpriteRenderer> flashPool = new Stack<SpriteRenderer>();

        Vector3 pitcherScale;
        Vector3 batterScale;
        double swingStart = double.NegativeInfinity;
        double pitcherKick = double.NegativeInfinity;
        bool holding;
        HoldRingDriver ringDriver;

        Vector3 BellPoint => pitcher.position + Vector3.up * 1.5f;

        public override IEnumerable<AudioClip> AudioClips => new[] { hitSound, bigHitSound, barelySound, missSound, whiffSound };

        protected override void OnBind()
        {
            ballTemplate.gameObject.SetActive(false);
            flashTemplate.gameObject.SetActive(false);
            pitcherScale = pitcher.localScale;
            batterScale = batter.localScale;
            SetPaddle(paddleRestAngle);
            ringDriver = new HoldRingDriver(holdRing, chargeColor, barelyFlashColor, Color.gray);
        }

        /// <summary>앞 구간에서 남은 공·번쩍임·홀드 표시를 치우고 투수·타자를 쉬는 자세로 돌린다.</summary>
        protected override void OnSegmentEnter(TimelineSegment segment)
        {
            for (int i = balls.Count - 1; i >= 0; i--) Despawn(i);
            ballByNote.Clear();
            foreach (var flash in flashes)
            {
                flash.Sprite.gameObject.SetActive(false);
                flashPool.Push(flash.Sprite);
            }
            flashes.Clear();

            swingStart = double.NegativeInfinity;
            pitcherKick = double.NegativeInfinity;
            holding = false;
            pitcher.localScale = pitcherScale;
            batter.localScale = batterScale;
            SetPaddle(paddleRestAngle);
            ringDriver.Reset();
            otamaton.Rest();
        }

        /// <summary>종(bell)이 예고한 노트는 선반에 줄서는 공, 나머지는 예고 큐 모양대로 날아오는 공이다.</summary>
        protected override void OnNoteSpawn(TimelineNote note, TimelinePattern pattern)
        {
            var cue = note.Cue;
            if (cue.CueId == HitBackCues.Bell) SpawnEchoBall(pattern, cue, note);
            else SpawnFlyingBall(note, cue.Beat, cue.CueId);
        }

        /// <summary>
        /// 앞 미니게임에서 넘어온 노트를 투수가 던진 공으로 이어받는다. 예고가 시작된 박부터 날아온 만큼 진행된 자리에 바로 보이고,
        /// 노트 박에 타격 지점에 닿는다. 예고 간격이 2박 가까이면 높이 뜬 공(lob), 홀드는 잡고 버티는 공(charge)이다.
        /// </summary>
        protected override void OnCarryNote(CarriedNote carried)
        {
            var note = carried.Note;
            bool hold = note.Type == NoteType.Hold;
            string look = hold ? HitBackCues.Charge : carried.LeadBeats >= 1.5 ? HitBackCues.Lob : HitBackCues.Throw;
            var ball = SpawnFlyingBall(note, carried.LaunchBeat, look);
            if (!hold) return;

            ringDriver.Begin(note, carried.Holding);
            if (!carried.Holding) return;
            // 이미 누르고 있는 홀드는 공을 잡고 버티는 자세로 시작한다.
            double now = Context.Conductor.SongTime;
            holding = true;
            ball.State = BallState.Held;
            ball.StateTime = now;
            ball.Sprite.gameObject.SetActive(true);
            otamaton.Press(now);
        }

        /// <summary>
        /// <paramref name="launchBeat"/>에 투수 손을 떠나 노트 박에 타격 지점에 닿는 공. 모양은 <paramref name="look"/>(throw·lob·charge)를 따른다.
        /// </summary>
        Ball SpawnFlyingBall(TimelineNote note, double launchBeat, string look)
        {
            float flightBeats = (float)(note.Beat - launchBeat);
            var ball = new Ball
            {
                Sprite = RentBall(),
                LaunchBeat = launchBeat,
                Note = note,
                State = BallState.Waiting,
                Size = look == HitBackCues.Lob ? 0.75f : look == HitBackCues.Charge ? 0.95f : 0.5f,
                ArcHeight = 1.4f * flightBeats + (look == HitBackCues.Lob ? 1f : 0f),
            };
            ball.Sprite.color = ColorFor(look);
            ball.Sprite.transform.localScale = Vector3.one * ball.Size;
            ball.Sprite.transform.position = releasePoint.position;
            ball.Sprite.gameObject.SetActive(false);
            balls.Add(ball);
            ballByNote[note.Id] = ball;
            return ball;
        }

        /// <summary>종이 울릴 때 나타나 선반에 줄서는 공. 선반 위 위치가 곧 응답 리듬이다.</summary>
        void SpawnEchoBall(TimelinePattern pattern, TimelineCue cue, TimelineNote note)
        {
            var ball = new Ball
            {
                Sprite = RentBall(),
                LaunchBeat = cue.Beat,
                Note = note,
                State = BallState.Waiting,
                Echo = true,
                Slot = EchoShelf.Slot(echoShelfLeft.position, echoShelfRight.position, note.Beat, pattern.AnchorBeat, note.Beat - cue.Beat),
                Size = 0.45f,
            };
            ball.Sprite.color = bellColor;
            ball.Sprite.transform.localScale = Vector3.one * ball.Size;
            ball.Sprite.gameObject.SetActive(false);
            balls.Add(ball);
            ballByNote[note.Id] = ball;
        }

        public override void OnCue(TimelineCue cue)
        {
            pitcherKick = cue.Time;
            if (cue.CueId == HitBackCues.Bell)
                SpawnFlash(BellPoint, bellColor, 0.6f, 2.2f, 0.35f, cue.Time);
            else if (cue.CueId == HitBackCues.Tick)
                SpawnFlash(hitPoint.position, chargeColor, 0.4f, 1.4f, 0.25f, cue.Time);
            else if (cue.CueId == HitBackCues.Charge && cue.TargetNoteId >= 0)
                ringDriver.Begin(Context.Timeline.Notes[cue.TargetNoteId]);
        }

        public override void OnInputPressed(double songTime)
        {
            swingStart = songTime;
            otamaton.Press(songTime);
        }

        public override void OnInputReleased(double songTime)
        {
            otamaton.Release();
            if (holding) swingStart = songTime;
        }

        public override void OnJudged(NoteJudgement judgement)
        {
            ballByNote.TryGetValue(judgement.Note.Id, out var ball);
            double now = Context.Conductor.SongTime;
            bool perfect = judgement.Grade == JudgeGrade.Perfect;
            ringDriver.OnJudged(judgement, now);
            otamaton.Judged(judgement.Grade == JudgeGrade.Miss, now);

            if (judgement.Phase == NotePhase.Press)
            {
                if (judgement.Grade == JudgeGrade.Miss)
                {
                    if (ball != null) Drop(ball, now);
                    Context.Sfx.PlayNow(missSound, 0.5f);
                    return;
                }

                if (judgement.Note.Type == NoteType.Hold)
                {
                    holding = true;
                    if (ball != null)
                    {
                        ball.State = BallState.Held;
                        ball.StateTime = now;
                        ball.Sprite.gameObject.SetActive(true);
                    }
                    Context.Sfx.PlayNow(perfect ? hitSound : barelySound, 0.6f);
                    return;
                }

                if (ball != null) Launch(ball, now, perfect, false);
                else SpawnFlash(batter.position + Vector3.up * 1.5f, bellColor, 0.6f, 2.2f, 0.3f, now);
                Context.Sfx.PlayNow(perfect ? hitSound : barelySound);
                SpawnFlash(hitPoint.position, perfect ? perfectFlashColor : barelyFlashColor, 0.5f, 1.8f, 0.2f, now);
                return;
            }

            holding = false;
            if (judgement.Grade == JudgeGrade.Miss)
            {
                if (ball != null && ball.State != BallState.Dropped) Drop(ball, now);
                if (judgement.HasInput) Context.Sfx.PlayNow(missSound, 0.5f);
                return;
            }
            if (ball != null) Launch(ball, now, perfect, true);
            Context.Sfx.PlayNow(perfect ? bigHitSound : barelySound);
            SpawnFlash(hitPoint.position, perfect ? perfectFlashColor : barelyFlashColor, 0.8f, 2.6f, 0.3f, now);
        }

        public override void OnWhiff(double songTime) => Context.Sfx.PlayNow(whiffSound, 0.5f);

        public override void OnPaused(bool paused)
        {
            if (paused) otamaton.Rest();
        }

        public override void OnStageFinished(ScoreTracker score)
        {
            holding = false;
            otamaton.Rest();
        }

        public override void Tick(double songTime, double songBeat)
        {
            float bounce = songBeat >= 0 ? Mathf.Exp(-6f * (float)(songBeat - Math.Floor(songBeat))) : 0f;
            pitcher.localScale = Squash(pitcherScale, 0.06f * bounce + 0.14f * Pulse(songTime - pitcherKick, 0.2));
            batter.localScale = Squash(batterScale, 0.06f * bounce);
            otamaton.Tick(songTime);
            UpdatePaddle(songTime);
            UpdateBalls(songTime, songBeat);
            UpdateFlashes(songTime);
            ringDriver.Tick(songTime, songBeat);
        }

        void UpdatePaddle(double songTime)
        {
            float angle = holding ? paddleHoldAngle : paddleRestAngle;
            float swing = Pulse(songTime - swingStart, SwingSeconds);
            SetPaddle(Mathf.Lerp(angle, paddleSwingAngle, swing));
        }

        void SetPaddle(float angle) => paddlePivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        void UpdateBalls(double songTime, double songBeat)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var ball = balls[i];
                var t = ball.Sprite.transform;
                switch (ball.State)
                {
                    case BallState.Waiting:
                        if (songBeat < ball.LaunchBeat) break;
                        ball.Sprite.gameObject.SetActive(true);
                        if (ball.Echo)
                        {
                            ball.State = BallState.Shelved;
                            goto case BallState.Shelved;
                        }
                        ball.State = BallState.Flying;
                        goto case BallState.Flying;

                    case BallState.Shelved:
                        t.position = EchoShelf.Position(BellPoint, ball.Slot, hitPoint.position, songBeat, ball.LaunchBeat, ball.Note.Beat);
                        if (songBeat - ball.Note.Beat > 2.0) Despawn(i);
                        break;

                    case BallState.Flying:
                    {
                        double span = Math.Max(1e-3, ball.Note.Beat - ball.LaunchBeat);
                        float u = (float)((songBeat - ball.LaunchBeat) / span);
                        t.position = Arc(releasePoint.position, hitPoint.position, ball.ArcHeight, u);
                        t.rotation = Quaternion.Euler(0f, 0f, -360f * u);
                        if (u > 3f) Despawn(i);
                        break;
                    }

                    case BallState.Held:
                    {
                        float held = (float)(songTime - ball.StateTime);
                        t.position = hitPoint.position + new Vector3(0.2f, 0.06f * Mathf.Sin(held * 40f), 0f);
                        t.localScale = Vector3.one * ball.Size * (1f + 0.2f * Mathf.Clamp01(held));
                        break;
                    }

                    default:
                    {
                        float dt = (float)(songTime - ball.StateTime);
                        t.position = ball.StartPosition + ball.Velocity * dt + Vector3.up * (0.5f * Gravity * dt * dt);
                        t.rotation = Quaternion.Euler(0f, 0f, ball.Spin * dt);
                        var c = ball.Sprite.color;
                        c.a = Mathf.Clamp01(1f - dt / FadeSeconds);
                        ball.Sprite.color = c;
                        if (dt >= FadeSeconds) Despawn(i);
                        break;
                    }
                }
            }
        }

        void Launch(Ball ball, double now, bool perfect, bool big)
        {
            ball.Sprite.gameObject.SetActive(true);
            ball.State = BallState.Launched;
            ball.StateTime = now;
            ball.StartPosition = ball.Sprite.transform.position;
            ball.Velocity = perfect ? new Vector3(-13f, 9f, 0f) * (big ? 1.3f : 1f) : new Vector3(-5f, 6f, 0f);
            ball.Spin = perfect ? 720f : 240f;
        }

        void Drop(Ball ball, double now)
        {
            ball.Sprite.gameObject.SetActive(true);
            ball.State = BallState.Dropped;
            ball.StateTime = now;
            ball.StartPosition = ball.Sprite.transform.position;
            ball.Velocity = new Vector3(2f, 3f, 0f);
            ball.Spin = -200f;
            ball.Sprite.color = Color.Lerp(ball.Sprite.color, Color.gray, 0.6f);
        }

        void Despawn(int index)
        {
            var ball = balls[index];
            balls.RemoveAt(index);
            ballByNote.Remove(ball.Note.Id);
            ball.Sprite.gameObject.SetActive(false);
            ballPool.Push(ball.Sprite);
        }

        void SpawnFlash(Vector3 position, Color color, float fromScale, float toScale, float duration, double startTime)
        {
            var sprite = flashPool.Count > 0 ? flashPool.Pop() : Instantiate(flashTemplate, transform);
            sprite.transform.position = position;
            sprite.gameObject.SetActive(true);
            flashes.Add(new Flash { Sprite = sprite, StartTime = startTime, Duration = duration, FromScale = fromScale, ToScale = toScale, Color = color });
        }

        void UpdateFlashes(double songTime)
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var flash = flashes[i];
                float t = (float)((songTime - flash.StartTime) / flash.Duration);
                if (t >= 1f || t < -1f)
                {
                    flash.Sprite.gameObject.SetActive(false);
                    flashPool.Push(flash.Sprite);
                    flashes.RemoveAt(i);
                    continue;
                }
                t = Mathf.Clamp01(t);
                flash.Sprite.transform.localScale = Vector3.one * Mathf.Lerp(flash.FromScale, flash.ToScale, t);
                var c = flash.Color;
                c.a = 1f - t;
                flash.Sprite.color = c;
            }
        }

        SpriteRenderer RentBall()
        {
            var sprite = ballPool.Count > 0 ? ballPool.Pop() : Instantiate(ballTemplate, transform);
            sprite.transform.rotation = Quaternion.identity;
            return sprite;
        }

        Color ColorFor(string cueId)
        {
            if (cueId == HitBackCues.Lob) return lobColor;
            if (cueId == HitBackCues.Charge) return chargeColor;
            return throwColor;
        }

        static Vector3 Arc(Vector3 from, Vector3 to, float height, float u)
        {
            var p = Vector3.LerpUnclamped(from, to, u);
            p.y += height * 4f * u * (1f - u);
            return p;
        }

        static float Pulse(double elapsed, double duration)
        {
            if (elapsed < 0 || elapsed >= duration) return 0f;
            return Mathf.Sin((float)(elapsed / duration) * Mathf.PI);
        }

        static Vector3 Squash(Vector3 baseScale, float amount)
        {
            return new Vector3(baseScale.x * (1f + amount * 0.5f), baseScale.y * (1f - amount), baseScale.z);
        }
    }
}
