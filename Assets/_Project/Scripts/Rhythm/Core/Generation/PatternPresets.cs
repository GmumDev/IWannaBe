using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    /// <summary>
    /// 원버튼 스테이지용 기본 패턴 세트. 새 스테이지의 패턴 라이브러리를 시작할 때 쓴다.
    /// 큐 규칙: throw → 1박 뒤, lob → 2박 뒤, charge → 1박 뒤 누르고 tick 1박 뒤에 뗌, bell → 4박 뒤 따라 치기.
    /// </summary>
    public static class PatternPresets
    {
        public const string CueThrow = "throw";
        public const string CueLob = "lob";
        public const string CueCharge = "charge";
        public const string CueTick = "tick";
        public const string CueBell = "bell";

        public static List<PatternDefinition> CreateStarterSet()
        {
            return new List<PatternDefinition>
            {
                Fixed("throw", 1, 1.0f, 0, 2, OnsetBand.Full, 0.5,
                    Cues((-1, CueThrow, 0)),
                    Taps(0)),

                Fixed("lob", 1, 0.85f, 0, 2, OnsetBand.Full, 1,
                    Cues((-2, CueLob, 0)),
                    Taps(0)),

                Fixed("double", 2, 0.95f, 1, 2, OnsetBand.Full, 1,
                    Cues((-1, CueThrow, 0), (-0.5, CueThrow, 1)),
                    Taps(0, 0.5)),

                Cooldown(3, Hold("charge", 2, 0.6f, 0, 2, OnsetBand.Low, 2,
                    Cues((-1, CueCharge, 0), (1, CueTick, -1)))),

                Cooldown(2, Fixed("triplet", 3, 0.7f, 2, 2, OnsetBand.Full, 1,
                    Cues((-1, CueThrow, 0), (-2.0 / 3, CueThrow, 1), (-1.0 / 3, CueThrow, 2)),
                    Taps(0, 1.0 / 3, 2.0 / 3))),

                new PatternDefinition
                {
                    id = "echo",
                    kind = PatternKind.CallAndResponse,
                    difficulty = 3,
                    weight = 0.85f,
                    cooldown = 4,
                    minLevel = 1,
                    maxLevel = 2,
                    band = OnsetBand.Full,
                    callCueId = CueBell,
                    callBeats = 4,
                    responseGrid = 2,
                    minResponseNotes = 2,
                    maxResponseNotes = 4,
                },
            };
        }

        static PatternDefinition Fixed(string id, int difficulty, float weight, int minLevel, int maxLevel, OnsetBand band,
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

        static PatternDefinition Hold(string id, int difficulty, float weight, int minLevel, int maxLevel, OnsetBand band,
            double holdBeats, List<PatternCueDef> cues)
        {
            var notes = new List<PatternNoteDef> { new PatternNoteDef { offset = 0, type = NoteType.Hold, holdBeats = holdBeats } };
            return Fixed(id, difficulty, weight, minLevel, maxLevel, band, 1, cues, notes);
        }

        static PatternDefinition Cooldown(int patterns, PatternDefinition definition)
        {
            definition.cooldown = patterns;
            return definition;
        }

        static List<PatternCueDef> Cues(params (double offset, string cueId, int target)[] items)
        {
            var list = new List<PatternCueDef>();
            foreach (var item in items)
                list.Add(new PatternCueDef { offset = item.offset, cueId = item.cueId, targetNote = item.target });
            return list;
        }

        static List<PatternNoteDef> Taps(params double[] offsets)
        {
            var list = new List<PatternNoteDef>();
            foreach (var offset in offsets)
                list.Add(new PatternNoteDef { offset = offset, type = NoteType.Tap });
            return list;
        }
    }
}
