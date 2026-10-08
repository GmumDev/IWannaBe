using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IWannabe.Rhythm.Charting;
using UnityEditor;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    /// <summary>
    /// 채보 품질 리포트. 분석기와 생성기가 여러 곡에서 쓸 만한 결과를 내는지, 박자 안정성 판정이 맞는지 본다.
    /// <list type="number">
    /// <item>합성 곡(<see cref="SyntheticSongs"/>): 정답 비트와 비교해 비트 정확도와 판정이 기대대로인지.</item>
    /// <item>테스트 곡(<see cref="SongFolder"/>의 오디오, git에 올리지 않음): 판정과 지표, 받아치기·베기 생성 설정으로 만든 채보의 지표.
    /// 곡마다 비트 클릭(마디 첫 박은 높은 소리)과 채보 클릭을 얹은 WAV를 남겨 귀로 확인할 수 있게 한다.</item>
    /// </list>
    /// 결과는 <see cref="OutputFolder"/>/report.md와 [ChartQuality] 로그에 남는다.
    /// </summary>
    [InitializeOnLoad]
    static class ChartQualityReport
    {
        // 이 파일이 있으면 다음 스크립트 컴파일 직후 한 번 실행한다. 내용에 export-pcm이 있으면 테스트 곡의 모노 PCM도 남긴다.
        const string TriggerFile = "Temp/IWannabe.ChartQuality";
        const string SongFolder = "Assets/_Local/TestSongs";
        const string OutputFolder = "Reports/ChartQuality";
        /// <summary>채보 노트가 이 온셋 강도(0~1) 이상인 칸에 있으면 소리가 있는 곳에 놓인 것으로 센다(생성기의 minNoteStrength와 같음).</summary>
        const float OnsetStrength = 0.12f;
        const float StrongOnsetStrength = 0.3f;

        sealed class ChartStats
        {
            public string Minigame;
            public int Notes;
            public int Holds;
            public double InputsPerMinute;
            public double OnOnset;
            public double OnStrongOnset;
            public string Phrases;
            public string Error;
        }

        static ChartQualityReport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(TriggerFile)) return;
            string tokens = File.ReadAllText(TriggerFile);
            File.Delete(TriggerFile);
            EditorApplication.delayCall += () => Run(tokens.Contains("export-pcm"));
        }

        [MenuItem("IWannabe/Test/Chart Quality Report")]
        static void RunFromMenu() => Run(false);

        static void Run(bool exportPcm)
        {
            var started = DateTime.Now;
            var md = new StringBuilder();
            md.AppendLine("# 채보 품질 리포트").AppendLine();
            md.AppendLine($"{started:yyyy-MM-dd HH:mm}, 분석기 기본 설정. 지표 뜻은 `BeatStability`·`BeatAccuracy` 주석에 있다.").AppendLine();
            try
            {
                Directory.CreateDirectory(OutputFolder);
                string synthetic = RunSynthetic(md);
                string songs = RunSongs(md, exportPcm);
                File.WriteAllText(Path.Combine(OutputFolder, "report.md"), md.ToString(), new UTF8Encoding(false));
                Debug.Log($"[ChartQuality] 끝({(DateTime.Now - started).TotalSeconds:0}초): {synthetic}, {songs}. 리포트 {Path.GetFullPath(OutputFolder)}/report.md");
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[ChartQuality] 취소함");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[ChartQuality] 끝 - 오류");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ───────────────────────── 합성 곡 ─────────────────────────

        static string RunSynthetic(StringBuilder md)
        {
            Progress("합성 곡 만드는 중", 0);
            var songs = SyntheticSongs.CreateAll();
            md.AppendLine("## 합성 곡").AppendLine();
            md.AppendLine("정답 비트를 아는 곡. ±20ms는 실제 박 중 20ms 안에 찾은 비트가 있는 비율, 오차는 짝지은 박의 평균 절대 오차다.").AppendLine();
            md.AppendLine("| 곡 | 설명 | 기대 | 판정 | BPM 비 | ±20ms | 오차 | 다운비트 | 지표 | 이유 |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            int matched = 0;
            for (int i = 0; i < songs.Count; i++)
            {
                var song = songs[i];
                Progress($"합성 곡 분석: {song.Id}", (i + 1f) / songs.Count);
                string expected = song.Expected + (song.ExpectTripleMeter ? " (3박)" : "");
                try
                {
                    var result = AudioAnalyzer.Analyze(song.Samples, song.SampleRate, new AnalyzerSettings());
                    var s = result.Stability;
                    bool triple = (s.Flags & BeatStabilityFlags.TripleMeter) != 0;
                    bool ok = s.Verdict == song.Expected && triple == song.ExpectTripleMeter;
                    if (ok) matched++;
                    string accuracy = "| - | - | - | - ";
                    if (song.BeatTimes.Length > 0)
                    {
                        var a = BeatAccuracy.Measure(result.BeatTimes, result.FirstDownbeatIndex, result.BeatsPerBar,
                            song.BeatTimes, song.FirstDownbeatIndex, song.BeatsPerBar);
                        accuracy = $"| {a.BpmRatio:0.00} | {a.Within20ms:P0} | {a.MeanAbsError * 1000:0.0}ms | {a.DownbeatMatch:P0} ";
                    }
                    md.AppendLine($"| {song.Id} | {song.Description} | {expected} | {(ok ? "" : "**")}{s.Verdict}{(triple ? " (3박)" : "")}{(ok ? "" : "**")} " +
                                  $"{accuracy}| {Metrics(s)} | {s.Reason} |");
                    Debug.Log($"[ChartQuality] 합성 {song.Id}: 기대 {expected}, {s} {(ok ? "" : "← 기대와 다름")}");
                }
                catch (InvalidOperationException e)
                {
                    bool ok = song.Expected == BeatStabilityVerdict.Unstable;
                    if (ok) matched++;
                    md.AppendLine($"| {song.Id} | {song.Description} | {expected} | 분석 실패 | - | - | - | - | - | {e.Message} |");
                    Debug.Log($"[ChartQuality] 합성 {song.Id}: 분석 실패 {e.Message}");
                }
            }
            md.AppendLine().AppendLine($"기대대로 {matched}/{songs.Count}").AppendLine();
            return $"합성 곡 {matched}/{songs.Count} 기대대로";
        }

        // ───────────────────────── 테스트 곡 ─────────────────────────

        static string RunSongs(StringBuilder md, bool exportPcm)
        {
            md.AppendLine("## 테스트 곡").AppendLine();
            if (!AssetDatabase.IsValidFolder(SongFolder))
            {
                md.AppendLine($"`{SongFolder}` 폴더가 없다.").AppendLine();
                return "테스트 곡 없음";
            }

            var profiles = SingleMinigameProfiles();
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SongFolder })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);

            var songRows = new StringBuilder();
            var chartRows = new StringBuilder();
            var verdicts = new Dictionary<string, int>();
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                string id = $"{i + 1:00}_{ShortName(path)}";
                Progress($"테스트 곡 분석: {id}", (i + 0.5f) / paths.Count);
                try
                {
                    var clip = PrepareClip(path);
                    float[] mono = ChartPipeline.ReadMonoSamples(clip, out int rate);
                    if (exportPcm) AudioFiles.WriteWav(Path.Combine(OutputFolder, "pcm", id + ".wav"), mono, rate);
                    double length = mono.Length / (double)rate;

                    AnalysisResult analysis;
                    try
                    {
                        analysis = AudioAnalyzer.Analyze(mono, rate, new AnalyzerSettings());
                    }
                    catch (InvalidOperationException e)
                    {
                        Count(verdicts, "분석 실패");
                        songRows.AppendLine($"| {id} | {length:0}초 | - | - | 분석 실패 | - | {e.Message} |");
                        Debug.Log($"[ChartQuality] {id}: 분석 실패 {e.Message}");
                        continue;
                    }

                    var s = analysis.Stability;
                    Count(verdicts, s.Verdict.ToString());
                    songRows.AppendLine($"| {id} | {length:0}초 | {analysis.Bpm:0.0} | {(analysis.ConstantTempo ? "고정" : "가변")} | {s.Verdict}" +
                                        $"{(s.Flags != BeatStabilityFlags.None ? $" ({s.Flags})" : "")} | {Metrics(s)} | {s.Reason} |");
                    Debug.Log($"[ChartQuality] {id}: BPM {analysis.Bpm:0.0}, {(analysis.ConstantTempo ? "고정" : "가변")} 템포, {s}");

                    var tempo = new TempoMap(analysis.BeatTimes);
                    var beatClicks = new List<(double, ClickKind)>();
                    for (int k = 0; k < analysis.BeatTimes.Length; k++)
                    {
                        bool downbeat = k >= analysis.FirstDownbeatIndex && (k - analysis.FirstDownbeatIndex) % analysis.BeatsPerBar == 0;
                        beatClicks.Add((analysis.BeatTimes[k], downbeat ? ClickKind.Downbeat : ClickKind.Beat));
                    }
                    WriteOverlay(id + "_beats", mono, rate, beatClicks);

                    foreach (var profile in profiles)
                    {
                        var stats = MeasureChart(profile, analysis, tempo, length, out var noteClicks);
                        chartRows.AppendLine(stats.Error != null
                            ? $"| {id} | {stats.Minigame} | 생성 실패: {stats.Error} | | | | |"
                            : $"| {id} | {stats.Minigame} | {stats.Notes} (홀드 {stats.Holds}) | {stats.InputsPerMinute:0} | {stats.OnOnset:P0} | {stats.OnStrongOnset:P0} | {stats.Phrases} |");
                        if (noteClicks != null && profile == profiles[0]) WriteOverlay($"{id}_{stats.Minigame}", mono, rate, noteClicks);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Count(verdicts, "오류");
                    songRows.AppendLine($"| {id} | - | - | - | 오류 | - | {e.Message} |");
                    Debug.LogException(e);
                }
            }

            md.AppendLine($"`{SongFolder}`의 {paths.Count}곡.").AppendLine();
            md.AppendLine("| 곡 | 길이 | BPM | 템포 | 판정 | 지표 | 이유 |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            md.Append(songRows).AppendLine();

            md.AppendLine("### 채보").AppendLine();
            md.AppendLine("스테이지의 채보 생성 프로필(패턴 라이브러리·생성 설정) 그대로 만들었다. 온셋 위는 누름이 온셋 강도 " +
                          $"{OnsetStrength} 이상인 칸에 있는 비율, 강한 온셋은 {StrongOnsetStrength} 이상이다.").AppendLine();
            md.AppendLine("| 곡 | 미니게임 | 노트 | 분당 입력 | 온셋 위 | 강한 온셋 위 | 활성 구절 |");
            md.AppendLine("|---|---|---|---|---|---|---|");
            md.Append(chartRows).AppendLine();

            md.AppendLine("### 듣기 파일").AppendLine();
            md.AppendLine("`songs/*_beats.wav`: 음악 + 비트 클릭(마디 첫 박은 높은 소리). " +
                          $"`songs/*_{(profiles.Count > 0 ? profiles[0].name.Replace("_ChartProfile", "").ToLowerInvariant() : "chart")}.wav`: 음악 + 채보 누름(높은 소리)·홀드 뗌(작은 소리). " +
                          "용량을 줄이려고 모노·반 샘플레이트로 남긴다.").AppendLine();

            var summary = new StringBuilder($"테스트 곡 {paths.Count}개");
            foreach (var pair in verdicts) summary.Append($" {pair.Key} {pair.Value}");
            return summary.ToString();
        }

        /// <summary>미니게임 하나짜리 스테이지의 채보 생성 프로필(받아치기, 베기). 리믹스는 구간 마디가 곡마다 달라 뺀다.</summary>
        static List<ChartGenerationProfile> SingleMinigameProfiles()
        {
            var list = new List<ChartGenerationProfile>();
            foreach (var guid in AssetDatabase.FindAssets("t:ChartGenerationProfile"))
            {
                var profile = AssetDatabase.LoadAssetAtPath<ChartGenerationProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile != null && profile.segments.Count == 0 && profile.patterns != null && profile.patterns.Patterns.Count > 0) list.Add(profile);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        /// <summary>테스트 곡은 무손실 PCM·Decompress On Load로 둔다(분석 때마다 다시 임포트하지 않게). 로컬 전용 폴더라 바꿔도 된다.</summary>
        static AudioClip PrepareClip(string path)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad || settings.compressionFormat != AudioCompressionFormat.PCM || importer.loadInBackground)
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static ChartStats MeasureChart(ChartGenerationProfile profile, AnalysisResult analysis, TempoMap tempo, double length,
            out List<(double, ClickKind)> clicks)
        {
            var stats = new ChartStats { Minigame = profile.name.Replace("_ChartProfile", "").ToLowerInvariant() };
            clicks = null;
            List<PatternInstance> instances;
            GeneratorReport report;
            try
            {
                instances = ChartGenerator.Generate(GeneratorInput.FromAnalysis(analysis, profile.patterns.Patterns, profile.generator), out report);
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                stats.Error = e.Message;
                return stats;
            }

            clicks = new List<(double, ClickKind)>();
            int presses = 0, onOnset = 0, onStrong = 0;
            foreach (var instance in instances)
            {
                foreach (var note in instance.notes)
                {
                    double beat = instance.anchorBeat + note.offset;
                    stats.Notes++;
                    presses++;
                    clicks.Add((tempo.BeatToTime(beat), ClickKind.Press));
                    float strength = GridAt(analysis.GridFull, analysis.GridPerBeat, beat);
                    if (strength >= OnsetStrength) onOnset++;
                    if (strength >= StrongOnsetStrength) onStrong++;
                    if (note.type == NoteType.Hold)
                    {
                        stats.Holds++;
                        clicks.Add((tempo.BeatToTime(beat + note.holdBeats), ClickKind.Release));
                    }
                }
            }
            stats.InputsPerMinute = presses / (length / 60);
            stats.OnOnset = presses > 0 ? onOnset / (double)presses : 0;
            stats.OnStrongOnset = presses > 0 ? onStrong / (double)presses : 0;
            stats.Phrases = $"{report.ActivePhraseCount}/{report.PhraseCount}";
            return stats;
        }

        static float GridAt(float[] grid, int perBeat, double beat)
        {
            int index = (int)Math.Round(beat * perBeat);
            return index >= 0 && index < grid.Length ? grid[index] : 0f;
        }

        static void WriteOverlay(string name, float[] mono, int rate, List<(double, ClickKind)> clicks)
        {
            var mixed = AudioFiles.Overlay(mono, rate, clicks, out int outputRate);
            AudioFiles.WriteWav(Path.Combine(OutputFolder, "songs", name + ".wav"), mixed, outputRate);
        }

        static string Metrics(BeatStability s) =>
            string.Format(CultureInfo.InvariantCulture,
                "비트 격자 {0:P0}/{1:0}ms, 온셋 격자 {2:P0}/{3:0}ms, 흔들림 {4:0.0}ms, 템포 폭 {5:P1}·변화 {6:P1}, 박 대비 {7:0.00}, 구간 {8:P0}, 3박 {9:0.0}",
                s.GridInlierRatio, s.GridErrorP90 * 1000, s.OnsetGridRatio, s.OnsetGridErrorP90 * 1000, s.LocalJitter * 1000, s.TempoRange, s.TempoJump,
                s.BeatContrast, s.Coverage, s.TripleMeterScore);

        /// <summary>파일 이름에서 숫자 접두·접미와 "no-copyright" 같은 꼬리표를 떼어 짧게 만든다.</summary>
        static string ShortName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            name = Regex.Replace(name, @"^\d+-", "");
            name = Regex.Replace(name, @"-\d+$", "");
            name = Regex.Replace(name, @"-?no-copyright(-music)?", "");
            name = Regex.Replace(name, @"[^a-z0-9_-]+", "-").Trim('-');
            return name.Length > 32 ? name.Substring(0, 32).TrimEnd('-') : name;
        }

        static void Count(Dictionary<string, int> counts, string key) => counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

        static void Progress(string message, float progress)
        {
            if (EditorUtility.DisplayCancelableProgressBar("채보 품질 리포트", message, progress)) throw new OperationCanceledException();
        }
    }
}
