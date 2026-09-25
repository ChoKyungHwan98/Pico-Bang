using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps the third-person camera readable while preserving a responsive aiming axis.
/// It searches a few nearby shoulder positions when the direct boom is blocked.
/// </summary>
[DefaultExecutionOrder(210)]
public sealed class ShoulderCameraRig : MonoBehaviour
{
    [SerializeField] private float normalDistance = 2.35f;
    [SerializeField] private float sprintExtraDistance = .35f;
    [SerializeField] private float downwardDistanceBonus = .45f;
    [SerializeField] private float maxDownwardBoomPitch = 22f;
    [SerializeField] private float sprintFieldOfView = 66f;
    [SerializeField] private float squeezedFieldOfView = 72f;
    [SerializeField] private float distanceSmoothTime = .12f;
    [SerializeField] private float cameraRadius = .28f;
    [SerializeField] private float wallBuffer = .12f;
    [SerializeField] private float shoulderEscape = .5f;
    [SerializeField] private float verticalEscape = .32f;
    [SerializeField] private float hidePlayerDistance = .62f;
    [Tooltip("벽을 이만큼(m) 미리 감지해 카메라가 서서히 다가간다. 실제로 닿을 때만 즉시 당긴다")]
    [SerializeField] private float anticipation = .9f;
    [Tooltip("미리 다가갈 때 따라가는 시간(초)")]
    [SerializeField] private float approachSmoothTime = .09f;
    // 몬스터는 카메라를 막는 벽이 아니다. 바로 뒤에서 따라올 때 몬스터 콜라이더에
    // 카메라가 붙어 버리면 화면이 갑자기 확대되거나 플레이어를 관통하므로 제외한다.
	[SerializeField] private LayerMask collisionMask = (1 << 0) | (1 << 8) | (1 << 10);

    private readonly RaycastHit[] wallHits = new RaycastHit[32];
    private static readonly float[] SideProbeAngles = { -12f, 12f };
    private PlayerController controller;
    private Camera gameplayCamera;
    private Renderer[] playerRenderers;
    private Vector3 originalLocalPosition;
    private Vector3 currentLocalPosition;
    private Vector3 positionVelocity;
    private float normalFieldOfView;
    private float normalNearClip;
    private bool playerHidden;
    private readonly System.Collections.Generic.List<Renderer> suppressedMonsterRenderers = new System.Collections.Generic.List<Renderer>();

