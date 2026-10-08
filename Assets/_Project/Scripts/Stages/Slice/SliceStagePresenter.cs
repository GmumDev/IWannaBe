using System;
using System.Collections.Generic;
using IWannabe.Otamaton;
using IWannabe.Rhythm;
using UnityEngine;

namespace IWannabe.Stages.Slice
{
    /// <summary>
    /// 스테이지 2 "베기". 왼쪽 스승이 던진 물건을 오른쪽 검객이 박에 맞춰 벤다.
    /// toss(과일)는 1박, high(대나무)는 2박 뒤에 도착한다. draw는 1박 뒤에 통나무가 링 안으로 들어와 멈추고,
    /// 누르고 있는 동안 검객이 마구 베다가 tick 다음 박에 떼면 통나무가 조각조각 흩어진다.
    /// 징(gong)이 울리면 같은 리듬을 4박 뒤에 따라 벤다.
    /// </summary>
    public sealed class SliceStagePresenter : StagePresenter
    {
        enum ItemState { Waiting, Flying, Shelved, Held, Cut, Dropped }

        sealed class Item
        {
            public SpriteRenderer Sprite;
            /// <summary>물건이 나타나 움직이기 시작하는 박(예고 큐의 박).</summary>
            public double LaunchBeat;
            public TimelineNote Note;
            public double ArrivalBeat;
            public ItemState State;
            /// <summary>따라 베기 등롱: 징이 울리면 선반에 줄섰다가 응답 박에 떨어진다.</summary>
            public bool Echo;
            public Vector3 Slot;
            public Vector3 Scale;
            public float ArcHeight;
            public float SpinPerBeat;
            public Vector3 StartPosition;
            /// <summary>떨어지기 시작할 때의 각도. 떨어지는 동안 곡 시간에 따라 돈다.</summary>
            public float StartAngle;
            public Vector3 Velocity;
            public double StateTime;
        }

        sealed class Fx
        {
            public SpriteRenderer Sprite;
            public double StartTime;
            public float Duration;
            public Vector3 FromScale;
            public Vector3 ToScale;
            public Vector3 Velocity;
            public float Spin;
            public Color Color;
            public bool Falls;
        }

        const float SwingSeconds = 0.14f;
        const float Gravity = -24f;

        [Header("Rig")]
        [SerializeField] Transform master;
        [SerializeField] Transform swordsman;
        [Tooltip("검객 역할의 플레이어 캐릭터. 입력 중엔 입을 벌리고, miss가 나면 잠시 Hit 눈이 된다.")]
        [SerializeField] OtamatonView otamaton;
        [SerializeField] Transform swordPivot;
        [SerializeField] Transform releasePoint;
        [SerializeField] Transform strikePoint;
        [SerializeField] Transform gong;
        [SerializeField] SpriteRenderer itemTemplate;
        [SerializeField] SpriteRenderer effectTemplate;
        [Tooltip("draw 홀드 동안 뗄 때까지 차오르는 링. 통나무가 이 안에 멈춘다. 베는 지점에 둔다.")]
        [SerializeField] ProgressRing holdRing;
        [Tooltip("따라 베기 등롱이 응답 리듬 모양대로 줄서는 선반의 양 끝.")]
        [SerializeField] Transform echoShelfLeft;
        [SerializeField] Transform echoShelfRight;

        [Header("Sprites")]
        [SerializeField] Sprite roundSprite;
        [SerializeField] Sprite stickSprite;
        [SerializeField] Sprite ringSprite;

        [Header("Look")]
        [SerializeField] Color fruitColor = new Color(0.96f, 0.64f, 0.38f);
        [SerializeField] Color bambooColor = new Color(0.56f, 0.75f, 0.43f);
        [SerializeField] Color logColor = new Color(0.63f, 0.47f, 0.33f);
        [SerializeField] Color streakColor = Color.white;
        [SerializeField] Color barelyColor = new Color(0.6f, 0.6f, 0.65f);
        [SerializeField] Color gongColor = new Color(0.91f, 0.77f, 0.42f);
        [SerializeField] Color lanternColor = new Color(1f, 0.72f, 0.01f);
        [SerializeField] float swordRestAngle = -35f;
        [SerializeField] float swordSlashAngle = 110f;

