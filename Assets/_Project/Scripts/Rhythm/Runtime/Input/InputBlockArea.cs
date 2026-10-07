using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 화면 전체가 버튼인 스테이지에서, 이 UI 영역을 누른 터치·클릭은 리듬 입력으로 치지 않는다.
    /// 일시정지 버튼처럼 플레이 중 눌러야 하는 UI에 붙인다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class InputBlockArea : MonoBehaviour
    {
        static readonly List<InputBlockArea> Active = new List<InputBlockArea>();

        RectTransform rect;
        Canvas canvas;

        void Awake()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public static bool Contains(Vector2 screenPoint)
        {
            foreach (var area in Active)
                if (area.ContainsPoint(screenPoint)) return true;
            return false;
        }

        bool ContainsPoint(Vector2 screenPoint)
        {
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active.Clear();
    }
}