    private void Awake()
    {
        controller = GetComponentInParent<PlayerController>();
        gameplayCamera = Camera.main;
        // 맵 벽(PrototypeMap_Walls)은 Ground 층에 있다. 씬에 저장된 마스크(0·8·10층)에는 빠져 있어
        // 카메라가 벽을 모르고 벽 속으로 들어갔다(2026-09-25 확인). 씬 값과 상관없이 항상 포함한다
        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) collisionMask |= 1 << ground;
        // 달릴 때 바람 속도선 — 씬을 고치지 않고 어깨 카메라가 있는 모든 씬에 붙인다
        if (GetComponent<SpeedLinesFx>() == null) gameObject.AddComponent<SpeedLinesFx>();
        playerRenderers = controller != null ? controller.GetComponentsInChildren<Renderer>(true) : null;
        originalLocalPosition = transform.localPosition;
        normalFieldOfView = gameplayCamera != null ? gameplayCamera.fieldOfView : 60f;
        normalNearClip = gameplayCamera != null ? gameplayCamera.nearClipPlane : .3f;
        currentLocalPosition = BasePosition(normalDistance);
        transform.localPosition = currentLocalPosition;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeCameraRender;
        RenderPipelineManager.endCameraRendering += AfterCameraRender;
    }

    private void LateUpdate()
    {
        if (gameplayCamera == null || controller == null || transform.parent == null) return;

        bool gameplay = GameFlowManager.Instance != null && GameFlowManager.Instance.IsGameRunning &&
            gameplayCamera.transform.IsChildOf(transform);
        float sprint = gameplay ? controller.SprintVisualStrength : 0f;
        if (!gameplay)
        {
            SetPlayerHidden(false);
            return;
        }

        float lookingDown = Mathf.Clamp01(-transform.parent.forward.y);
        float distance = normalDistance + sprintExtraDistance * sprint + downwardDistanceBonus * lookingDown;
        // hard = 카메라가 벽을 뚫지 않는 한계. soft = 같은 방향으로 anticipation만큼 더 뒤를 살펴
        // 벽이 가까워지는 만큼 미리 당긴 자리. soft 쪽으로 부드럽게 가되 hard보다 멀면 즉시 당긴다 —
        // 벽이 들어올 때 한 프레임에 확 당겨지던 튐(측정 최대 1.33m)을 줄인다
        Vector3 hard = FindBestCameraPosition(distance, cameraRadius);
        Vector3 soft = hard;
        float hardLength = hard.magnitude;
        if (anticipation > 0f && hardLength > .01f)
        {
            Vector3 dir = hard / hardLength;
            float farReach = ResolveCandidate(dir * (hardLength + anticipation), cameraRadius).magnitude;
            float eased = Mathf.Min(hardLength, farReach - anticipation);
            // 벽은 뒤에서만이 아니라 옆에서도 쓸고 들어온다(몸을 돌릴 때). 좌우 12° 방향도 살펴 미리 당긴다
            foreach (float angle in SideProbeAngles)
            {
                float sideReach = ResolveCandidate(Quaternion.AngleAxis(angle, Vector3.up) * dir * hardLength, cameraRadius).magnitude;
                eased = Mathf.Min(eased, Mathf.Lerp(hardLength, sideReach, .6f));
            }
            soft = dir * Mathf.Max(Mathf.Min(hardLength, .35f), eased);
        }

        bool movingOut = soft.sqrMagnitude > currentLocalPosition.sqrMagnitude + .0001f;
        currentLocalPosition = Vector3.SmoothDamp(currentLocalPosition, soft, ref positionVelocity,
            movingOut ? distanceSmoothTime : approachSmoothTime);
        if (currentLocalPosition.sqrMagnitude > hard.sqrMagnitude)
        {
            currentLocalPosition = hard;
            positionVelocity = Vector3.zero;
        }
        transform.localPosition = currentLocalPosition;

        float cameraDistance = currentLocalPosition.magnitude;
        float squeeze = 1f - Mathf.InverseLerp(.55f, normalDistance, cameraDistance);
        float wantedFov = Mathf.Max(Mathf.Lerp(normalFieldOfView, sprintFieldOfView, sprint),
            Mathf.Lerp(normalFieldOfView, squeezedFieldOfView, squeeze));
        gameplayCamera.fieldOfView = Mathf.Lerp(gameplayCamera.fieldOfView, wantedFov,
            1f - Mathf.Exp(-Time.deltaTime * 9f));
        gameplayCamera.nearClipPlane = Mathf.Lerp(normalNearClip, .1f, squeeze);
        SetPlayerHidden(cameraDistance < hidePlayerDistance);
    }

    private Vector3 FindBestCameraPosition(float distance, float radius)
    {
        Vector3 direct = BasePosition(distance);
        Vector3 best = ResolveCandidate(direct, radius);
        Vector3 directResolved = best;
        float bestScore = Score(best, direct, 0f);
        float directReach = best.magnitude / Mathf.Max(.01f, direct.magnitude);
        if (directReach > .82f) return best;

        Vector3[] alternatives =
        {
            direct + Vector3.right * shoulderEscape,
            direct - Vector3.right * shoulderEscape * .7f,
            direct + Vector3.up * verticalEscape,
            direct + Vector3.right * shoulderEscape * .65f + Vector3.up * verticalEscape
        };
        for (int i = 0; i < alternatives.Length; i++)
        {
            Vector3 resolved = ResolveCandidate(alternatives[i], radius);
            float score = Score(resolved, alternatives[i], .08f);
            // 옆·위로 비킨 자리는 기준점에서는 뚫려 있어도 캐릭터 머리에서 보면 벽 뒤일 수 있다 — 머리에서 보여야 쓴다
            if (score > bestScore && VisibleFromHead(resolved))
            {
                best = resolved;
                bestScore = score;
            }
        }
        return VisibleFromHead(best) ? best : directResolved;
    }

    private bool VisibleFromHead(Vector3 localPosition)
    {
        Vector3 head = controller.transform.position + Vector3.up * 1.35f;
        Vector3 world = transform.parent.TransformPoint(localPosition);
        Vector3 d = world - head;
        float length = d.magnitude;
        return length < .01f || CastDistance(head, d / length, length, .05f) >= length - .05f;
    }

    private Vector3 BasePosition(float distance)
    {
        Vector3 offset = new Vector3(originalLocalPosition.x, originalLocalPosition.y, -distance);
        // The camera still aims at the full pitch. Only its orbit is capped so looking
        // down does not lift it far above the runner and push the runner off screen.
        float pitch = Mathf.DeltaAngle(0f, transform.parent.localEulerAngles.x);
        float excessDown = Mathf.Max(0f, pitch - maxDownwardBoomPitch);
        return Quaternion.Euler(-excessDown, 0f, 0f) * offset;
    }

    /// <summary>
    /// 카메라 후보 자리를 벽 안쪽으로 당긴다. 2단계:
    /// ① 캐릭터 머리 → 어깨 기준점: 기준점이 벽에 묻혀 있으면 기준점부터 안쪽으로 당긴다.
    ///    (구 검사는 시작점이 이미 벽에 닿아 있으면 그 벽을 못 본다 — 장애물 뒤로 카메라가 21% 빠지던 원인)
    /// ② 기준점 → 카메라: 구 검사 + 보조 광선. 광선은 구가 놓친 벽을 잡는다.
    /// </summary>
    private Vector3 ResolveCandidate(Vector3 localCandidate, float radius)
    {
        Vector3 head = controller.transform.position + Vector3.up * 1.35f;
        Vector3 pivot = transform.parent.position;
        Vector3 toPivot = pivot - head;
        float pivotLength = toPivot.magnitude;
        if (pivotLength > .01f)
        {
            float pivotAllowed = CastDistance(head, toPivot / pivotLength, pivotLength, Mathf.Min(radius, .2f));
            if (pivotAllowed < pivotLength) pivot = head + toPivot / pivotLength * pivotAllowed;
        }

        Vector3 worldCandidate = transform.parent.TransformPoint(localCandidate);
        Vector3 delta = worldCandidate - pivot;
        float length = delta.magnitude;
        if (length < .01f) return transform.parent.InverseTransformPoint(pivot);
        float allowed = CastDistance(pivot, delta / length, length, radius);
        Vector3 safeWorld = pivot + delta / length * allowed;
        return transform.parent.InverseTransformPoint(safeWorld);
    }

    /// <summary>from에서 dir로 length만큼 갈 때 벽 앞 wallBuffer에서 멈추는 거리(구 검사 + 광선 중 가까운 쪽).</summary>
    private float CastDistance(Vector3 from, Vector3 dir, float length, float radius)
    {
        float allowed = length;
        int count = Physics.SphereCastNonAlloc(from, radius, dir, wallHits, length, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = wallHits[i];
            if (Ignored(hit.collider)) continue;
            // 시작부터 겹친 충돌(distance 0)은 방향 정보가 없다 — 광선 검사에 맡긴다
            if (hit.distance <= 0f && hit.point == Vector3.zero) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - wallBuffer));
        }
        count = Physics.RaycastNonAlloc(from, dir, wallHits, length + radius, collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = wallHits[i];
            if (Ignored(hit.collider)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - radius - wallBuffer));
        }
        return allowed;
    }

    private bool Ignored(Collider c) =>
        c == null || c.transform.IsChildOf(controller.transform) || c.GetComponentInParent<MonsterAI>() != null;

    private static float Score(Vector3 resolved, Vector3 desired, float offsetPenalty)
    {
        float reach = resolved.magnitude;
        float completion = reach / Mathf.Max(.01f, desired.magnitude);
        return reach + completion * .8f - offsetPenalty;
    }

    private void SetPlayerHidden(bool hidden)
    {
        if (playerHidden == hidden || playerRenderers == null) return;
        playerHidden = hidden;
        foreach (Renderer renderer in playerRenderers)
        {
            if (renderer != null) renderer.forceRenderingOff = hidden;
        }
    }

    private void BeforeCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != gameplayCamera || controller == null ||
            GameFlowManager.Instance == null || !GameFlowManager.Instance.IsGameRunning) return;

        // 메인 카메라와 플레이어 사이에 끼어든 몬스터만 메인 화면에서 잠깐 숨긴다.
        // 후방 카메라는 별도로 렌더링되므로 추적자는 그곳에 계속 보인다.
        Vector3 playerPosition = controller.transform.position;
        Vector3 cameraPosition = gameplayCamera.transform.position;
        Vector3 behind = -controller.transform.forward;
        foreach (MonsterAI monster in MonsterAI.activeMonsters)
        {
            if (monster == null || !monster.isActiveAndEnabled) continue;
            Vector3 offset = monster.transform.position - playerPosition;
            offset.y = 0f;
            if (Vector3.Dot(offset, behind) < .15f ||
                Vector3.Distance(monster.transform.position, cameraPosition) > 3.4f) continue;
            foreach (Renderer renderer in monster.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                suppressedMonsterRenderers.Add(renderer);
            }
        }
    }

    private void AfterCameraRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == gameplayCamera) RestoreMonsterRenderers();
    }

    private void RestoreMonsterRenderers()
    {
        foreach (Renderer renderer in suppressedMonsterRenderers)
            if (renderer != null) renderer.forceRenderingOff = false;
        suppressedMonsterRenderers.Clear();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
        RenderPipelineManager.endCameraRendering -= AfterCameraRender;
        RestoreMonsterRenderers();
        SetPlayerHidden(false);
        if (gameplayCamera == null) return;
        gameplayCamera.fieldOfView = normalFieldOfView;
        gameplayCamera.nearClipPlane = normalNearClip;
    }
}
