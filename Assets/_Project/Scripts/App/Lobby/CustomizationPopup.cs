using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using IWannabe.Rhythm;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace IWannabe.App
{
    /// <summary>
    /// 로비 꾸미기 팝업(화면 오른쪽, 왼쪽에 탭·오른쪽에 아이콘 그리드). 열 때는
    /// ① 로비 화면을 왼쪽으로 밀어 캐릭터가 팝업을 뺀 영역 가운데에 오게 하고 ② 팝업을 오른쪽 밖에서 들여온 뒤
    /// ③ 입력을 받는다. 닫을 때는 그 역순이다. 팝업 바깥을 누르거나 취소(Esc·패드 B)로 닫는다.
    /// 아이콘을 누르면 바로 장착된다.
    /// </summary>
    public sealed class CustomizationPopup : MonoBehaviour
    {
        enum State { Closed, Opening, Open, Closing }

        readonly struct Section
        {
            public readonly string Heading;
            public readonly WardrobeSlot Slot;

            public Section(string heading, WardrobeSlot slot)
            {
                Heading = heading;
                Slot = slot;
            }
        }

        /// <summary>탭 순서. 첫 탭이 열 때마다 보이는 기본 탭이다. 칸이 하나뿐인 탭은 제목을 생략한다.</summary>
        static readonly (string title, Section[] sections)[] Tabs =
        {
            ("플레이어", new[] { new Section("몸통", WardrobeSlot.Body), new Section("눈", WardrobeSlot.Eyes) }),
            ("로비 배경", new[] { new Section(null, WardrobeSlot.Background) }),
            ("로비 BGM", new[] { new Section(null, WardrobeSlot.Bgm) }),
        };

        [Header("Screen")]
        [Tooltip("팝업이 열릴 때 왼쪽으로 밀려나는 로비 화면(배경·캐릭터·버튼).")]
        [SerializeField] RectTransform screen;
        [SerializeField] Button openButton;
        [Tooltip("팝업 바깥을 덮는 투명한 막. 누르면 팝업이 닫히고, 그 아래 로비는 눌리지 않는다.")]
        [SerializeField] Button outsideArea;

        [Header("Panel")]
        [Tooltip("오른쪽 끝에 붙은 팝업(피벗 x = 1). 높이에 맞춰 너비가 정해진다.")]
        [SerializeField] RectTransform panel;
        [SerializeField] CanvasGroup panelGroup;
        [SerializeField, Min(0f)] float rightMargin = 40f;
        [SerializeField, Min(0.01f)] float shiftSeconds = 0.25f;
        [SerializeField, Min(0.01f)] float slideSeconds = 0.25f;
        [SerializeField] Ease ease = Ease.OutCubic;

        [Header("Content")]
        [SerializeField] Button tabTemplate;
        [SerializeField] Color tabColor = Color.white;
        [SerializeField] Color currentTabColor = new Color(0.91f, 0.77f, 0.42f);
        [SerializeField] ScrollRect itemScroll;
        [Tooltip("제목(Text)과 아이콘 그리드(GridLayoutGroup)로 된 칸 묶음.")]
        [SerializeField] RectTransform sectionTemplate;
        [SerializeField] WardrobeCellView cellTemplate;
        [Tooltip("해금 조건 문구에 스테이지 이름을 쓴다.")]
        [SerializeField] StageCatalog stageCatalog;

        readonly List<Button> tabButtons = new List<Button>();
        readonly List<GameObject> sections = new List<GameObject>();
        readonly List<WardrobeCellView> cells = new List<WardrobeCellView>();
        State state = State.Closed;
        int currentTab;
        GameObject lastSelected;

        /// <summary>남은 영역(화면 왼쪽 끝 ~ 팝업 왼쪽 끝)의 가운데가 화면 가운데에 오도록 미는 거리.</summary>
        float ShiftX => -(panel.rect.width + rightMargin) * 0.5f;
        float ShownX => -rightMargin;
        float HiddenX => panel.rect.width;

        void Start()
        {
            tabTemplate.gameObject.SetActive(false);
            sectionTemplate.gameObject.SetActive(false);
            cellTemplate.gameObject.SetActive(false);
            for (int i = 0; i < Tabs.Length; i++)
            {
                var tab = Instantiate(tabTemplate, tabTemplate.transform.parent);
                tab.name = $"Tab_{i}";
                tab.GetComponentInChildren<Text>(true).text = Tabs[i].title;
                tab.gameObject.SetActive(true);
                int index = i;
                tab.onClick.AddListener(() => ShowTab(index));
                tabButtons.Add(tab);
            }

            openButton.onClick.AddListener(Open);
            outsideArea.onClick.AddListener(Close);
            outsideArea.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed += OnWardrobeChanged;
        }

        void OnDestroy()
        {
            if (Wardrobe.Instance != null) Wardrobe.Instance.Changed -= OnWardrobeChanged;
        }

        public void Open()
        {
            if (state != State.Closed || Wardrobe.Instance == null || Wardrobe.Instance.Catalog == null) return;
            OpenAsync(destroyCancellationToken).Forget();
        }

        public void Close()
        {
            if (state != State.Open) return;
            CloseAsync(destroyCancellationToken).Forget();
        }

        async UniTaskVoid OpenAsync(CancellationToken cancellationToken)
        {
            state = State.Opening;
            // 움직이는 동안 로비 버튼이 눌리지 않게 바로 막는다. 막을 눌러 닫는 건 다 열린 뒤부터다.
            outsideArea.gameObject.SetActive(true);
            SetPanelInteractable(false);
            panel.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            ShowTab(0);
            SetPanelX(HiddenX);

            await MoveX(screen, ShiftX, shiftSeconds, cancellationToken);
            await MoveX(panel, ShownX, slideSeconds, cancellationToken);

            SetPanelInteractable(true);
            state = State.Open;
            Select(tabButtons[currentTab].gameObject);
        }

        async UniTaskVoid CloseAsync(CancellationToken cancellationToken)
        {
            state = State.Closing;
            SetPanelInteractable(false);

            await MoveX(panel, HiddenX, slideSeconds, cancellationToken);
            panel.gameObject.SetActive(false);
            await MoveX(screen, 0f, shiftSeconds, cancellationToken);

            outsideArea.gameObject.SetActive(false);
            state = State.Closed;
            Select(openButton.gameObject);
        }

        void Update()
        {
            if (state != State.Open) return;
            if (WasPressed(module => module.cancel))
            {
                Close();
                return;
            }
            KeepSelectionInside();
        }

        void LateUpdate()
        {
            // 열려 있는 동안 창 크기가 바뀌어도 밀린 거리·팝업 위치를 맞춘다.
            if (state != State.Open) return;
            SetScreenX(ShiftX);
            SetPanelX(ShownX);
        }

        void ShowTab(int index)
        {
            currentTab = index;
            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].image.color = i == index ? currentTabColor : tabColor;

            foreach (var section in sections)
            {
                section.SetActive(false); // Destroy는 프레임 끝에 일어나므로 레이아웃에서 바로 빼 둔다
                Destroy(section);
            }
            sections.Clear();
            cells.Clear();

            var catalog = Wardrobe.Instance.Catalog;
            foreach (var spec in Tabs[index].sections)
            {
                var section = Instantiate(sectionTemplate, sectionTemplate.parent);
                section.name = $"Section_{spec.Slot}";
                section.gameObject.SetActive(true);
                sections.Add(section.gameObject);

                var heading = section.GetComponentInChildren<Text>(true);
                heading.gameObject.SetActive(spec.Heading != null);
                heading.text = spec.Heading;

                var grid = section.GetComponentInChildren<GridLayoutGroup>(true).transform;
                foreach (var item in catalog.Items(spec.Slot))
                {
                    var cell = Instantiate(cellTemplate, grid);
                    cell.name = $"Cell_{item.id}";
                    cell.gameObject.SetActive(true);
                    cell.Bind(spec.Slot, item);
                    cell.Button.onClick.AddListener(() => Wardrobe.Instance.Equip(cell.Slot, cell.Item));
                    cells.Add(cell);
                }
            }
            RefreshCells();
            itemScroll.StopMovement();
            itemScroll.content.anchoredPosition = Vector2.zero; // 맨 위부터
        }

        void OnWardrobeChanged(WardrobeSlot slot)
        {
            if (state != State.Closed) RefreshCells();
        }

        void RefreshCells()
        {
            var wardrobe = Wardrobe.Instance;
            var body = (wardrobe.Equipped(WardrobeSlot.Body) as BodyItem)?.parts;
            var eyes = (wardrobe.Equipped(WardrobeSlot.Eyes) as EyesItem)?.parts;
            foreach (var cell in cells)
            {
                bool unlocked = Wardrobe.IsUnlocked(cell.Item);
                string lockText = unlocked ? null : $"'{StageName(cell.Item.unlockStageId)}' 클리어";
                cell.Refresh(wardrobe.Equipped(cell.Slot) == cell.Item, unlocked, lockText, body, eyes);
            }
        }

        string StageName(string stageId)
        {
            if (stageCatalog != null)
                foreach (var entry in stageCatalog.Stages)
                    if (entry.stageId == stageId) return entry.displayName;
            return stageId;
        }

        /// <summary>패드·키보드로 움직일 때 선택이 팝업 밖(뒤의 로비 버튼)으로 나가지 않게 한다.</summary>
        void KeepSelectionInside()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return;
            var selected = eventSystem.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(panel))
            {
                lastSelected = selected;
                return;
            }
            // 마우스로 빈 곳을 눌러 선택이 풀린 건 그대로 두고, 방향 입력이 오면 그때 되돌린다.
            if (selected != null || WasPressed(module => module.move))
                Select(lastSelected != null && lastSelected.activeInHierarchy ? lastSelected : tabButtons[currentTab].gameObject);
        }

        static bool WasPressed(Func<InputSystemUIInputModule, InputActionReference> pick)
        {
            var module = EventSystem.current != null ? EventSystem.current.currentInputModule as InputSystemUIInputModule : null;
            var action = module != null ? pick(module)?.action : null;
            return action != null && action.WasPressedThisFrame();
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
        }

        void SetPanelInteractable(bool interactable)
        {
            panelGroup.interactable = interactable;
            panelGroup.blocksRaycasts = interactable;
        }

        void SetScreenX(float x) => screen.anchoredPosition = new Vector2(x, screen.anchoredPosition.y);

        void SetPanelX(float x) => panel.anchoredPosition = new Vector2(x, panel.anchoredPosition.y);

        /// <summary>가로로만 옮기고 끝날 때까지 기다린다. 시간 배율과 무관하게 실제 시간으로 움직인다.</summary>
        UniTask MoveX(RectTransform target, float x, float seconds, CancellationToken cancellationToken) =>
            target.DOAnchorPosX(x, seconds).SetEase(ease).SetUpdate(true).SetLink(target.gameObject)
                .ToUniTask(TweenCancelBehaviour.KillAndCancelAwait, cancellationToken);
    }
}
