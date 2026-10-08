using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 스테이지 콘텐츠(스테이지 그룹과, 그 스테이지가 쓰는 미니게임·곡 그룹)를 로드·언로드한다. 동시에 한 스테이지만 들고 있으며,
    /// 다른 스테이지가 남아 있는 상태에서 로드를 요청하면 예외를 던진다.
    /// 언로드는 번들이 비동기로 내려가므로 실제로 사라질 때까지 기다린 뒤 끝난다.
    /// </summary>
    public sealed class StageContentLoader
    {
        const float AudioLoadTimeoutSeconds = 10f;
        const float BundleUnloadTimeoutSeconds = 10f;
        /// <summary>진행도 중 에셋 로드가 차지하는 몫. 나머지는 오디오 데이터 준비.</summary>
        const float AssetProgressShare = 0.8f;

        AsyncOperationHandle<StageDefinition> handle;
        readonly HashSet<string> stageBundles = new HashSet<string>();

        public StageDefinition Current { get; private set; }
        public StageReference CurrentReference { get; private set; }
        public bool HasStage => handle.IsValid();

        /// <summary>스테이지를 로드한다. 실패하면 오류를 남기고 <see cref="HasStage"/>가 false인 채로 끝난다.</summary>
        public async UniTask LoadAsync(StageReference reference, IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (reference == null || !reference.RuntimeKeyIsValid())
                throw new ArgumentException("불러올 스테이지 참조가 비어 있습니다.", nameof(reference));
            if (HasStage)
                throw new InvalidOperationException($"'{Current?.DisplayName}' 스테이지가 아직 로드되어 있습니다. 먼저 언로드해야 합니다.");

            var bundlesBefore = LoadedBundleNames();
            handle = Addressables.LoadAssetAsync<StageDefinition>(reference.RuntimeKey);
            try
            {
                var assetProgress = Progress.Create<float>(p => progress?.Report(p * AssetProgressShare));
                await handle.ToUniTask(assetProgress, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ReleaseFailedLoad();
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StageContentLoader] 스테이지 로드 실패: {e.Message}");
                ReleaseFailedLoad();
                return;
            }

            Current = handle.Result;
            CurrentReference = reference;
            stageBundles.Clear();
            foreach (var name in LoadedBundleNames())
                if (!bundlesBefore.Contains(name)) stageBundles.Add(name);

            var clips = await LoadAudioAsync(Current, progress, cancellationToken);
            progress?.Report(1f);
            Debug.Log($"[StageContentLoader] 로드: '{Current.DisplayName}' (이 스테이지 번들 {stageBundles.Count}개, " +
                      $"미니게임 {Current.Minigames.Count}개, {MemoryReport(clips)})");
        }

        /// <summary>
        /// 연출 여러 개를 함께 올리는 리믹스의 메모리 확인용 요약. 오디오는 클립별 런타임 크기의 합이고
        /// 전체 할당은 이 시점의 엔진 전체 값이다(릴리스 빌드에서는 0으로 나온다).
        /// </summary>
        static string MemoryReport(List<AudioClip> clips)
        {
            const double Mb = 1024.0 * 1024.0;
            long audio = 0;
            foreach (var clip in clips) audio += Profiler.GetRuntimeMemorySizeLong(clip);
            return $"오디오 {clips.Count}개 {audio / Mb:0.0}MB, 전체 할당 {Profiler.GetTotalAllocatedMemoryLong() / Mb:0}MB";
        }

        /// <summary>현재 스테이지를 해제하고, 그 번들과 에셋이 메모리에서 내려갈 때까지 기다린다.</summary>
        public async UniTask UnloadAsync(CancellationToken cancellationToken)
        {
            if (!HasStage) return;

            string name = Current != null ? Current.DisplayName : "?";
            Current = null;
            CurrentReference = null;
            Addressables.Release(handle);
            handle = default;

            float deadline = Time.realtimeSinceStartup + BundleUnloadTimeoutSeconds;
            while (AnyStageBundleLoaded() && Time.realtimeSinceStartup < deadline)
                await UniTask.Yield(cancellationToken);
            if (AnyStageBundleLoaded())
                Debug.LogWarning("[StageContentLoader] 번들 언로드가 제한 시간 안에 끝나지 않았습니다.");
            stageBundles.Clear();

            // 에디터의 AssetDatabase 모드처럼 번들 없이 로드된 에셋도 여기서 내려간다.
            await Resources.UnloadUnusedAssets().ToUniTask(cancellationToken: cancellationToken);
            Debug.Log($"[StageContentLoader] 언로드 완료: '{name}'");
        }

        /// <summary>앱 종료 등으로 기다릴 수 없을 때 즉시 해제한다.</summary>
        public void ReleaseImmediate()
        {
            if (!HasStage) return;
            Addressables.Release(handle);
            handle = default;
            Current = null;
            CurrentReference = null;
            stageBundles.Clear();
        }

        void ReleaseFailedLoad()
        {
            Addressables.Release(handle);
            handle = default;
        }

        /// <summary>곡과, 스테이지가 쓰는 모든 미니게임의 큐 효과음·연출용 오디오를 미리 디코딩해 둔다.</summary>
        static async UniTask<List<AudioClip>> LoadAudioAsync(StageDefinition stage, IProgress<float> progress, CancellationToken cancellationToken)
        {
            var clips = new List<AudioClip>();
            void Add(AudioClip clip)
            {
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            }

            if (stage.Song != null) Add(stage.Song.Clip);
            foreach (var minigame in stage.Minigames)
            {
                if (minigame == null) continue;
                foreach (var cue in minigame.CueSounds) Add(cue?.clip);
                if (minigame.PresenterPrefab != null)
                    foreach (var clip in minigame.PresenterPrefab.AudioClips) Add(clip);
            }

            foreach (var clip in clips)
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();

            float deadline = Time.realtimeSinceStartup + AudioLoadTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                int loaded = clips.FindAll(c => c.loadState != AudioDataLoadState.Loading).Count;
                progress?.Report(AssetProgressShare + (1f - AssetProgressShare) * loaded / Math.Max(1, clips.Count));
                if (loaded == clips.Count) break;
                await UniTask.Yield(cancellationToken);
            }
            return clips;
        }

        bool AnyStageBundleLoaded()
        {
            if (stageBundles.Count == 0) return false;
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle != null && stageBundles.Contains(bundle.name)) return true;
            return false;
        }

        static HashSet<string> LoadedBundleNames()
        {
            var names = new HashSet<string>();
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle != null) names.Add(bundle.name);
            return names;
        }
    }
}
