using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace IWannabe.App
{
    /// <summary>
    /// 장착한 로비 BGM을 반복 재생한다. 곡은 장착할 때만 불러온다.
    /// 로딩 화면이 걷힌 뒤에 재생을 시작하고 화면 전환이 시작되면 줄인다. 스테이지 곡을 BGM으로 쓸 때
    /// 스테이지 언로드가 끝나기 전에 같은 곡 번들을 붙잡지 않기 위해서다.
    /// </summary>
    public sealed class LobbyBgm : MonoBehaviour
    {
        [SerializeField] AudioSource source;
        [SerializeField, Range(0f, 1f)] float volume = 0.6f;
        [Tooltip("재생을 시작할 때 키우고, 화면 전환이 시작될 때 줄이는 시간(초).")]
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.4f;

        AsyncOperationHandle<AudioClip> clipHandle;
        CancellationTokenSource loading;
        Tween fade;
        bool playing;

        void Start()
        {
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            RunAsync(destroyCancellationToken).Forget();
        }

        void OnDestroy()
        {
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed -= OnChanged;
            CancelLoading();
            source.Stop();
            source.clip = null;
            ReleaseClip();
        }

        /// <summary>로딩 화면이 걷히면 틀고, 다음 화면 전환이 시작되면 줄인다(로비는 그 전환으로 내려간다).</summary>
        async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            var flow = StageFlow.Instance;
            await UniTask.WaitWhile(() => flow != null && flow.IsTransitioning, cancellationToken: cancellationToken);

            var wardrobe = Wardrobe.Instance;
            if (wardrobe == null) return;
            wardrobe.Changed += OnChanged;
            playing = true;
            Play(wardrobe.Equipped(WardrobeSlot.Bgm) as BgmItem);
            FadeTo(volume);

            if (flow == null) return;
            await UniTask.WaitUntil(() => flow.IsTransitioning, cancellationToken: cancellationToken);
            FadeTo(0f);
        }

        void OnChanged(WardrobeSlot slot)
        {
            if (slot == WardrobeSlot.Bgm && playing) Play(Wardrobe.Instance.Equipped(slot) as BgmItem);
        }

        void Play(BgmItem item)
        {
            CancelLoading();
            source.Stop();
            source.clip = null;
            ReleaseClip();
            if (item == null || item.clip == null || !item.clip.RuntimeKeyIsValid()) return;

            loading = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            LoadAndPlayAsync(item, loading.Token).Forget();
        }

        async UniTaskVoid LoadAndPlayAsync(BgmItem item, CancellationToken cancellationToken)
        {
            var handle = Addressables.LoadAssetAsync<AudioClip>(item.clip.RuntimeKey);
            // 다른 곡으로 바꾸거나 로비가 내려가 취소되는 건 평범한 흐름이라 예외 없이 값으로 받고,
            // 실패도 핸들 상태로 확인한다.
            bool canceled = await UniTask.WaitUntil(() => handle.IsDone, cancellationToken: cancellationToken)
                .SuppressCancellationThrow();
            if (canceled)
            {
                Addressables.Release(handle);
                return;
            }
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[LobbyBgm] '{item.displayName}' 곡을 불러오지 못했습니다: {handle.OperationException?.Message}");
                Addressables.Release(handle);
                return;
            }

            clipHandle = handle;
            source.clip = handle.Result;
            source.Play();
        }

        void FadeTo(float target)
        {
            fade?.Kill();
            fade = source.DOFade(target, fadeSeconds).SetEase(Ease.Linear).SetUpdate(true).SetLink(gameObject);
        }

        void CancelLoading()
        {
            if (loading == null) return;
            loading.Cancel();
            loading.Dispose();
            loading = null;
        }

        void ReleaseClip()
        {
            if (clipHandle.IsValid()) Addressables.Release(clipHandle);
            clipHandle = default;
        }
    }
}
