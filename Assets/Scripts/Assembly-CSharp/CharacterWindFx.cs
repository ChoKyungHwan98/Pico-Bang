using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 달릴 때 캐릭터 몸 둘레를 스쳐 지나가는 가는 바람 줄기(레퍼런스: 카트라이더 카트 옆 바람).
///
/// - 줄기 모양: Resources/Vfx/WindStreak(가운데 밝고 양 끝이 부드럽게 사라지는 줄기, 더하기 합성) — 딱딱한 선이 아니다
/// - 위치: 달리는 방향 앞쪽 0.6m에서, 몸 둘레(몸통 가운데는 비움) 고리 위에 생겨 몸 옆·위로 흘러 지나간다
/// - 움직임: 월드 공간에서 달리는 반대 방향으로 조금 흐름 → 플레이어가 11로 달리면 화면에서는 뒤로 빠르게 스쳐 감.
///   늘인 빌보드라 속도 방향으로 길게 보인다
/// - 세기: 달리기 세기(PlayerController.SprintVisualStrength)에 비례, 걸을 때는 없음
/// - 메인 카메라에서만 보인다(후방 미러·포트폴리오 전술 지도에서는 숨김)
/// </summary>
[DefaultExecutionOrder(230)]
public sealed class CharacterWindFx : MonoBehaviour
{
    [Tooltip("달리기 최대일 때 초당 줄기 수")]
    public float maxRate = 60f;

    [Tooltip("줄기 폭(m)")]
    public Vector2 width = new Vector2(.03f, .06f);

    [Tooltip("줄기 수명(초)")]
    public Vector2 lifetime = new Vector2(.1f, .18f);

    [Tooltip("몸 둘레 고리 반지름(m) — 안쪽은 몸이라 비운다")]
    public Vector2 ringRadius = new Vector2(.38f, .7f);

    [Tooltip("고리 중심 높이(m)")]
    public float ringHeight = .95f;

    [Tooltip("몸 앞 얼마나(m)에서 생기나")]
    public float spawnAhead = .7f;

    [Tooltip("월드에서 뒤로 흐르는 속도(m/s). 달리기 속도에 더해져 화면에서 스쳐 가는 빠르기가 된다")]
    public float drift = 7f;

    [Tooltip("속도에 곱해 줄기 길이를 만든다")]
    public float stretch = .09f;

    public Color color = new Color(.78f, .93f, 1f, .9f);

    private PlayerController controller;
    private Rigidbody body;
    private Camera gameplayCamera;
    private ParticleSystem system;
    private ParticleSystemRenderer systemRenderer;
    private float emitCarry;

    /// <summary>테스트·캡처용: 0보다 크면 달리기 세기 대신 이 값을 쓴다.</summary>
    public static float DebugStrength;

    private void Awake()
    {
        controller = GetComponentInParent<PlayerController>();
        body = controller != null ? controller.GetComponent<Rigidbody>() : null;
        gameplayCamera = Camera.main;
        Build();
    }

    private void Build()
    {
        var material = Resources.Load<Material>("Vfx/WindStreak");
        var go = new GameObject("~CharacterWind");
        go.transform.SetParent(transform, false);
        system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSize = new ParticleSystem.MinMaxCurve(width.x, width.y);
        main.startColor = color;
        main.maxParticles = 160;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = system.emission;
        emission.enabled = false;   // 직접 Emit — 달리기 세기와 방향을 매 프레임 반영

        var shape = system.shape;
        shape.enabled = false;

        // 생기자마자 확 켜지지 않고, 끝에서 부드럽게 사라진다
        var col = system.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .18f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        systemRenderer = go.GetComponent<ParticleSystemRenderer>();
        systemRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        systemRenderer.velocityScale = stretch;
        systemRenderer.lengthScale = 2f;
        // 늘이는 길이는 카메라(=달리는 플레이어)와의 상대 속도로 — 월드 속도만 쓰면 달릴 때 줄기가 짧은 점선처럼 보인다
        systemRenderer.cameraVelocityScale = stretch;
        systemRenderer.sortMode = ParticleSystemSortMode.None;
        systemRenderer.shadowCastingMode = ShadowCastingMode.Off;
        systemRenderer.receiveShadows = false;
        systemRenderer.alignment = ParticleSystemRenderSpace.View;
        if (material != null) systemRenderer.sharedMaterial = material;
        system.Play();
    }

    private void OnEnable() { RenderPipelineManager.beginCameraRendering += BeforeCameraRender; }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
        if (systemRenderer != null) systemRenderer.forceRenderingOff = false;
    }

    private void BeforeCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (systemRenderer == null) return;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        systemRenderer.forceRenderingOff = camera != gameplayCamera;
    }

    private void LateUpdate()
    {
        if (system == null || controller == null) return;
        bool running = GameFlowManager.Instance != null && GameFlowManager.Instance.IsGameRunning;
        float strength = DebugStrength > 0f ? DebugStrength : (running ? controller.SprintVisualStrength : 0f);
        if (strength <= .01f) { emitCarry = 0f; return; }

        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        velocity.y = 0f;
        Vector3 forward = velocity.sqrMagnitude > 1f ? velocity.normalized : Vector3.ProjectOnPlane(controller.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = controller.transform.position + Vector3.up * ringHeight + forward * spawnAhead;

        emitCarry += maxRate * strength * Time.deltaTime;
        int count = Mathf.FloorToInt(emitCarry);
        emitCarry -= count;
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            // 고리 위 한 점: 몸 옆(허리~어깨 높이)만. 머리 위는 비운다 — 조준선(화면 가운데)을 가로지른다
            float angle = Random.Range(-35f, 55f) * Mathf.Deg2Rad;
            if (Random.value < .5f) angle = Mathf.PI - angle;
            float radius = Random.Range(ringRadius.x, ringRadius.y);
            Vector3 offset = right * Mathf.Cos(angle) * radius * 1.15f + Vector3.up * Mathf.Sin(angle) * radius * .9f;
            p.position = center + offset + forward * Random.Range(-.25f, .25f);
            // 뒤로 흐르며 몸을 따라 살짝 바깥으로 벌어진다 — 공기가 몸을 비켜 가는 느낌
            p.velocity = -forward * drift * Random.Range(.8f, 1.25f) + offset.normalized * Random.Range(.2f, .8f);
            p.startLifetime = Random.Range(lifetime.x, lifetime.y);
            p.startSize = Random.Range(width.x, width.y);
            Color c = color;
            c.a *= strength * Random.Range(.55f, 1f);
            p.startColor = c;
            system.Emit(p, 1);
        }
    }
}