        [Header("Hold Flurry")]
        [Tooltip("홀드 중 칼자국이 새로 생기는 간격(초).")]
        [SerializeField, Min(0.03f)] float flurryInterval = 0.08f;
        [Tooltip("홀드 중 칼을 휘두르는 빠르기(초당 왕복).")]
        [SerializeField, Min(1f)] float flurrySwingsPerSecond = 7f;
        [Tooltip("정확히 뗐을 때 통나무가 흩어지는 조각 수. 아슬아슬이면 절반.")]
        [SerializeField, Range(3, 16)] int shatterPieces = 9;

        [Header("Feedback Sounds")]
        [SerializeField] AudioClip slashSound;
        [SerializeField] AudioClip bigSlashSound;
        [SerializeField] AudioClip barelySound;
        [SerializeField] AudioClip missSound;
        [SerializeField] AudioClip whiffSound;

        readonly List<Item> items = new List<Item>();
        readonly Dictionary<int, Item> itemByNote = new Dictionary<int, Item>();
        readonly Stack<SpriteRenderer> itemPool = new Stack<SpriteRenderer>();
        readonly List<Fx> effects = new List<Fx>();
        readonly Stack<SpriteRenderer> effectPool = new Stack<SpriteRenderer>();

        Vector3 masterScale;
        Vector3 swordsmanScale;
        Vector3 gongScale;
        double swingStart = double.NegativeInfinity;
        double masterToss = double.NegativeInfinity;
        double gongHit = double.NegativeInfinity;
        bool chopping;
        double nextFlurryTime;
        bool flurrySoundToggle;
        HoldRingDriver ringDriver;

        public override IEnumerable<AudioClip> AudioClips => new[] { slashSound, bigSlashSound, barelySound, missSound, whiffSound };

        protected override void OnBind()
        {
            itemTemplate.gameObject.SetActive(false);
            effectTemplate.gameObject.SetActive(false);
            masterScale = master.localScale;
            swordsmanScale = swordsman.localScale;
            gongScale = gong.localScale;
            SetSword(swordRestAngle);
            ringDriver = new HoldRingDriver(holdRing, gongColor, barelyColor, Color.gray);
        }

        /// <summary>앞 구간에서 남은 물건·조각·칼자국·홀드 표시를 치우고 스승·검객·징을 쉬는 자세로 돌린다.</summary>
        protected override void OnSegmentEnter(TimelineSegment segment)
        {
            for (int i = items.Count - 1; i >= 0; i--) Despawn(i);
            itemByNote.Clear();
            foreach (var fx in effects)
            {
                fx.Sprite.gameObject.SetActive(false);
                effectPool.Push(fx.Sprite);
            }
            effects.Clear();

            swingStart = double.NegativeInfinity;
            masterToss = double.NegativeInfinity;
            gongHit = double.NegativeInfinity;
            chopping = false;
            master.localScale = masterScale;
            swordsman.localScale = swordsmanScale;
            gong.localScale = gongScale;
            SetSword(swordRestAngle);
            ringDriver.Reset();
            otamaton.Rest();
        }

        /// <summary>징(gong)이 예고한 노트는 선반에 줄서는 등롱, 나머지는 예고 큐 모양대로 날아오는 물건이다.</summary>
        protected override void OnNoteSpawn(TimelineNote note, TimelinePattern pattern)
        {
            var cue = note.Cue;
            if (cue.CueId == SliceCues.Gong) SpawnEchoLantern(pattern, cue, note);
            else SpawnThrownItem(note, cue.Beat, cue.CueId);
        }

