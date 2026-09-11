using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 피드백 반영 2차:
///   1) 설명 문구 교체
///   2) 홈 카메라를 이전 구도(캐릭터 우측 + 근접)로 되돌림
///   3) 사격 시 머리가 돌아가던 문제 — 마스크에서 Head 제외
/// </summary>
public static class PicoBangFixPass2
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";
    private const string MaskPath  = "Assets/AnimatorController/UpperBody.mask";

    // 이전 구도 — 가깝고 낮게, 크게 틀어서 캐릭터가 화면 오른쪽에 온다
    private const float CamDistance   = 2.45f;
    private const float CamHeight     = 0.72f;
    private const float CamLookHeight = 1.45f;
    private const float CamYawOffset  = 15f;

    [MenuItem("Pico-Bang/9. 피드백 반영 2차")]
    public static void RunAll()
    {
        FixHeadRotation();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RestoreCameraFraming(scene);
        UpdateDescription(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[PicoBang] 피드백 2차 반영 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  머리 회전 제거
    // ─────────────────────────────────────────────────────────
    private static void FixHeadRotation()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { Debug.LogWarning("[PicoBang] 상체 마스크를 찾지 못함"); return; }

        // 사격 애니메이션이 머리 회전까지 들고 있어서 쏠 때마다 고개가 돌아갔다.
        // 머리는 하체와 함께 Base Layer가 담당하게 둔다.
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);

        EditorUtility.SetDirty(mask);
        AssetDatabase.SaveAssetIfDirty(mask);
        Debug.Log("[PicoBang] 상체 마스크에서 Head 제외 — 사격 시 고개 고정");

        // 컨트롤러가 마스크 변경을 다시 읽도록 표시
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/AnimatorController/CharMove.controller");
        if (ac != null) { EditorUtility.SetDirty(ac); }
    }

    // ─────────────────────────────────────────────────────────
    //  카메라 구도 복귀
    // ─────────────────────────────────────────────────────────
    private static void RestoreCameraFraming(Scene scene)
    {
        GameObject introCam = FindRoot(scene, "IntroCamPos");
        GameObject startPos = FindRoot(scene, "PlayerStartPos");
        if (introCam == null || startPos == null) { return; }

        Vector3 p = startPos.transform.position;
        Vector3 f = startPos.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.001f) { f = Vector3.forward; }
        f.Normalize();

        Vector3 camPos = p + f * CamDistance + Vector3.up * CamHeight;
        Vector3 lookAt = p + Vector3.up * CamLookHeight;

        introCam.transform.position = camPos;
        introCam.transform.rotation = Quaternion.LookRotation(lookAt - camPos, Vector3.up);
        introCam.transform.Rotate(0f, -CamYawOffset, 0f, Space.World);

        EditorUtility.SetDirty(introCam);
        Debug.Log("[PicoBang] 홈 카메라를 이전 구도(우측 근접)로 복귀");
    }

    // ─────────────────────────────────────────────────────────
    //  설명 문구
    // ─────────────────────────────────────────────────────────
    private static void UpdateDescription(Scene scene)
    {
        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { return; }

        Transform sub = uiRoot.transform.Find("Home_Panel/Tagline_Sub");
        if (sub == null) { Debug.LogWarning("[PicoBang] 설명 문구 오브젝트 없음"); return; }

        TMP_Text t = sub.GetComponent<TMP_Text>();
        if (t == null) { return; }

        t.text = "추격자를 따돌려 과녁을 부수고 포탈을 열어 탈출하세요";
        EditorUtility.SetDirty(t);
        Debug.Log("[PicoBang] 설명 문구 교체");
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
