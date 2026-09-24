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
    // 몬스터는 카메라를 막는 벽이 아니다. 바로 뒤에서 따라올 때 몬스터 콜라이더에
    // 카메라가 붙어 버리면 화면이 갑자기 확대되거나 플레이어를 관통하므로 제외한다.
	[SerializeField] private LayerMask collisionMask = (1 << 0) | (1 << 8) | (1 << 10);

    private readonly RaycastHit[] wallHits = new RaycastHit[32];
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
        Vector3 wanted = FindBestCameraPosition(distance);

        bool movingCloser = wanted.sqrMagnitude + .01f < currentLocalPosition.sqrMagnitude;
        if (movingCloser)
        {
            currentLocalPosition = wanted;
            positionVelocity = Vector3.zero;
        }
        else
        {
            currentLocalPosition = Vector3.SmoothDamp(currentLocalPosition, wanted,
                ref positionVelocity, distanceSmoothTime);
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

    private Vector3 FindBestCameraPosition(float distance)
    {
        Vector3 direct = BasePosition(distance);
        Vector3 best = ResolveCandidate(direct);
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
            Vector3 resolved = ResolveCandidate(alternatives[i]);
            float score = Score(resolved, alternatives[i], .08f);
            if (score > bestScore)
            {
                best = resolved;
                bestScore = score;
            }
        }
        return best;
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

    private Vector3 ResolveCandidate(Vector3 localCandidate)
    {
        Vector3 pivot = transform.parent.position;
        Vector3 worldCandidate = transform.parent.TransformPoint(localCandidate);
        Vector3 delta = worldCandidate - pivot;
        float length = delta.magnitude;
        if (length < .01f) return localCandidate;

        int count = Physics.SphereCastNonAlloc(pivot, cameraRadius, delta / length, wallHits,
            length, collisionMask, QueryTriggerInteraction.Ignore);
        float allowed = length;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = wallHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(controller.transform) ||
                hit.collider.GetComponentInParent<MonsterAI>() != null) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(.18f, hit.distance - wallBuffer));
        }
        Vector3 safeWorld = pivot + delta / length * allowed;
        return transform.parent.InverseTransformPoint(safeWorld);
    }

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
