using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace IWannabe.App
{
    /// <summary>
    /// 장착한 로비 배경을 그린다. 이미지가 있으면 장착할 때 불러와 화면을 빈틈없이 덮고(비율 유지),
    /// 없으면 항목 색으로 채운다.
    /// </summary>
    public sealed class LobbyBackground : MonoBehaviour
    {
        [SerializeField] Image image;
        [SerializeField] AspectRatioFitter fitter;

        AsyncOperationHandle<Sprite> spriteHandle;
        CancellationTokenSource loading;

        void Start()
        {
            var wardrobe = Wardrobe.Instance;
            if (wardrobe == null) return;
            wardrobe.Changed += OnChanged;
            Apply(wardrobe.Equipped(WardrobeSlot.Background) as BackgroundItem);
        }

        void OnDestroy()
        {
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed -= OnChanged;
            CancelLoading();
            ReleaseSprite();
        }

        void OnChanged(WardrobeSlot slot)
        {
            if (slot == WardrobeSlot.Background) Apply(Wardrobe.Instance.Equipped(slot) as BackgroundItem);
        }

        void Apply(BackgroundItem item)
        {
            CancelLoading();
            image.color = item != null ? item.color : Color.white;
            if (item == null || item.image == null || !item.image.RuntimeKeyIsValid())
            {
                SetSprite(null);
                ReleaseSprite();
                return;
            }
            loading = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            LoadAsync(item, loading.Token).Forget();
        }

        /// <summary>새 이미지가 올 때까지 이전 이미지를 보여 주다가 바꾼다.</summary>
        async UniTaskVoid LoadAsync(BackgroundItem item, CancellationToken cancellationToken)
        {
            var handle = Addressables.LoadAssetAsync<Sprite>(item.image.RuntimeKey);
            // 다른 배경으로 바꾸거나 로비가 내려가 취소되는 건 평범한 흐름이라 예외 없이 값으로 받고,
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
                Debug.LogError($"[LobbyBackground] '{item.displayName}' 배경을 불러오지 못했습니다: {handle.OperationException?.Message}");
                Addressables.Release(handle);
                return;
            }

            ReleaseSprite();
            spriteHandle = handle;
            SetSprite(handle.Result);
        }

        void SetSprite(Sprite sprite)
        {
            image.sprite = sprite;
            // 단색이면 비율은 상관없다. 이미지면 원본 비율로 화면을 덮는다.
            fitter.aspectRatio = sprite != null ? sprite.rect.width / sprite.rect.height : 1f;
        }

        void CancelLoading()
        {
            if (loading == null) return;
            loading.Cancel();
            loading.Dispose();
            loading = null;
        }

        void ReleaseSprite()
        {
            if (spriteHandle.IsValid()) Addressables.Release(spriteHandle);
            spriteHandle = default;
        }
    }
}
