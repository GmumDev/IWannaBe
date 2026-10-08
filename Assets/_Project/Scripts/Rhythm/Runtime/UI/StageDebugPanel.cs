using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 플레이 중 진행 상태를 화면 왼쪽에 보여 주는 디버그 패널(IMGUI). 지금 치는 패턴의 큐·노트, 다가오는 노트,
    /// 구간·이어받기, 판정 통계와 평균 오차, 곡 시간·박·BPM, 지연 보정, 입력, 프레임을 띄운다.
    /// 보여 주기만 하고 입력은 받지 않는다. 에디터·개발 빌드에서만 켜지고, F3을 누를 때마다
    /// 자세히 → 간단히(투수 쪽을 가리지 않는 짧은 요약) → 끄기 순서로 바뀐다.
    /// </summary>
    public sealed class StageDebugPanel : MonoBehaviour
    {
        public enum Mode { Full, Compact, Hidden }

        const int RecentJudgementCount = 6;
        const int UpcomingNoteCount = 5;
        const int CompactUpcomingNoteCount = 3;
        const float Padding = 12f;
        // HUD 캔버스와 같은 기준 해상도. 이 크기 기준으로 그리고 화면 크기에 맞춰 늘린다.
        const float ReferenceWidth = 1920f;
        const float ReferenceHeight = 1080f;

        const string TitleColor = "#E9C46A";
        const string DimColor = "#9AA5B1";
        const string PerfectColor = "#4FD1C5";
        const string BarelyColor = "#F4A261";
        const string MissColor = "#E76F51";
        const string CarryColor = "#FFD166";

        [SerializeField] StageRunner runner;
        [SerializeField] Conductor conductor;
        [SerializeField] RhythmInput input;
        [Tooltip("처음 모드. 플레이 중 F3으로 자세히 → 간단히 → 끄기 순서로 바꾼다.")]
        [SerializeField] Mode startMode = Mode.Full;
        [Tooltip("기준 해상도(1920×1080)에서 패널 왼쪽 위 위치.")]
        [SerializeField] Vector2 position = new Vector2(24f, 112f);
        [Tooltip("기준 해상도에서의 너비. 결과 창(가운데 860)과 겹치지 않게 530 아래로 둔다.")]
        [SerializeField, Min(200f)] float width = 500f;
        [SerializeField, Min(10)] int fontSize = 18;

        readonly StringBuilder text = new StringBuilder(2048);
        readonly GUIContent content = new GUIContent();
        readonly Queue<NoteJudgement> recent = new Queue<NoteJudgement>();
        readonly List<TimelineNote> upcoming = new List<TimelineNote>();
        readonly Dictionary<int, NoteJudgement> pressResults = new Dictionary<int, NoteJudgement>();
        readonly Dictionary<int, NoteJudgement> releaseResults = new Dictionary<int, NoteJudgement>();

        Mode mode;
        GUIStyle style;
        Texture2D background;
        int noteCursor;
        int whiffs;
        int timedInputs;
        double deltaSum;
        double deltaSquareSum;
        int heldInputs;
        double lastPressTime = double.NaN;
        float smoothedFrameSeconds;

        void Awake()
        {
            // 릴리스 빌드에서는 그리지도 집계하지도 않는다.
            if (!Debug.isDebugBuild)
            {
                enabled = false;
                return;
            }
            mode = startMode;
        }

        /// <summary>지금 모드. 플레이 중에는 F3으로 바뀌고, 점검 도구가 직접 바꿀 수도 있다.</summary>
        public Mode CurrentMode
        {
            get => mode;
            set => mode = value;
        }

        bool Visible => mode != Mode.Hidden;
        bool Full => mode == Mode.Full;

        void OnEnable()
        {
            runner.NoteJudged += OnJudged;
            runner.NoteWhiffed += OnWhiffed;
            input.Pressed += OnPressed;
            input.Released += OnReleased;
        }

        void OnDisable()
        {
            runner.NoteJudged -= OnJudged;
            runner.NoteWhiffed -= OnWhiffed;
            input.Pressed -= OnPressed;
            input.Released -= OnReleased;
        }

        void OnDestroy()
        {
            if (background != null) Destroy(background);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) mode = (Mode)(((int)mode + 1) % 3);

            float frame = Time.unscaledDeltaTime;
            smoothedFrameSeconds = smoothedFrameSeconds <= 0f ? frame : Mathf.Lerp(smoothedFrameSeconds, frame, 0.1f);
        }

        // 진행자가 이번 프레임을 처리한 뒤에 내용을 만든다.
        void LateUpdate()
        {
            if (Visible) Rebuild();
        }

        void OnGUI()
        {
            if (!Visible || string.IsNullOrEmpty(content.text)) return;
            EnsureStyle();

            float scale = Mathf.Sqrt(Screen.width / ReferenceWidth * (Screen.height / ReferenceHeight));
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float textWidth = width - Padding * 2f;
            float height = style.CalcHeight(content, textWidth);
            var box = new Rect(position.x, position.y, width, height + Padding * 2f);
            GUI.DrawTexture(box, background);
            GUI.Label(new Rect(box.x + Padding, box.y + Padding, textWidth, height), content, style);
            GUI.matrix = previous;
        }

        void EnsureStyle()
        {
            if (style != null) return;
            style = new GUIStyle(GUI.skin.label)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                fontSize = fontSize,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
            };
            style.normal.textColor = new Color(0.93f, 0.95f, 0.96f);
            background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            background.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.7f));
            background.Apply();
        }

        // ───────── 내용 ─────────

        void Rebuild()
        {
            text.Clear();
            text.Append(Full ? "<b>디버그</b> 자세히  " : "<b>디버그</b> 간단히  ")
                .Append(Colored(Full ? "F3 간단히" : "F3 끄기", DimColor)).Append('\n');

            var definition = runner.Definition;
            var timeline = runner.Timeline;
            var judge = runner.Judge;
            if (definition == null || timeline == null || judge == null || conductor.TempoMap == null)
            {
                text.Append(runner.HasFailed ? "오류로 시작하지 못했습니다" : "스테이지 준비 중");
                content.text = text.ToString();
                return;
            }

            double time = conductor.SongTime;
            double beat = conductor.SongBeat;
            CollectUpcoming(timeline, judge);
            AppendStage(definition, time, beat);
            AppendSegment(definition, timeline, beat);
            AppendNotes(definition, timeline, judge, time, beat);
            AppendScore();
            if (Full) AppendInputAndChart(definition, timeline);
            content.text = text.ToString();
        }

        /// <summary>판정이 끝나지 않은 노트를 시간순으로 몇 개 모은다. 첫 노트가 지금 치는 노트다.</summary>
        void CollectUpcoming(ChartTimeline timeline, Judge judge)
        {
            var notes = timeline.Notes;
            while (noteCursor < notes.Count && judge.IsFinished(notes[noteCursor])) noteCursor++;
            upcoming.Clear();
            int count = Full ? UpcomingNoteCount : CompactUpcomingNoteCount;
            for (int i = noteCursor; i < notes.Count && upcoming.Count < count; i++)
                if (!judge.IsFinished(notes[i])) upcoming.Add(notes[i]);
        }

        void AppendStage(StageDefinition definition, double time, double beat)
        {
            var song = definition.Song;
            int beatsPerBar = Math.Max(1, song.BeatsPerBar);
            double fromDownbeat = beat - song.FirstDownbeatIndex;
            int bar = (int)Math.Floor(fromDownbeat / beatsPerBar);
            double beatInBar = fromDownbeat - bar * beatsPerBar;
            double bpm = 60.0 / conductor.TempoMap.SecondsPerBeatAt(beat);

            text.Append($"{definition.DisplayName} ({definition.StageId}) · {StateLabel(runner.CurrentState)}");
            if (runner.AutoPlay) text.Append(" · 자동 플레이");
            text.Append('\n');
            text.Append($"곡 {time:0.00} / {song.Clip.length:0.00}s · {bar + 1}마디 {beatInBar + 1:0.0}박");
            if (Full) text.Append($" (박 {beat:0.00})");
            text.Append($" · BPM {bpm:0.0}\n");
            if (Full)
                text.Append($"출력 지연 보정 {conductor.OutputLatency * 1000:0}ms · 입력 보정 {runner.InputOffsetSeconds * 1000:+0;-0;0}ms\n");
        }

        void AppendSegment(StageDefinition definition, ChartTimeline timeline, double beat)
        {
            int current = runner.CurrentSegment;
            if (current < 0) return;
            var segments = timeline.Segments;
            if (Full) Section("구간");
            else text.Append("구간 ");
            text.Append($"{current + 1}/{segments.Count} {MinigameLabel(definition, segments[current].MinigameId)}");
            if (current + 1 < segments.Count)
            {
                var next = segments[current + 1];
                text.Append($" · {MinigameLabel(definition, next.MinigameId)}까지 {next.SwitchBeat - beat:0.0}박");
            }
            text.Append('\n');
            if (!Full || segments.Count <= 1) return;
            text.Append($"전환 {runner.SwitchCount}회 · 이어받은 노트 {runner.CarriedNoteCount}개 · 엉뚱한 구간 이벤트 {Warn(runner.StrayEventCount)}\n");
            if (current + 1 < segments.Count)
            {
                // 전환은 구간 시작 박이 기본이고, 입력과 겹치면 입력 사이로 옮겨진다.
                var next = segments[current + 1];
                double shift = next.SwitchBeat - next.StartBeat;
                text.Append($"다음 전환 {next.SwitchBeat:0.00}박");
                if (Math.Abs(shift) > 1e-3) text.Append(Colored($"(구간 시작 {next.StartBeat:0.##}박에서 {shift:+0.00;-0.00}박)", CarryColor));
                text.Append($" · 가장 가까운 입력과 {next.SwitchClearance * 1000:0}ms\n");
            }
        }

        void AppendNotes(StageDefinition definition, ChartTimeline timeline, Judge judge, double time, double beat)
        {
            if (Full) Section("지금 치는 패턴");
            else text.Append("패턴 ");
            if (upcoming.Count == 0)
            {
                text.Append("남은 노트 없음\n");
                return;
            }

            var pattern = timeline.Patterns[upcoming[0].PatternIndex];
            text.Append($"{pattern.PatternId} · {MinigameLabel(definition, timeline.Segments[pattern.Segment].MinigameId)} 구간 {pattern.Segment + 1}");
            if (Full) text.Append($" · 앵커 {pattern.AnchorBeat:0.##}박");
            if (IsCarried(pattern.Segment)) text.Append(" · ").Append(Colored("이어받는 중", CarryColor));
            text.Append('\n');
            if (Full)
            {
                foreach (var cue in pattern.Cues)
                {
                    text.Append($"  큐 {cue.CueId} {cue.Beat:0.00}박 {Relative(cue.Beat - beat)} {(cue.Time <= time ? "울림" : "대기")}");
                    if (cue.TargetNoteId >= 0) text.Append($" → #{cue.TargetNoteId}");
                    text.Append('\n');
                }
                foreach (var note in pattern.Notes)
                    text.Append($"  노트 #{note.Id} {TypeLabel(note)} {note.Beat:0.00}박 {Relative(note.Beat - beat)} {NoteState(note, judge)}\n");
                Section("다가오는 노트");
            }

            foreach (var note in upcoming)
            {
                var owner = timeline.Patterns[note.PatternIndex];
                text.Append($"  #{note.Id} {TypeLabel(note)} {(note.Time - time) * 1000:+0;-0;0}ms · {owner.PatternId}");
                if (Full) text.Append($" · {note.Beat:0.00}박 · 구간 {note.Segment + 1}");
                if (IsCarried(note.Segment)) text.Append(' ').Append(Colored("이어받음", CarryColor));
                if (judge.IsHoldingNote(note)) text.Append(' ').Append(Colored("누르는 중", CarryColor));
                text.Append('\n');
            }
        }

        /// <summary>앞 구간 패턴의 노트를 지금 미니게임이 이어받아 처리하는 중인지.</summary>
        bool IsCarried(int segment) => segment < runner.CurrentSegment;

        void AppendScore()
        {
            var score = runner.Score;
            double soFar = score.JudgedParts == 0 ? 1.0 : (score.Perfect + 0.5 * score.Barely) / score.JudgedParts;
            if (!Full)
            {
                text.Append($"{Colored("P", PerfectColor)} {score.Perfect} · {Colored("아슬", BarelyColor)} {score.Barely} · {Colored("Miss", MissColor)} {score.Miss}" +
                            $" · {soFar * 100:0.0}%({RankLabel(soFar)})");
                if (timedInputs > 0) text.Append($" · 평균 오차 {OffsetLabel(deltaSum / timedInputs)}");
                text.Append('\n');
                return;
            }

            Section("판정");
            text.Append($"{Colored("Perfect", PerfectColor)} {score.Perfect} · {Colored("아슬아슬", BarelyColor)} {score.Barely} · " +
                        $"{Colored("Miss", MissColor)} {score.Miss} · 헛치기 {whiffs} · 판정 단위 {score.JudgedParts}/{score.TotalParts}\n");
            text.Append($"정확도 지금까지 {soFar * 100:0.0}%({RankLabel(soFar)}) · 최종 최저 {score.Accuracy * 100:0.0}%\n");
            if (timedInputs > 0)
            {
                double mean = deltaSum / timedInputs;
                double deviation = Math.Sqrt(Math.Max(0, deltaSquareSum / timedInputs - mean * mean));
                text.Append($"평균 오차 {OffsetLabel(mean)} · 표준편차 {deviation * 1000:0}ms · 맞힌 입력 {timedInputs}개\n");
            }
            if (recent.Count > 0)
            {
                text.Append("최근(오래된 것부터) ");
                bool first = true;
                foreach (var judgement in recent)
                {
                    if (!first) text.Append(" · ");
                    text.Append(Result(judgement));
                    first = false;
                }
                text.Append('\n');
            }
        }

        void AppendInputAndChart(StageDefinition definition, ChartTimeline timeline)
        {
            Section("기타");
            text.Append(heldInputs > 0 ? $"입력 누르는 중({heldInputs})" : "입력 뗀 상태");
            text.Append(double.IsNaN(lastPressTime) ? " · 마지막 누름 -" : $" · 마지막 누름 {lastPressTime:0.00}s");
            if (runner.AutoPlay) text.Append(" · 실제 입력 무시(자동 플레이)");
            text.Append('\n');
            float frame = Mathf.Max(1e-4f, smoothedFrameSeconds);
            text.Append($"{1f / frame:0} fps({frame * 1000:0.0}ms) · 노트 {timeline.Notes.Count} · 패턴 {timeline.Patterns.Count} · 구간 {timeline.Segments.Count}\n");
            string info = definition.Chart.GenerationInfo;
            if (!string.IsNullOrEmpty(info))
            {
                int end = info.IndexOf('\n');
                text.Append("채보 ").Append(Colored(end < 0 ? info : info.Substring(0, end), DimColor)).Append('\n');
            }
        }

        // ───────── 이벤트 집계 ─────────

        void OnJudged(NoteJudgement judgement)
        {
            recent.Enqueue(judgement);
            while (recent.Count > RecentJudgementCount) recent.Dequeue();
            (judgement.Phase == NotePhase.Press ? pressResults : releaseResults)[judgement.Note.Id] = judgement;
            if (!judgement.HasInput || judgement.Grade == JudgeGrade.Miss) return;
            timedInputs++;
            deltaSum += judgement.Delta;
            deltaSquareSum += judgement.Delta * judgement.Delta;
        }

        void OnWhiffed(double time) => whiffs++;

        void OnPressed(RhythmInputEvent e)
        {
            heldInputs++;
            lastPressTime = conductor.RealtimeToSongTime(e.Time);
        }

        void OnReleased(RhythmInputEvent e) => heldInputs = Math.Max(0, heldInputs - 1);

        // ───────── 글자 만들기 ─────────

        void Section(string title) => text.Append("<b><color=").Append(TitleColor).Append('>').Append(title).Append("</color></b>\n");

        string NoteState(TimelineNote note, Judge judge)
        {
            pressResults.TryGetValue(note.Id, out var press);
            releaseResults.TryGetValue(note.Id, out var release);
            if (judge.IsHoldingNote(note)) return $"{Colored("누르는 중", CarryColor)} 누름 {Result(press)}";
            if (!judge.IsFinished(note)) return "대기";
            if (note.Type == NoteType.Hold) return $"누름 {Result(press)} · 뗌 {Result(release)}";
            return Result(press);
        }

        static string Result(NoteJudgement judgement)
        {
            if (judgement.Note == null) return "-";
            string grade = judgement.Grade == JudgeGrade.Perfect ? Colored("Perfect", PerfectColor)
                : judgement.Grade == JudgeGrade.Barely ? Colored("아슬아슬", BarelyColor)
                : Colored("Miss", MissColor);
            string phase = judgement.Phase == NotePhase.Release ? "(뗌)" : string.Empty;
            return judgement.HasInput ? $"{grade}{phase} {judgement.Delta * 1000:+0;-0;0}ms" : $"{grade}{phase}";
        }

        /// <summary>평균 입력 오차. 음수면 빠르게, 양수면 늦게 누른다. 1ms 안쪽은 정확으로 본다.</summary>
        static string OffsetLabel(double meanSeconds)
        {
            double ms = meanSeconds * 1000;
            string tendency = Math.Abs(ms) < 1 ? "정확" : ms < 0 ? "빠름" : "느림";
            return $"{ms:+0;-0;0}ms({tendency})";
        }

        static string TypeLabel(TimelineNote note) => note.Type == NoteType.Hold ? $"홀드 {note.EndBeat - note.Beat:0.##}박" : "탭";

        static string Relative(double beats) => $"({beats:+0.00;-0.00;0.00}박)";

        static string MinigameLabel(StageDefinition definition, string minigameId)
        {
            var minigame = definition.FindMinigame(minigameId);
            return minigame != null ? $"{minigame.DisplayName}({minigame.MinigameId})" : $"?({minigameId})";
        }

        static string RankLabel(double accuracy) =>
            accuracy >= ScoreTracker.SuperbThreshold ? "Superb" : accuracy >= ScoreTracker.OkThreshold ? "OK" : "TryAgain";

        static string StateLabel(StageRunner.State state)
        {
            switch (state)
            {
                case StageRunner.State.Loading: return "준비 중";
                case StageRunner.State.Playing: return "플레이 중";
                case StageRunner.State.Paused: return "일시정지";
                case StageRunner.State.Finished: return "끝남";
                default: return "오류";
            }
        }

        static string Warn(int count) => count == 0 ? "0건" : Colored($"{count}건", MissColor);

        static string Colored(string value, string color) => $"<color={color}>{value}</color>";
    }
}
