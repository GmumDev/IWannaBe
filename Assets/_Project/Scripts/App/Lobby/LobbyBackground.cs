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
        int request;

        void Start()
        {
            var wardrobe = Wardrobe.Instance;
            if (wardrobe == null) return;
            wardrobe.Changed += OnChanged;
            Apply(wardrobe.Equipped(WardrobeSlot.Background) as BackgroundItem);
        }

        void OnDestroy()
        {
            request++;
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed -= OnChanged;
            ReleaseSprite();
        }

        void OnChanged(WardrobeSlot slot)
        {
            if (slot == WardrobeSlot.Background) Apply(Wardrobe.Instance.Equipped(slot) as BackgroundItem);
        }

        void Apply(BackgroundItem item)
        {
            int current = ++request;
            image.color = item != null ? item.color : Color.white;
            if (item == null || item.image == null || !item.image.RuntimeKeyIsValid())
            {
                SetSprite(null);
                ReleaseSprite();
                return;
            }

            // 새 이미지가 올 때까지 이전 이미지를 보여 주다가 바꾼다.
            var load = Addressables.LoadAssetAsync<Sprite>(item.image.RuntimeKey);
            load.Completed += done =>
            {
                if (current != request || done.Status != AsyncOperationStatus.Succeeded)
                {
                    if (done.Status != AsyncOperationStatus.Succeeded)
                        Debug.LogError($"[LobbyBackground] '{item.displayName}' 배경을 불러오지 못했습니다.");
                    Addressables.Release(done);
                    return;
                }
                ReleaseSprite();
                spriteHandle = done;
                SetSprite(done.Result);
            };
        }

        void SetSprite(Sprite sprite)
        {
            image.sprite = sprite;
            // 단색이면 비율은 상관없다. 이미지면 원본 비율로 화면을 덮는다.
            fitter.aspectRatio = sprite != null ? sprite.rect.width / sprite.rect.height : 1f;
        }

        void ReleaseSprite()
        {
            if (spriteHandle.IsValid()) Addressables.Release(spriteHandle);
            spriteHandle = default;
        }
    }
}
