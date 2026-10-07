using System.Collections;
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
            StartCoroutine(EnterRoutine(stage));
        }

        /// <summary>같은 스테이지를 처음부터. 콘텐츠는 그대로 두고 플레이 씬만 다시 연다.</summary>
        public void RetryStage()
        {
            if (IsTransitioning || !content.HasStage) return;
            StartCoroutine(RetryRoutine());
        }

        public void ExitToLobby()
        {
            if (IsTransitioning) return;
            StartCoroutine(ExitRoutine());
        }

        /// <summary>플레이 씬이 연출 준비를 마쳤음을 알린다. 이때 로딩 화면을 걷는다.</summary>
        public void ReportStagePrepared() => stagePrepared = true;

        /// <summary>
        /// 에디터에서 플레이 씬을 바로 실행해 로드된 스테이지가 없을 때, 현재 씬을 유지한 채 스테이지를 불러온다.
        /// </summary>
        public IEnumerator LoadStageInPlace(StageReference stage)
        {
            if (IsTransitioning || content.HasStage) yield break;
            IsTransitioning = true;
            stagePrepared = false;
            loadingScreen.ShowImmediate("스테이지를 불러오는 중");
            yield return content.Load(stage, loadingScreen.SetProgress);
            StartCoroutine(RevealWhenPrepared(Time.realtimeSinceStartup));
        }

        IEnumerator EnterRoutine(StageReference stage)
        {
            IsTransitioning = true;
            float started = Time.realtimeSinceStartup;
            yield return loadingScreen.Show("스테이지를 불러오는 중");

            if (content.HasStage)
            {
                // 다른 스테이지가 남아 있으면 그 씬을 먼저 내리고 콘텐츠를 언로드한 다음에 로드한다.
                loadingScreen.SetMessage("이전 스테이지 정리 중");
                yield return SceneManager.LoadSceneAsync(lobbySceneName);
                yield return content.Unload();
                loadingScreen.SetMessage("스테이지를 불러오는 중");
            }

            yield return content.Load(stage, loadingScreen.SetProgress);
            if (!content.HasStage)
            {
                loadingScreen.SetMessage("스테이지를 불러오지 못했습니다");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return ReturnToLobby();
                yield break;
            }

            stagePrepared = false;
            yield return SceneManager.LoadSceneAsync(stageSceneName);
            yield return RevealWhenPrepared(started);
        }

        IEnumerator RetryRoutine()
        {
            IsTransitioning = true;
            float started = Time.realtimeSinceStartup;
            yield return loadingScreen.Show("다시 시작");
            loadingScreen.SetProgress(1f);
            stagePrepared = false;
            yield return SceneManager.LoadSceneAsync(stageSceneName);
            yield return RevealWhenPrepared(started);
        }

        IEnumerator ExitRoutine()
        {
            IsTransitioning = true;
            yield return loadingScreen.Show("로비로 돌아가는 중");
            yield return ReturnToLobby();
        }

        /// <summary>스테이지 씬을 내린 뒤 콘텐츠를 언로드한다. 연출 인스턴스가 사라진 다음에 해제해야 안전하다.</summary>
        IEnumerator ReturnToLobby()
        {
            float started = Time.realtimeSinceStartup;
            yield return SceneManager.LoadSceneAsync(lobbySceneName);
            yield return content.Unload();
            loadingScreen.SetProgress(1f);
            yield return WaitMinimum(started);
            yield return loadingScreen.Hide();
            IsTransitioning = false;
        }

        IEnumerator RevealWhenPrepared(float started)
        {
            while (!stagePrepared) yield return null;
            yield return WaitMinimum(started);
            yield return loadingScreen.Hide();
            IsTransitioning = false;
        }

        IEnumerator WaitMinimum(float started)
        {
            float remaining = minimumLoadingSeconds - (Time.realtimeSinceStartup - started);
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
        }
    }
}
