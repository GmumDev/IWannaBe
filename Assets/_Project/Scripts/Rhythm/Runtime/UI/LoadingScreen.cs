using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 씬 전환과 스테이지 언로드·로드 사이를 가리는 전체 화면 로딩 페이지.
    /// 진행도는 <see cref="IProgress{T}"/>로 받아 로더에 그대로 넘길 수 있다.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class LoadingScreen : MonoBehaviour, IProgress<float>
    {
        [SerializeField] CanvasGroup group;
        [SerializeField] Text messageText;
        [Tooltip("가로로 늘어나는 진행 바. 앵커 오른쪽 끝을 진행도에 맞춘다.")]
        [SerializeField] RectTransform progressFill;
        [SerializeField] RectTransform spinner;
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.2f;
        [SerializeField] float spinDegreesPerSecond = 270f;

        Tween fade;
        Tween spin;

        public bool IsVisible => group.alpha > 0f;

        void Awake()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (spinner != null && spinDegreesPerSecond > 0f)
            {
                spin = spinner.DOLocalRotate(new Vector3(0f, 0f, -360f), 360f / spinDegreesPerSecond, RotateMode.FastBeyond360)
                    .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true).SetLink(gameObject).Pause();
            }
        }

        /// <summary>메시지를 바꾸고 진행도를 비운 뒤 화면을 덮는다. 덮는 동안 아래 화면의 입력을 막는다.</summary>
        public async UniTask ShowAsync(string message, CancellationToken cancellationToken = default)
        {
            SetMessage(message);
            SetProgress(0f);
            group.blocksRaycasts = true;
            spin?.Play();
            await FadeTo(1f, cancellationToken);
        }

        public async UniTask HideAsync(CancellationToken cancellationToken = default)
        {
            await FadeTo(0f, cancellationToken);
            group.blocksRaycasts = false;
            spin?.Pause();
        }

        public void ShowImmediate(string message)
        {
            fade?.Kill();
            SetMessage(message);
            SetProgress(0f);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            spin?.Play();
        }

        public void HideImmediate()
        {
            fade?.Kill();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            spin?.Pause();
        }

        public void SetMessage(string message) => messageText.text = message ?? string.Empty;

        public void SetProgress(float normalized)
        {
            if (progressFill != null) progressFill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        void IProgress<float>.Report(float value) => SetProgress(value);

        UniTask FadeTo(float alpha, CancellationToken cancellationToken)
        {
            fade?.Kill();
            fade = group.DOFade(alpha, fadeSeconds).SetEase(Ease.Linear).SetUpdate(true).SetLink(gameObject);
            return fade.ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
        }
    }
}
