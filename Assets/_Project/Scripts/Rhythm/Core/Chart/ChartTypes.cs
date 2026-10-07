using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    public enum NoteType
    {
        Tap,
        /// <summary>누름 시점과 뗌 시점을 각각 판정한다.</summary>
        Hold,
    }

    /// <summary>
    /// 플레이어에게 다음 입력을 예고하는 신호(효과음 + 연출). 시점은 패턴 앵커 기준 박 단위.
    /// </summary>
    [Serializable]
    public sealed class ChartCue
    {
        public double offset;
        public string cueId;
        /// <summary>이 큐가 예고하는 노트의 패턴 내 인덱스. 예고 대상이 없으면 -1.</summary>
        public int targetNote = -1;
    }

    /// <summary>플레이어가 버튼을 눌러야 하는 시점. 앵커 기준 박 단위.</summary>
    [Serializable]
    public sealed class ChartNote
    {
        public double offset;
        public NoteType type;
        /// <summary>Hold일 때 누름부터 뗌까지의 길이(박).</summary>
        public double holdBeats;
    }

    /// <summary>
    /// 채보에 배치된 패턴 하나. 큐와 노트를 앵커 기준 상대 위치로 들고 있어서
    /// 앵커만 옮기면 패턴 전체가 함께 움직인다.
    /// </summary>
    [Serializable]
    public sealed class PatternInstance
    {
        public string patternId;
        /// <summary>오프셋 0이 놓이는 비트 번호(TempoMap 기준).</summary>
        public double anchorBeat;
        public List<ChartCue> cues = new List<ChartCue>();
        public List<ChartNote> notes = new List<ChartNote>();
    }
}
