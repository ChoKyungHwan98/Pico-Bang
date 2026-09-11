using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 설정 패널을 씬에 만들어 넣고, 홈 화면 버튼에 동작을 연결한다.
/// 씬 YAML을 직접 건드리지 않고 정식 Unity API로만 작업한다.
/// </summary>
public static class PicoBangUIBuilder
{
    private const string ScenePath     = "Assets/Scenes/ShooterInGame.unity";
    private const string FontAssetPath = "Assets/Font/Jalnan2 SDF.asset";
    private const string PanelName     = "Settings_Panel";

    // 팔레트 — 로고의 주황을 강조색으로 가져왔다
    private static readonly Color Accent     = new Color(1.00f, 0.55f, 0.10f, 1f);
    private static readonly Color PanelBg    = new Color(0.10f, 0.11f, 0.15f, 0.97f);
    private static readonly Color DimBg      = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color TrackBg    = new Color(0.20f, 0.22f, 0.28f, 1f);
    private static readonly Color TextMain   = new Color(0.96f, 0.96f, 0.98f, 1f);
    private static readonly Color TextDim    = new Color(0.70f, 0.72f, 0.78f, 1f);
    private static readonly Color ButtonBg   = new Color(0.18f, 0.20f, 0.26f, 1f);

    private static TMP_FontAsset font;
    private static Sprite spriteUI;
    private static Sprite spriteBackground;
    private static Sprite spriteKnob;
    private static Sprite spriteCheckmark;

    [MenuItem("Pico-Bang/3. 설정 패널 생성 + 홈 버튼 연결")]
    public static void BuildSettingsPanel()
    {
        font             = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        spriteUI         = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        spriteBackground = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        spriteKnob       = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        spriteCheckmark  = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { Debug.LogError("[PicoBang] 'UI' 캔버스를 찾을 수 없음"); return; }

        // 이미 있으면 지우고 다시 만든다 (반복 실행 가능하게)
        Transform existing = uiRoot.transform.Find(PanelName);
        if (existing != null) { Object.DestroyImmediate(existing.gameObject); }

        SettingsMenu menu = CreatePanel(uiRoot.transform);

        WireHomeButtons(uiRoot.transform, menu);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 설정 패널 생성 및 버튼 연결 완료");
    }

    // ─────────────────────────────────────────────────────────

