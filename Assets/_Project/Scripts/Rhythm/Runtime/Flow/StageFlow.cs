using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 로비 ↔ 스테이지 이동을 맡는 상주 오브젝트(AppRoot 프리팹). 씬이 바뀌어도 유지된다.
    /// 전환 순서는 항상 "로딩 화면 → (스테이지 씬 내림 → 콘텐츠 언로드) → 콘텐츠 로드 → 씬 로드 → 준비 완료 → 로딩 화면 걷기"이며,
    /// 콘텐츠 로더가 한 번에 한 스테이지만 들고 있어 두 스테이지가 함께 메모리에 올라가지 않는다.
    /// </summary>
    public sealed class StageFlow : MonoBehaviour
    {
        [SerializeField] LoadingScreen loadingScreen;
        [SerializeField] string lobbySceneName = "Lobby";
        [SerializeField] string stageSceneName = "StagePlay";
        [Tooltip("로딩이 순식간에 끝나도 화면이 깜빡이지 않도록 로딩 페이지를 보여 주는 최소 시간(초).")]
        [SerializeField, Min(0f)] float minimumLoadingSeconds = 0.5f;

        readonly StageContentLoader content = new StageContentLoader();
        bool stagePrepared;

        public static StageFlow Instance { get; private set; }

        public StageDefinition CurrentStage => content.Current;
        public StageReference CurrentReference => content.CurrentReference;
        /// <summary>전환 중에는 로딩 화면이 떠 있고, 로비·스테이지는 입력을 받지 않아야 한다.</summary>
        public bool IsTransitioning { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 씬마다 AppRoot가 놓여 있어도 처음 만들어진 하나만 남는다.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            loadingScreen.HideImmediate();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            content.ReleaseImmediate();
        }

        public void EnterStage(StageReference stage)
        {
            if (IsTransitioning) return;
            RunTransition(token => EnterAsync(stage, token));
        }

        /// <summary>같은 스테이지를 처음부터. 콘텐츠는 그대로 두고 플레이 씬만 다시 연다.</summary>
        public void RetryStage()
        {
            if (IsTransitioning || !content.HasStage) return;
            RunTransition(RetryAsync);
        }

        public void ExitToLobby()
        {
            if (IsTransitioning) return;
            RunTransition(ExitAsync);
        }

        /// <summary>플레이 씬이 연출 준비를 마쳤음을 알린다. 이때 로딩 화면을 걷는다.</summary>
        public void ReportStagePrepared() => stagePrepared = true;

        /// <summary>
        /// 에디터에서 플레이 씬을 바로 실행해 로드된 스테이지가 없을 때, 현재 씬을 유지한 채 스테이지를 불러온다.
        /// 로드가 끝나면 돌아오고, 로딩 화면은 <see cref="ReportStagePrepared"/> 뒤에 걷힌다.
        /// </summary>
        public async UniTask LoadStageInPlaceAsync(StageReference stage, CancellationToken cancellationToken)
        {
            if (IsTransitioning || content.HasStage) return;
            IsTransitioning = true;
            stagePrepared = false;
            loadingScreen.ShowImmediate("스테이지를 불러오는 중");
            try
            {
                await content.LoadAsync(stage, loadingScreen, cancellationToken);
            }
            catch
            {
                IsTransitioning = false;
                throw;
            }
            RunTransition(token => RevealWhenPreparedAsync(Time.realtimeSinceStartup, token));
        }

        /// <summary>전환 하나를 돌린다. 도중에 예외가 나도 전환 상태가 풀리도록 마지막에 정리한다.</summary>
        void RunTransition(Func<CancellationToken, UniTask> transition) => RunTransitionAsync(transition, destroyCancellationToken).Forget();

        async UniTaskVoid RunTransitionAsync(Func<CancellationToken, UniTask> transition, CancellationToken cancellationToken)
        {
            IsTransitioning = true;
            try
            {
                await transition(cancellationToken);
            }
            finally
            {
                IsTransitioning = false;
            }
        }

        async UniTask EnterAsync(StageReference stage, CancellationToken cancellationToken)
        {
            float started = Time.realtimeSinceStartup;
            await loadingScreen.ShowAsync("스테이지를 불러오는 중", cancellationToken);

            if (content.HasStage)
            {
                // 다른 스테이지가 남아 있으면 그 씬을 먼저 내리고 콘텐츠를 언로드한 다음에 로드한다.
                loadingScreen.SetMessage("이전 스테이지 정리 중");
                await LoadSceneAsync(lobbySceneName, cancellationToken);
                await content.UnloadAsync(cancellationToken);
                loadingScreen.SetMessage("스테이지를 불러오는 중");
            }

            await content.LoadAsync(stage, loadingScreen, cancellationToken);
            if (!content.HasStage)
            {
                loadingScreen.SetMessage("스테이지를 불러오지 못했습니다");
                await UniTask.Delay(TimeSpan.FromSeconds(1.5), DelayType.Realtime, cancellationToken: cancellationToken);
                await ReturnToLobbyAsync(cancellationToken);
                return;
            }

            stagePrepared = false;
            await LoadSceneAsync(stageSceneName, cancellationToken);
            await RevealWhenPreparedAsync(started, cancellationToken);
        }

        async UniTask RetryAsync(CancellationToken cancellationToken)
        {
            float started = Time.realtimeSinceStartup;
            await loadingScreen.ShowAsync("다시 시작", cancellationToken);
            loadingScreen.SetProgress(1f);
            stagePrepared = false;
            await LoadSceneAsync(stageSceneName, cancellationToken);
            await RevealWhenPreparedAsync(started, cancellationToken);
        }

        async UniTask ExitAsync(CancellationToken cancellationToken)
        {
            await loadingScreen.ShowAsync("로비로 돌아가는 중", cancellationToken);
            await ReturnToLobbyAsync(cancellationToken);
        }

        /// <summary>스테이지 씬을 내린 뒤 콘텐츠를 언로드한다. 연출 인스턴스가 사라진 다음에 해제해야 안전하다.</summary>
        async UniTask ReturnToLobbyAsync(CancellationToken cancellationToken)
        {
            float started = Time.realtimeSinceStartup;
            await LoadSceneAsync(lobbySceneName, cancellationToken);
            await content.UnloadAsync(cancellationToken);
            loadingScreen.SetProgress(1f);
            await WaitMinimumAsync(started, cancellationToken);
            await loadingScreen.HideAsync(cancellationToken);
        }

        async UniTask RevealWhenPreparedAsync(float started, CancellationToken cancellationToken)
        {
            await UniTask.WaitUntil(() => stagePrepared, cancellationToken: cancellationToken);
            await WaitMinimumAsync(started, cancellationToken);
            await loadingScreen.HideAsync(cancellationToken);
        }

        UniTask WaitMinimumAsync(float started, CancellationToken cancellationToken)
        {
            float remaining = minimumLoadingSeconds - (Time.realtimeSinceStartup - started);
            return remaining > 0f
                ? UniTask.Delay(TimeSpan.FromSeconds(remaining), DelayType.Realtime, cancellationToken: cancellationToken)
                : UniTask.CompletedTask;
        }

        static UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken) =>
            SceneManager.LoadSceneAsync(sceneName).ToUniTask(cancellationToken: cancellationToken);
    }
}
