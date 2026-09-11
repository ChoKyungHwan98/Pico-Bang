using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 홈 화면 재구성.
///
/// 문제였던 것:
///   1) 배경이 게임 화면 그대로라 "만드는 중"으로 읽혔다
///   2) 좌측 쏠림이 의도된 비대칭이 아니라 정렬 실수처럼 보였다
///   3) 무슨 게임인지 알 수 없었다
///
/// 해결:
///   - 전용 카메라 앵글 — 캐릭터를 아래에서 올려다보는 히어로 샷. 배경은 하늘.
///   - 좌측 스크림(3단 계조)으로 글자 영역만 어둡게. 캐릭터는 밝게 남긴다.
///   - 태그라인 + 조작 안내를 넣어 첫 화면에서 게임이 설명되게.
/// </summary>
public static class PicoBangHomeStage
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";
    private const string FontAssetPath = "Assets/Font/Jalnan2 SDF.asset";

    private static readonly Color Accent   = new Color(1.00f, 0.55f, 0.10f, 1f);
    private static readonly Color TextMain = new Color(0.94f, 0.95f, 0.97f, 1f);
    private static readonly Color TextDim  = new Color(0.66f, 0.69f, 0.76f, 1f);
    private static readonly Color Scrim    = new Color(0.04f, 0.045f, 0.07f, 1f);

    // 카메라 프레이밍 — 값이 마음에 안 들면 씬에서 IntroCamPos를 직접 옮기면 된다
    private const float CamDistance   = 2.45f;   // 캐릭터 앞으로 떨어지는 거리
    private const float CamHeight     = 0.72f;   // 낮게 깔아 올려다보는 각
    private const float CamLookHeight = 1.45f;   // 머리 근처를 본다
    private const float CamYawOffset  = 15f;     // 캐릭터를 화면 우측으로 밀어내는 각

    [MenuItem("Pico-Bang/7. 홈 화면 무대 재구성")]
    public static void Rebuild()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        SetupCamera(scene);
        SetupLayout(scene, font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 홈 화면 무대 재구성 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  전용 카메라 앵글
    // ─────────────────────────────────────────────────────────
    private static void SetupCamera(Scene scene)
    {
        GameObject introCam = FindRoot(scene, "IntroCamPos");
        GameObject startPos = FindRoot(scene, "PlayerStartPos");

        if (introCam == null || startPos == null)
        {
            Debug.LogWarning("[PicoBang] IntroCamPos / PlayerStartPos 를 찾지 못해 카메라 배치를 건너뜀");
            return;
        }

        Vector3 p = startPos.transform.position;
        Vector3 f = startPos.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.001f) { f = Vector3.forward; }
        f.Normalize();

        // 캐릭터 정면에 서서 낮은 위치에서 올려다본다.
        // 위를 향하면 배경이 벽이 아니라 하늘이 되어 프로토타입 격자가 사라진다.
        Vector3 camPos = p + f * CamDistance + Vector3.up * CamHeight;
        Vector3 lookAt = p + Vector3.up * CamLookHeight;

        introCam.transform.position = camPos;
        introCam.transform.rotation = Quaternion.LookRotation(lookAt - camPos, Vector3.up);

        // 카메라를 왼쪽으로 틀면 피사체가 화면 오른쪽으로 밀린다 → 좌측에 글자 자리가 생긴다
        introCam.transform.Rotate(0f, -CamYawOffset, 0f, Space.World);

        EditorUtility.SetDirty(introCam);
        Debug.Log($"[PicoBang] 홈 카메라 배치: pos={camPos}, 캐릭터 우측 배치");
    }

    // ─────────────────────────────────────────────────────────
    //  레이아웃
    // ─────────────────────────────────────────────────────────
    private static void SetupLayout(Scene scene, TMP_FontAsset font)
    {
        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { Debug.LogError("[PicoBang] UI 캔버스 없음"); return; }

        Transform home = uiRoot.transform.Find("Home_Panel");
        if (home == null) { Debug.LogError("[PicoBang] Home_Panel 없음"); return; }

        // ── 좌측 스크림 ──
        // 화면 전체를 덮으면 캐릭터까지 어두워진다. 글자가 놓이는 좌측만 덮고
        // 오른쪽으로 3단에 걸쳐 옅어지게 해서 그라데이션처럼 보이게 한다.
        Transform dim = home.Find("Dim_Layer");
        if (dim != null)
        {
            Image dimImg = dim.GetComponent<Image>();
            if (dimImg != null)
            {
                dimImg.color = new Color(Scrim.r, Scrim.g, Scrim.b, 0.80f);
                EditorUtility.SetDirty(dimImg);
            }
            SetAnchors(dim.GetComponent<RectTransform>(), 0f, 0f, 0.60f, 1f);
            dim.SetAsFirstSibling();
        }

        MakeFeather(home, "Scrim_Feather_1", 0.60f, 0.70f, 0.46f);
        MakeFeather(home, "Scrim_Feather_2", 0.70f, 0.78f, 0.22f);
        MakeFeather(home, "Scrim_Feather_3", 0.78f, 0.86f, 0.08f);

        // ── 로고 ──
        Transform title = home.Find("GameTitleImage");
        if (title != null)
        {
            SetAnchors(title.GetComponent<RectTransform>(), 0.055f, 0.60f, 0.52f, 0.93f);
            Image ti = title.GetComponent<Image>();
            if (ti != null) { ti.preserveAspect = true; EditorUtility.SetDirty(ti); }
            title.SetAsLastSibling();
        }

        // ── 태그라인 ──
        TMP_Text tagline = EnsureText(home, "Tagline", font, 34f, TextAlignmentOptions.MidlineLeft);
        tagline.text = "달려라. 부숴라. 들키지 마라.";
        tagline.color = Accent;
        SetAnchors(tagline.rectTransform, 0.06f, 0.525f, 0.56f, 0.60f);

        TMP_Text sub = EnsureText(home, "Tagline_Sub", font, 24f, TextAlignmentOptions.TopLeft);
        sub.text = "과녁을 부숴 출구를 열고, 다섯 마리의 추격자를 따돌려 탈출하라.";
        sub.color = TextDim;
        SetAnchors(sub.rectTransform, 0.06f, 0.455f, 0.58f, 0.525f);

        // ── 버튼 그룹 ──
        Transform group = home.Find("ButtonGroup");
        if (group != null)
        {
            SetAnchors(group.GetComponent<RectTransform>(), 0.06f, 0.15f, 0.40f, 0.43f);
            VerticalLayoutGroup vlg = group.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childAlignment = TextAnchor.MiddleLeft;
                vlg.spacing = 18f;
                vlg.childForceExpandHeight = false;
                EditorUtility.SetDirty(vlg);
            }
            group.SetAsLastSibling();
        }

        // ── 조작 안내 ──
        TMP_Text controls = EnsureText(home, "Controls_Hint", font, 22f, TextAlignmentOptions.BottomLeft);
        controls.text = "<color=#FF8C1A>WASD</color> 이동    <color=#FF8C1A>Shift</color> 달리기    " +
                        "<color=#FF8C1A>Space</color> 점프    <color=#FF8C1A>좌클릭</color> 사격";
        controls.color = TextDim;
        controls.richText = true;
        SetAnchors(controls.rectTransform, 0.06f, 0.055f, 0.62f, 0.115f);

        Debug.Log("[PicoBang] 홈 레이아웃 재배치 완료");
    }

    // ─────────────────────────────────────────────────────────

    private static void MakeFeather(Transform parent, string name, float xMin, float xMax, float alpha)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null) { go = existing.gameObject; }
        else
        {
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
        }

        Image img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = new Color(Scrim.r, Scrim.g, Scrim.b, alpha);
        img.raycastTarget = false;

        SetAnchors(go.GetComponent<RectTransform>(), xMin, 0f, xMax, 1f);
        go.transform.SetSiblingIndex(1);
        EditorUtility.SetDirty(go);
    }

    private static TMP_Text EnsureText(Transform parent, string name, TMP_FontAsset font, float size, TextAlignmentOptions align)
    {
        Transform existing = parent.Find(name);
        GameObject go;
        if (existing != null) { go = existing.gameObject; }
        else
        {
            go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
        }

        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
        if (font != null) { t.font = font; }
        t.fontSize = size;
        t.alignment = align;
        t.raycastTarget = false;
        t.color = TextMain;
        go.transform.SetAsLastSibling();
        EditorUtility.SetDirty(t);
        return t;
    }

    private static void SetAnchors(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        if (rt == null) { return; }
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        EditorUtility.SetDirty(rt);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) { return go; }
        }
        return null;
    }
}
