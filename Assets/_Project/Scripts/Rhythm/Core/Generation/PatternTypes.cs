using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    public enum PatternKind
    {
        /// <summary>정해진 큐·노트 배치를 그대로 놓는다.</summary>
        Fixed,
        /// <summary>
        /// 콜 앤 리스폰스. 앞 구간에서 큐가 리듬을 들려주고 다음 구간에서 그대로 따라 친다.
        /// 리듬은 응답 구간의 실제 온셋에서 뽑는다.
        /// </summary>
        CallAndResponse,
    }

    [Serializable]
    public sealed class PatternCueDef
    {
        public double offset;
        public string cueId;
        public int targetNote = -1;
    }

    [Serializable]
    public sealed class PatternNoteDef
    {
        public double offset;
        public NoteType type;
        public double holdBeats;
    }

    /// <summary>
    /// 스테이지가 쓰는 패턴 하나의 정의. 버튼이 하나뿐이므로 플레이어는 "큐 종류"만 듣고
    /// 입력 타이밍을 알 수 있어야 한다. 같은 cueId는 항상 같은 간격 뒤의 입력을 뜻하도록 설계한다.
    /// </summary>
    [Serializable]
    public sealed class PatternDefinition
    {
        public string id;
        public PatternKind kind;
        /// <summary>1(쉬움)~5(어려움). 생성 설정의 난이도 이하만 쓰인다.</summary>
        public int difficulty = 1;
        public float weight = 1f;
        /// <summary>한 번 쓴 뒤 다시 쓰기 전에 거쳐야 할 다른 패턴 수. 홀드·따라 치기처럼 특별한 패턴에 쓴다.</summary>
        public int cooldown;
        /// <summary>이 패턴을 쓸 수 있는 구간 강도 범위(0~2).</summary>
        public int minLevel;
        public int maxLevel = 2;
        /// <summary>입력 위치를 고를 때 참고할 주파수 대역.</summary>
        public OnsetBand band = OnsetBand.Full;
        /// <summary>앵커(오프셋 0)를 놓을 수 있는 간격(박). 1이면 정박, 0.5면 반박에도 놓인다.</summary>
        public double anchorStep = 1;
        public List<PatternCueDef> cues = new List<PatternCueDef>();
        public List<PatternNoteDef> notes = new List<PatternNoteDef>();

        // CallAndResponse 전용
        public string callCueId = "bell";
        /// <summary>콜 길이(박). 응답은 각 큐로부터 정확히 이만큼 뒤에 온다.</summary>
        public int callBeats = 4;
        /// <summary>응답 리듬을 고르는 격자(박당 칸 수). 2면 8분음표.</summary>
        public int responseGrid = 2;
        public int minResponseNotes = 2;
        public int maxResponseNotes = 4;
    }
}
