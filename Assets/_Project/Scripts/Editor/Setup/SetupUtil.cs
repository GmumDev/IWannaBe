using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IWannabe.EditorTools
{
    /// <summary>스테이지 구성에 쓰는 고정 경로.</summary>
    static class SetupPaths
    {
        public const string Root = "Assets/_Project";
        public const string Music = Root + "/Music";
        public const string Sfx = Root + "/SFX";
        public const string Shapes = Root + "/Textures/Shapes";
        public const string Data = Root + "/Data";
        public const string StageData = Data + "/Stages";
        public const string StagePrefabs = Root + "/Prefabs/Stages";
        public const string AppPrefabs = Root + "/Prefabs/App";
        public const string Scenes = Root + "/Scenes";
        public const string Settings = Data + "/RhythmSettings.asset";
        public const string Catalog = Data + "/StageCatalog.asset";
        public const string AppRoot = AppPrefabs + "/AppRoot.prefab";
        public const string LobbyScene = Scenes + "/Lobby.unity";
        public const string StageScene = Scenes + "/StagePlay.unity";
        public const string SpriteMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
    }

    /// <summary>에셋·프리팹을 코드로 만들 때 쓰는 공용 함수.</summary>
    static class SetupUtil
    {
        public static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (!created) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static T LoadOrCreate<T>(string path) where T : ScriptableObject => LoadOrCreate<T>(path, out _);

        public static T LoadRequired<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException($"{typeof(T).Name} 에셋을 찾지 못했습니다.", path);
            return asset;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException($"잘못된 폴더 경로: {path}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>private [SerializeField] 필드에 참조를 넣는다. 필드 이름이 틀리면 바로 예외로 알린다.</summary>
        public static void Assign(Object target, params (string field, Object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var property = serialized.FindProperty(field);
                if (property == null) throw new InvalidOperationException($"{target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Transform Node(Transform parent, string name, Vector2 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            return node;
        }

        public static SpriteRenderer Shape(Transform parent, string name, Sprite sprite, Material material, Vector2 position, Vector2 scale, Color color, int order)
        {
            var node = Node(parent, name, position);
            node.localScale = new Vector3(scale.x, scale.y, 1f);
            var renderer = node.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (material != null) renderer.sharedMaterial = material;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var color) ? color : Color.magenta;

        public static Color Hex(string hex, float alpha)
        {
            var color = Hex(hex);
            color.a = alpha;
            return color;
        }
    }
}
