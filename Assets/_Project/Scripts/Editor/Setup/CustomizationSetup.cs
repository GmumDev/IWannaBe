using System.Collections.Generic;
using System.IO;
using IWannabe.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static IWannabe.EditorTools.SetupUtil;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 꾸미기 카탈로그와 Customization 그룹을 맞춘다.
    /// 오타마톤 파츠는 Textures/Otamaton에서 파일 이름 규칙으로 찾아 카탈로그에 없는 것만 더하고,
    /// 배경·BGM은 목록이 비어 있을 때만 기본 항목을 채운다. 손으로 고친 항목은 덮어쓰지 않는다.
    /// </summary>
    static class CustomizationSetup
    {
        const string DefaultPartName = "default";

        /// <summary>카탈로그를 맞추고 Addressables에 등록한 뒤 카탈로그의 GUID를 돌려준다.</summary>
        public static string Build(IReadOnlyList<StageRecipe> recipes)
        {
            EnsureFolder(SetupPaths.CustomizationData);
            var catalog = LoadOrCreate<CustomizationCatalog>(SetupPaths.CustomizationCatalog);
            AddMissingParts(catalog);
            if (catalog.Items(WardrobeSlot.Background).Count == 0) AddDefaultBackgrounds(catalog, recipes);
            if (catalog.Items(WardrobeSlot.Bgm).Count == 0) AddDefaultBgms(catalog, recipes);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // 파츠 이미지는 스테이지 프리팹(기본 모습)과 카탈로그가 함께 쓰므로 이 그룹에 명시적으로 넣어
            // 스테이지 번들마다 복사되지 않게 한다.
            var group = AddressablesSetup.Group(AddressablesSetup.CustomizationGroup);
            foreach (var path in Directory.GetFiles(SetupPaths.Otamaton, "*.png", SearchOption.AllDirectories))
            {
                string assetPath = path.Replace('\\', '/');
                string address = "Otamaton/" + assetPath.Substring(SetupPaths.Otamaton.Length + 1).Replace(".png", string.Empty);
                AddressablesSetup.Register(assetPath, group, address);
            }
            return AddressablesSetup.Register(SetupPaths.CustomizationCatalog, group, "Customization/Catalog").guid;
        }

        static void AddMissingParts(CustomizationCatalog catalog)
        {
            foreach (var (folder, name) in FindParts(OtamatonSprites.BodyPrefix))
                if (!catalog.Contains(WardrobeSlot.Body, name))
                    catalog.Add(new BodyItem { id = name, displayName = DisplayName(name), parts = OtamatonSprites.LoadBody(folder, name) });

            foreach (var (folder, name) in FindParts(OtamatonSprites.EyePrefix))
                if (!catalog.Contains(WardrobeSlot.Eyes, name))
                    catalog.Add(new EyesItem { id = name, displayName = DisplayName(name), parts = OtamatonSprites.LoadEyes(folder, name) });
        }

        /// <summary>{prefix}{이름}_close.png를 찾아 (폴더, 이름)을 돌려준다. 기본 파츠가 맨 앞에 와서 처음 장착값이 된다.</summary>
        static List<(string folder, string name)> FindParts(string prefix)
        {
            var parts = new List<(string folder, string name)>();
            foreach (var path in Directory.GetFiles(SetupPaths.Otamaton, $"{prefix}*{OtamatonSprites.ClosedSuffix}.png", SearchOption.AllDirectories))
            {
                string file = Path.GetFileNameWithoutExtension(path);
                string name = file.Substring(prefix.Length, file.Length - prefix.Length - OtamatonSprites.ClosedSuffix.Length);
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!parts.Exists(p => p.name == name)) parts.Add((folder, name));
            }
            parts.Sort((a, b) => a.name == DefaultPartName ? -1 : b.name == DefaultPartName ? 1 : string.CompareOrdinal(a.name, b.name));
            return parts;
        }

        static string DisplayName(string partName) => partName == DefaultPartName ? "기본" : partName;

        /// <summary>임시 배경: 단색 3종. 로비 글자가 진한 색이라 밝은 색만 쓴다. 마지막 것은 마지막 스테이지를 깨면 열린다.</summary>
        static void AddDefaultBackgrounds(CustomizationCatalog catalog, IReadOnlyList<StageRecipe> recipes)
        {
            catalog.Add(new BackgroundItem { id = "cream", displayName = "크림", color = Hex("FDF0D5") });
            catalog.Add(new BackgroundItem { id = "sky", displayName = "하늘", color = Hex("D6EAF8") });
            catalog.Add(new BackgroundItem
            {
                id = "sunset", displayName = "노을", color = Hex("F8D3C5"),
                unlockStageId = recipes.Count > 0 ? recipes[recipes.Count - 1].StageId : null,
            });
        }

        /// <summary>임시 BGM: 무음(기본)과 각 스테이지 곡(그 스테이지를 깨면 열림). 곡은 Music 그룹의 것을 주소로 가리킨다.</summary>
        static void AddDefaultBgms(CustomizationCatalog catalog, IReadOnlyList<StageRecipe> recipes)
        {
            catalog.Add(new BgmItem { id = "none", displayName = "없음", clip = new AssetReferenceT<AudioClip>(string.Empty) });
            foreach (var recipe in recipes)
            {
                catalog.Add(new BgmItem
                {
                    id = recipe.StageId,
                    displayName = recipe.DisplayName,
                    unlockStageId = recipe.StageId,
                    clip = new AssetReferenceT<AudioClip>(AssetDatabase.AssetPathToGUID(recipe.MusicPath)),
                });
            }
        }
    }
}
