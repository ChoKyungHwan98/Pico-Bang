using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// A persistent rear camera panel. It shows the space behind the player at all times,
/// then briefly cuts closer to the most dangerous pursuer when one closes in.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(250)]
public sealed class RearViewMirror : MonoBehaviour
{
    [SerializeField] private Vector2 panelSize = new Vector2(430f, 220f);
    [SerializeField] private float triggerDistance = 17f;
    [SerializeField] private float resetDistance = 22f;
    [SerializeField] private float showDuration = 1.25f;
    [SerializeField] private float replayCooldown = 2.8f;
    [SerializeField] private float wideFieldOfView = 58f;
    [SerializeField] private float zoomFieldOfView = 31f;
    [SerializeField] private float idleCameraDistance = 7.5f;
    [SerializeField] private float idleCameraHeight = 2.1f;
    [SerializeField] private float threatIntroTime = .28f;
    [SerializeField] private float threatOutroTime = .24f;

    private Camera gameplayCamera;
    private Camera cutInCamera;
    private Transform player;
    private RenderTexture texture;
    private GameObject canvasObject;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform frame;
    private Image border;
    private MonsterAI focusedThreat;
    private MonsterAI lastTriggeredThreat;
    private float showElapsed;
    private float nextAllowedTime;
    private readonly RaycastHit[] cameraHits = new RaycastHit[16];
    private Renderer[] playerVisuals;
    private int[] playerVisualLayers;
    private bool visualsIsolated;

    private static readonly Color Orange = new Color(1f, .32f, .08f, 1f);

    private void Awake()
    {
        gameplayCamera = GetComponent<Camera>();
        PlayerCamera legacyController = GetComponent<PlayerCamera>();
        if (legacyController != null && legacyController.PlayerTarget != null)
            player = legacyController.PlayerTarget.transform;
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player != null)
        {
            playerVisuals = player.GetComponentsInChildren<Renderer>(true);
            playerVisualLayers = new int[playerVisuals.Length];
            for (int i = 0; i < playerVisuals.Length; i++)
                playerVisualLayers[i] = playerVisuals[i].gameObject.layer;

            // 후방 카메라에서 플레이어 몸이 화면을 막지 않도록 전용 렌더 레이어로 옮긴다.
            // 메인 카메라는 이 레이어를 계속 그리며, 후방 카메라만 제외한다.
            if (gameplayCamera != null) gameplayCamera.cullingMask |= 1 << 31;
            SetPlayerVisualIsolation(true);
        }

        texture = new RenderTexture(768, 384, 24, RenderTextureFormat.ARGB32)
        {
            name = "ThreatCutIn_Runtime",
            antiAliasing = 1,
            useMipMap = false
        };
        texture.Create();

        GameObject cameraObject = new GameObject("~ThreatCutInCamera");
        cameraObject.transform.SetParent(transform, false);
        cutInCamera = cameraObject.AddComponent<Camera>();
        cutInCamera.CopyFrom(gameplayCamera);
        cutInCamera.tag = "Untagged";
        cutInCamera.targetTexture = texture;
        cutInCamera.rect = new Rect(0f, 0f, 1f, 1f);
        cutInCamera.depth = -10f;
        cutInCamera.farClipPlane = 70f;
        cutInCamera.nearClipPlane = .12f;
        cutInCamera.cullingMask = gameplayCamera.cullingMask & ~(1 << 5) & ~(1 << 31);
        cutInCamera.allowHDR = false;
        cutInCamera.allowMSAA = false;
        UniversalAdditionalCameraData data = cutInCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        cutInCamera.enabled = false;

