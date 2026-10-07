using UnityEngine;
using UnityEngine.UI;

namespace IWannabe.App
{
    public enum StageAvailability
    {
        Locked,
        Open,
        Cleared,
    }

    /// <summary>로비의 스테이지 진입 버튼 하나. 이름과 해금 상태를 보여 준다.</summary>
    public sealed class StageButtonView : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] Text titleText;
        [SerializeField] Text statusText;
        [SerializeField] Color lockedTint = new Color(1f, 1f, 1f, 0.45f);

        public Button Button => button;

        public void Bind(string title, StageAvailability availability, string lockedHint)
        {
            titleText.text = title;
            button.interactable = availability != StageAvailability.Locked;
            switch (availability)
            {
                case StageAvailability.Cleared:
                    statusText.text = "클리어";
                    break;
                case StageAvailability.Open:
                    statusText.text = "도전 가능";
                    break;
                default:
                    statusText.text = lockedHint;
                    break;
            }
            var titleColor = titleText.color;
            titleColor.a = availability == StageAvailability.Locked ? lockedTint.a : 1f;
            titleText.color = titleColor;
        }
    }
}