        /// <summary>
        /// 앞 미니게임에서 넘어온 노트를 스승이 던진 물건으로 이어받는다. 예고가 시작된 박부터 날아온 만큼 진행된 자리에 바로 보이고,
        /// 노트 박에 베는 지점에 닿는다. 예고 간격이 2박 가까이면 대나무(high), 홀드는 통나무(draw)다.
        /// </summary>
        protected override void OnCarryNote(CarriedNote carried)
        {
            var note = carried.Note;
            bool hold = note.Type == NoteType.Hold;
            string look = hold ? SliceCues.Draw : carried.LeadBeats >= 1.5 ? SliceCues.High : SliceCues.Toss;
            var item = SpawnThrownItem(note, carried.LaunchBeat, look);
            if (!hold) return;

            ringDriver.Begin(note, carried.Holding);
            if (!carried.Holding) return;
            // 이미 누르고 있는 홀드는 통나무를 링 안에 붙잡고 난도질하는 중으로 시작한다.
            double now = Context.Conductor.SongTime;
            chopping = true;
            nextFlurryTime = now;
            item.State = ItemState.Held;
            item.StateTime = now;
            item.Sprite.gameObject.SetActive(true);
            otamaton.Press(now);
        }

        /// <summary>
        /// <paramref name="launchBeat"/>에 스승 손을 떠나 노트 박에 베는 지점(링 중심)에 닿는 물건. 모양은 <paramref name="look"/>(toss·high·draw)를 따른다.
        /// 통나무(draw)는 그 자리에 멈춰 난도질당한다.
        /// </summary>
        Item SpawnThrownItem(TimelineNote note, double launchBeat, string look)
        {
            bool draw = look == SliceCues.Draw;
            bool high = look == SliceCues.High;
            float flight = (float)(note.Beat - launchBeat);

            var item = new Item
            {
                Sprite = RentItem(),
                LaunchBeat = launchBeat,
                Note = note,
                ArrivalBeat = note.Beat,
                State = ItemState.Waiting,
                Scale = draw ? new Vector3(1.1f, 0.45f, 1f) : high ? new Vector3(0.32f, 1.3f, 1f) : new Vector3(0.6f, 0.6f, 1f),
                ArcHeight = draw ? 1.6f * flight : high ? 1.4f * flight + 1.5f : 1.5f * flight,
                SpinPerBeat = draw ? 90f : high ? -260f : -180f,
            };
            item.Sprite.sprite = draw || high ? stickSprite : roundSprite;
            item.Sprite.color = draw ? logColor : high ? bambooColor : fruitColor;
            item.Sprite.transform.localScale = item.Scale;
            item.Sprite.transform.position = releasePoint.position;
            item.Sprite.gameObject.SetActive(false);
            items.Add(item);
            itemByNote[note.Id] = item;
            return item;
        }

        /// <summary>징이 울릴 때 나타나 선반에 줄서는 등롱. 선반 위 위치가 곧 응답 리듬이다.</summary>
        void SpawnEchoLantern(TimelinePattern pattern, TimelineCue cue, TimelineNote note)
        {
            var item = new Item
            {
                Sprite = RentItem(),
                LaunchBeat = cue.Beat,
                Note = note,
                ArrivalBeat = note.Beat,
                State = ItemState.Waiting,
                Echo = true,
                Slot = EchoShelf.Slot(echoShelfLeft.position, echoShelfRight.position, note.Beat, pattern.AnchorBeat, note.Beat - cue.Beat),
                Scale = new Vector3(0.5f, 0.65f, 1f),
            };
            item.Sprite.sprite = stickSprite;
            item.Sprite.color = lanternColor;
            item.Sprite.transform.localScale = item.Scale;
            item.Sprite.gameObject.SetActive(false);
            items.Add(item);
            itemByNote[note.Id] = item;
        }

