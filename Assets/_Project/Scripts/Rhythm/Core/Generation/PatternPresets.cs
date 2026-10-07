using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    /// <summary>
    /// 패턴 정의를 짧게 조립하는 도우미. 스테이지별 패턴 구성(어떤 큐를 몇 박 뒤에 칠지)은
    /// 각 스테이지 설정 쪽에서 이 함수들로 만든다.
    /// </summary>
    public static class PatternPresets
    {
        public static PatternDefinition Fixed(string id, int difficulty, float weight, int minLevel, int maxLevel, OnsetBand band,
            double anchorStep, List<PatternCueDef> cues, List<PatternNoteDef> notes)
        {
            return new PatternDefinition
            {
                id = id,
                kind = PatternKind.Fixed,
                difficulty = difficulty,
                weight = weight,
                minLevel = minLevel,
                maxLevel = maxLevel,
                band = band,
                anchorStep = anchorStep,
                cues = cues,
                notes = notes,
            };
        }

        /// <summary>오프셋 0에서 누르고 <paramref name="holdBeats"/>박 뒤에 떼는 홀드 패턴.</summary>
        public static PatternDefinition Hold(string id, int difficulty, float weight, int minLevel, int maxLevel, OnsetBand band,
            double holdBeats, List<PatternCueDef> cues)
        {
            var notes = new List<PatternNoteDef> { new PatternNoteDef { offset = 0, type = NoteType.Hold, holdBeats = holdBeats } };
            return Fixed(id, difficulty, weight, minLevel, maxLevel, band, 1, cues, notes);
        }

        /// <summary>콜 앤 리스폰스. 큐가 들려준 리듬을 <paramref name="callBeats"/>박 뒤에 따라 친다.</summary>
        public static PatternDefinition Echo(string id, string callCueId, int difficulty, float weight, int minLevel, int maxLevel,
            int callBeats = 4, int minNotes = 2, int maxNotes = 4)
        {
            return new PatternDefinition
            {
                id = id,
                kind = PatternKind.CallAndResponse,
                difficulty = difficulty,
                weight = weight,
                minLevel = minLevel,
                maxLevel = maxLevel,
                band = OnsetBand.Full,
                callCueId = callCueId,
                callBeats = callBeats,
                responseGrid = 2,
                minResponseNotes = minNotes,
                maxResponseNotes = maxNotes,
            };
        }

        public static PatternDefinition WithCooldown(int patterns, PatternDefinition definition)
        {
            definition.cooldown = patterns;
            return definition;
        }

        /// <summary>(앵커 기준 박, 큐 ID, 예고 대상 노트 인덱스 또는 -1) 목록으로 큐를 만든다.</summary>
        public static List<PatternCueDef> Cues(params (double offset, string cueId, int target)[] items)
        {
            var list = new List<PatternCueDef>();
            foreach (var item in items)
                list.Add(new PatternCueDef { offset = item.offset, cueId = item.cueId, targetNote = item.target });
            return list;
        }

        public static List<PatternNoteDef> Taps(params double[] offsets)
        {
            var list = new List<PatternNoteDef>();
            foreach (var offset in offsets)
                list.Add(new PatternNoteDef { offset = offset, type = NoteType.Tap });
            return list;
        }
    }
}
