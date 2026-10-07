using System.Collections.Generic;
using IWannabe.Rhythm.Charting;
using IWannabe.Stages.Slice;
using UnityEngine;
using static IWannabe.EditorTools.SetupUtil;
using static IWannabe.Rhythm.Charting.PatternPresets;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 스테이지 2 "베기": 132 BPM 태고·고토·피리, 밤 배경. 스승이 던진 물건을 검객이 벤다.
    /// 받아치기보다 한 단계 어렵게(난이도 4) 생성하고, 8분음표 4연타(volley)가 추가된다.
    /// </summary>
    sealed class SliceStageRecipe : StageRecipe
    {
        public override string StageId => "slice";
        public override string DisplayName => "베기";
        public override string AssetName => "Slice";
        public override Color Background => Hex("2B2D42");
        public override Color HudInk => Hex("EDF2F4");

        public override List<PatternDefinition> CreatePatterns() => new List<PatternDefinition>
        {
            Fixed("toss", 1, 1.0f, 0, 2, OnsetBand.Full, 0.5, Cues((-1, SliceCues.Toss, 0)), Taps(0)),
            Fixed("high", 1, 0.85f, 0, 2, OnsetBand.Full, 1, Cues((-2, SliceCues.High, 0)), Taps(0)),
            Fixed("pair", 2, 1.0f, 1, 2, OnsetBand.Full, 1,
                Cues((-1, SliceCues.Toss, 0), (-0.5, SliceCues.Toss, 1)), Taps(0, 0.5)),
            WithCooldown(3, Hold("draw", 2, 0.6f, 0, 2, OnsetBand.Low, 2,
                Cues((-1, SliceCues.Draw, 0), (1, SliceCues.Tick, -1)))),
            WithCooldown(2, Fixed("triple", 3, 0.75f, 2, 2, OnsetBand.Full, 1,
                Cues((-1, SliceCues.Toss, 0), (-2.0 / 3, SliceCues.Toss, 1), (-1.0 / 3, SliceCues.Toss, 2)),
                Taps(0, 1.0 / 3, 2.0 / 3))),
            WithCooldown(3, Fixed("volley", 4, 0.8f, 2, 2, OnsetBand.Full, 1,
                Cues((-1, SliceCues.Toss, 0), (-0.5, SliceCues.Toss, 1), (0, SliceCues.Toss, 2), (0.5, SliceCues.Toss, 3)),
                Taps(0, 0.5, 1, 1.5))),
            WithCooldown(4, Echo("echo", SliceCues.Gong, 3, 0.85f, 1, 2)),
        };

        public override GeneratorSettings CreateGeneratorSettings() => new GeneratorSettings { seed = 20261008, difficulty = 4 };

        public override IEnumerable<(string cueId, string sfx, float volume)> CueSounds => new[]
        {
            (SliceCues.Toss, "toss", 0.9f),
            (SliceCues.High, "high", 0.85f),
            (SliceCues.Draw, "draw", 0.8f),
            (SliceCues.Tick, "tick", 0.8f),
            (SliceCues.Gong, "gong", 0.8f),
        };

        public override GameObject BuildPresenterRoot(StagePrefabKit kit)
        {
            var shapes = kit.Shapes;
            var material = kit.SpriteMaterial;
            var root = new GameObject($"{AssetName}Stage");
            var presenter = root.AddComponent<SliceStagePresenter>();
            var t = root.transform;

            Shape(t, "Moon", shapes.Circle, material, new Vector2(-6.2f, 3.2f), new Vector2(1.5f, 1.5f), Hex("EDF2F4", 0.9f), -5);
            Shape(t, "Ground", shapes.Square, material, new Vector2(0f, -4.6f), new Vector2(40f, 4f), Hex("3A3F58"), 0);
            Shape(t, "GroundLine", shapes.Square, material, new Vector2(0f, -2.62f), new Vector2(40f, 0.08f), Hex("8D99AE"), 1);

            var master = Node(t, "Master", new Vector2(-6f, -2.6f));
            Shape(master, "Body", shapes.Circle, material, new Vector2(0f, 0.8f), new Vector2(1.6f, 1.6f), Hex("8D99AE"), 5);
            Shape(master, "Eye", shapes.Circle, material, new Vector2(0.35f, 1.05f), new Vector2(0.18f, 0.18f), Hex("2B2D42"), 6);
            var release = Node(master, "ReleasePoint", new Vector2(0.9f, 1f));
            var gong = Node(t, "Gong", new Vector2(-4.4f, -0.6f));
            Shape(gong, "Rim", shapes.Ring, material, Vector2.zero, new Vector2(1.1f, 1.1f), Hex("E9C46A"), 4);
            Shape(gong, "Face", shapes.Circle, material, Vector2.zero, new Vector2(0.85f, 0.85f), Hex("E9C46A", 0.55f), 3);

            var swordsman = Node(t, "Swordsman", new Vector2(4.8f, -2.6f));
            var otamaton = OtamatonRig(swordsman, "Otamaton", new Vector2(0f, 1f), kit.Otamaton, material, 5);
            // 칼자루는 눈·입을 가리지 않도록 몸 바깥 왼쪽에 둔다.
            var swordPivot = Node(swordsman, "SwordPivot", new Vector2(-1.2f, 0.85f));
            Shape(swordPivot, "Blade", shapes.Square, material, new Vector2(0f, 1f), new Vector2(0.1f, 1.9f), Hex("EDF2F4"), 8);
            Shape(swordPivot, "Hilt", shapes.Square, material, Vector2.zero, new Vector2(0.2f, 0.4f), Hex("6D4C41"), 8);
            var strike = Node(swordsman, "StrikePoint", new Vector2(-2.1f, 1.7f));

            var item = Shape(t, "ItemTemplate", shapes.Circle, material, Vector2.zero, Vector2.one, Color.white, 10);
            var effect = Shape(t, "EffectTemplate", shapes.Square, material, Vector2.zero, Vector2.one, Color.white, 20);
            item.gameObject.SetActive(false);
            effect.gameObject.SetActive(false);

            // 링은 검객이 박마다 눌리는 영향을 받지 않도록 루트 아래, 베는 지점(월드 2.7, -0.9)에 둔다.
            var ring = Ring(t, "HoldRing", new Vector2(2.7f, -0.9f), 0.9f, 0.12f, material, 15);
            var shelfLeft = Node(t, "EchoShelfLeft", new Vector2(0f, 2.2f));
            var shelfRight = Node(t, "EchoShelfRight", new Vector2(3.4f, 2.2f));

            Assign(presenter,
                ("master", master), ("swordsman", swordsman), ("otamaton", otamaton), ("swordPivot", swordPivot),
                ("releasePoint", release), ("strikePoint", strike), ("gong", gong),
                ("itemTemplate", item), ("effectTemplate", effect),
                ("holdRing", ring), ("echoShelfLeft", shelfLeft), ("echoShelfRight", shelfRight),
                ("roundSprite", shapes.Circle), ("stickSprite", shapes.Square), ("ringSprite", shapes.Ring),
                ("slashSound", LoadSfx("slash")), ("bigSlashSound", LoadSfx("slash_big")),
                ("barelySound", LoadSfx("slash_barely")), ("missSound", LoadSfx("miss")),
                ("whiffSound", LoadSfx("whiff")));
            return root;
        }
    }
}
