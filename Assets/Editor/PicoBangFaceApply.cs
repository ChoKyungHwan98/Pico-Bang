using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 홈 화면 표정을 "눈 뜨고 웃는" 조합으로 설정한다.
/// Joy(6)는 100으로 올리면 눈이 ^^ 로 감기므로 Fun(9)을 주로 쓴다.
/// </summary>
public static class PicoBangFaceApply
{
    private const string ScenePath = "Assets/Scenes/ShooterInGame.unity";

    [MenuItem("Pico-Bang/12. 홈 표정 적용 (눈 뜨고 웃기)")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameFlowManager flow = Object.FindFirstObjectByType<GameFlowManager>();
        if (flow == null) { Debug.LogError("[PicoBang] GameFlowManager 없음"); return; }

        SerializedObject so = new SerializedObject(flow);
        SerializedProperty arr = so.FindProperty("homeExpression");
        if (arr == null) { Debug.LogError("[PicoBang] homeExpression 프로퍼티 없음"); return; }

        // 도야가오 — 눈은 뜬 채로 입꼬리만 올라간 여유로운 표정.
        //
        // Joy(6)는 100이면 눈이 ^^ 로 완전히 감기지만, 낮게 주면
        // 눈매가 살짝 휘고 입꼬리만 올라가는 지점이 나온다.
        // Fun(9)은 눈을 부릅뜨게 만들어 무섭게 보였으므로 쓰지 않는다.
        arr.arraySize = 1;
        SerializedProperty e0 = arr.GetArrayElementAtIndex(0);
        e0.FindPropertyRelative("index").intValue = 6;      // Joy
        e0.FindPropertyRelative("weight").floatValue = 30f;

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flow);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PicoBang] 홈 표정 = Joy(6) 30 적용 (도야가오 방향)");
    }
}
