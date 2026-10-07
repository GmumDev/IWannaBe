using System.IO;
using UnityEditor;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>프로토타입 연출용 기본 도형 스프라이트(원·고리·사각형). 크기 1 유닛으로 임포트한다.</summary>
    public sealed class ShapeSprites
    {
        public Sprite Circle;
        public Sprite Ring;
        public Sprite Square;

        public static ShapeSprites Ensure(string folder)
        {
            return new ShapeSprites
            {
                Circle = EnsureSprite($"{folder}/Circle.png", 256, (x, y, r) => Edge(r - Distance(x, y, r))),
                Ring = EnsureSprite($"{folder}/Ring.png", 256, (x, y, r) => Edge(14f - Mathf.Abs(Distance(x, y, r) - (r - 16f)))),
                Square = EnsureSprite($"{folder}/Square.png", 32, (x, y, r) => 1f),
            };
        }

        static float Distance(int x, int y, float r) => Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));

        static float Edge(float insideDistance) => Mathf.Clamp01(insideDistance / 1.5f);

        static Sprite EnsureSprite(string path, int size, System.Func<int, int, float, float> alpha)
        {
            if (!File.Exists(path))
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float radius = size / 2f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha(x, y, radius) * 255f));
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return SetupUtil.LoadSprite(path, size);
        }
    }
}