        public override void OnCue(TimelineCue cue)
        {
            if (cue.CueId == SliceCues.Gong)
            {
                gongHit = cue.Time;
                SpawnRing(gong.position, gongColor, 0.6f, 2.4f, 0.4f, cue.Time);
            }
            else if (cue.CueId == SliceCues.Tick)
            {
                SpawnRing(swordPivot.position, streakColor, 0.3f, 1.2f, 0.2f, cue.Time);
            }
            else
            {
                masterToss = cue.Time;
                if (cue.CueId == SliceCues.Draw && cue.TargetNoteId >= 0)
                    ringDriver.Begin(Context.Timeline.Notes[cue.TargetNoteId]);
            }
        }

        public override void OnInputPressed(double songTime)
        {
            otamaton.Press(songTime);
            // 난도질 중에는 칼이 계속 움직이므로 누름마다 따로 휘두르지 않는다.
            if (!chopping) swingStart = songTime;
        }

        public override void OnInputReleased(double songTime)
        {
            otamaton.Release();
            if (chopping) swingStart = songTime; // 마무리 일격
        }

        public override void OnJudged(NoteJudgement judgement)
        {
            itemByNote.TryGetValue(judgement.Note.Id, out var item);
            double now = Context.Conductor.SongTime;
            bool perfect = judgement.Grade == JudgeGrade.Perfect;
            ringDriver.OnJudged(judgement, now);
            otamaton.Judged(judgement.Grade == JudgeGrade.Miss, now);

            if (judgement.Phase == NotePhase.Press)
            {
                if (judgement.Grade == JudgeGrade.Miss)
                {
                    if (item != null) Drop(item, now);
                    Context.Sfx.PlayNow(missSound, 0.5f);
                    return;
                }

                if (judgement.Note.Type == NoteType.Hold)
                {
                    // 통나무를 링 안에 붙잡고 뗄 때까지 난도질한다.
                    chopping = true;
                    nextFlurryTime = now;
                    if (item != null)
                    {
                        item.Sprite.gameObject.SetActive(true);
                        item.State = ItemState.Held;
                        item.StateTime = now;
                    }
                    Context.Sfx.PlayNow(perfect ? slashSound : barelySound, 0.6f);
                    return;
                }

                if (item != null) Cut(item, now, perfect, false);
                else SpawnStreak(strikePoint.position + Vector3.up * 0.4f, perfect, false, now);
                Context.Sfx.PlayNow(perfect ? slashSound : barelySound);
                return;
            }

            chopping = false;
            if (judgement.Grade == JudgeGrade.Miss)
            {
                if (item != null && item.State != ItemState.Dropped) Drop(item, now);
                if (judgement.HasInput) Context.Sfx.PlayNow(missSound, 0.5f);
                return;
            }
            if (item != null) Shatter(item, now, perfect);
            Context.Sfx.PlayNow(perfect ? bigSlashSound : barelySound);
        }

        public override void OnWhiff(double songTime) => Context.Sfx.PlayNow(whiffSound, 0.6f);

        public override void OnPaused(bool paused)
        {
            if (paused) otamaton.Rest();
        }

        public override void OnStageFinished(ScoreTracker score)
        {
            chopping = false;
            otamaton.Rest();
        }

        public override void Tick(double songTime, double songBeat)
        {
            float bounce = songBeat >= 0 ? Mathf.Exp(-6f * (float)(songBeat - Math.Floor(songBeat))) : 0f;
            master.localScale = Squash(masterScale, 0.05f * bounce + 0.12f * Pulse(songTime - masterToss, 0.2));
            float flurry = chopping ? 0.06f * Mathf.Abs(Mathf.Sin((float)(songTime * Math.PI * flurrySwingsPerSecond))) : 0f;
            swordsman.localScale = Squash(swordsmanScale, 0.05f * bounce + flurry);
            otamaton.Tick(songTime);
            gong.localScale = gongScale * (1f + 0.25f * Pulse(songTime - gongHit, 0.25));

            if (chopping)
            {
                // 칼이 쉬지 않고 왕복하며, 일정 간격으로 통나무 위에 무작위 칼자국을 남긴다.
                float sweep = 0.5f + 0.5f * Mathf.Sin((float)(songTime * Math.PI * 2.0 * flurrySwingsPerSecond));
                SetSword(Mathf.Lerp(swordRestAngle, swordSlashAngle, sweep));
                UpdateFlurry(songTime);
            }
            else
            {
                SetSword(Mathf.Lerp(swordRestAngle, swordSlashAngle, Pulse(songTime - swingStart, SwingSeconds)));
            }

            UpdateItems(songTime, songBeat);
            UpdateEffects(songTime);
            ringDriver.Tick(songTime, songBeat);
        }

