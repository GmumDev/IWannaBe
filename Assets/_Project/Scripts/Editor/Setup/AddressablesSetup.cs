using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// Addressables 그룹·항목 구성과 중복 검사. 여러 번들(또는 씬)이 함께 쓰는 에셋은 반드시 한 그룹에
    /// 명시적으로 등록해야 번들마다 복사되지 않는다.
    /// </summary>
    static class AddressablesSetup
    {
        public const string MusicGroup = "Music";
        public const string SharedGroup = "Shared";
        public const string CustomizationGroup = "Customization";

        static AddressableAssetSettings Settings => AddressableAssetSettingsDefaultObject.GetSettings(true);

        public static AddressableAssetGroup Group(string name,
            BundledAssetGroupSchema.BundlePackingMode packing = BundledAssetGroupSchema.BundlePackingMode.PackTogether)
        {
            var settings = Settings;
            var group = settings.FindGroup(name)
                ?? settings.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema.BundleMode != packing)
            {
                schema.BundleMode = packing;
                EditorUtility.SetDirty(schema);
            }
            return group;
        }

        /// <summary>에셋을 그룹에 명시적으로 넣고(다른 그룹에 있으면 옮긴다) 주소를 정한다.</summary>
        public static AddressableAssetEntry Register(string assetPath, AddressableAssetGroup group, string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) throw new FileNotFoundException("Addressables에 등록할 에셋이 없습니다.", assetPath);
            var settings = Settings;
            var entry = settings.CreateOrMoveEntry(guid, group, false, true);
            entry.address = address;
            EditorUtility.SetDirty(settings);
            return entry;
        }

        [MenuItem("IWannabe/Setup/Check Duplicate Assets")]
        public static void CheckDuplicatesFromMenu() => CheckDuplicates();

        /// <summary>
        /// 번들끼리, 그리고 빌드 씬과 번들 사이에 같은 에셋이 두 벌 들어가는지 검사해 로그로 남긴다.
        /// 로딩 화면(AppRoot)의 스피너 도형은 씬이 직접 참조하므로 씬·번들 중복으로 잡히는 게 정상이다.
        /// </summary>
        public static void CheckDuplicates()
        {
            var settings = Settings;
            var report = new StringBuilder("[Addressables] 중복 검사");
            int issues = Append(report, "번들끼리", new CheckBundleDupeDependencies().RefreshAnalysis(settings));
            issues += Append(report, "빌드 씬과 번들", new CheckSceneDupeDependencies().RefreshAnalysis(settings));
            if (issues == 0) Debug.Log(report.ToString());
            else Debug.LogWarning(report.ToString());
        }

        static int Append(StringBuilder report, string label, List<AnalyzeRule.AnalyzeResult> results)
        {
            var warnings = results.FindAll(r => r.severity == MessageType.Warning || r.severity == MessageType.Error);
            report.Append($"\n- {label}: {(warnings.Count == 0 ? "중복 없음" : $"{warnings.Count}건")}");
            foreach (var result in warnings)
                report.Append("\n    ").Append(result.resultName.Replace(AnalyzeRule.kDelimiter.ToString(), "  |  "));
            foreach (var result in results)
                if (result.severity == MessageType.None && result.resultName.Contains("failed"))
                    report.Append("\n    (검사 실패) ").Append(result.resultName);
            return warnings.Count;
        }
    }
}
