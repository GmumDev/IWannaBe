using IWannabe.Otamaton;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace IWannabe.EditorTools
{
    /// <summary>로비·플레이 씬과 로딩 화면의 uGUI를 코드로 만드는 도우미.</summary>
    static class SetupUi
    {
        public static readonly Color Ink = new Color(0.15f, 0.27f, 0.33f);
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);

        static Font font;
        static Sprite roundedSprite;

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static Sprite Rounded => roundedSprite != null ? roundedSprite : roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rect = Rect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text Label(RectTransform rect, string text, int size, TextAnchor alignment, Color color, FontStyle style = FontStyle.Normal)
        {
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Image Panel(RectTransform rect, Color color, bool rounded)
        {
            var image = rect.gameObject.AddComponent<Image>();
            if (rounded)
            {
                image.sprite = Rounded;
                image.type = Image.Type.Sliced;
            }
            image.color = color;
            return image;
        }

        public static Button MakeButton(string name, Transform parent, string text, Vector2 size, int fontSize)
        {
            var rect = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var image = Panel(rect, Color.white, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyButtonColors(button);

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            Label(Stretch("Label", rect), text, fontSize, TextAnchor.MiddleCenter, Ink, FontStyle.Bold);
            return button;
        }

        public static void ApplyButtonColors(Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.54f);
            colors.selectedColor = new Color(1f, 0.88f, 0.54f);
            colors.pressedColor = new Color(0.96f, 0.64f, 0.38f);
            button.colors = colors;
        }

        /// <summary>세로로만 스크롤되는 영역. 내용(Content)은 위에 붙어 자식 크기만큼 늘어나고, 영역 밖은 잘린다.</summary>
        public static ScrollRect VerticalScroll(RectTransform rect, out RectTransform content)
        {
            // 빈 곳을 끌어도 스크롤되도록 투명한 바탕으로 입력을 받는다.
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;

            var viewport = Stretch("Viewport", rect);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect("Content", viewport, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        /// <summary>자식을 위에서부터 쌓고 너비를 가득 채우는 세로 레이아웃. 높이는 자식의 선호 높이를 따른다.</summary>
        public static VerticalLayoutGroup Stack(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        /// <summary>몸통·눈 Image 두 장을 겹친 <see cref="OtamatonImage"/>. 눈이 뒤 형제라 몸통 앞에 그려진다.</summary>
        public static OtamatonImage OtamatonUi(RectTransform rect, bool raycast)
        {
            var view = rect.gameObject.AddComponent<OtamatonImage>();
            var body = OtamatonPart(rect, "Body", raycast);
            var eye = OtamatonPart(rect, "Eye", false);
            SetupUtil.Assign(view, ("bodyImage", body), ("eyeImage", eye));
            return view;
        }

        static Image OtamatonPart(RectTransform parent, string name, bool raycast)
        {
            // 이미지는 실행 중에 장착한 파츠로 채운다. 씬이 직접 참조하면 번들과 앱 데이터에 두 벌이 된다.
            var image = Stretch(name, parent).gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = raycast;
            image.enabled = false;
            return image;
        }

        public static VerticalLayoutGroup Column(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing)
        {
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }
    }
}