        BuildUi();
        SetVisible(false);
    }

    private void BuildUi()
    {
        canvasObject = new GameObject("~ThreatCutInUI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        GameObject frameObject = new GameObject("ThreatCutIn", typeof(RectTransform), typeof(Image));
        frameObject.transform.SetParent(canvas.transform, false);
        frame = frameObject.GetComponent<RectTransform>();
        frame.anchorMin = frame.anchorMax = new Vector2(1f, 1f);
        frame.pivot = new Vector2(1f, 1f);
        frame.anchoredPosition = new Vector2(-28f, -30f);
        frame.sizeDelta = panelSize + new Vector2(12f, 12f);
        border = frameObject.GetComponent<Image>();
        border.color = Orange;
        border.raycastTarget = false;

        GameObject clipObject = new GameObject("CutInClip", typeof(RectTransform), typeof(RectMask2D));
        clipObject.transform.SetParent(frame, false);
        RectTransform clip = clipObject.GetComponent<RectTransform>();
        clip.anchorMin = Vector2.zero;
        clip.anchorMax = Vector2.one;
        clip.offsetMin = new Vector2(6f, 6f);
        clip.offsetMax = new Vector2(-6f, -6f);

        GameObject imageObject = new GameObject("ThreatImage", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(clip, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
        RawImage raw = imageObject.GetComponent<RawImage>();
        raw.texture = texture;
        raw.raycastTarget = false;

    }

    private void LateUpdate()
    {
        bool gameplay = player != null && gameplayCamera.enabled &&
            (GameFlowManager.Instance == null || GameFlowManager.Instance.IsGameRunning);
        if (!gameplay)
        {
            SetVisible(false);
            focusedThreat = null;
            return;
        }

        if (!canvas.enabled) SetVisible(true);

        MonsterAI candidate = FindThreat(out float candidateDistance);
        if (candidate == null)
        {
            if (lastTriggeredThreat != null &&
                Vector3.Distance(lastTriggeredThreat.transform.position, player.position) > resetDistance)
            {
                lastTriggeredThreat = null;
            }
        }
        else if (Time.time >= nextAllowedTime && ShouldTrigger(candidate, candidateDistance))
        {
            focusedThreat = candidate;
            lastTriggeredThreat = candidate;
            showElapsed = 0f;
            nextAllowedTime = Time.time + replayCooldown;
        }

        if (focusedThreat != null)
        {
            showElapsed += Time.deltaTime;
            if (showElapsed < showDuration && focusedThreat != null)
            {
                UpdateCamera(focusedThreat);
                UpdateThreatAnimation();
                return;
            }

            focusedThreat = null;
            showElapsed = 0f;
        }

        UpdateIdleCamera();
        UpdateIdleUi();
    }

    private bool ShouldTrigger(MonsterAI candidate, float distance)
    {
        // 같은 몬스터가 가까이 머무는 동안 줌을 계속 반복하지 않는다.
        return candidate != lastTriggeredThreat;
    }

    private MonsterAI FindThreat(out float bestDistance)
    {
        MonsterAI best = null;
        bestDistance = float.PositiveInfinity;
        float bestScore = float.NegativeInfinity;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;

        foreach (MonsterAI monster in MonsterAI.activeMonsters)
        {
            if (monster == null || monster.IsInStun || monster.IsGivingUp) continue;
            Vector3 toMonster = Vector3.ProjectOnPlane(monster.transform.position - player.position, Vector3.up);
            float distance = toMonster.magnitude;
            if (distance < .01f || distance > triggerDistance) continue;
            Vector3 direction = toMonster / distance;
            float rearAmount = -Vector3.Dot(forward, direction);
            if (rearAmount < -.2f) continue;

            bool pursuing = monster.CurrentState == MonsterAI.State.Chase ||
                monster.CurrentState == MonsterAI.State.Intercept ||
                monster.CurrentState == MonsterAI.State.Prepare ||
                monster.CurrentState == MonsterAI.State.Investigate;
            Vector3 towardPlayer = -direction;
            float closingSpeed = Vector3.Dot(monster.PlanarVelocity, towardPlayer);
            if (!pursuing && distance > 8f) continue;
            if (closingSpeed < .15f && distance > 10f) continue;

            float score = (triggerDistance - distance) + rearAmount * 5f + Mathf.Max(0f, closingSpeed) * .7f;
            if (monster.CurrentState == MonsterAI.State.Chase) score += 2f;
            if (score <= bestScore) continue;
            best = monster;
            bestScore = score;
            bestDistance = distance;
        }
        return best;
    }

    private void UpdateCamera(MonsterAI threat)
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 playerChest = player.position + Vector3.up * 1.25f;
        // 플레이어 앞쪽에서 뒤를 바라봐야 실제 사이드미러가 된다.
        // 몸은 후방 카메라에서 제외했으므로 위/옆으로 과하게 비킬 필요가 없다.
        Vector3 desired = player.position + Vector3.up * 1.75f + forward * .9f + right * .28f;
        Vector3 travel = desired - playerChest;
        int hitCount = Physics.SphereCastNonAlloc(playerChest, .18f, travel.normalized, cameraHits,
            travel.magnitude, gameplayCamera.cullingMask & ~(1 << 8), QueryTriggerInteraction.Ignore);
        float allowed = travel.magnitude;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = cameraHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(player)) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(.12f, hit.distance - .12f));
        }
        desired = playerChest + travel.normalized * allowed;

        Vector3 target = threat.transform.position + Vector3.up * 1.35f;
        Vector3 focus = target;
        cutInCamera.transform.SetPositionAndRotation(desired,
            Quaternion.LookRotation(focus - desired, Vector3.up));
        float zoom = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(showElapsed / threatIntroTime));
        if (showElapsed > showDuration - threatOutroTime)
            zoom = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(showDuration - threatOutroTime, showDuration, showElapsed));
        // 얼굴만 화면을 가득 채우지 않도록 초근접 상황에서는 필요한 화각을 확보한다.
        float fitFieldOfView = Mathf.Atan2(1.7f, Vector3.Distance(desired, target)) * 2f * Mathf.Rad2Deg;
        float safeZoom = Mathf.Max(zoomFieldOfView, fitFieldOfView);
        cutInCamera.fieldOfView = Mathf.Lerp(wideFieldOfView, safeZoom, zoom);
    }

    private void UpdateIdleCamera()
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 chest = player.position + Vector3.up * 1.25f;
        // 상시 화면은 플레이어 바로 앞에서 뒤쪽 통로를 본다. 멀리 떨어진 추적자는
        // 넓게 보이고, 가까워지면 같은 축에서 한 번만 줌인한다.
        Vector3 desired = player.position + forward * .9f + right * .28f + Vector3.up * idleCameraHeight;
        Vector3 travel = desired - chest;
        float length = travel.magnitude;
        if (length > .01f)
        {
            int mask = gameplayCamera.cullingMask & ~(1 << 8) & ~(1 << 31) & ~(1 << 5);
            int hitCount = Physics.SphereCastNonAlloc(chest, .16f, travel / length, cameraHits,
                length, mask, QueryTriggerInteraction.Ignore);
            float allowed = length;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = cameraHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(player)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(.25f, hit.distance - .12f));
            }
            desired = chest + travel / length * allowed;
        }

        Vector3 focus = player.position - forward * idleCameraDistance + Vector3.up * 1.1f;
        cutInCamera.transform.SetPositionAndRotation(desired,
            Quaternion.LookRotation(focus - desired, Vector3.up));
        cutInCamera.fieldOfView = Mathf.Lerp(cutInCamera.fieldOfView, wideFieldOfView,
            1f - Mathf.Exp(-Time.deltaTime * 12f));
    }

    private void UpdateThreatAnimation()
    {
        canvasGroup.alpha = 1f;
        float punch = Mathf.Sin(Mathf.Clamp01(showElapsed / .28f) * Mathf.PI) * .045f;
        frame.localScale = Vector3.one * (1f + punch);
        frame.anchoredPosition = new Vector2(-28f, -30f);
        border.color = new Color(Orange.r, Orange.g, Orange.b,
            .82f + Mathf.Sin(Time.time * 16f) * .16f);

    }

    private void UpdateIdleUi()
    {
        canvasGroup.alpha = 1f;
        frame.localScale = Vector3.one;
        frame.anchoredPosition = new Vector2(-28f, -30f);
        border.color = new Color(.18f, .72f, .92f, .82f);
    }

    private void SetVisible(bool visible)
    {
        if (canvas != null) canvas.enabled = visible;
        if (cutInCamera != null) cutInCamera.enabled = visible;
        if (canvasGroup != null && !visible) canvasGroup.alpha = 0f;
    }

    private void SetPlayerVisualIsolation(bool isolated)
    {
        if (visualsIsolated == isolated || playerVisuals == null) return;
        visualsIsolated = isolated;
        for (int i = 0; i < playerVisuals.Length; i++)
        {
            if (playerVisuals[i] == null) continue;
            playerVisuals[i].gameObject.layer = isolated ? 31 : playerVisualLayers[i];
        }
    }

    private void OnDestroy()
    {
        SetPlayerVisualIsolation(false);
        if (canvasObject != null) Destroy(canvasObject);
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
        }
    }
}
