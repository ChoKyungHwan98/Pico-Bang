using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 달릴 때 화면 가장자리로 바람을 가르는 속도선(Mirza Beig "Anime Speed Lines" 전체 화면 패스).
///
/// 전체 화면 패스는 같은 렌더러를 쓰는 모든 카메라에 걸린다 — 후방 미러(오른쪽 위)와
/// 포트폴리오 전술 지도(오른쪽 아래) 카메라에도. 그래서 카메라가 그리기 직전마다 세기를 바꾼다:
/// 메인 카메라일 때만 달리기 세기, 다른 카메라일 때는 0. URP는 카메라마다 그리기를 끝내고 다음 카메라로 가므로
/// 카메라별로 다른 값이 적용된다. 이전 속도선이 빠진 이유가 이것이었다(2026-09-24 어깨 카메라 작업).
/// </summary>
[DefaultExecutionOrder(220)]
public sealed class SpeedLinesFx : MonoBehaviour
{
    private static readonly int IntensityId = Shader.PropertyToID("_FullscreenIntensity");

    [Tooltip("달리기 최대일 때 속도선 세기(0~1)")]
    [Range(0f, 1f)] public float maxIntensity = .85f;

    [Tooltip("세기가 올라가는/내려가는 속도(초당)")]
    public float riseSpeed = 3f, fallSpeed = 4f;

    private PlayerController controller;
    private Camera gameplayCamera;
    private Material material;
    private FullScreenPassRendererFeature feature;
    private float intensity;

    public float Intensity => intensity;

    private void Awake()
    {
        controller = GetComponentInParent<PlayerController>();
        gameplayCamera = Camera.main;
        FindFeature();
    }

    /// <summary>현재 렌더러에서 속도선 머티리얼을 쓰는 전체 화면 패스를 찾아 켠다.</summary>
    private void FindFeature()
    {
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null) return;
        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (!(field?.GetValue(asset) is ScriptableRendererData[] list)) return;
        foreach (var data in list)
        {
            if (data == null) continue;
            foreach (var f in data.rendererFeatures)
            {
                if (f is FullScreenPassRendererFeature pass && pass.passMaterial != null &&
                    pass.passMaterial.HasProperty(IntensityId))
                {
                    feature = pass;
                    material = pass.passMaterial;
                    feature.SetActive(true);
                    return;
                }
            }
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeCameraRender;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
        intensity = 0f;
        // 머티리얼은 에셋이다 — 플레이가 끝나도 값이 남지 않게 0으로 돌려 둔다
        if (material != null) material.SetFloat(IntensityId, 0f);
    }

    private void Update()
    {
        bool running = GameFlowManager.Instance != null && GameFlowManager.Instance.IsGameRunning;
        float target = running && controller != null ? controller.SprintVisualStrength * maxIntensity : 0f;
        intensity = Mathf.MoveTowards(intensity, target, Time.deltaTime * (target > intensity ? riseSpeed : fallSpeed));
    }

    private void BeforeCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (material == null) return;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        material.SetFloat(IntensityId, camera == gameplayCamera ? intensity : 0f);
    }
}
