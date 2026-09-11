using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 피드백 반영 패스:
///   1) 태그라인 제거
///   2) 홈 레이아웃 정리 + 카메라 프레이밍 완화
///   3) 속도선 렌더러 피처 비활성화
///   4) 사격 시 하체가 멈추던 문제 — Upper Layer에 아바타 마스크 부여 + 발사 속도 상향
/// </summary>
public static class PicoBangFixPass
{
    private const string ScenePath      = "Assets/Scenes/ShooterInGame.unity";
    private const string FontAssetPath  = "Assets/Font/Jalnan2 SDF.asset";
    private const string FeaturePath    = "Assets/MonoBehaviour/FullScreenPassRendererFeature.asset";
    private const string ControllerPath = "Assets/AnimatorController/CharMove.controller";
    private const string MaskPath       = "Assets/AnimatorController/UpperBody.mask";

    private static readonly Color TextDim = new Color(0.72f, 0.75f, 0.81f, 1f);
    private static readonly Color Scrim   = new Color(0.04f, 0.045f, 0.07f, 1f);

    // 카메라 — 이전보다 멀고 높게, 덜 틀어서 캐릭터가 화면 안에 온전히 들어오게
    private const float CamDistance   = 3.1f;
    private const float CamHeight     = 0.95f;
    private const float CamLookHeight = 1.30f;
    private const float CamYawOffset  = 10f;

    [MenuItem("Pico-Bang/8. 피드백 반영 패스")]
    public static void RunAll()
    {
        DisableSpeedLines();
        FixShootAnimation();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        ReframeCamera(scene);
        CleanLayout(scene, font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[PicoBang] 피드백 반영 완료");
    }

    // ─────────────────────────────────────────────────────────
    //  1. 속도선 끄기
    // ─────────────────────────────────────────────────────────
    private static void DisableSpeedLines()
    {
        ScriptableObject feature = AssetDatabase.LoadAssetAtPath<ScriptableObject>(FeaturePath);
        if (feature == null) { Debug.LogWarning("[PicoBang] 렌더러 피처를 찾지 못함"); return; }

        SerializedObject so = new SerializedObject(feature);
        SerializedProperty active = so.FindProperty("m_Active");
        if (active != null)
        {
            active.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            Debug.Log("[PicoBang] 속도선 렌더러 피처 비활성화 (에셋·머티리얼은 보존)");
        }
    }

    // ─────────────────────────────────────────────────────────
    //  2. 사격 애니메이션 — 상체만 덮도록
    // ─────────────────────────────────────────────────────────
    private static void FixShootAnimation()
    {
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ac == null) { Debug.LogError("[PicoBang] CharMove.controller 로드 실패"); return; }

        // 상체 마스크 — 다리와 루트를 빼야 달리는 동작이 살아남는다
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            AssetDatabase.CreateAsset(mask, MaskPath);
            Debug.Log("[PicoBang] 상체 아바타 마스크 생성");
        }