    private static SettingsMenu CreatePanel(Transform parent)
    {
        GameObject panel = NewUI(PanelName, parent);
        Stretch(panel.GetComponent<RectTransform>());
        panel.transform.SetAsLastSibling();          // 다른 UI 위에 뜨도록

        CanvasGroup group = panel.AddComponent<CanvasGroup>();
        SettingsMenu menu = panel.AddComponent<SettingsMenu>();
        menu.group = group;

        // 배경 어둡게 — 클릭 차단 역할도 한다
        GameObject dim = NewUI("Dim", panel.transform);
        Stretch(dim.GetComponent<RectTransform>());
        Image dimImg = dim.AddComponent<Image>();
        dimImg.color = DimBg;

        // 본체 창
        GameObject window = NewUI("Window", panel.transform);
        RectTransform wrt = window.GetComponent<RectTransform>();
        wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
        wrt.pivot = new Vector2(0.5f, 0.5f);
        wrt.anchoredPosition = Vector2.zero;
        wrt.sizeDelta = new Vector2(760f, 0f);   // 높이는 내용에 맞춰 자동

        Image winImg = window.AddComponent<Image>();
        winImg.sprite = spriteBackground;
        winImg.type = Image.Type.Sliced;
        winImg.color = PanelBg;

        VerticalLayoutGroup vlg = window.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(46, 46, 38, 38);
        vlg.spacing = 18f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        // childControlHeight를 끄면 LayoutElement의 높이가 통째로 무시된다.
        // 켜야 각 줄이 지정한 높이를 갖는다.
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // 내용이 늘어나도 창이 따라 커지도록 — 넘쳐서 잘리는 일이 없게
        ContentSizeFitter fitter = window.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목
        TMP_Text title = NewText("Title", window.transform, "설정", 52, TextAlignmentOptions.Center);
        title.color = Accent;
        SetHeight(title.gameObject, 72f);

        // 강조 밑줄
        GameObject rule = NewUI("Rule", window.transform);
        Image ruleImg = rule.AddComponent<Image>();
        ruleImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
        SetHeight(rule, 3f);

        Spacer(window.transform, 6f);

        menu.masterSlider      = SliderRow(window.transform, "전체 음량",   0f, 1f, out menu.masterValue);
        menu.bgmSlider         = SliderRow(window.transform, "배경음",     0f, 1f, out menu.bgmValue);
        menu.sfxSlider         = SliderRow(window.transform, "효과음",     0f, 1f, out menu.sfxValue);
        menu.sensitivitySlider = SliderRow(window.transform, "마우스 감도", 0.02f, 0.5f, out menu.sensitivityValue);

        Spacer(window.transform, 8f);

        menu.fullscreenToggle = ToggleRow(window.transform, "전체 화면");

        Spacer(window.transform, 14f);

        // 하단 버튼 두 개
        GameObject row = NewUI("ButtonRow", window.transform);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        SetHeight(row, 74f);

        menu.resetButton = MakeButton(row.transform, "기본값", ButtonBg, TextDim);
        menu.closeButton = MakeButton(row.transform, "닫기",   Accent,   new Color(0.12f, 0.09f, 0.05f));

        return menu;
    }

    private static void WireHomeButtons(Transform uiRoot, SettingsMenu menu)
    {
        Transform group = uiRoot.Find("Home_Panel/ButtonGroup");
        if (group == null) { Debug.LogWarning("[PicoBang] ButtonGroup을 못 찾음 — 버튼 연결 건너뜀"); return; }

        // 설정 버튼 → 설정 열기
        Button setting = group.Find("SettingButton")?.GetComponent<Button>();
        if (setting != null)
        {
            ClearPersistentListeners(setting.onClick);
            UnityEventTools.AddVoidPersistentListener(setting.onClick, new UnityAction(menu.Open));
            Debug.Log("[PicoBang] SettingButton 연결됨");
        }

        // 나가기 버튼 → GameFlowManager.OnExitClicked
        Button exit = group.Find("ExitButton")?.GetComponent<Button>();
        GameFlowManager flow = Object.FindFirstObjectByType<GameFlowManager>();
        if (exit != null && flow != null)
        {
            ClearPersistentListeners(exit.onClick);
            UnityEventTools.AddVoidPersistentListener(exit.onClick, new UnityAction(flow.OnExitClicked));
            Debug.Log("[PicoBang] ExitButton 연결됨");
        }
    }

