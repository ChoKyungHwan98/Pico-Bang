using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 배치모드에서 -executeMethod로 호출하는 프로젝트 작업 도구.
/// 씬/프리팹을 정식 Unity API로 수정한다 (YAML 직접 편집 금지).
/// </summary>
public static class PicoBangTools
{
    private const string ScenePath    = "Assets/Scenes/ShooterInGame.unity";
    private const string FontTtfPath  = "Assets/Font/Jalnan2.ttf";
    private const string FontAssetDir = "Assets/Font";
    private const string FontAssetPath = "Assets/Font/Jalnan2 SDF.asset";

    // ─────────────────────────────────────────────────────────
    //  1. 잘난체 TMP 폰트 에셋 생성
    // ─────────────────────────────────────────────────────────
    [MenuItem("Pico-Bang/1. 잘난체 폰트 에셋 생성")]
    public static void CreateJalnanFontAsset()
    {
        Font source = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
        if (source == null)
        {
            Debug.LogError($"[PicoBang] 폰트 원본을 찾을 수 없음: {FontTtfPath}");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
        {
            Debug.Log("[PicoBang] 폰트 에셋이 이미 있음 — 생성 건너뜀");
            return;
        }

        // 한글 11,172자를 정적 아틀라스로 구우면 텍스처가 감당이 안 된다.
        // Dynamic 모드로 두면 실제로 쓰인 글자만 런타임에 아틀라스로 올라간다.
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            source,
            90,                          // sampling point size
            9,                           // atlas padding
            GlyphRenderMode.SDFAA,
            1024, 1024,
            AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (fontAsset == null)
        {
            Debug.LogError("[PicoBang] 폰트 에셋 생성 실패");
            return;
        }

        fontAsset.name = "Jalnan2 SDF";
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

        // 머티리얼과 아틀라스 텍스처를 서브에셋으로 붙인다
        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }
        if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
        {
            fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PicoBang] 폰트 에셋 생성 완료: {FontAssetPath}");
    }

    // ─────────────────────────────────────────────────────────
    //  2. 씬·프리팹의 모든 TMP 텍스트를 잘난체로 교체
    // ─────────────────────────────────────────────────────────
    [MenuItem("Pico-Bang/2. 모든 텍스트를 잘난체로 교체")]
    public static void ApplyJalnanEverywhere()
    {
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fontAsset == null)
        {
            Debug.LogError("[PicoBang] 폰트 에셋이 없다. 1번을 먼저 실행할 것");
            return;
        }

        int sceneChanged = 0;
        int prefabChanged = 0;

        // ── 씬 ──
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (TMP_Text t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.font == fontAsset) { continue; }
            Undo.RecordObject(t, "Font swap");
            t.font = fontAsset;
            EditorUtility.SetDirty(t);
            sceneChanged++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // ── 프리팹 ──
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;

            foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.font == fontAsset) { continue; }
                t.font = fontAsset;
                dirty = true;
                prefabChanged++;
            }

            if (dirty) { PrefabUtility.SaveAsPrefabAsset(root, path); }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PicoBang] 폰트 교체 완료 — 씬 {sceneChanged}개 / 프리팹 {prefabChanged}개");
    }

    // ─────────────────────────────────────────────────────────
    //  진단: 씬 구조 덤프 (UI 작업 전 현황 파악용)
    // ─────────────────────────────────────────────────────────
    [MenuItem("Pico-Bang/9. 씬 UI 구조 덤프")]
    public static void DumpSceneUI()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var lines = new List<string>();
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            DumpRecursive(root.transform, 0, lines);
        }
        Debug.Log("[PicoBang][SCENEDUMP]\n" + string.Join("\n", lines));
    }

    private static void DumpRecursive(Transform t, int depth, List<string> lines)
    {
        string indent = new string(' ', depth * 2);
        string comps = string.Join(", ",
            t.GetComponents<Component>()
             .Where(c => c != null)
             .Select(c => c.GetType().Name)
             .Where(n => n != "Transform" && n != "RectTransform"));

        string active = t.gameObject.activeSelf ? "" : " [비활성]";
        lines.Add($"{indent}{t.name}{active}{(string.IsNullOrEmpty(comps) ? "" : "  <" + comps + ">")}");

        for (int i = 0; i < t.childCount; i++)
        {
            DumpRecursive(t.GetChild(i), depth + 1, lines);
        }
    }
}
