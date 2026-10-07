using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IWannabe.App
{
    /// <summary>
    /// 스테이지 카탈로그 순서대로 진입 버튼을 만든다. 앞 스테이지를 클리어해야 다음 버튼이 열린다.
    /// 입장은 <see cref="StageFlow"/>에 맡기며, 콘텐츠 로드는 로딩 화면 뒤에서 이루어진다.
    /// </summary>
    public sealed class LobbyController : MonoBehaviour
    {
        [SerializeField] StageCatalog catalog;
        [SerializeField] StageButtonView stageButtonTemplate;
        [SerializeField] Transform stageButtonContainer;

        [Header("Calibration")]
        [SerializeField] Text offsetLabel;
        [SerializeField] Button offsetDownButton;
        [SerializeField] Button offsetUpButton;
        [SerializeField, Min(1)] int offsetStepMs = 10;

        void Start()
        {
            stageButtonTemplate.gameObject.SetActive(false);
            Button firstSelectable = null;
            var stages = catalog.Stages;
            for (int i = 0; i < stages.Count; i++)
            {
                var entry = stages[i];
                var view = Instantiate(stageButtonTemplate, stageButtonContainer);
                view.name = $"Stage_{entry.stageId}";
                view.gameObject.SetActive(true);

                var availability = !catalog.IsUnlocked(i) ? StageAvailability.Locked
                    : StageProgress.IsCleared(entry.stageId) ? StageAvailability.Cleared
                    : StageAvailability.Open;
                string lockedHint = i > 0 ? $"잠김 · '{stages[i - 1].displayName}' 클리어 시 열림" : "잠김";
                view.Bind(entry.displayName, availability, lockedHint);

                var selected = entry;
                view.Button.onClick.AddListener(() => EnterStage(selected));
                if (availability != StageAvailability.Locked) firstSelectable = view.Button;
            }

            offsetDownButton.onClick.AddListener(() => ChangeOffset(-offsetStepMs));
            offsetUpButton.onClick.AddListener(() => ChangeOffset(offsetStepMs));
            RefreshOffset();

            // 패드 사용자를 위해 가장 최근에 열린 스테이지를 미리 선택해 둔다.
            if (firstSelectable != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(firstSelectable.gameObject);
        }

        void EnterStage(StageCatalogEntry entry)
        {
            var flow = StageFlow.Instance;
            if (flow == null || flow.IsTransitioning) return;
            if (entry.stage == null || !entry.stage.RuntimeKeyIsValid())
            {
                Debug.LogError($"[Lobby] '{entry.displayName}' 스테이지 참조가 비어 있습니다.");
                return;
            }
            flow.EnterStage(entry.stage);
        }

        void ChangeOffset(int deltaMs)
        {
            PlayerCalibration.InputOffsetMs += deltaMs;
            RefreshOffset();
        }

        void RefreshOffset()
        {
            offsetLabel.text = $"입력 보정 {PlayerCalibration.InputOffsetMs:+0;-0;0} ms";
        }
    }
}
