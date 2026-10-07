using System;
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
                      $"첫 다운비트 {result.FirstDownbeatIndex}, 고정 템포 {result.ConstantTempo}, 구간 강도 {string.Join("", result.BarLevel)}");
            return result;
        }

        public static GeneratorReport Generate(ChartGenerationProfile profile)
        {
            if (profile.analysis == null || !profile.analysis.HasData) throw new InvalidOperationException("곡 분석 결과가 없습니다. 먼저 곡 분석을 실행하세요.");
            if (profile.patterns == null || profile.patterns.Patterns.Count == 0) throw new InvalidOperationException("패턴 라이브러리가 비어 있습니다.");
            if (profile.output == null) throw new InvalidOperationException("출력할 ChartData가 지정되지 않았습니다.");

            var input = GeneratorInput.FromAnalysis(profile.analysis.ToResult(), profile.patterns.Patterns, profile.generator);
            var instances = ChartGenerator.Generate(input, out var report);

            Undo.RecordObject(profile.output, "Generate Chart");
            string info = $"시드 {profile.generator.seed}, 난이도 {profile.generator.difficulty}, {DateTime.Now:yyyy-MM-dd HH:mm}\n{report}";
            profile.output.SetPatterns(instances, info);
            EditorUtility.SetDirty(profile.output);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ChartPipeline] '{profile.output.name}' 생성 완료: {report}");
            return report;
        }

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
