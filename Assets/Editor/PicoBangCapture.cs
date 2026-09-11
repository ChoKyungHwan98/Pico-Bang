using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 배치모드에서 게임 화면을 PNG로 렌더한다.
///
/// 플레이를 돌리지 않고도 홈 화면이 실제로 어떻게 보이는지 원본 해상도로 확인할 수 있다.
/// Screen Space - Overlay 캔버스는 카메라 렌더에 잡히지 않으므로,
/// 캡처하는 동안만 Screen Space - Camera로 바꿨다가 되돌린다.
/// </summary>
public static class PicoBangCapture
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";

    private static readonly string OutDir =
        @"C:\Users\Admin\AppData\Local\Temp\claude\C--Users-Admin-Documents-Pico-Bang-\f4a38466-5e8f-4423-9477-4d0142c064e2\scratchpad\shots";

    private const int Width  = 1920;
    private const int Height = 1080;

    [MenuItem("Pico-Bang/14. 홈 화면 캡처")]
    public static void CaptureHome()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Camera cam = FindMainCamera(scene);
        if (cam == null) { Debug.LogError("[PicoBang] Main Camera 없음"); return; }

        // 홈 상태 재현 — 배치모드에서는 GameFlowManager.Start()가 돌지 않는다
        GameObject introCam = FindRoot(scene, "IntroCamPos");
        if (introCam != null)
        {
            cam.transform.SetPositionAndRotation(
                introCam.transform.position, introCam.transform.rotation);
        }

        ApplyHomeFace(scene);
        ApplyHomeUIState(scene);

        Canvas uiCanvas = null;
        RenderMode originalMode = RenderMode.ScreenSpaceOverlay;
        Camera originalWorldCam = null;

        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot != null)
        {
            uiCanvas = uiRoot.GetComponent<Canvas>();
            if (uiCanvas != null)
            {
                originalMode = uiCanvas.renderMode;
                originalWorldCam = uiCanvas.worldCamera;

                uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                uiCanvas.worldCamera = cam;
                uiCanvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
            }
        }

        string path = Render(cam, "home");

        // 원상 복구 — 씬을 저장하지 않지만 안전하게 되돌린다
        if (uiCanvas != null)
        {
            uiCanvas.renderMode = originalMode;
            uiCanvas.worldCamera = originalWorldCam;
        }

        Debug.Log($"[PicoBang][SHOT] {path}");
    }

    /// <summary>씬 뷰 없이 원하는 지점을 자유롭게 찍는다 (구도 검토용).</summary>
    [MenuItem("Pico-Bang/15. 캐릭터 정면 캡처")]
    public static void CaptureCharacter()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Camera cam = FindMainCamera(scene);
        GameObject start = FindRoot(scene, "PlayerStartPos");
        if (cam == null || start == null) { Debug.LogError("[PicoBang] 대상 없음"); return; }

        ApplyHomeFace(scene);

        Vector3 p = start.transform.position;
        Vector3 f = start.transform.forward;
        f.y = 0f;
        f.Normalize();

        cam.transform.position = p + f * 1.6f + Vector3.up * 1.35f;
        cam.transform.LookAt(p + Vector3.up * 1.32f);

        string path = Render(cam, "face");
        Debug.Log($"[PicoBang][SHOT] {path}");
    }

    // ─────────────────────────────────────────────────────────

    private static string Render(Camera cam, string label)
    {
        Directory.CreateDirectory(OutDir);

        RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 4
        };

        RenderTexture prevTarget = cam.targetTexture;
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        string path = Path.Combine(OutDir, $"{label}.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());

        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);

        return path;
    }

    /// <summary>
    /// 홈 상태의 UI를 재현한다.
    /// 배치모드에서는 GameFlowManager가 돌지 않아 씬에 저장된 상태 그대로 찍히므로,
    /// 인게임 HUD와 설정 패널이 같이 나와버린다.
    /// </summary>
    private static void ApplyHomeUIState(Scene scene)
    {
        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { return; }
        Transform ui = uiRoot.transform;

        SetActive(ui, "Home_Panel", true);
        SetActive(ui, "Ingame_Panel", false);
        SetActive(ui, "GameOver_Panel", false);
        SetActive(ui, "GameClear_Panel", false);
        SetActive(ui, "Intro_UI", false);
        SetActive(ui, "Portal_Notice_Text", false);

        // 설정 패널은 항상 활성이고 CanvasGroup으로 껐다 켠다
        Transform settings = ui.Find("Settings_Panel");
        if (settings != null)
        {
            CanvasGroup g = settings.GetComponent<CanvasGroup>();
            if (g != null) { g.alpha = 0f; g.blocksRaycasts = false; g.interactable = false; }
        }

        GameObject crosshair = FindRoot(scene, "Crosshair");
        if (crosshair != null) { crosshair.SetActive(false); }
    }

    private static void SetActive(Transform parent, string name, bool active)
    {
        Transform t = parent.Find(name);
        if (t != null) { t.gameObject.SetActive(active); }
    }

    /// <summary>GameFlowManager에 설정된 홈 표정을 얼굴에 실제로 반영한다.</summary>
    private static void ApplyHomeFace(Scene scene)
    {
        GameFlowManager flow = Object.FindFirstObjectByType<GameFlowManager>();
        if (flow == null) { return; }

        SerializedObject so = new SerializedObject(flow);
        SkinnedMeshRenderer face = so.FindProperty("faceRenderer")?.objectReferenceValue as SkinnedMeshRenderer;
        if (face == null || face.sharedMesh == null) { return; }

        for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
        {
            face.SetBlendShapeWeight(i, 0f);
        }

        SerializedProperty arr = so.FindProperty("homeExpression");
        if (arr == null || arr.arraySize == 0) { return; }

        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            int idx = e.FindPropertyRelative("index").intValue;
            float w = e.FindPropertyRelative("weight").floatValue;
            if (idx >= 0 && idx < face.sharedMesh.blendShapeCount)
            {
                face.SetBlendShapeWeight(idx, w);
            }
        }
    }

    private static Camera FindMainCamera(Scene scene)
    {
        foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.CompareTag("MainCamera")) { return c; }
        }
        return Object.FindFirstObjectByType<Camera>();
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
