using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 3차 폴리싱: 과녁 프리팹 재구성 + 속도선 프로퍼티 이름 교정.
/// </summary>
public static class PicoBangPolish3
{
    private const string ScenePath  = "Assets/Scenes/ShooterInGame.unity";
    private const string PrefabPath = "Assets/GameObject/Target.prefab";
    private const string MatDir     = "Assets/Material";

    [MenuItem("Pico-Bang/6. 과녁 재구성 + 속도선 연결 교정")]
    public static void RunAll()
    {
        RebuildTargetPrefab();
        FixSpeedEffectProperty();
        AssetDatabase.SaveAssets();
        Debug.Log("[PicoBang] 3차 폴리싱 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  과녁 — 벽에 붙는 원형 과녁으로 교체
    // ─────────────────────────────────────────────────────────
    private static void RebuildTargetPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError("[PicoBang] Target.prefab 로드 실패"); return; }

        try
        {
            // 기존 시각 요소 제거 (스크립트·레이어는 유지)
            foreach (MeshFilter mf in root.GetComponents<MeshFilter>())   { Object.DestroyImmediate(mf); }
            foreach (MeshRenderer mr in root.GetComponents<MeshRenderer>()) { Object.DestroyImmediate(mr); }
            foreach (MeshCollider mc in root.GetComponents<MeshCollider>()) { Object.DestroyImmediate(mc); }

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }

            // 판정용 콜라이더 — 얇은 원판이라 박스로 충분하고 메시콜라이더보다 싸다
            BoxCollider box = root.GetComponent<BoxCollider>();
            if (box == null) { box = root.AddComponent<BoxCollider>(); }
            box.center = new Vector3(0f, 0f, 0.03f);
            box.size = new Vector3(1.02f, 1.02f, 0.14f);
            box.isTrigger = false;

            if (root.GetComponent<TargetSpin>() == null)
            {
                root.AddComponent<TargetSpin>();
            }

            // 고리 — 바깥부터 안쪽으로. 정면(+Z)이 벽 바깥을 향한다.
            Material white = GetMaterial("Target_White", new Color(0.95f, 0.95f, 0.93f), 0f);
            Material red   = GetMaterial("Target_Red",   new Color(0.88f, 0.16f, 0.18f), 0f);
            Material gold  = GetMaterial("Target_Gold",  new Color(1.00f, 0.72f, 0.15f), 1.6f);

            AddRing(root.transform, "Ring_1_Outer", 1.00f, 0.000f, white);
            AddRing(root.transform, "Ring_2",       0.78f, 0.012f, red);
            AddRing(root.transform, "Ring_3",       0.56f, 0.024f, white);
            AddRing(root.transform, "Ring_4",       0.34f, 0.036f, red);
            AddRing(root.transform, "Bullseye",     0.15f, 0.048f, gold);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[PicoBang] 과녁 프리팹 재구성 완료 (원형 5단)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AddRing(Transform parent, string name, float diameter, float zOffset, Material mat)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        disc.transform.SetParent(parent, false);

        // 실린더 축은 Y다. -90도 돌려 축을 +Z(정면)로 세운다.
        disc.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        disc.transform.localPosition = new Vector3(0f, 0f, zOffset);
        disc.transform.localScale = new Vector3(diameter, 0.02f, diameter);

        // 판정은 루트의 BoxCollider가 담당한다
        Collider c = disc.GetComponent<Collider>();
        if (c != null) { Object.DestroyImmediate(c); }

        MeshRenderer mr = disc.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Material GetMaterial(string name, Color color, float emission)
    {
        string path = $"{MatDir}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { return existing; }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { shader = Shader.Find("Standard"); }

        Material mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.SetFloat("_Smoothness", 0.25f);

        if (emission > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", color * emission);
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ─────────────────────────────────────────────────────────
    //  속도선 — 씬에 저장된 프로퍼티 이름이 셰이더와 달랐다
    // ─────────────────────────────────────────────────────────
    private static void FixSpeedEffectProperty()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc == null) { Debug.LogWarning("[PicoBang] PlayerController 없음"); return; }

        SerializedObject so = new SerializedObject(pc);

        SerializedProperty prop = so.FindProperty("materialPropertyName");
        if (prop != null)
        {
            string before = prop.stringValue;
            prop.stringValue = "_FullscreenIntensity";
            Debug.Log($"[PicoBang] 속도선 프로퍼티 이름: '{before}' → '_FullscreenIntensity'");
        }

        // 달리기 속도는 10, 걷기는 5 — 임계값을 9로 두면 미세한 감속에도 꺼진다
        SerializedProperty th = so.FindProperty("runThreshold");
        if (th != null) { th.floatValue = 8f; }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pc);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