    private static void ClearPersistentListeners(UnityEvent evt)
    {
        for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(evt, i);
        }
    }

    // ── 조립 헬퍼 ────────────────────────────────────────────

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) { return go; }
        }
        return null;
    }

    private static GameObject NewUI(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetHeight(GameObject go, float h)
    {
        LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
        le.flexibleHeight = 0f;
    }

    private static void Spacer(Transform parent, float h)
    {
        GameObject go = NewUI("Spacer", parent);
        SetHeight(go, h);
    }

    private static TMP_Text NewText(string name, Transform parent, string content, float size, TextAlignmentOptions align)
    {
        GameObject go = NewUI(name, parent);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) { t.font = font; }
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.color = TextMain;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    private static Slider SliderRow(Transform parent, string label, float min, float max, out TMP_Text valueLabel)
    {
        GameObject row = NewUI("Row_" + label, parent);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        SetHeight(row, 62f);

        TMP_Text lab = NewText("Label", row.transform, label, 30, TextAlignmentOptions.MidlineLeft);
        SetWidth(lab.gameObject, 220f);

        Slider slider = MakeSlider(row.transform, min, max);
        LayoutElement sle = slider.gameObject.AddComponent<LayoutElement>();
        sle.flexibleWidth = 1f;
        sle.minHeight = 28f;

        valueLabel = NewText("Value", row.transform, "-", 28, TextAlignmentOptions.MidlineRight);
        valueLabel.color = Accent;
        SetWidth(valueLabel.gameObject, 110f);

        return slider;
    }

    private static void SetWidth(GameObject go, float w)
    {
        LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = w;
        le.flexibleWidth = 0f;
    }

    private static Slider MakeSlider(Transform parent, float min, float max)
    {
        GameObject root = NewUI("Slider", parent);
        Slider slider = root.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;

        GameObject bg = NewUI("Background", root.transform);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 12f);
        bgRt.anchoredPosition = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.sprite = spriteBackground;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = TrackBg;

        GameObject fillArea = NewUI("Fill Area", root.transform);
        RectTransform faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.sizeDelta = new Vector2(-20f, 12f);
        faRt.anchoredPosition = Vector2.zero;

        GameObject fill = NewUI("Fill", fillArea.transform);
        RectTransform fillRt = fill.GetComponent<RectTransform>();
        fillRt.sizeDelta = new Vector2(10f, 0f);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.sprite = spriteUI;
        fillImg.type = Image.Type.Sliced;
        fillImg.color = Accent;

        GameObject handleArea = NewUI("Handle Slide Area", root.transform);
        RectTransform haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 0f);
        haRt.anchorMax = new Vector2(1f, 1f);
        haRt.sizeDelta = new Vector2(-20f, 0f);
        haRt.anchoredPosition = Vector2.zero;

        GameObject handle = NewUI("Handle", handleArea.transform);
        RectTransform hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(26f, 26f);
        Image hImg = handle.AddComponent<Image>();
        hImg.sprite = spriteKnob;
        hImg.color = Color.white;

        slider.fillRect = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = hImg;
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    private static Toggle ToggleRow(Transform parent, string label)
    {
        GameObject row = NewUI("Row_" + label, parent);
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        SetHeight(row, 62f);

        TMP_Text lab = NewText("Label", row.transform, label, 30, TextAlignmentOptions.MidlineLeft);
        SetWidth(lab.gameObject, 220f);

        GameObject tg = NewUI("Toggle", row.transform);
        SetWidth(tg, 46f);
        Toggle toggle = tg.AddComponent<Toggle>();

        GameObject bg = NewUI("Background", tg.transform);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(0f, 0.5f);
        bgRt.pivot = new Vector2(0f, 0.5f);
        bgRt.sizeDelta = new Vector2(40f, 40f);
        bgRt.anchoredPosition = Vector2.zero;
        Image bgImg = bg.AddComponent<Image>();
        bgImg.sprite = spriteUI;
        bgImg.type = Image.Type.Sliced;
        bgImg.color = TrackBg;

        GameObject check = NewUI("Checkmark", bg.transform);
        RectTransform cRt = check.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(30f, 30f);
        cRt.anchoredPosition = Vector2.zero;
        Image cImg = check.AddComponent<Image>();
        cImg.sprite = spriteCheckmark;
        cImg.color = Accent;

        toggle.targetGraphic = bgImg;
        toggle.graphic = cImg;

        return toggle;
    }

    private static Button MakeButton(Transform parent, string label, Color bg, Color textColor)
    {
        GameObject go = NewUI("Button_" + label, parent);
        Image img = go.AddComponent<Image>();
        img.sprite = spriteUI;
        img.type = Image.Type.Sliced;
        img.color = bg;

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        TMP_Text t = NewText("Text (TMP)", go.transform, label, 30, TextAlignmentOptions.Center);
        t.color = textColor;
        Stretch(t.rectTransform);

        return btn;
    }
}
