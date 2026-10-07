using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace IWannabe.Rhythm
{
    /// <summary>씬 전환과 스테이지 언로드·로드 사이를 가리는 전체 화면 로딩 페이지.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] Text messageText;
        [Tooltip("가로로 늘어나는 진행 바. 앵커 오른쪽 끝을 진행도에 맞춘다.")]
        [SerializeField] RectTransform progressFill;
        [SerializeField] RectTransform spinner;
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.2f;
        [SerializeField] float spinDegreesPerSecond = 270f;

        public bool IsVisible => group.alpha > 0f;

        void Awake()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
        }

        void Update()
        {
            if (IsVisible && spinner != null)
                spinner.Rotate(0f, 0f, -spinDegreesPerSecond * Time.unscaledDeltaTime);
        }

        public IEnumerator Show(string message)
        {
            SetMessage(message);
            SetProgress(0f);
            group.blocksRaycasts = true;
            yield return Fade(1f);
        }

        public IEnumerator Hide()
        {
            yield return Fade(0f);
            group.blocksRaycasts = false;
        }

        public void ShowImmediate(string message)
        {
            SetMessage(message);
            SetProgress(0f);
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }

        public void HideImmediate()
        {
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        public void SetMessage(string message) => messageText.text = message ?? string.Empty;

        public void SetProgress(float normalized)
        {
            if (progressFill != null) progressFill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        IEnumerator Fade(float target)
        {
            float start = group.alpha;
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(start, target, t / fadeSeconds);
                yield return null;
            }
            group.alpha = target;
        }
    }
}
