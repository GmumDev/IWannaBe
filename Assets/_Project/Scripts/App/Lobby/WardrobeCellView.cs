using IWannabe.Otamaton;
using UnityEngine;
using UnityEngine.UI;

namespace IWannabe.App
{
    /// <summary>
    /// 꾸미기 그리드의 칸 하나. 항목 종류에 맞는 아이콘(오타마톤 미리보기·배경 색·BGM 표시)과 이름을 보여 주고,
    /// 장착 중이면 테두리를, 잠겼으면 흐린 모습과 해금 조건을 보여 준다.
    /// </summary>
    public sealed class WardrobeCellView : MonoBehaviour
    {
        const string BgmGlyph = "♪";
        const string SilenceGlyph = "-";

        [SerializeField] Button button;
        [SerializeField] GameObject equippedFrame;
        [SerializeField] CanvasGroup content;
        [SerializeField] OtamatonImage otamatonIcon;
        [SerializeField] Image icon;
        [SerializeField] Image swatch;
        [SerializeField] Text glyph;
        [SerializeField] Text nameText;
        [SerializeField, Range(0f, 1f)] float lockedAlpha = 0.4f;

        public Button Button => button;
        public WardrobeSlot Slot { get; private set; }
        public CustomizationItem Item { get; private set; }

        public void Bind(WardrobeSlot slot, CustomizationItem item)
        {
            Slot = slot;
            Item = item;
        }

        /// <summary>장착 상태에 맞춰 다시 그린다. 오타마톤 파츠는 지금 장착한 나머지 파츠와 합쳐 보여 준다.</summary>
        public void Refresh(bool equipped, bool unlocked, string lockText, OtamatonBody wornBody, OtamatonEyes wornEyes)
        {
            equippedFrame.SetActive(equipped);
            content.alpha = unlocked ? 1f : lockedAlpha;
            nameText.text = unlocked ? Item.displayName : lockText;

            bool showOtamaton = Item is BodyItem || Item is EyesItem;
            otamatonIcon.gameObject.SetActive(showOtamaton);
            if (showOtamaton)
            {
                var body = Item is BodyItem bodyItem ? bodyItem.parts : wornBody;
                var eyes = Item is EyesItem eyesItem ? eyesItem.parts : wornEyes;
                otamatonIcon.Show(body, eyes);
            }

            bool showIcon = !showOtamaton && Item.icon != null;
            icon.gameObject.SetActive(showIcon);
            if (showIcon) icon.sprite = Item.icon;

            bool showSwatch = !showOtamaton && !showIcon && Item is BackgroundItem;
            swatch.gameObject.SetActive(showSwatch);
            if (showSwatch) swatch.color = ((BackgroundItem)Item).color;

            glyph.gameObject.SetActive(!showOtamaton && !showIcon && !showSwatch);
            if (Item is BgmItem bgm)
                glyph.text = bgm.clip != null && bgm.clip.RuntimeKeyIsValid() ? BgmGlyph : SilenceGlyph;
        }
    }
}
