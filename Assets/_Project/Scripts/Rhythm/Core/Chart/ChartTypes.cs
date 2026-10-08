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
        /// <summary>이 패턴이 속한 구간(<see cref="ChartSegment"/> 목록의 인덱스). 큐가 있는 구간이다. 구간이 없는 채보는 0.</summary>
        public int segment;
        public List<ChartCue> cues = new List<ChartCue>();
        public List<ChartNote> notes = new List<ChartNote>();
    }

    /// <summary>
    /// 곡의 한 구간을 맡는 미니게임. 리믹스는 구간이 여럿이고, 미니게임 하나짜리 스테이지는 구간이 없다.
    /// 구간은 다음 구간의 시작 비트에서 끝난다. 패턴의 큐는 자기 구간 안에 있고, 노트는 다음 구간으로 넘어갈 수 있다.
    /// </summary>
    [Serializable]
    public sealed class ChartSegment
    {
        /// <summary>스테이지의 미니게임 목록에서 찾을 ID. 큐 ID는 이 미니게임 안에서만 해석된다.</summary>
        public string minigameId;
        /// <summary>구간이 시작하는 비트 번호(TempoMap 기준). 첫 구간은 곡 처음부터라 이 값을 쓰지 않는다.</summary>
        public double startBeat;
    }
}
