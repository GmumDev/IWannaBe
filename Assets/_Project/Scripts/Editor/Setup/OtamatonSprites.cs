using IWannabe.Otamaton;
using UnityEngine;

namespace IWannabe.EditorTools
{
    /// <summary>
    /// 오타마톤 파츠 이미지 한 벌. 폴더 안 파일 이름 규칙:
    /// Body_{이름}_close/open, Eye_{이름}_close/open, Hit_Eye_{이름}_close/open (.png).
    /// 256px 이미지가 2유닛(기존 캐릭터 몸통 크기)이 되도록 스프라이트로 임포트한다.
    /// </summary>
    public sealed class OtamatonSprites
    {
        const float PixelsPerUnit = 128f;

        public OtamatonBody Body;
        public OtamatonEyes Eyes;

        public static OtamatonSprites Ensure(string folder, string name)
        {
            return new OtamatonSprites
            {
                Body = new OtamatonBody
                {
                    close = Load(folder, $"Body_{name}_close"),
                    open = Load(folder, $"Body_{name}_open"),
                },
                Eyes = new OtamatonEyes
                {
                    close = Load(folder, $"Eye_{name}_close"),
                    open = Load(folder, $"Eye_{name}_open"),
                    hitClose = Load(folder, $"Hit_Eye_{name}_close"),
                    hitOpen = Load(folder, $"Hit_Eye_{name}_open"),
                },
            };
        }

        static Sprite Load(string folder, string file) => SetupUtil.LoadSprite($"{folder}/{file}.png", PixelsPerUnit);
    }
}
