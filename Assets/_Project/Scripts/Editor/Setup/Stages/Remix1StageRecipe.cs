using System.Collections.Generic;
using IWannabe.Rhythm.Charting;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 리믹스 프로토타입: 받아치기 곡(124 BPM) 위에서 받아치기와 베기가 번갈아 나온다.
    /// 앞쪽은 8마디씩 바꾸고, 곡이 가장 격렬한 24~31마디는 4마디씩 바꿔 전환을 촘촘히 확인한다.
    /// 곡은 받아치기 것을 다시 쓴다(정식 리믹스 곡은 1챕터를 만들 때 따로 둔다).
    /// </summary>
    sealed class Remix1StageRecipe : RemixStageRecipe
    {
        readonly MinigameStageRecipe hitBack;
        readonly MinigameStageRecipe slice;

        public Remix1StageRecipe(MinigameStageRecipe hitBack, MinigameStageRecipe slice)
        {
            this.hitBack = hitBack;
            this.slice = slice;
        }

        public override string StageId => "remix1";
        public override string DisplayName => "리믹스";
        public override string AssetName => "Remix1";
        public override string MusicPath => hitBack.MusicPath;

        // 구간이 짧아 한 구절을 통째로 쉬면 미니게임이 거의 안 보이므로, 쉬는 확률을 낮춘다.
        public override GeneratorSettings CreateGeneratorSettings() => new GeneratorSettings
        {
            seed = 20261009,
            difficulty = 3,
            restChanceByLevel = new[] { 0.2f, 0.05f, 0f },
        };

        public override IReadOnlyList<(MinigameStageRecipe minigame, int startBar)> Segments => new[]
        {
            (hitBack, 0),
            (slice, 8),
            (hitBack, 16),
            (slice, 24),
            (hitBack, 28),
            (slice, 32),
        };
    }
}
