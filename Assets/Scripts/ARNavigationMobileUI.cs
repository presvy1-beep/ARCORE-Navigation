using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds a touch-sized portrait AR navigation interface over the camera background.</summary>
[DefaultExecutionOrder(100)]
public sealed class ARNavigationMobileUI : MonoBehaviour
{
    private static readonly Color Ink = new Color32(29, 45, 49, 255);
    private static readonly Color MutedInk = new Color32(89, 108, 109, 255);
    private static readonly Color Accent = new Color32(18, 132, 119, 255);
    private static readonly Color Paper = new Color32(248, 248, 244, 245);
    private static readonly Color Soft = new Color32(232, 240, 236, 255);
    private ARNavigationController controller;
    private Canvas canvas;
    private RectTransform safeAreaRoot;
    private RectTransform pickerSafeAreaRoot;
    private GameObject picker;
    private Transform pickerContent;
    private RectTransform pickerCardRect;
    private RectTransform pickerViewportRect;
    private RectTransform pickerContentRect;
    private ScrollRect scrollRect;
    private TMP_Text pickerTitle;
    private TMP_Text startLabel;
    private TMP_Text destinationLabel;
    private TMP_Text trackingLabel;
    private TMP_Text instructionLabel;
    private TMP_Text distanceLabel;
    private Button calibrateButton;
    private Button startButton;
    private Button cancelButton;
    private bool pickingStart;
    private readonly List<GameObject> pickerEntries = new List<GameObject>();
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private const int PickerRowHeight = 76;
    private const int PickerRowSpacing = 8;

    private void Start()
    {
        controller = GetComponent<ARNavigationController>();
        if (controller == null) controller = FindAnyObjectByType<ARNavigationController>();
        if (controller == null) { enabled = false; return; }
        var oldCanvas = GameObject.Find("UI Canvas");
        if (oldCanvas != null) oldCanvas.SetActive(false);
        CreateInterface();
        RefreshInterface();
    }

