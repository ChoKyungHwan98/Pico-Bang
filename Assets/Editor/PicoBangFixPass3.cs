using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 피드백 반영 3차:
///   1) 사격 시 머리 흔들림 완전 제거 — 마스크에서 Body(척추)까지 제외
///   2) 홈 카메라를 사용자가 직접 잡은 값으로 고정
/// </summary>
public static class PicoBangFixPass3
{
    private const string ScenePath      = "Assets/Scenes/ShooterInGame.unity";
    private const string MaskPath       = "Assets/AnimatorController/UpperBody.mask";
    private const string ControllerPath = "Assets/AnimatorController/CharMove.controller";

    // 사용자가 플레이 모드에서 직접 잡은 Main Camera 값
    private static readonly Vector3 HomeCamPosition = new Vector3(0f, 1.267f, 0.678f);
    private static readonly Vector3 HomeCamEuler    = new Vector3(0.989f, 165.636f, -4.629f);

    [MenuItem("Pico-Bang/10. 피드백 반영 3차")]
    public static void RunAll()
    {
        StopHeadShake();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyHomeCamera(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[PicoBang] 피드백 3차 반영 완료");
    }

    private static void StopHeadShake()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { Debug.LogError("[PicoBang] 상체 마스크 없음"); return; }

        // Head는 이미 껐지만 머리뼈는 척추의 자식이다.
        // Body(척추·가슴)가 켜져 있으면 상체가 비틀리면서 머리가 따라 움직인다.
        // 팔·손만 남기면 사격 동작이 하체와 몸통에 전혀 간섭하지 않는다.
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

        EditorUtility.SetDirty(mask);
        AssetDatabase.SaveAssetIfDirty(mask);

        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ac != null)
        {
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssetIfDirty(ac);
        }

        Debug.Log("[PicoBang] 상체 마스크: 팔·손만 활성 (척추·머리·다리 전부 제외)");
    }

    private static void ApplyHomeCamera(Scene scene)
    {
        GameObject introCam = null;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "IntroCamPos") { introCam = go; break; }
        }
        if (introCam == null) { Debug.LogError("[PicoBang] IntroCamPos 없음"); return; }

        introCam.transform.position = HomeCamPosition;
        introCam.transform.rotation = Quaternion.Euler(HomeCamEuler);

        EditorUtility.SetDirty(introCam);
        Debug.Log($"[PicoBang] 홈 카메라 고정: pos={HomeCamPosition}, rot={HomeCamEuler}");
    }
}
