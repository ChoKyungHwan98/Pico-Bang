using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 홈 화면 시각 정리. 버튼 스타일 통일, 반응 추가, 로고에 미세한 움직임.
/// </summary>
public static class PicoBangHomePolish
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";
    private const string FontAssetPath = "Assets/Font/Jalnan2 SDF.asset";

    private static readonly Color Accent      = new Color(1.00f, 0.55f, 0.10f, 1f);
    private static readonly Color AccentText  = new Color(0.13f, 0.09f, 0.04f, 1f);
    private static readonly Color NeutralBg   = new Color(0.16f, 0.18f, 0.23f, 0.95f);
    private static readonly Color NeutralText = new Color(0.93f, 0.94f, 0.97f, 1f);
    private static readonly Color DangerBg    = new Color(0.24f, 0.14f, 0.16f, 0.95f);
    private static readonly Color DangerText  = new Color(0.95f, 0.72f, 0.72f, 1f);

    [MenuItem("Pico-Bang/4. 홈 화면 폴리싱")]
    public static void PolishHome()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        Sprite uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        GameObject uiRoot = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "UI") { uiRoot = go; break; }
        }
        if (uiRoot == null) { Debug.LogError("[PicoBang] UI 캔버스 없음"); return; }

        Transform home = uiRoot.transform.Find("Home_Panel");
        if (home == null) { Debug.LogError("[PicoBang] Home_Panel 없음"); return; }

        // ── 로고에 미세한 부유감 ──
        Transform title = home.Find("GameTitleImage");
        if (title != null && title.GetComponent<UIFloat>() == null)
        {
            title.gameObject.AddComponent<UIFloat>();
            Debug.Log("[PicoBang] 로고 부유 효과 추가");
        }

        // ── 버튼 그룹 정렬 ──
        Transform group = home.Find("ButtonGroup");
        if (group == null) { Debug.LogError("[PicoBang] ButtonGroup 없음"); return; }

        VerticalLayoutGroup vlg = group.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.spacing = 22f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            EditorUtility.SetDirty(vlg);
        }

        StyleButton(group.Find("StartButton"),   "게임 시작", Accent,    AccentText,  font, uiSprite, 40f, 92f);
        StyleButton(group.Find("SettingButton"), "설정",     NeutralBg, NeutralText, font, uiSprite, 34f, 78f);
        StyleButton(group.Find("ExitButton"),    "나가기",   DangerBg,  DangerText,  font, uiSprite, 34f, 78f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 홈 화면 폴리싱 완료");
    }

    private static void StyleButton(
        Transform t, string label, Color bg, Color textColor,
        TMP_FontAsset font, Sprite sprite, float fontSize, float height)
    {
        if (t == null) { Debug.LogWarning($"[PicoBang] 버튼 없음: {label}"); return; }

        Image img = t.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = bg;
            EditorUtility.SetDirty(img);
        }

        LayoutElement le = t.GetComponent<LayoutElement>();
        if (le == null) { le = t.gameObject.AddComponent<LayoutElement>(); }
        le.minHeight = le.preferredHeight = height;
        le.flexibleHeight = 0f;
        le.minWidth = le.preferredWidth = 420f;
        le.flexibleWidth = 0f;
        EditorUtility.SetDirty(le);

        Button btn = t.GetComponent<Button>();
        if (btn != null)
        {
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.selectedColor = Color.white;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            EditorUtility.SetDirty(btn);
        }

        if (t.GetComponent<UIHoverScale>() == null)
        {
            t.gameObject.AddComponent<UIHoverScale>();
        }

        TMP_Text txt = t.GetComponentInChildren<TMP_Text>(true);
        if (txt != null)
        {
            if (font != null) { txt.font = font; }
            txt.text = label;
            txt.fontSize = fontSize;
            txt.color = textColor;
            txt.alignment = TextAlignmentOptions.Center;
            RectTransform rt = txt.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            EditorUtility.SetDirty(txt);
        }
    }
}
