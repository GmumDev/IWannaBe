using System.Collections.Generic;
using IWannabe.Rhythm.Charting;
using IWannabe.Stages.HitBack;
using UnityEngine;
using static IWannabe.EditorTools.SetupUtil;
using static IWannabe.Rhythm.Charting.PatternPresets;

namespace IWannabe.EditorTools
{
    /// <summary>스테이지 1 "받아치기": 124 BPM 신스 팝, 투수가 던진 공을 타자가 받아친다.</summary>
    sealed class HitBackStageRecipe : StageRecipe
    {
        public override string StageId => "hitback";
        public override string DisplayName => "받아치기";
        public override string AssetName => "HitBack";
        public override Color Background => Hex("FDF0D5");
        public override Color HudInk => Hex("264653");

        public override List<PatternDefinition> CreatePatterns() => new List<PatternDefinition>
        {
            Fixed("throw", 1, 1.0f, 0, 2, OnsetBand.Full, 0.5, Cues((-1, HitBackCues.Throw, 0)), Taps(0)),
            Fixed("lob", 1, 0.85f, 0, 2, OnsetBand.Full, 1, Cues((-2, HitBackCues.Lob, 0)), Taps(0)),
            Fixed("double", 2, 0.95f, 1, 2, OnsetBand.Full, 1,
                Cues((-1, HitBackCues.Throw, 0), (-0.5, HitBackCues.Throw, 1)), Taps(0, 0.5)),
            WithCooldown(3, Hold("charge", 2, 0.6f, 0, 2, OnsetBand.Low, 2,
                Cues((-1, HitBackCues.Charge, 0), (1, HitBackCues.Tick, -1)))),
            WithCooldown(2, Fixed("triplet", 3, 0.7f, 2, 2, OnsetBand.Full, 1,
                Cues((-1, HitBackCues.Throw, 0), (-2.0 / 3, HitBackCues.Throw, 1), (-1.0 / 3, HitBackCues.Throw, 2)),
                Taps(0, 1.0 / 3, 2.0 / 3))),
            WithCooldown(4, Echo("echo", HitBackCues.Bell, 3, 0.85f, 1, 2)),
        };

        public override IEnumerable<(string cueId, string sfx, float volume)> CueSounds => new[]
        {
            (HitBackCues.Throw, "throw", 0.9f),
            (HitBackCues.Lob, "lob", 0.9f),
            (HitBackCues.Charge, "charge", 0.8f),
            (HitBackCues.Tick, "tick", 0.8f),
            (HitBackCues.Bell, "bell", 0.9f),
        };

        public override GameObject BuildPresenterRoot(StagePrefabKit kit)
        {
            var shapes = kit.Shapes;
            var material = kit.SpriteMaterial;
            var root = new GameObject($"{AssetName}Stage");
            var presenter = root.AddComponent<HitBackStagePresenter>();
            var t = root.transform;
            Shape(t, "Ground", shapes.Square, material, new Vector2(0f, -4.6f), new Vector2(40f, 4f), Hex("8AB17D"), 0);
            Shape(t, "GroundLine", shapes.Square, material, new Vector2(0f, -2.62f), new Vector2(40f, 0.08f), Hex("6A994E"), 1);

            // 캐릭터 피벗은 발밑이라 박마다 눌릴 때 바닥에 붙어 있다.
            var pitcher = Node(t, "Pitcher", new Vector2(-5.6f, -2.6f));
            Shape(pitcher, "Body", shapes.Circle, material, new Vector2(0f, 1f), new Vector2(2f, 2f), Hex("E76F51"), 5);
            Shape(pitcher, "EyeL", shapes.Circle, material, new Vector2(0.2f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
            Shape(pitcher, "EyeR", shapes.Circle, material, new Vector2(0.62f, 1.35f), new Vector2(0.22f, 0.22f), Hex("264653"), 6);
            var release = Node(pitcher, "ReleasePoint", new Vector2(1.1f, 1.6f));

            var batter = Node(t, "Batter", new Vector2(5.4f, -2.6f));
            var otamaton = OtamatonRig(batter, "Otamaton", new Vector2(0f, 1f), kit.Otamaton, material, 5);
            // 패들은 쉬는 자세에서도 눈·입을 가리지 않도록 몸 바깥 왼쪽에 둔다.
            var pivot = Node(batter, "PaddlePivot", new Vector2(-1.2f, 0.9f));
            Shape(pivot, "Paddle", shapes.Square, material, new Vector2(0f, 0.8f), new Vector2(0.28f, 1.6f), Hex("6D4C41"), 7);
            var hit = Node(batter, "HitPoint", new Vector2(-1.7f, 1.9f));

            var ball = Shape(t, "BallTemplate", shapes.Circle, material, Vector2.zero, Vector2.one, Color.white, 10);
            var flash = Shape(t, "FlashTemplate", shapes.Ring, material, Vector2.zero, Vector2.one, Color.white, 20);
            ball.gameObject.SetActive(false);
            flash.gameObject.SetActive(false);

            // 링은 캐릭터가 박마다 눌리는 영향을 받지 않도록 루트 아래, 타격 지점(월드 3.7, -0.7)에 둔다.
            var ring = Ring(t, "HoldRing", new Vector2(3.7f, -0.7f), 0.75f, 0.12f, material, 15);
            var shelfLeft = Node(t, "EchoShelfLeft", new Vector2(1f, 2f));
            var shelfRight = Node(t, "EchoShelfRight", new Vector2(4.2f, 2f));

            Assign(presenter,
                ("pitcher", pitcher), ("batter", batter), ("otamaton", otamaton), ("paddlePivot", pivot),
                ("releasePoint", release), ("hitPoint", hit),
                ("ballTemplate", ball), ("flashTemplate", flash),
                ("holdRing", ring), ("echoShelfLeft", shelfLeft), ("echoShelfRight", shelfRight),
                ("hitSound", LoadSfx("hit")), ("bigHitSound", LoadSfx("hit_big")),
                ("barelySound", LoadSfx("hit_barely")), ("missSound", LoadSfx("miss")),
                ("whiffSound", LoadSfx("whiff")));
            return root;
        }
    }
}
