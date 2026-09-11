using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 2차 폴리싱: 크로스헤어 / 게임오버·클리어 화면 / 발사체 튜닝.
/// </summary>
public static class PicoBangPolish2
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";
    private const string FontAssetPath = "Assets/Font/Jalnan2 SDF.asset";

    private static readonly Color Accent     = new Color(1.00f, 0.55f, 0.10f, 1f);
    private static readonly Color AccentText = new Color(0.13f, 0.09f, 0.04f, 1f);
    private static readonly Color DeepBg     = new Color(0.055f, 0.06f, 0.085f, 1f);
    private static readonly Color OverRed    = new Color(1.00f, 0.35f, 0.32f, 1f);

    [MenuItem("Pico-Bang/5. 크로스헤어 + 결과화면 + 발사체 튜닝")]
    public static void RunAll()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        BuildCrosshair(scene);
        PolishResultPanels(scene);
        TuneProjectile();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 2차 폴리싱 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  크로스헤어
    // ─────────────────────────────────────────────────────────
    private static void BuildCrosshair(Scene scene)
    {
        GameObject root = FindRoot(scene, "Crosshair");
        if (root == null) { Debug.LogWarning("[PicoBang] Crosshair 루트 없음"); return; }

        // 기존 내용 비우고 다시 만든다
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        }
        foreach (CrosshairFx old in root.GetComponents<CrosshairFx>())
        {
            Object.DestroyImmediate(old);
        }

        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        GameObject reticle = NewUI("Reticle", root.transform);
        RectTransform rrt = reticle.GetComponent<RectTransform>();
        rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = Vector2.zero;
        rrt.sizeDelta = Vector2.zero;

        CrosshairFx fx = root.AddComponent<CrosshairFx>();

        // 눈금 4개 — 위 / 아래 / 왼쪽 / 오른쪽
        const float thick = 5f;
        const float length = 16f;
        RectTransform[] ticks = new RectTransform[4];
        Graphic[] tint = new Graphic[5];

        ticks[0] = MakeTick(reticle.transform, "Tick_Up",    new Vector2(thick, length), sprite, out tint[0]);
        ticks[1] = MakeTick(reticle.transform, "Tick_Down",  new Vector2(thick, length), sprite, out tint[1]);
        ticks[2] = MakeTick(reticle.transform, "Tick_Left",  new Vector2(length, thick), sprite, out tint[2]);
        ticks[3] = MakeTick(reticle.transform, "Tick_Right", new Vector2(length, thick), sprite, out tint[3]);

        // 중심점 — 조준 기준이 명확해야 한다
        RectTransform dot = MakeTick(reticle.transform, "Dot", new Vector2(5f, 5f), sprite, out tint[4]);
        dot.anchoredPosition = Vector2.zero;

        fx.ticks = ticks;
        fx.tintTargets = tint;
        fx.baseGap = 18f;
        fx.kickGap = 34f;

        EditorUtility.SetDirty(root);
        Debug.Log("[PicoBang] 크로스헤어 재구성 완료");
    }

    private static RectTransform MakeTick(Transform parent, string name, Vector2 size, Sprite sprite, out Graphic graphic)
    {
        GameObject go = NewUI(name, parent);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;

        // 밝은 벽에서도 보이도록 어두운 외곽선을 깐다
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);

        graphic = img;
        return rt;
    }

    // ─────────────────────────────────────────────────────────
    //  게임오버 / 클리어 화면
    // ─────────────────────────────────────────────────────────
    private static void PolishResultPanels(Scene scene)
    {
        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { return; }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        PolishPanel(uiRoot.transform.Find("GameOver_Panel"), "GAME OVER", OverRed, font, sprite);
        PolishPanel(uiRoot.transform.Find("GameClear_Panel"), null, Accent, font, sprite);
    }

    private static void PolishPanel(Transform panel, string titleText, Color titleColor, TMP_FontAsset font, Sprite sprite)
    {
        if (panel == null) { return; }

        // 화면을 완전히 덮게 — 가장자리로 게임 화면이 비쳐 보이던 문제
        RectTransform rt = panel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.anchoredPosition3D = Vector3.zero;
        }

        Image bg = panel.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = null;
            bg.type = Image.Type.Simple;
            bg.color = DeepBg;      // 완전 불투명
            EditorUtility.SetDirty(bg);
        }

        // 제목
        Transform title = panel.Find("Title_Text");
        if (title != null)
        {
            TMP_Text t = title.GetComponent<TMP_Text>();
            if (t != null)
            {
                if (font != null) { t.font = font; }
                if (!string.IsNullOrEmpty(titleText)) { t.text = titleText; }
                t.color = titleColor;
                t.fontSize = 96f;
                t.alignment = TextAlignmentOptions.Center;
                t.characterSpacing = 6f;
                EditorUtility.SetDirty(t);
            }
        }

        // 버튼 — 홈 화면과 같은 언어로
        foreach (Button btn in panel.GetComponentsInChildren<Button>(true))
        {
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = Accent;
                EditorUtility.SetDirty(img);
            }

            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            EditorUtility.SetDirty(btn);

            if (btn.GetComponent<UIHoverScale>() == null)
            {
                btn.gameObject.AddComponent<UIHoverScale>();
            }

            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minHeight = le.preferredHeight = 84f;
                le.minWidth = le.preferredWidth = 360f;
                EditorUtility.SetDirty(le);
            }
            else
            {
                RectTransform brt = btn.GetComponent<RectTransform>();
                if (brt != null) { brt.sizeDelta = new Vector2(360f, 84f); }
            }

            TMP_Text bt = btn.GetComponentInChildren<TMP_Text>(true);
            if (bt != null)
            {
                if (font != null) { bt.font = font; }
                bt.color = AccentText;
                bt.fontSize = 36f;
                bt.alignment = TextAlignmentOptions.Center;
                RectTransform trt = bt.rectTransform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                EditorUtility.SetDirty(bt);
            }
        }

        Debug.Log($"[PicoBang] {panel.name} 폴리싱 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  발사체 속도
    // ─────────────────────────────────────────────────────────
    private static void TuneProjectile()
    {
        PlayerShooter shooter = Object.FindFirstObjectByType<PlayerShooter>();
        if (shooter == null) { Debug.LogWarning("[PicoBang] PlayerShooter 없음"); return; }

        SerializedObject so = new SerializedObject(shooter);
        SetFloat(so, "projectileSpeed", 135f);
        SetFloat(so, "projectileTrailTime", 0.09f);
        SetFloat(so, "projectileSize", 0.24f);
        SetFloat(so, "muzzleFlashLength", 2.2f);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(shooter);
        Debug.Log("[PicoBang] 발사체 속도 135로 상향");
    }

    private static void SetFloat(SerializedObject so, string prop, float value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null) { p.floatValue = value; }
        else { Debug.LogWarning($"[PicoBang] 프로퍼티 없음: {prop}"); }
    }

    // ─────────────────────────────────────────────────────────

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
}
