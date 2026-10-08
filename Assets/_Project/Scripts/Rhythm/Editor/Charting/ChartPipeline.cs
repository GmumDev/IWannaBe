using System;
using System.Collections.Generic;
using System.Text;
using IWannabe.Rhythm.Charting;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>곡 분석과 채보 생성을 실행하고 결과를 에셋에 기록한다.</summary>
    public static class ChartPipeline
    {
        public static AnalysisResult Analyze(ChartGenerationProfile profile)
        {
            if (profile.song == null || profile.song.Clip == null) throw new InvalidOperationException("SongData에 오디오 클립이 없습니다.");
            if (profile.analysis == null) throw new InvalidOperationException("SongAnalysis 에셋이 지정되지 않았습니다.");

            var clip = profile.song.Clip;
            float[] mono = ReadMonoSamples(clip, out int sampleRate);
            var result = AudioAnalyzer.Analyze(mono, sampleRate, profile.analyzer);

            Undo.RecordObjects(new Object[] { profile.analysis, profile.song }, "Analyze Song");
            profile.analysis.Store(clip, result);
            profile.song.SetBeatMap(result.BeatTimes, result.BeatsPerBar, result.FirstDownbeatIndex, (float)result.Bpm);
            EditorUtility.SetDirty(profile.analysis);
            EditorUtility.SetDirty(profile.song);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ChartPipeline] '{clip.name}' 분석 완료: BPM {result.Bpm:F2}, 비트 {result.BeatTimes.Length}개, " +
                      $"첫 다운비트 {result.FirstDownbeatIndex}, 고정 템포 {result.ConstantTempo}, 구간 강도 {string.Join("", result.BarLevel)}\n" +
                      $"박자 안정성: {result.Stability} {result.Stability.Reason}");
            return result;
        }

        public static GeneratorReport Generate(ChartGenerationProfile profile)
        {
            if (profile.analysis == null || !profile.analysis.HasData) throw new InvalidOperationException("곡 분석 결과가 없습니다. 먼저 곡 분석을 실행하세요.");
            if (profile.output == null) throw new InvalidOperationException("출력할 ChartData가 지정되지 않았습니다.");

            var analysis = profile.analysis.ToResult();
            var chartSegments = new List<ChartSegment>();
            var info = new StringBuilder($"시드 {profile.generator.seed}, 난이도 {profile.generator.difficulty}, {DateTime.Now:yyyy-MM-dd HH:mm}");
            List<PatternInstance> instances;
            GeneratorReport report;

            if (profile.segments.Count == 0)
            {
                if (profile.patterns == null || profile.patterns.Patterns.Count == 0) throw new InvalidOperationException("패턴 라이브러리가 비어 있습니다.");
                var input = GeneratorInput.FromAnalysis(analysis, profile.patterns.Patterns, profile.generator);
                instances = ChartGenerator.Generate(input, out report);
                info.Append('\n').Append(report);
            }
            else
            {
                instances = GenerateSegments(profile, analysis, chartSegments, out report, info);
            }

            Undo.RecordObject(profile.output, "Generate Chart");
            profile.output.SetChart(instances, chartSegments, info.ToString());
            EditorUtility.SetDirty(profile.output);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ChartPipeline] '{profile.output.name}' 생성 완료: {info}");
            return report;
        }

        /// <summary>
        /// 리믹스: 곡 전체를 한 번에 채우되, 패턴은 큐가 들어가는 구간의 미니게임 패턴 라이브러리에서 고른다.
        /// 구간 경계에서 쉬지 않으므로 앞 미니게임의 큐로 시작해 다음 미니게임에서 입력하는 패턴(이어받기)이 생긴다.
        /// </summary>
        static List<PatternInstance> GenerateSegments(ChartGenerationProfile profile, AnalysisResult analysis,
            List<ChartSegment> chartSegments, out GeneratorReport report, StringBuilder info)
        {
            var plans = profile.segments;
            int bars = analysis.BarLevel?.Length ?? 0;
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                string label = $"구간 {i + 1}";
                if (plan == null || plan.minigame == null || string.IsNullOrEmpty(plan.minigame.MinigameId))
                    throw new InvalidOperationException($"{label}: 미니게임(ID 포함)이 지정되지 않았습니다.");
                if (plan.patterns == null || plan.patterns.Patterns.Count == 0)
                    throw new InvalidOperationException($"{label}: 패턴 라이브러리가 비어 있습니다.");
                if (i == 0 && plan.startBar != 0) throw new InvalidOperationException("첫 구간은 0마디에서 시작해야 합니다.");
                if (i > 0 && plan.startBar <= plans[i - 1].startBar) throw new InvalidOperationException($"{label}: 시작 마디가 앞 구간보다 커야 합니다.");
                if (plan.startBar >= bars) throw new InvalidOperationException($"{label}: 시작 마디 {plan.startBar}가 곡 길이({bars}마디)를 넘습니다.");
            }

            var sets = new List<GeneratorPatternSet>();
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                double start = BarToBeat(analysis, plan.startBar);
                sets.Add(new GeneratorPatternSet
                {
                    Patterns = plan.patterns.Patterns,
                    StartBeat = i == 0 ? double.NegativeInfinity : start,
                    EndBeat = i + 1 < plans.Count ? BarToBeat(analysis, plans[i + 1].startBar) : double.PositiveInfinity,
                    Label = plan.minigame.MinigameId,
                });
                chartSegments.Add(new ChartSegment { minigameId = plan.minigame.MinigameId, startBeat = i == 0 ? 0 : start });
            }

            var input = GeneratorInput.FromAnalysis(analysis, null, profile.generator);
            input.PatternSets = sets;
            List<PatternInstance> instances;
            try
            {
                instances = ChartGenerator.Generate(input, out report);
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException(e.Message, e);
            }

            info.Append('\n').Append(report);
            // 플레이 때와 같은 방법으로 전환 시각을 정해 본다(입력과 겹치면 입력 사이로 옮겨진다).
            var timeline = ChartTimeline.Build(instances, chartSegments, new TempoMap(analysis.BeatTimes));
            int totalCarried = 0;
            for (int i = 0; i < plans.Count; i++)
            {
                int patterns = 0, notes = 0, carried = 0;
                foreach (var instance in instances)
                {
                    if (instance.segment != i) continue;
                    patterns++;
                    notes += instance.notes.Count;
                    if (profile.generator.IsCarryOver(LastInputBeat(instance), sets[i].EndBeat)) carried++;
                }
                totalCarried += carried;
                int endBar = i + 1 < plans.Count ? plans[i + 1].startBar : analysis.BarLevel.Length;
                info.Append($"\n{i + 1}. {plans[i].minigame.DisplayName} {plans[i].startBar}~{endBar}마디: 패턴 {patterns}개, 노트 {notes}개");
                if (i > 0)
                {
                    var segment = timeline.Segments[i];
                    double shift = segment.SwitchBeat - segment.StartBeat;
                    info.Append($", 전환 {segment.SwitchBeat:0.##}박");
                    if (Math.Abs(shift) > 1e-3) info.Append($"(입력과 겹쳐 {shift:+0.##;-0.##}박 옮김)");
                    info.Append($"·가장 가까운 입력과 {segment.SwitchClearance * 1000:0}ms");
                }
                if (carried > 0) info.Append($", 다음 미니게임이 이어받는 패턴 {carried}개");
                if (notes == 0) Debug.LogWarning($"[ChartPipeline] 구간 {i + 1}({plans[i].minigame.DisplayName})에 노트가 하나도 없습니다.");
            }
            info.Append($"\n이어받기 {totalCarried}회 / 구간 경계 {plans.Count - 1}곳");
            return instances;
        }

        /// <summary>패턴의 마지막 입력(뗌 포함) 박.</summary>
        static double LastInputBeat(PatternInstance instance)
        {
            double last = double.MinValue;
            foreach (var note in instance.notes)
                last = Math.Max(last, instance.anchorBeat + note.offset + (note.type == NoteType.Hold ? note.holdBeats : 0));
            return last;
        }

        static double BarToBeat(AnalysisResult analysis, int bar) => analysis.FirstDownbeatIndex + bar * analysis.BeatsPerBar;

        /// <summary>
        /// 오디오 클립의 샘플을 모노로 읽는다. 압축 상태로 임포트된 클립은 GetData가 0만 돌려주고,
        /// 손실 압축은 분석 결과를 흔들므로 잠시 무손실 PCM·Decompress On Load로 다시 임포트해 읽고 되돌린다.
        /// </summary>
        public static float[] ReadMonoSamples(AudioClip clip, out int sampleRate)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException($"오디오 임포터를 찾지 못했습니다: {path}");

            var original = importer.defaultSampleSettings;
            bool originalBackground = importer.loadInBackground;
            bool switched = original.loadType != AudioClipLoadType.DecompressOnLoad
                || original.compressionFormat != AudioCompressionFormat.PCM
                || originalBackground;
            if (switched)
            {
                var temporary = original;
                temporary.loadType = AudioClipLoadType.DecompressOnLoad;
                temporary.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = temporary;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }

            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) throw new InvalidOperationException("오디오 샘플을 읽지 못했습니다.");

                int channels = clip.channels;
                var mono = new float[clip.samples];
                bool silent = true;
                for (int i = 0; i < mono.Length; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++) sum += data[i * channels + c];
                    mono[i] = sum / channels;
                    if (silent && mono[i] != 0f) silent = false;
                }
                if (silent)
                    throw new InvalidOperationException("샘플이 모두 0입니다. 현재 플랫폼의 오디오 임포트 오버라이드를 잠시 끄고 다시 분석하세요.");

                sampleRate = clip.frequency;
                return mono;
            }
            finally
            {
                if (switched)
                {
                    importer.defaultSampleSettings = original;
                    importer.loadInBackground = originalBackground;
                    importer.SaveAndReimport();
                }
            }
        }
    }
}
