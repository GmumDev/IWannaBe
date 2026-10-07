using IWannabe.Otamaton;
using UnityEngine;
using UnityEngine.EventSystems;

namespace IWannabe.App
{
    /// <summary>
    /// 로비의 플레이어 캐릭터. 장착한 모습을 따라가며, 평소엔 입을 다물고 있다가
    /// 캐릭터를 누르고 있는 동안(클릭·터치) 입을 벌린다.
    /// </summary>
    public sealed class LobbyCharacter : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] OtamatonImage view;

        int pressedPointers;

        void OnEnable()
        {
            OtamatonAppearance.Changed += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            OtamatonAppearance.Changed -= Refresh;
            pressedPointers = 0;
            view.MouthOpen = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressedPointers++;
            view.MouthOpen = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressedPointers = Mathf.Max(0, pressedPointers - 1);
            if (pressedPointers == 0) view.MouthOpen = false;
        }

        void Refresh() => view.Show(OtamatonAppearance.Body, OtamatonAppearance.Eyes);
    }
}
