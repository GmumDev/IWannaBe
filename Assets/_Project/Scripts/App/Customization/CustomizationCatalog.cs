using System;
using System.Collections.Generic;
using IWannabe.Otamaton;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace IWannabe.App
{
    /// <summary>꾸미기에서 하나씩 장착하는 칸.</summary>
    public enum WardrobeSlot
    {
        Body,
        Eyes,
        Background,
        Bgm,
    }

    /// <summary>꾸미기 항목 하나의 공통 정보.</summary>
    [Serializable]
    public abstract class CustomizationItem
    {
        [Tooltip("저장·장착에 쓰는 고유 ID. 바꾸면 이 항목을 장착하던 기록이 기본값으로 돌아간다.")]
        public string id;
        public string displayName;
        [Tooltip("이 스테이지를 클리어하면 해금된다. 비우면 처음부터 쓸 수 있다.")]
        public string unlockStageId;
        [Tooltip("꾸미기 그리드에 보여 줄 아이콘. 비우면 항목 종류에 맞는 기본 표시를 쓴다.")]
        public Sprite icon;
    }

    [Serializable]
    public sealed class BodyItem : CustomizationItem
    {
        public OtamatonBody parts = new OtamatonBody();
    }

    [Serializable]
    public sealed class EyesItem : CustomizationItem
    {
        public OtamatonEyes parts = new OtamatonEyes();
    }

    [Serializable]
    public sealed class BackgroundItem : CustomizationItem
    {
        [Tooltip("이미지가 없으면 이 색으로 채운다. 이미지가 있으면 이미지에 곱하는 색(보통 흰색).")]
        public Color color = Color.white;
        [Tooltip("장착할 때만 불러오는 배경 이미지(Addressables).")]
        public AssetReferenceSprite image;
    }

    [Serializable]
    public sealed class BgmItem : CustomizationItem
    {
        [Tooltip("장착할 때만 불러오는 곡(Addressables). 비우면 무음.")]
        public AssetReferenceT<AudioClip> clip;
    }

    /// <summary>
    /// 꾸미기 항목 목록. 오타마톤 파츠처럼 작은 이미지는 직접 참조하고,
    /// 배경 이미지·곡처럼 큰 에셋은 주소로만 가리켜 장착할 때 불러온다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Customization Catalog", fileName = "CustomizationCatalog")]
    public sealed class CustomizationCatalog : ScriptableObject
    {
        [SerializeField] List<BodyItem> bodies = new List<BodyItem>();
        [SerializeField] List<EyesItem> eyes = new List<EyesItem>();
        [SerializeField] List<BackgroundItem> backgrounds = new List<BackgroundItem>();
        [SerializeField] List<BgmItem> bgms = new List<BgmItem>();

        public IReadOnlyList<CustomizationItem> Items(WardrobeSlot slot)
        {
            switch (slot)
            {
                case WardrobeSlot.Body: return bodies;
                case WardrobeSlot.Eyes: return eyes;
                case WardrobeSlot.Background: return backgrounds;
                default: return bgms;
            }
        }

        public bool Contains(WardrobeSlot slot, string id)
        {
            foreach (var item in Items(slot))
                if (item.id == id) return true;
            return false;
        }

        /// <summary>항목을 종류에 맞는 목록 끝에 더한다(에디터 구성용).</summary>
        public void Add(CustomizationItem item)
        {
            switch (item)
            {
                case BodyItem body: bodies.Add(body); break;
                case EyesItem eye: eyes.Add(eye); break;
                case BackgroundItem background: backgrounds.Add(background); break;
                case BgmItem bgm: bgms.Add(bgm); break;
                default: throw new ArgumentException($"알 수 없는 꾸미기 항목: {item?.GetType().Name}");
            }
        }
    }
}
