using UnityEngine;
using UnityEngine.UI;

namespace IWannabe.Otamaton
{
    /// <summary>
    /// UI(Canvas)에 그리는 오타마톤. 몸통 위에 눈을 겹친다(눈 Image가 몸통보다 뒤 형제라 항상 앞).
    /// 로비 캐릭터와 꾸미기 아이콘에 쓴다. 이미지가 없는 파츠는 감춘다(빈 Image는 흰 사각형으로 보이므로).
    /// </summary>
    public sealed class OtamatonImage : MonoBehaviour
    {
        [SerializeField] Image bodyImage;
        [SerializeField] Image eyeImage;

        OtamatonBody body;
        OtamatonEyes eyes;
        bool mouthOpen;

        public void Show(OtamatonBody body, OtamatonEyes eyes)
        {
            this.body = body;
            this.eyes = eyes;
            Refresh();
        }

        public bool MouthOpen
        {
            get => mouthOpen;
            set
            {
                mouthOpen = value;
                Refresh();
            }
        }

        void Refresh()
        {
            SetSprite(bodyImage, body?.Pick(mouthOpen));
            SetSprite(eyeImage, eyes?.Pick(mouthOpen, false));
        }

        static void SetSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }
}
