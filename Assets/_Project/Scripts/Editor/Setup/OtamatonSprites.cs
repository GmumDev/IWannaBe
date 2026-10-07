using IWannabe.Otamaton;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 오타마톤 파츠 이미지. 폴더 안 파일 이름 규칙:
    /// Body_{이름}_close/open, Eye_{이름}_close/open, Hit_Eye_{이름}_close/open (.png).
    /// 256px 이미지가 2유닛(기존 캐릭터 몸통 크기)이 되도록 스프라이트로 임포트한다.
    /// </summary>
    public sealed class OtamatonSprites
    {
        public const string BodyPrefix = "Body_";
        public const string EyePrefix = "Eye_";
        public const string ClosedSuffix = "_close";
        const float PixelsPerUnit = 128f;

        public OtamatonBody Body;
        public OtamatonEyes Eyes;

        public static OtamatonSprites Ensure(string folder, string name)
        {
            return new OtamatonSprites { Body = LoadBody(folder, name), Eyes = LoadEyes(folder, name) };
        }

        public static OtamatonBody LoadBody(string folder, string name)
        {
            return new OtamatonBody
            {
                close = Load(folder, $"{BodyPrefix}{name}_close"),
                open = Load(folder, $"{BodyPrefix}{name}_open"),
            };
        }

        public static OtamatonEyes LoadEyes(string folder, string name)
        {
            return new OtamatonEyes
            {
                close = Load(folder, $"{EyePrefix}{name}_close"),
                open = Load(folder, $"{EyePrefix}{name}_open"),
                hitClose = Load(folder, $"Hit_{EyePrefix}{name}_close"),
                hitOpen = Load(folder, $"Hit_{EyePrefix}{name}_open"),
            };
        }

        static Sprite Load(string folder, string file) => SetupUtil.LoadSprite($"{folder}/{file}.png", PixelsPerUnit);
    }
}
