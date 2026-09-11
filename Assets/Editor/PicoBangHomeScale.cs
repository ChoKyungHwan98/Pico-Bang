using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 홈 화면 좌측 컬럼 크기 조정.
/// 캐릭터가 화면의 3분의 2를 차지하는데 로고와 글자가 너무 작아 눌려 보였다.
/// 제목이 화면에서 가장 강해야 하므로 로고를 키우고 글자 크기를 함께 올린다.
/// </summary>
public static class PicoBangHomeScale
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";

    [MenuItem("Pico-Bang/13. 홈 좌측 컬럼 크기 조정")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject uiRoot = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "UI") { uiRoot = go; break; }
        }
        if (uiRoot == null) { Debug.LogError("[PicoBang] UI 없음"); return; }

        Transform home = uiRoot.transform.Find("Home_Panel");
        if (home == null) { Debug.LogError("[PicoBang] Home_Panel 없음"); return; }

        // 로고 — 제목이 가장 강해야 한다
        SetAnchors(home.Find("GameTitleImage"), 0.050f, 0.585f, 0.430f, 0.955f);

        // 설명 — 로고 바로 아래, 읽히는 크기로
        Transform sub = home.Find("Tagline_Sub");
        SetAnchors(sub, 0.055f, 0.495f, 0.500f, 0.570f);
        SetFontSize(sub, 30f);

        // 버튼 그룹 — 폭을 조금 넓혀 로고와 균형
        SetAnchors(home.Find("ButtonGroup"), 0.055f, 0.155f, 0.310f, 0.455f);

        // 조작 안내 — 화면 밑에 붙어 잘리던 것을 띄우고 키운다
        Transform hint = home.Find("Controls_Hint");
        SetAnchors(hint, 0.055f, 0.075f, 0.600f, 0.140f);
        SetFontSize(hint, 25f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 홈 좌측 컬럼 크기 조정 완료");
    }

    private static void SetAnchors(Transform t, float xMin, float yMin, float xMax, float yMax)
    {
        if (t == null) { Debug.LogWarning("[PicoBang] 대상 없음"); return; }
        RectTransform rt = t.GetComponent<RectTransform>();
        if (rt == null) { return; }
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        EditorUtility.SetDirty(rt);
    }

    private static void SetFontSize(Transform t, float size)
    {
        if (t == null) { return; }
        TMP_Text txt = t.GetComponent<TMP_Text>();
        if (txt == null) { return; }
        txt.fontSize = size;
        EditorUtility.SetDirty(txt);
    }
}