        AnimatorControllerLayer[] layers = ac.layers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].name != "Upper Layer") { continue; }

            layers[i].avatarMask = mask;
            layers[i].defaultWeight = 1f;

            // 발사 동작을 조금 더 빠르게 — 굼뜨면 연사감이 죽는다
            foreach (ChildAnimatorState cs in layers[i].stateMachine.states)
            {
                if (cs.state != null && cs.state.name == "Shoot")
                {
                    cs.state.speed = 1.45f;
                    EditorUtility.SetDirty(cs.state);
                    Debug.Log("[PicoBang] Shoot 상태 속도 1.45로 상향");
                }
            }
        }
        ac.layers = layers;

        EditorUtility.SetDirty(ac);
        Debug.Log("[PicoBang] Upper Layer에 상체 마스크 적용 — 달리면서 사격 가능");
    }

    // ─────────────────────────────────────────────────────────
    //  3. 카메라 프레이밍
    // ─────────────────────────────────────────────────────────
    private static void ReframeCamera(Scene scene)
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
        Debug.Log("[PicoBang] 홈 카메라 프레이밍 완화");
    }

    // ─────────────────────────────────────────────────────────
    //  4. 레이아웃 정리
    // ─────────────────────────────────────────────────────────
    private static void CleanLayout(Scene scene, TMP_FontAsset font)
    {
        GameObject uiRoot = FindRoot(scene, "UI");
        if (uiRoot == null) { return; }
        Transform home = uiRoot.transform.Find("Home_Panel");
        if (home == null) { return; }

        // 태그라인 제거
        Transform tagline = home.Find("Tagline");
        if (tagline != null)
        {
            Object.DestroyImmediate(tagline.gameObject);
            Debug.Log("[PicoBang] 태그라인 제거");
        }

        // 스크림을 좁혀서 캐릭터가 밝게 남도록
        Transform dim = home.Find("Dim_Layer");
        if (dim != null)
        {
            Image di = dim.GetComponent<Image>();
            if (di != null) { di.color = new Color(Scrim.r, Scrim.g, Scrim.b, 0.78f); }
            SetAnchors(dim.GetComponent<RectTransform>(), 0f, 0f, 0.42f, 1f);
        }
        Feather(home, "Scrim_Feather_1", 0.42f, 0.50f, 0.44f);
        Feather(home, "Scrim_Feather_2", 0.50f, 0.57f, 0.20f);
        Feather(home, "Scrim_Feather_3", 0.57f, 0.64f, 0.07f);

        // 좌측 여백을 하나로 통일하고 세로 리듬을 정리한다
        const float left = 0.065f;

        Transform title = home.Find("GameTitleImage");
        if (title != null)
        {
            SetAnchors(title.GetComponent<RectTransform>(), left, 0.615f, left + 0.30f, 0.925f);
            Image ti = title.GetComponent<Image>();
            if (ti != null) { ti.preserveAspect = true; EditorUtility.SetDirty(ti); }
            title.SetAsLastSibling();
        }

        // 설명 한 줄은 남긴다 — 무슨 게임인지 알려주는 유일한 문장
        Transform subT = home.Find("Tagline_Sub");
        if (subT != null)
        {
            TMP_Text sub = subT.GetComponent<TMP_Text>();
            if (sub != null)
            {
                if (font != null) { sub.font = font; }
                sub.text = "과녁을 부숴 출구를 열고, 다섯 추격자를 따돌려 탈출하라.";
                sub.fontSize = 23f;
                sub.color = TextDim;
                sub.alignment = TextAlignmentOptions.MidlineLeft;
                EditorUtility.SetDirty(sub);
            }
            SetAnchors(subT.GetComponent<RectTransform>(), left, 0.545f, left + 0.40f, 0.605f);
            subT.SetAsLastSibling();
        }

        // 버튼 — 폭은 그룹이 정하게 두고, LayoutElement는 높이만 붙잡는다
        Transform group = home.Find("ButtonGroup");
        if (group != null)
        {
            SetAnchors(group.GetComponent<RectTransform>(), left, 0.175f, left + 0.215f, 0.49f);

            VerticalLayoutGroup vlg = group.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.spacing = 16f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.padding = new RectOffset(0, 0, 0, 0);
                EditorUtility.SetDirty(vlg);
            }

            foreach (Transform child in group)
            {
                LayoutElement le = child.GetComponent<LayoutElement>();
                if (le == null) { continue; }
                // 폭 제약을 풀어야 그룹 폭과 버튼 폭이 어긋나지 않는다
                le.minWidth = -1f;
                le.preferredWidth = -1f;
                le.flexibleWidth = 1f;
                EditorUtility.SetDirty(le);
            }
            group.SetAsLastSibling();
        }

        // 조작 안내
        Transform hint = home.Find("Controls_Hint");
        if (hint != null)
        {
            TMP_Text h = hint.GetComponent<TMP_Text>();
            if (h != null) { h.fontSize = 21f; h.color = TextDim; EditorUtility.SetDirty(h); }
            SetAnchors(hint.GetComponent<RectTransform>(), left, 0.065f, left + 0.46f, 0.125f);
            hint.SetAsLastSibling();
        }

        Debug.Log("[PicoBang] 홈 레이아웃 정리 완료");
    }

    // ─────────────────────────────────────────────────────────

    private static void Feather(Transform parent, string name, float xMin, float xMax, float alpha)
    {
        Transform t = parent.Find(name);
        if (t == null) { return; }
        Image img = t.GetComponent<Image>();
        if (img != null) { img.color = new Color(Scrim.r, Scrim.g, Scrim.b, alpha); EditorUtility.SetDirty(img); }
        SetAnchors(t.GetComponent<RectTransform>(), xMin, 0f, xMax, 1f);
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
