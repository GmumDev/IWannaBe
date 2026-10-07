using System;
using IWannabe.Otamaton;
using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace IWannabe.App
{
    /// <summary>
    /// 꾸미기 장착 상태(상주 오브젝트 AppRoot). 시작할 때 카탈로그를 한 번 불러와 들고 있고,
    /// 장착한 항목 ID를 기기(PlayerPrefs)에 저장한다. 오타마톤 모습은 <see cref="OtamatonAppearance"/>로 알려
    /// 로비·스테이지 캐릭터가 따라가게 한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Wardrobe : MonoBehaviour
    {
        const string KeyPrefix = "IWannabe.Wardrobe.";
        static readonly WardrobeSlot[] Slots = (WardrobeSlot[])Enum.GetValues(typeof(WardrobeSlot));

        [Tooltip("꾸미기 카탈로그(Addressables). 씬이 직접 참조하지 않아야 번들과 앱 데이터에 중복으로 들어가지 않는다.")]
        [SerializeField] AssetReferenceT<CustomizationCatalog> catalog;

        readonly CustomizationItem[] equipped = new CustomizationItem[Slots.Length];
        AsyncOperationHandle<CustomizationCatalog> handle;

        public static Wardrobe Instance { get; private set; }

        public CustomizationCatalog Catalog { get; private set; }

        /// <summary>장착이 바뀐 칸.</summary>
        public event Action<WardrobeSlot> Changed;

        void Awake()
        {
            // 씬마다 AppRoot가 놓여 있어도 처음 것 하나만 쓴다(나머지는 StageFlow가 지운다).
            if (Instance != null && Instance != this) return;
            Instance = this;

            if (catalog == null || !catalog.RuntimeKeyIsValid())
            {
                Debug.LogError("[Wardrobe] 꾸미기 카탈로그가 지정되지 않았습니다.");
                return;
            }
            // 로비·스테이지가 시작하기 전에 장착 상태가 정해져 있도록 동기로 불러온다(로컬 번들이라 짧다).
            handle = Addressables.LoadAssetAsync<CustomizationCatalog>(catalog.RuntimeKey);
            Catalog = handle.WaitForCompletion();
            if (Catalog == null)
            {
                Debug.LogError($"[Wardrobe] 꾸미기 카탈로그를 불러오지 못했습니다: {handle.OperationException?.Message}");
                return;
            }

            foreach (var slot in Slots)
                equipped[(int)slot] = Resolve(slot);
            PublishAppearance();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (handle.IsValid()) Addressables.Release(handle);
        }

        public CustomizationItem Equipped(WardrobeSlot slot) => equipped[(int)slot];

        public static bool IsUnlocked(CustomizationItem item) =>
            item != null && (string.IsNullOrEmpty(item.unlockStageId) || StageProgress.IsCleared(item.unlockStageId));

        /// <summary>바로 장착하고 저장한다. 잠긴 항목은 무시한다.</summary>
        public void Equip(WardrobeSlot slot, CustomizationItem item)
        {
            if (!IsUnlocked(item) || equipped[(int)slot] == item) return;
            equipped[(int)slot] = item;
            PlayerPrefs.SetString(KeyPrefix + slot, item.id);
            PlayerPrefs.Save();
            if (slot == WardrobeSlot.Body || slot == WardrobeSlot.Eyes) PublishAppearance();
            Changed?.Invoke(slot);
        }

        /// <summary>저장된 항목이 없거나 잠겼으면(기록이 지워진 경우 등) 쓸 수 있는 첫 항목으로 돌아간다.</summary>
        CustomizationItem Resolve(WardrobeSlot slot)
        {
            string saved = PlayerPrefs.GetString(KeyPrefix + slot, string.Empty);
            CustomizationItem fallback = null;
            foreach (var item in Catalog.Items(slot))
            {
                if (!IsUnlocked(item)) continue;
                if (item.id == saved) return item;
                fallback ??= item;
            }
            return fallback;
        }

        void PublishAppearance()
        {
            var body = Equipped(WardrobeSlot.Body) as BodyItem;
            var eyes = Equipped(WardrobeSlot.Eyes) as EyesItem;
            OtamatonAppearance.Set(body?.parts, eyes?.parts);
        }
    }
}