    private void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || Screen.safeArea != lastSafeArea)
        {
            ApplySafeArea();
            UpdatePickerGeometry();
        }
        RefreshInterface();
    }

    private void CreateInterface()
    {
        var canvasObject = new GameObject("AR Navigation Mobile Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0f;

        var safeObject = new GameObject("Safe Area Content", typeof(RectTransform));
        safeObject.transform.SetParent(canvas.transform, false);
        safeAreaRoot = safeObject.GetComponent<RectTransform>();
        ApplySafeArea();

        var topTitle = MakeText("SECOND FLOOR  ·  MAHINDRA UNIVERSITY", 24, FontStyles.Bold, Ink);
        topTitle.transform.SetParent(safeAreaRoot, false);
        Place(topTitle.rectTransform, new Vector2(0.045f, 0.945f), new Vector2(0.955f, 0.99f), Vector2.zero, Vector2.zero);
        topTitle.alignment = TextAlignmentOptions.MidlineLeft;

        var panelObject = new GameObject("Navigation Control Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panelObject.transform.SetParent(safeAreaRoot, false);
        var panelRect = panelObject.GetComponent<RectTransform>();
        Place(panelRect, new Vector2(0.035f, 0.025f), new Vector2(0.965f, 0.505f), Vector2.zero, Vector2.zero);
        panelObject.GetComponent<Image>().color = Paper;
        var layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 20, 20);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var heading = AddTextRow(panelObject.transform, "Plan your route", 34, 44, FontStyles.Bold, Ink);
        heading.alignment = TextAlignmentOptions.MidlineLeft;

        var tracking = CreateSurfaceRow(panelObject.transform, "Tracking status", 54, new Color32(239, 243, 239, 255));
        trackingLabel = MakeText("Checking AR tracking…", 20, FontStyles.Normal, MutedInk);
        trackingLabel.transform.SetParent(tracking.transform, false);
        Place(trackingLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 0), new Vector2(-16, 0));
        trackingLabel.fontSize = 20;
        trackingLabel.fontStyle = FontStyles.Normal;

        startLabel = CreateActionButton(panelObject.transform, "Choose starting location", 78, false, OpenStartPicker, 23).GetComponentInChildren<TMP_Text>();
        destinationLabel = CreateActionButton(panelObject.transform, "Choose destination", 78, false, OpenDestinationPicker, 23).GetComponentInChildren<TMP_Text>();

        var guidanceSurface = CreateSurfaceRow(panelObject.transform, "Navigation guidance", 136, Soft);
        var guidanceGroup = guidanceSurface.AddComponent<VerticalLayoutGroup>();
        guidanceGroup.padding = new RectOffset(16, 16, 10, 10);
        guidanceGroup.spacing = 4;
        guidanceGroup.childControlWidth = true;
        guidanceGroup.childControlHeight = true;
        guidanceGroup.childForceExpandHeight = false;
        instructionLabel = MakeText("Select a start location.", 27, FontStyles.Bold, Ink);
        instructionLabel.textWrappingMode = TextWrappingModes.Normal;
        instructionLabel.transform.SetParent(guidanceSurface.transform, false);
        AddLayout(instructionLabel.gameObject, -1, 72);
        distanceLabel = MakeText("Route distance will appear here", 20, FontStyles.Normal, MutedInk);
        distanceLabel.transform.SetParent(guidanceSurface.transform, false);
        AddLayout(distanceLabel.gameObject, -1, 32);

        calibrateButton = CreateActionButton(panelObject.transform, "Calibrate Map", 76, false, controller.CalibrateAlignment, 22).GetComponent<Button>();
        startButton = CreateActionButton(panelObject.transform, "Start Navigation", 82, true, controller.StartNavigation, 24).GetComponent<Button>();
        cancelButton = CreateActionButton(panelObject.transform, "Cancel Navigation", 68, false, controller.CancelNavigation, 20).GetComponent<Button>();

        CreatePicker();
    }

    private void CreatePicker()
    {
        picker = new GameObject("Location Picker", typeof(RectTransform), typeof(Image), typeof(Button));
        picker.transform.SetParent(canvas.transform, false);
        var rect = picker.GetComponent<RectTransform>();
        Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var backdrop = picker.GetComponent<Image>();
        backdrop.color = new Color(0.035f, 0.075f, 0.08f, 0.76f);
        picker.GetComponent<Button>().targetGraphic = backdrop;
        picker.GetComponent<Button>().onClick.AddListener(ClosePicker);

        var pickerSafeObject = new GameObject("Picker Safe Area", typeof(RectTransform));
        pickerSafeObject.transform.SetParent(picker.transform, false);
        pickerSafeAreaRoot = pickerSafeObject.GetComponent<RectTransform>();
        ApplySafeArea();

        var card = new GameObject("Picker Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        card.transform.SetParent(pickerSafeAreaRoot, false);
        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        pickerCardRect = cardRect;
        card.GetComponent<Image>().color = new Color32(248, 248, 244, 255);
        var layout = card.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 12;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = new GameObject("Picker Header", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.transform.SetParent(card.transform, false);
        AddLayout(header, -1, 58);
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 12;
        headerLayout.childAlignment = TextAnchor.MiddleCenter;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        pickerTitle = AddTextRow(header.transform, "Choose a room", 28, 54, FontStyles.Bold, Ink);
        pickerTitle.alignment = TextAlignmentOptions.MidlineLeft;
        pickerTitle.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var closeButton = CreateActionButton(header.transform, "Close", 56, false, ClosePicker, 19);
        closeButton.GetComponent<LayoutElement>().preferredWidth = 150f;
        closeButton.GetComponent<LayoutElement>().minWidth = 120f;

        var scrollObject = new GameObject("Room List", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
        scrollObject.transform.SetParent(card.transform, false);
        scrollObject.GetComponent<LayoutElement>().minHeight = 260f;
        scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollObject.transform, false);
        var viewportRect = viewport.GetComponent<RectTransform>();
        Place(viewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.GetComponent<Image>().color = new Color32(255, 255, 255, 185);
        pickerViewportRect = viewportRect;
        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        content.transform.SetParent(viewport.transform, false);
        pickerContent = content.transform;
        var contentRect = content.GetComponent<RectTransform>();
        Place(contentRect, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
        contentRect.pivot = new Vector2(0.5f, 1f);
        pickerContentRect = contentRect;
        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(10, 10, 10, 10);
        contentLayout.spacing = PickerRowSpacing;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.content = contentRect;
        scrollRect.viewport = viewportRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 48f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Build the seven buttons once from the controller's actual waypoint references.
        for (int i = 0; i < controller.RoomCount; i++)
        {
            int index = i;
            var buttonObject = CreateActionButton(pickerContent, controller.GetRoomName(i), PickerRowHeight, false, () =>
            {
                if (pickingStart) controller.SelectStartRoom(index);
                else controller.SelectDestination(index);
                ClosePicker();
                RefreshInterface();
            }, 21);
            pickerEntries.Add(buttonObject);
        }

        UpdatePickerGeometry();
        picker.SetActive(false);
    }

    private void OpenStartPicker()
    {
        pickingStart = true;
        PopulatePicker("Choose your current location");
    }

    private void OpenDestinationPicker()
    {
        pickingStart = false;
        PopulatePicker("Choose your destination");
    }

    private void PopulatePicker(string title)
    {
        pickerTitle.text = title;
        UpdatePickerGeometry();
        picker.SetActive(true);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(pickerCardRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(pickerContentRect);
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    private void ClosePicker() { if (picker != null) picker.SetActive(false); }

    private void ApplySafeArea()
    {
        if (safeAreaRoot == null) return;
        Rect safeArea = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0 || safeArea.width <= 0f || safeArea.height <= 0f)
            safeArea = new Rect(0, 0, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        safeAreaRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
        if (pickerSafeAreaRoot != null)
        {
            pickerSafeAreaRoot.anchorMin = safeAreaRoot.anchorMin;
            pickerSafeAreaRoot.anchorMax = safeAreaRoot.anchorMax;
            pickerSafeAreaRoot.offsetMin = Vector2.zero;
            pickerSafeAreaRoot.offsetMax = Vector2.zero;
        }
        lastSafeArea = safeArea;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
    }

    private void UpdatePickerGeometry()
    {
        if (pickerCardRect == null || pickerSafeAreaRoot == null || pickerContentRect == null) return;
        Canvas.ForceUpdateCanvases();
        float safeWidth = pickerSafeAreaRoot.rect.width;
        float safeHeight = pickerSafeAreaRoot.rect.height;
        float width = Mathf.Min(960f, Mathf.Max(300f, safeWidth - 48f));
        float contentHeight = 20f + controller.RoomCount * PickerRowHeight + Mathf.Max(0, controller.RoomCount - 1) * PickerRowSpacing;
        float desiredCardHeight = 16f + 58f + 12f + contentHeight + 16f;
        float cardHeight = Mathf.Min(desiredCardHeight, Mathf.Max(560f, safeHeight - 48f));
        pickerCardRect.sizeDelta = new Vector2(width, cardHeight);
        pickerContentRect.sizeDelta = new Vector2(0f, contentHeight);
        if (pickerViewportRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(pickerViewportRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(pickerContentRect);
    }

    private void RefreshInterface()
    {
        if (controller == null || canvas == null) return;
        startLabel.text = string.IsNullOrEmpty(controller.SelectedStartName) ? "Choose starting location" : $"START  ·  {controller.SelectedStartName}";
        destinationLabel.text = string.IsNullOrEmpty(controller.SelectedDestinationName) ? "Choose destination" : $"DESTINATION  ·  {controller.SelectedDestinationName}";
        trackingLabel.text = controller.TrackingMessage;
        trackingLabel.color = controller.TrackingReady ? Accent : new Color32(143, 89, 45, 255);
        instructionLabel.text = controller.GuidanceMessage;
        if (controller.RemainingDistance > 0f && controller.IsNavigating)
            distanceLabel.text = $"{controller.RemainingDistance:0.0} m remaining";
        else distanceLabel.text = controller.StatusMessage;
        calibrateButton.interactable = controller.HasStart && controller.TrackingReady;
        var calibrateText = calibrateButton.GetComponentInChildren<TMP_Text>();
        calibrateText.text = controller.IsCalibrated ? "Recalibrate Map" : "Calibrate Map";
        startButton.interactable = controller.CanStartNavigation;
        startButton.GetComponentInChildren<TMP_Text>().text = controller.IsNavigating ? "Navigation Active" : "Start Navigation";
        cancelButton.interactable = controller.IsNavigating;
    }

    private GameObject CreateActionButton(Transform parent, string label, int height, bool primary, UnityEngine.Events.UnityAction action, float fontSize)
    {
        var obj = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        AddLayout(obj, -1, height);
        var image = obj.GetComponent<Image>();
        image.color = primary ? Accent : Color.white;
        var button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var colors = button.colors;
        colors.normalColor = primary ? Accent : Color.white;
        colors.highlightedColor = primary ? new Color32(25, 151, 136, 255) : Soft;
        colors.pressedColor = new Color32(205, 225, 218, 255);
        colors.disabledColor = new Color32(182, 193, 188, 210);
        button.colors = colors;
        var text = MakeText(label, fontSize, FontStyles.Bold, primary ? Color.white : Ink);
        text.alignment = TextAlignmentOptions.Center;
        text.transform.SetParent(obj.transform, false);
        Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 8), new Vector2(-20, -8));
        return obj;
    }

    private GameObject CreateSurfaceRow(Transform parent, string name, int height, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        AddLayout(obj, -1, height);
        obj.GetComponent<Image>().color = color;
        return obj;
    }

    private TMP_Text AddTextRow(Transform parent, string value, float size, int height, FontStyles style, Color color)
    {
        var text = MakeText(value, size, style, color);
        text.transform.SetParent(parent, false);
        AddLayout(text.gameObject, -1, height);
        return text;
    }

    private static TMP_Text MakeText(string value, float size, FontStyles style, Color color)
    {
        var obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var text = obj.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static void AddLayout(GameObject obj, float width, float height)
    {
        var element = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
