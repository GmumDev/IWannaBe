using UnityEngine;

namespace IWannabe.Stages
{
    /// <summary>
    /// 12시 방향에서 시계 방향으로 차오르는 링. 홀드처럼 "언제 떼야 하는지"를 보여 줄 때 쓴다.
    /// 흐린 바탕 원(track) 위에 진행만큼의 호(fill)를 그린다.
    /// </summary>
    public sealed class ProgressRing : MonoBehaviour
    {
        [SerializeField] LineRenderer track;
        [SerializeField] LineRenderer fill;
        [SerializeField, Min(8)] int segments = 64;
        [SerializeField, Min(0.05f)] float radius = 0.85f;

        public bool Visible => gameObject.activeSelf;

        void Awake() => Rebuild();

        /// <summary>바탕 원을 다시 그린다. 반지름·분할 수를 바꾼 뒤 호출한다.</summary>
        public void Rebuild()
        {
            track.useWorldSpace = false;
            track.loop = true;
            track.positionCount = segments;
            for (int i = 0; i < segments; i++)
                track.SetPosition(i, PointAt(2f * Mathf.PI * i / segments));
            fill.useWorldSpace = false;
            fill.loop = false;
        }

        public void Show(Color trackColor, Color fillColor)
        {
            gameObject.SetActive(true);
            transform.localScale = Vector3.one;
            SetColors(trackColor, fillColor);
            SetProgress(0f);
        }

        public void Hide() => gameObject.SetActive(false);

        public void SetColors(Color trackColor, Color fillColor)
        {
            track.startColor = track.endColor = trackColor;
            fill.startColor = fill.endColor = fillColor;
        }

        public void SetScale(float scale) => transform.localScale = Vector3.one * scale;

        public void SetProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (progress <= 0f)
            {
                fill.positionCount = 0;
                return;
            }
            int count = Mathf.Max(2, Mathf.CeilToInt(segments * progress) + 1);
            fill.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float sweep = 2f * Mathf.PI * progress * i / (count - 1);
                fill.SetPosition(i, PointAt(Mathf.PI * 0.5f - sweep));
            }
        }

        Vector3 PointAt(float angle) => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
    }
}