        void UpdateFlurry(double songTime)
        {
            if (songTime - nextFlurryTime > 0.5) nextFlurryTime = songTime; // 일시정지 등으로 밀렸으면 몰아서 만들지 않는다
            while (songTime >= nextFlurryTime)
            {
                var offset = new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), UnityEngine.Random.Range(-0.25f, 0.25f), 0f);
                SpawnSlashMark(strikePoint.position + offset, UnityEngine.Random.Range(0f, 180f), UnityEngine.Random.Range(1.2f, 2.2f), nextFlurryTime);
                flurrySoundToggle = !flurrySoundToggle;
                if (flurrySoundToggle) Context.Sfx.PlayNow(whiffSound, 0.25f);
                nextFlurryTime += flurryInterval;
            }
        }

        void SetSword(float angle) => swordPivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        void UpdateItems(double songTime, double songBeat)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var t = item.Sprite.transform;
                switch (item.State)
                {
                    case ItemState.Waiting:
                        if (songBeat < item.LaunchBeat) break;
                        item.Sprite.gameObject.SetActive(true);
                        if (item.Echo)
                        {
                            item.State = ItemState.Shelved;
                            goto case ItemState.Shelved;
                        }
                        item.State = ItemState.Flying;
                        goto case ItemState.Flying;

                    case ItemState.Shelved:
                        t.position = EchoShelf.Position(gong.position, item.Slot, strikePoint.position, songBeat, item.LaunchBeat, item.Note.Beat);
                        t.rotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin((float)(songBeat * Math.PI)));
                        if (songBeat - item.Note.Beat > 2.0) Despawn(i);
                        break;

                    case ItemState.Flying:
                    {
                        double span = Math.Max(1e-3, item.ArrivalBeat - item.LaunchBeat);
                        float u = (float)((songBeat - item.LaunchBeat) / span);
                        var p = Vector3.LerpUnclamped(releasePoint.position, strikePoint.position, u);
                        p.y += item.ArcHeight * 4f * u * (1f - u);
                        t.position = p;
                        t.rotation = Quaternion.Euler(0f, 0f, item.SpinPerBeat * (float)(songBeat - item.LaunchBeat));
                        if (u > 3f) Despawn(i);
                        break;
                    }

                    case ItemState.Held:
                    {
                        // 링 안에 붙잡힌 채 칼을 맞아 떨리고, 링이 차는 만큼 조금씩 깎여 작아진다.
                        double span = Math.Max(1e-3, item.Note.EndBeat - item.Note.Beat);
                        float progress = Mathf.Clamp01((float)((songBeat - item.Note.Beat) / span));
                        float time = (float)songTime;
                        t.position = strikePoint.position + new Vector3(0.05f * Mathf.Sin(time * 53f), 0.05f * Mathf.Sin(time * 47f), 0f);
                        t.rotation = Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(time * 31f));
                        t.localScale = item.Scale * (1f - 0.25f * progress);
                        if (songBeat - item.Note.EndBeat > 2.0) Despawn(i);
                        break;
                    }

                    case ItemState.Dropped:
                    {
                        float dt = (float)(songTime - item.StateTime);
                        t.position = item.StartPosition + item.Velocity * dt + Vector3.up * (0.5f * Gravity * dt * dt);
                        t.rotation = Quaternion.Euler(0f, 0f, item.StartAngle - 300f * dt);
                        var c = item.Sprite.color;
                        c.a = Mathf.Clamp01(1f - dt / 1.2f);
                        item.Sprite.color = c;
                        if (dt >= 1.2f) Despawn(i);
                        break;
                    }

                    default:
                        Despawn(i);
                        break;
                }
            }
        }

        /// <summary>물건을 두 조각으로 나눠 날려 보내고 칼자국을 남긴다.</summary>
        void Cut(Item item, double now, bool perfect, bool big)
        {
            var t = item.Sprite.transform;
            var position = item.State == ItemState.Waiting ? strikePoint.position : t.position;
            var half = new Vector3(item.Scale.x * 0.5f, item.Scale.y, 1f);
            var right = t.right * (item.Scale.x * 0.25f);
            float spread = perfect ? 1f : 0.35f;
            var color = perfect ? item.Sprite.color : Color.Lerp(item.Sprite.color, barelyColor, 0.5f);

            SpawnPiece(item.Sprite.sprite, position - right, t.rotation, half, color, new Vector3(-4f, 7f, 0f) * spread, 420f * spread, now);
            SpawnPiece(item.Sprite.sprite, position + right, t.rotation, half, color, new Vector3(3f, 5f, 0f) * spread, -360f * spread, now);
            SpawnStreak(position, perfect, big, now);
            if (big) SpawnRing(position, streakColor, 0.8f, 3f, 0.3f, now);

            item.State = ItemState.Cut;
            item.Sprite.gameObject.SetActive(false);
        }

        /// <summary>난도질한 통나무를 여러 조각으로 흩뿌린다. 아슬아슬이면 조각이 적고 덜 퍼진다.</summary>
        void Shatter(Item item, double now, bool perfect)
        {
            var t = item.Sprite.transform;
            var center = t.position;
            var size = t.localScale;
            int count = perfect ? shatterPieces : Mathf.Max(3, shatterPieces / 2);
            float spread = perfect ? 1f : 0.6f;
            var color = perfect ? item.Sprite.color : Color.Lerp(item.Sprite.color, barelyColor, 0.5f);

            for (int k = 0; k < count; k++)
            {
                float along = k / (float)(count - 1) - 0.5f; // 통나무 길이 방향 -0.5~0.5
                var position = center + t.right * (along * size.x) + Vector3.up * UnityEngine.Random.Range(-0.1f, 0.1f);
                var scale = new Vector3(size.x / count * UnityEngine.Random.Range(1f, 1.6f), size.y * UnityEngine.Random.Range(0.5f, 1f), 1f);
                var velocity = new Vector3(along * 9f + UnityEngine.Random.Range(-1.5f, 1.5f), UnityEngine.Random.Range(3f, 8f), 0f) * spread;
                var rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
                SpawnPiece(item.Sprite.sprite, position, rotation, scale, color, velocity, UnityEngine.Random.Range(-720f, 720f) * spread, now);
            }
            SpawnStreak(center, perfect, true, now);
            SpawnRing(center, streakColor, 0.8f, 3f, 0.3f, now);

            item.State = ItemState.Cut;
            item.Sprite.gameObject.SetActive(false);
        }

        void Drop(Item item, double now)
        {
            item.Sprite.gameObject.SetActive(true);
            if (item.State == ItemState.Waiting) item.Sprite.transform.position = strikePoint.position;
            item.State = ItemState.Dropped;
            item.StateTime = now;
            item.StartPosition = item.Sprite.transform.position;
            item.StartAngle = item.Sprite.transform.eulerAngles.z;
            item.Velocity = new Vector3(2.5f, 2f, 0f);
            item.Sprite.color = Color.Lerp(item.Sprite.color, Color.gray, 0.5f);
        }

        void Despawn(int index)
        {
            var item = items[index];
            items.RemoveAt(index);
            itemByNote.Remove(item.Note.Id);
            item.Sprite.gameObject.SetActive(false);
            itemPool.Push(item.Sprite);
        }

        SpriteRenderer RentItem()
        {
            var sprite = itemPool.Count > 0 ? itemPool.Pop() : Instantiate(itemTemplate, transform);
            sprite.transform.rotation = Quaternion.identity;
            return sprite;
        }

        // ───────── 효과(조각·칼자국·고리) ─────────

        void SpawnPiece(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 scale, Color color, Vector3 velocity, float spin, double now)
        {
            var fx = RentEffect(sprite, position, rotation);
            effects.Add(new Fx
            {
                Sprite = fx, StartTime = now, Duration = 1.0f, FromScale = scale, ToScale = scale,
                Velocity = velocity, Spin = spin, Color = color, Falls = true,
            });
        }

        void SpawnStreak(Vector3 position, bool perfect, bool big, double now)
        {
            var fx = RentEffect(stickSprite, position, Quaternion.Euler(0f, 0f, big ? -20f : -32f));
            var scale = big ? new Vector3(6.5f, 0.16f, 1f) : new Vector3(4f, 0.09f, 1f);
            effects.Add(new Fx
            {
                Sprite = fx, StartTime = now, Duration = big ? 0.3f : 0.18f,
                FromScale = new Vector3(scale.x * 0.4f, scale.y, 1f), ToScale = scale,
                Color = perfect ? streakColor : barelyColor,
            });
        }

        /// <summary>홀드 중 난도질 칼자국. 짧게 그어졌다가 사라진다.</summary>
        void SpawnSlashMark(Vector3 position, float angle, float length, double startTime)
        {
            var fx = RentEffect(stickSprite, position, Quaternion.Euler(0f, 0f, angle));
            effects.Add(new Fx
            {
                Sprite = fx, StartTime = startTime, Duration = 0.12f,
                FromScale = new Vector3(length * 0.3f, 0.06f, 1f), ToScale = new Vector3(length, 0.06f, 1f),
                Color = streakColor,
            });
        }

        void SpawnRing(Vector3 position, Color color, float from, float to, float duration, double startTime)
        {
            var fx = RentEffect(ringSprite, position, Quaternion.identity);
            effects.Add(new Fx
            {
                Sprite = fx, StartTime = startTime, Duration = duration,
                FromScale = Vector3.one * from, ToScale = Vector3.one * to, Color = color,
            });
        }

        SpriteRenderer RentEffect(Sprite sprite, Vector3 position, Quaternion rotation)
        {
            var fx = effectPool.Count > 0 ? effectPool.Pop() : Instantiate(effectTemplate, transform);
            fx.sprite = sprite;
            fx.transform.SetPositionAndRotation(position, rotation);
            fx.gameObject.SetActive(true);
            return fx;
        }

        void UpdateEffects(double songTime)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var fx = effects[i];
                float elapsed = (float)(songTime - fx.StartTime);
                float t = elapsed / fx.Duration;
                if (t >= 1f || t < -1f)
                {
                    fx.Sprite.gameObject.SetActive(false);
                    effectPool.Push(fx.Sprite);
                    effects.RemoveAt(i);
                    continue;
                }
                t = Mathf.Clamp01(t);
                elapsed = Mathf.Max(0f, elapsed);
                var transformFx = fx.Sprite.transform;
                transformFx.localScale = Vector3.Lerp(fx.FromScale, fx.ToScale, t);
                if (fx.Falls)
                {
                    transformFx.position += (fx.Velocity + Vector3.up * (Gravity * elapsed)) * Time.deltaTime;
                    transformFx.Rotate(0f, 0f, fx.Spin * Time.deltaTime);
                }
                var c = fx.Color;
                c.a = 1f - t;
                fx.Sprite.color = c;
            }
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
