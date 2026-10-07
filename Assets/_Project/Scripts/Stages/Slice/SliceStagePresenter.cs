using System;
using System.Collections.Generic;
using IWannabe.Rhythm;
using UnityEngine;

namespace IWannabe.Stages.Slice
{
    /// <summary>
    /// 스테이지 2 "베기". 왼쪽 스승이 던진 물건을 오른쪽 검객이 박에 맞춰 벤다.
    /// toss(과일)는 1박, high(대나무)는 2박 뒤에 도착한다. draw는 1박 뒤에 칼자루를 잡고 버티다가
    /// tick 다음 박에 떼어 발도하며, 통나무는 바로 그 순간 도착한다. 징(gong)이 울리면 같은 리듬을 4박 뒤에 따라 벤다.
    /// </summary>
    public sealed class SliceStagePresenter : StagePresenter
    {
        enum ItemState { Waiting, Flying, Cut, Dropped }

        sealed class Item
        {
            public SpriteRenderer Sprite;
            public TimelineCue Cue;
            public TimelineNote Note;
            public double ArrivalBeat;
            public ItemState State;
            public Vector3 Scale;
            public float ArcHeight;
            public float SpinPerBeat;
            public Vector3 StartPosition;
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
        [SerializeField] Transform swordPivot;
        [SerializeField] Transform releasePoint;
        [SerializeField] Transform strikePoint;
        [SerializeField] Transform gong;
        [SerializeField] SpriteRenderer itemTemplate;
        [SerializeField] SpriteRenderer effectTemplate;

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
        [SerializeField] float swordRestAngle = -35f;
        [SerializeField] float swordSlashAngle = 110f;
        [SerializeField] float swordStanceAngle = -75f;

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
        bool inStance;

        public override IEnumerable<AudioClip> AudioClips => new[] { slashSound, bigSlashSound, barelySound, missSound, whiffSound };

        protected override void OnBind()
        {
            itemTemplate.gameObject.SetActive(false);
            effectTemplate.gameObject.SetActive(false);
            masterScale = master.localScale;
            swordsmanScale = swordsman.localScale;
            gongScale = gong.localScale;
            SetSword(swordRestAngle);
        }

        public override void OnPatternSpawn(TimelinePattern pattern)
        {
            foreach (var cue in pattern.Cues)
            {
                if (cue.TargetNoteId < 0 || cue.CueId == SliceCues.Gong) continue;

                var note = Context.Timeline.Notes[cue.TargetNoteId];
                bool draw = cue.CueId == SliceCues.Draw;
                bool high = cue.CueId == SliceCues.High;
                // 발도(draw)는 칼을 뽑는 순간, 즉 뗌 시점에 통나무가 도착한다.
                double arrival = draw ? note.EndBeat : note.Beat;
                float flight = (float)(arrival - cue.Beat);

                var item = new Item
                {
                    Sprite = RentItem(),
                    Cue = cue,
                    Note = note,
                    ArrivalBeat = arrival,
                    State = ItemState.Waiting,
                    Scale = draw ? new Vector3(1.3f, 0.5f, 1f) : high ? new Vector3(0.32f, 1.3f, 1f) : new Vector3(0.6f, 0.6f, 1f),
                    ArcHeight = draw ? 1.1f * flight : high ? 1.4f * flight + 1.5f : 1.5f * flight,
                    SpinPerBeat = draw ? 40f : high ? -260f : -180f,
                };
                item.Sprite.sprite = draw || high ? stickSprite : roundSprite;
                item.Sprite.color = draw ? logColor : high ? bambooColor : fruitColor;
                item.Sprite.transform.localScale = item.Scale;
                item.Sprite.transform.position = releasePoint.position;
                item.Sprite.gameObject.SetActive(false);
                items.Add(item);
                itemByNote[note.Id] = item;
            }
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
            }
        }

        public override void OnInputPressed(double songTime)
        {
            // 발도 자세 중에는 누름이 아니라 뗌이 칼을 휘두른다.
            if (!inStance) swingStart = songTime;
        }

        public override void OnInputReleased(double songTime)
        {
            if (inStance) swingStart = songTime;
        }

        public override void OnJudged(NoteJudgement judgement)
        {
            itemByNote.TryGetValue(judgement.Note.Id, out var item);
            double now = Context.Conductor.SongTime;
            bool perfect = judgement.Grade == JudgeGrade.Perfect;

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
                    inStance = true; // 통나무는 아직 날아오는 중: 뗌 판정 때 벤다
                    return;
                }

                if (item != null) Cut(item, now, perfect, false);
                else SpawnStreak(strikePoint.position + Vector3.up * 0.4f, perfect, false, now);
                Context.Sfx.PlayNow(perfect ? slashSound : barelySound);
                return;
            }

            inStance = false;
            if (judgement.Grade == JudgeGrade.Miss)
            {
                if (item != null && item.State != ItemState.Dropped) Drop(item, now);
                if (judgement.HasInput) Context.Sfx.PlayNow(missSound, 0.5f);
                return;
            }
            if (item != null) Cut(item, now, perfect, true);
            Context.Sfx.PlayNow(perfect ? bigSlashSound : barelySound);
        }

        public override void OnWhiff(double songTime) => Context.Sfx.PlayNow(whiffSound, 0.6f);

        public override void OnStageFinished(ScoreTracker score) => inStance = false;

        public override void Tick(double songTime, double songBeat)
        {
            float bounce = songBeat >= 0 ? Mathf.Exp(-6f * (float)(songBeat - Math.Floor(songBeat))) : 0f;
            master.localScale = Squash(masterScale, 0.05f * bounce + 0.12f * Pulse(songTime - masterToss, 0.2));
            float stance = inStance ? 0.12f : 0f;
            swordsman.localScale = Squash(swordsmanScale, 0.05f * bounce + stance);
            gong.localScale = gongScale * (1f + 0.25f * Pulse(songTime - gongHit, 0.25));

            float baseAngle = inStance ? swordStanceAngle : swordRestAngle;
            SetSword(Mathf.Lerp(baseAngle, swordSlashAngle, Pulse(songTime - swingStart, SwingSeconds)));

            UpdateItems(songTime, songBeat);
            UpdateEffects(songTime);
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
                        if (songBeat < item.Cue.Beat) break;
                        item.State = ItemState.Flying;
                        item.Sprite.gameObject.SetActive(true);
                        goto case ItemState.Flying;

                    case ItemState.Flying:
                    {
                        double span = Math.Max(1e-3, item.ArrivalBeat - item.Cue.Beat);
                        float u = (float)((songBeat - item.Cue.Beat) / span);
                        var p = Vector3.LerpUnclamped(releasePoint.position, strikePoint.position, u);
                        p.y += item.ArcHeight * 4f * u * (1f - u);
                        t.position = p;
                        t.rotation = Quaternion.Euler(0f, 0f, item.SpinPerBeat * (float)(songBeat - item.Cue.Beat));
                        if (u > 3f) Despawn(i);
                        break;
                    }

                    case ItemState.Dropped:
                    {
                        float dt = (float)(songTime - item.StateTime);
                        t.position = item.StartPosition + item.Velocity * dt + Vector3.up * (0.5f * Gravity * dt * dt);
                        t.Rotate(0f, 0f, -300f * Time.deltaTime);
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

        void Drop(Item item, double now)
        {
            item.Sprite.gameObject.SetActive(true);
            if (item.State == ItemState.Waiting) item.Sprite.transform.position = strikePoint.position;
            item.State = ItemState.Dropped;
            item.StateTime = now;
            item.StartPosition = item.Sprite.transform.position;
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
