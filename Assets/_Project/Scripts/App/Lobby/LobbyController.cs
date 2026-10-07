using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace IWannabe.App
{
    /// <summary>스테이지 카탈로그로 진입 버튼을 만들고, 고른 스테이지를 플레이 씬에 넘긴다.</summary>
    public sealed class LobbyController : MonoBehaviour
    {
        [SerializeField] StageCatalog catalog;
        [SerializeField] Button stageButtonTemplate;
        [SerializeField] Transform stageButtonContainer;
        [SerializeField] string stageSceneName = "StagePlay";

        [Header("Calibration")]
        [SerializeField] Text offsetLabel;
        [SerializeField] Button offsetDownButton;
        [SerializeField] Button offsetUpButton;
        [SerializeField, Min(1)] int offsetStepMs = 10;

        void Start()
        {
            stageButtonTemplate.gameObject.SetActive(false);
            Button first = null;
            foreach (var entry in catalog.Stages)
            {
                var button = Instantiate(stageButtonTemplate, stageButtonContainer);
                button.name = $"Stage_{entry.displayName}";
                button.gameObject.SetActive(true);
                var label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = entry.displayName;
                var selected = entry;
                button.onClick.AddListener(() => EnterStage(selected));
                if (first == null) first = button;
            }

            offsetDownButton.onClick.AddListener(() => ChangeOffset(-offsetStepMs));
            offsetUpButton.onClick.AddListener(() => ChangeOffset(offsetStepMs));
            RefreshOffset();

            if (first != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        void EnterStage(StageCatalogEntry entry)
        {
            if (entry.stage == null || !entry.stage.RuntimeKeyIsValid())
            {
                Debug.LogError($"[Lobby] '{entry.displayName}' 스테이지 참조가 비어 있습니다.");
                return;
            }
            StageSession.Request(entry.stage);
            SceneManager.LoadScene(stageSceneName);
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
