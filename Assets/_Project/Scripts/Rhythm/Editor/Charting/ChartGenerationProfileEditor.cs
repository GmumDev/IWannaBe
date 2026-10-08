using System;
using UnityEditor;
using UnityEngine;

namespace IWannabe.Rhythm.EditorTools
{
    [CustomEditor(typeof(ChartGenerationProfile))]
    public sealed class ChartGenerationProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (ChartGenerationProfile)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("채보 파이프라인", EditorStyles.boldLabel);

            bool canAnalyze = profile.song != null && profile.song.Clip != null && profile.analysis != null;
            bool canGenerate = profile.analysis != null && profile.analysis.HasData && profile.HasPatterns && profile.output != null;

            using (new EditorGUI.DisabledScope(!canAnalyze))
            {
                if (GUILayout.Button("1. 곡 분석 (비트·다운비트·온셋)"))
                    Run(() => ChartPipeline.Analyze(profile));
            }
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                if (GUILayout.Button("2. 채보 생성"))
                    Run(() => ChartPipeline.Generate(profile));
            }
            using (new EditorGUI.DisabledScope(!canAnalyze || !profile.HasPatterns || profile.output == null))
            {
                if (GUILayout.Button("분석 + 생성 한 번에"))
                    Run(() =>
                    {
                        ChartPipeline.Analyze(profile);
                        ChartPipeline.Generate(profile);
                    });
            }

            if (profile.analysis != null && profile.analysis.HasData)
            {
                var a = profile.analysis;
                EditorGUILayout.HelpBox(
                    $"분석: {a.AnalyzedAt}\nBPM {a.Bpm:F2} · 비트 {a.BeatCount}개 · 첫 다운비트 {a.FirstDownbeatIndex} · 고정 템포 {(a.ConstantTempo ? "예" : "아니오")}\n" +
                    $"마디별 강도(0~2): {string.Join("", a.BarLevel)}",
                    MessageType.None);
            }
            if (profile.output != null && !string.IsNullOrEmpty(profile.output.GenerationInfo))
                EditorGUILayout.HelpBox(profile.output.GenerationInfo, MessageType.None);
        }

        static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("채보 파이프라인", e.Message, "확인");
            }
        }
    }
}
