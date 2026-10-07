using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IWannabe.Rhythm
{
    /// <summary>스테이지 공용 HUD: 진행도, 판정 표시, 일시정지·결과 패널.</summary>
    public sealed class StageHud : MonoBehaviour
    {
        [SerializeField] Text stageNameText;
        [SerializeField] Text centerText;
        [SerializeField] Text judgementText;
        [Tooltip("가로로 늘어나는 진행 바. 앵커 오른쪽 끝을 진행도에 맞춘다.")]
        [SerializeField] RectTransform progressFill;
        [SerializeField] Button pauseButton;

        [Header("Pause")]
        [SerializeField] GameObject pausePanel;
        [SerializeField] Button resumeButton;
        [SerializeField] Button pauseRetryButton;
        [SerializeField] Button pauseExitButton;

        [Header("Result")]
        [SerializeField] GameObject resultPanel;
        [SerializeField] Text resultRankText;
        [SerializeField] Text resultDetailText;
        [SerializeField] Button resultRetryButton;
        [SerializeField] Button resultExitButton;

        [Header("Judgement")]
        [Tooltip("판정 글자와 오차(ms)를 띄운다. 조정용이며 실제 리듬세상처럼 숨기려면 끈다.")]
        [SerializeField] bool showJudgementText = true;
        [SerializeField, Min(0.1f)] float judgementFadeSeconds = 0.6f;
        [SerializeField] Color perfectColor = new Color(0.16f, 0.62f, 0.56f);
        [SerializeField] Color barelyColor = new Color(0.91f, 0.6f, 0.2f);
        [SerializeField] Color missColor = new Color(0.85f, 0.25f, 0.25f);

        public event Action PauseClicked;
        public event Action ResumeClicked;
        public event Action RetryClicked;
        public event Action ExitClicked;

        float judgementTimer;
        Color judgementColor;

        void Awake()
        {
            pauseButton.onClick.AddListener(() => PauseClicked?.Invoke());
            resumeButton.onClick.AddListener(() => ResumeClicked?.Invoke());
            pauseRetryButton.onClick.AddListener(() => RetryClicked?.Invoke());
            pauseExitButton.onClick.AddListener(() => ExitClicked?.Invoke());
            resultRetryButton.onClick.AddListener(() => RetryClicked?.Invoke());
            resultExitButton.onClick.AddListener(() => ExitClicked?.Invoke());

            pausePanel.SetActive(false);
            resultPanel.SetActive(false);
            judgementText.text = string.Empty;
            SetPauseButtonVisible(false);
        }

        void Update()
        {
            if (judgementTimer <= 0) return;
            judgementTimer -= Time.unscaledDeltaTime;
            var c = judgementColor;
            c.a = Mathf.Clamp01(judgementTimer / judgementFadeSeconds);
            judgementText.color = c;
        }

        public void SetStageName(string value) => stageNameText.text = value;

        public void ShowCenter(string value)
        {
            centerText.text = value ?? string.Empty;
            centerText.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }

        public void SetProgress(float normalized) => progressFill.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);

        public void SetPauseButtonVisible(bool visible) => pauseButton.gameObject.SetActive(visible);

        public void ShowJudgement(NoteJudgement judgement)
        {
            if (!showJudgementText) return;
            string label;
            switch (judgement.Grade)
            {
                case JudgeGrade.Perfect:
                    label = "Perfect";
                    judgementColor = perfectColor;
                    break;
                case JudgeGrade.Barely:
                    label = "아슬아슬";
                    judgementColor = barelyColor;
                    break;
                default:
                    label = "Miss";
                    judgementColor = missColor;
                    break;
            }
            if (judgement.Phase == NotePhase.Release) label += " (뗌)";
            if (judgement.HasInput) label += $"  {judgement.Delta * 1000:+0;-0;0}ms";
            judgementText.text = label;
            judgementText.color = judgementColor;
            judgementTimer = judgementFadeSeconds;
        }

        public void ShowPause(bool visible)
        {
            pausePanel.SetActive(visible);
            resumeButton.gameObject.SetActive(true);
            if (visible) Select(resumeButton);
        }

        public void ShowError(string message)
        {
            ShowCenter(message);
            pausePanel.SetActive(true);
            resumeButton.gameObject.SetActive(false);
            pauseRetryButton.gameObject.SetActive(false);
            Select(pauseExitButton);
        }

        public void ShowResult(ScoreTracker score)
        {
            pausePanel.SetActive(false);
            resultPanel.SetActive(true);
            switch (score.Rank)
            {
                case StageRank.Superb: resultRankText.text = "최고예요!"; break;
                case StageRank.Ok: resultRankText.text = "괜찮아요"; break;
                default: resultRankText.text = "다시 도전!"; break;
            }
            resultDetailText.text =
                $"정확도 {score.Accuracy * 100:0}%\nPerfect {score.Perfect}   아슬아슬 {score.Barely}   Miss {score.Miss}";
            Select(resultRetryButton);
        }

        static void Select(Selectable selectable)
        {
            if (EventSystem.current != null && selectable != null)
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }
}
