using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 사용자가 에디터에서 직접 잡은 값을 씬에 고정한다.
///   - 홈 화면 표정: mouth_I 66 + Angry 43.4 (도야가오)
///   - 시작 위치/회전
/// </summary>
public static class PicoBangApplyUserValues
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";

    // 사용자가 잡은 시작 트랜스폼
    private static readonly Vector3 StartPosition = new Vector3(-0.1251164f, 0.07844073f, 0.004084607f);
    private static readonly Vector3 StartEuler    = new Vector3(0f, 8.347f, 0f);

    // 사용자가 잡은 표정 (인덱스 2 = mouth_I, 7 = Angry)
    private static readonly int[]   FaceIndices = { 2, 7 };
    private static readonly float[] FaceWeights = { 66f, 43.4f };

    [MenuItem("Pico-Bang/16. 사용자 지정 표정·시작위치 적용")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ApplyStartTransform(scene);
        ApplyExpression();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 사용자 지정 값 적용 완료");
    }

    private static void ApplyStartTransform(Scene scene)
    {
        Quaternion rot = Quaternion.Euler(StartEuler);

        // 런타임에는 ResetToHomeState가 playerStartPoint로 순간이동시키므로
        // 여기가 실제 기준점이다.
        GameObject startPos = FindRoot(scene, "PlayerStartPos");
        if (startPos != null)
        {
            startPos.transform.SetPositionAndRotation(StartPosition, rot);
            EditorUtility.SetDirty(startPos);
            Debug.Log($"[PicoBang] PlayerStartPos = {StartPosition} / {StartEuler}");
        }
        else
        {
            Debug.LogWarning("[PicoBang] PlayerStartPos 없음");
        }

        // 씬에 저장된 캐릭터 위치도 맞춰둔다 (에디터에서 보이는 모습과 캡처가 일치하도록)
        GameObject player = FindRoot(scene, "pico_chan_chr_pico_00");
        if (player != null)
        {
            player.transform.SetPositionAndRotation(StartPosition, rot);
            EditorUtility.SetDirty(player);
            Debug.Log("[PicoBang] 캐릭터 초기 트랜스폼도 동일하게 설정");
        }
    }

    private static void ApplyExpression()
    {
        GameFlowManager flow = Object.FindFirstObjectByType<GameFlowManager>();
        if (flow == null) { Debug.LogError("[PicoBang] GameFlowManager 없음"); return; }

        SerializedObject so = new SerializedObject(flow);
        SerializedProperty arr = so.FindProperty("homeExpression");
        if (arr == null) { Debug.LogError("[PicoBang] homeExpression 없음"); return; }

        arr.arraySize = FaceIndices.Length;
        for (int i = 0; i < FaceIndices.Length; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("index").intValue = FaceIndices[i];
            e.FindPropertyRelative("weight").floatValue = FaceWeights[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);
        Debug.Log("[PicoBang] 홈 표정 = mouth_I 66 + Angry 43.4");
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
