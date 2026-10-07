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
        [Tooltip("화면 전환이 시작될 때 소리를 줄이는 시간(초).")]
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.4f;

        AsyncOperationHandle<AudioClip> clipHandle;
        bool started;
        int request;

        void Start()
        {
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed += OnChanged;
        }

        void OnDestroy()
        {
            request++;
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed -= OnChanged;
            source.Stop();
            source.clip = null;
            ReleaseClip();
        }

        void Update()
        {
            var flow = StageFlow.Instance;
            bool transitioning = flow != null && flow.IsTransitioning;
            if (!started && !transitioning && Wardrobe.Instance != null)
            {
                started = true;
                Play(Wardrobe.Instance.Equipped(WardrobeSlot.Bgm) as BgmItem);
            }
            float target = transitioning ? 0f : volume;
            source.volume = Mathf.MoveTowards(source.volume, target, volume / fadeSeconds * Time.unscaledDeltaTime);
        }

        void OnChanged(WardrobeSlot slot)
        {
            if (slot == WardrobeSlot.Bgm && started) Play(Wardrobe.Instance.Equipped(slot) as BgmItem);
        }

        void Play(BgmItem item)
        {
            int current = ++request;
            source.Stop();
            source.clip = null;
            ReleaseClip();
            if (item == null || item.clip == null || !item.clip.RuntimeKeyIsValid()) return;

            var load = Addressables.LoadAssetAsync<AudioClip>(item.clip.RuntimeKey);
            load.Completed += done =>
            {
                if (current != request || done.Status != AsyncOperationStatus.Succeeded)
                {
                    if (done.Status != AsyncOperationStatus.Succeeded)
                        Debug.LogError($"[LobbyBgm] '{item.displayName}' 곡을 불러오지 못했습니다.");
                    Addressables.Release(done);
                    return;
                }
                clipHandle = done;
                source.clip = done.Result;
                source.Play();
            };
        }

        void ReleaseClip()
        {
            if (clipHandle.IsValid()) Addressables.Release(clipHandle);
            clipHandle = default;
        }
    }
}
