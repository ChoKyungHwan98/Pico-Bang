using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 홈 화면 표정/스크림 조정.
///   - 얼굴 블렌드셰이프 목록 덤프 (어떤 표정을 쓸지 고르기 위해)
///   - 계단처럼 보이던 그라데이션 스크림 제거
/// </summary>
public static class PicoBangFace
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";

    [MenuItem("Pico-Bang/11. 표정 목록 덤프 + 그라데이션 제거")]
    public static void RunAll()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        DumpBlendShapes(scene);
        RemoveGradient(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 완료");
    }

    private static void DumpBlendShapes(Scene scene)
    {
        // GameFlowManager가 참조하는 faceRenderer를 찾는다
        GameFlowManager flow = Object.FindFirstObjectByType<GameFlowManager>();
        SkinnedMeshRenderer face = null;
        int smileIndex = -1;

        if (flow != null)
        {
            SerializedObject so = new SerializedObject(flow);
            SerializedProperty fr = so.FindProperty("faceRenderer");
            SerializedProperty si = so.FindProperty("smileIndex");
            if (fr != null) { face = fr.objectReferenceValue as SkinnedMeshRenderer; }
            if (si != null) { smileIndex = si.intValue; }
        }

        if (face == null)
        {
            // 이름으로 재시도
            foreach (SkinnedMeshRenderer r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.name.ToLower().Contains("face")) { face = r; break; }
            }
        }

        if (face == null || face.sharedMesh == null)
        {
            Debug.LogWarning("[PicoBang] 얼굴 SkinnedMeshRenderer를 찾지 못함");
            return;
        }

        Mesh m = face.sharedMesh;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"[PicoBang][BLENDSHAPES] renderer='{face.name}' mesh='{m.name}' count={m.blendShapeCount} 현재 smileIndex={smileIndex}");
        for (int i = 0; i < m.blendShapeCount; i++)
        {
            float w = face.GetBlendShapeWeight(i);
            string mark = (i == smileIndex) ? "  <== 현재 사용중" : "";
            sb.AppendLine($"  [{i,2}] {m.GetBlendShapeName(i)}   (현재 weight {w}){mark}");
        }
        Debug.Log(sb.ToString());
    }

    private static void RemoveGradient(Scene scene)
    {
        GameObject uiRoot = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "UI") { uiRoot = go; break; }
        }
        if (uiRoot == null) { return; }

        Transform home = uiRoot.transform.Find("Home_Panel");
        if (home == null) { return; }

        // 단계별 알파 밴드가 계단처럼 보였다. 전부 제거한다.
        for (int i = 1; i <= 3; i++)
        {
            Transform t = home.Find($"Scrim_Feather_{i}");
            if (t != null) { Object.DestroyImmediate(t.gameObject); }
        }

        // 남은 딤은 경계선이 생기지 않도록 화면 전체를 아주 옅게 덮는다
        Transform dim = home.Find("Dim_Layer");
        if (dim != null)
        {
            RectTransform rt = dim.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = dim.GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0.03f, 0.035f, 0.055f, 0.30f);
                EditorUtility.SetDirty(img);
            }
            dim.SetAsFirstSibling();
        }

        // 균일한 딤만 남으니 글자 대비를 그림자로 보강한다
        AddShadow(home.Find("Tagline_Sub"));
        AddShadow(home.Find("Controls_Hint"));

        Debug.Log("[PicoBang] 그라데이션 밴드 제거, 균일 딤으로 교체");
    }

    private static void AddShadow(Transform t)
    {
        if (t == null) { return; }
        TMP_Text txt = t.GetComponent<TMP_Text>();
        if (txt == null) { return; }

        Shadow sh = t.GetComponent<Shadow>();
        if (sh == null) { sh = t.gameObject.AddComponent<Shadow>(); }
        sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
        sh.effectDistance = new Vector2(2f, -2f);
        EditorUtility.SetDirty(sh);
    }
}
