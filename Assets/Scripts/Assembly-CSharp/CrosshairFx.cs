using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 크로스헤어 연출.
///
/// 네 개의 눈금이 중심에서 일정 간격 떨어져 있고, 쏘면 순간적으로 벌어졌다 돌아온다.
/// 맞히면 색이 잠깐 튄다 — "맞았다"는 확인이 즉시 오는 것이 사격감의 핵심이다.
/// </summary>
public class CrosshairFx : MonoBehaviour
{
	public static CrosshairFx Instance { get; private set; }

	[Header("Ticks (상 / 하 / 좌 / 우)")]
	public RectTransform[] ticks = new RectTransform[4];

	public Graphic[] tintTargets;

	[Header("Gap")]
	[Tooltip("평상시 중심에서 눈금까지의 거리(px)")]
	public float baseGap = 18f;

	[Tooltip("발사 순간 벌어지는 거리(px)")]
	public float kickGap = 34f;

	[Tooltip("원래 간격으로 돌아오는 속도")]
	public float recoverSpeed = 9f;

	[Header("Color")]
	public Color idleColor = new Color(1f, 1f, 1f, 0.9f);

	public Color hitColor = new Color(1f, 0.55f, 0.10f, 1f);

	public Color targetColor = new Color(.45f, 1f, .92f, 1f);

	[Tooltip("쏘면 조준점 앞의 벽에 막힐 때 색(어깨에서 나가는 발사가 가까운 벽 모서리에 걸림)")]
	public Color blockedColor = new Color(1f, .3f, .3f, 1f);

	[Tooltip("재장전 중 투명도 — 조준선이 닫히며 밝아지면 쏠 수 있다")]
	[Range(0f, 1f)] public float coolingAlpha = .35f;

	[Tooltip("다 닫혀 쏠 수 있게 된 순간 살짝 커지는 크기")]
	public float readyPopScale = 1.35f;

	[Tooltip("눈금 테두리(어두운 외곽선) — 밝은 벽 위에서도 보이게")]
	public Color outlineColor = new Color(0f, 0f, 0f, .75f);

	[SerializeField] private float aimRange = 100f;

	[Tooltip("맞혔을 때 색이 유지되는 시간")]
	public float hitFlashTime = 0.18f;

	private float currentGap;
	private float hitFlashRemaining;
	private float popRemaining;
	private bool wasReady = true;
	private RectTransform reticleRoot;
	private Camera aimCamera;
	private Transform player;
	private readonly RaycastHit[] aimHits = new RaycastHit[24];

	private void Awake()
	{
		Instance = this;
		currentGap = baseGap;
		aimCamera = Camera.main;
		player = GameObject.FindGameObjectWithTag("Player")?.transform;
		RectTransform reticle = transform.Find("Reticle") as RectTransform;
		reticleRoot = reticle;
		RectTransform dot = reticle != null ? reticle.Find("Dot") as RectTransform : null;
		if (dot != null) dot.sizeDelta = new Vector2(6f, 6f);
		// 체크무늬 벽 위에서 흰 눈금이 묻혔다 — 어두운 외곽선을 둘러 어디서나 보이게 한다
		if (tintTargets != null)
			foreach (Graphic g in tintTargets)
			{
				if (g == null || g.GetComponent<Outline>() != null) continue;
				var outline = g.gameObject.AddComponent<Outline>();
				outline.effectColor = outlineColor;
				outline.effectDistance = new Vector2(1.5f, -1.5f);
			}
	}

	private void OnDestroy()
	{
		if (Instance == this) { Instance = null; }
	}

	private void OnEnable()
	{
		currentGap = baseGap;
		hitFlashRemaining = 0f;
		ApplyGap();
		ApplyTint(idleColor);
	}

	/// <summary>발사 순간 호출 — 눈금이 벌어진다.</summary>
	public void Kick()
	{
		currentGap = Mathf.Max(currentGap, kickGap);
	}

	/// <summary>명중 순간 호출 — 색이 튄다.</summary>
	public void Hit()
	{
		hitFlashRemaining = hitFlashTime;
	}

	/// <summary>
	/// 조준선이 알려 주는 세 가지(2026-09-25 조작감 폴리싱):
	/// ① 재장전 — 쏘면 벌어지고 흐려졌다가, 쿨타임 동안 닫히며 밝아진다. 다 닫히면 살짝 튀며 "쏠 수 있음"
	/// ② 조준 대상 — 과녁·몬스터를 겨누면 청록
	/// ③ 막힘 — 어깨에서 나가는 발사가 가까운 벽에 걸리면 빨강(조준선은 과녁 위인데 벽을 맞히는 혼란 방지)
	/// </summary>
	private void Update()
	{
		PlayerShooter shooter = PlayerShooter.Instance;
		float readiness = shooter != null && shooter.isActiveAndEnabled ? shooter.Readiness : 1f;
		bool ready = readiness >= 1f;
		if (ready && !wasReady) popRemaining = .12f;
		wasReady = ready;

		// 쿨타임 동안 kickGap → baseGap으로 닫힌다(발사 직후의 순간 벌어짐보다 느리게, 장전 진행을 그대로 보여 줌)
		float chargeGap = Mathf.Lerp(kickGap, baseGap, readiness);
		if (currentGap > chargeGap)
			currentGap = Mathf.Lerp(currentGap, chargeGap, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
		if (currentGap < chargeGap) currentGap = chargeGap;
		if (ready && currentGap - baseGap < 0.2f) currentGap = baseGap;
		ApplyGap();

		if (popRemaining > 0f) popRemaining -= Time.deltaTime;
		if (reticleRoot != null)
			reticleRoot.localScale = Vector3.one * (popRemaining > 0f ? Mathf.Lerp(1f, readyPopScale, popRemaining / .12f) : 1f);

		Color color;
		if (hitFlashRemaining > 0f)
		{
			hitFlashRemaining -= Time.deltaTime;
			color = hitColor;
		}
		else
		{
			color = AimState(shooter);
		}
		color.a *= ready ? 1f : Mathf.Lerp(coolingAlpha, 1f, readiness * readiness);
		ApplyTint(color);
	}

	private Color AimState(PlayerShooter shooter)
	{
		if (aimCamera == null) aimCamera = Camera.main;
		if (aimCamera == null) return idleColor;
		Ray ray = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
		if (shooter != null && shooter.IsShotBlocked(ray, out _)) return blockedColor;
		return IsAimingAtTarget() ? targetColor : idleColor;
	}

	private bool IsAimingAtTarget()
	{
		if (aimCamera == null) aimCamera = Camera.main;
		if (aimCamera == null) return false;
		Ray ray = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
		int count = Physics.RaycastNonAlloc(ray, aimHits, aimRange, ~0, QueryTriggerInteraction.Ignore);
		RaycastHit nearest = default;
		float nearestDistance = float.PositiveInfinity;
		for (int i = 0; i < count; i++)
		{
			RaycastHit candidate = aimHits[i];
			if (candidate.collider == null || (player != null && candidate.collider.transform.IsChildOf(player))) continue;
			if (candidate.distance >= nearestDistance) continue;
			nearest = candidate;
			nearestDistance = candidate.distance;
		}
		if (nearest.collider == null) return false;
		return nearest.collider.GetComponentInParent<Target>() != null ||
			nearest.collider.GetComponentInParent<MonsterAI>() != null;
	}

	private void ApplyGap()
	{
		if (ticks == null) { return; }
		// 0=상 1=하 2=좌 3=우
		Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
		for (int i = 0; i < ticks.Length && i < 4; i++)
		{
			if (ticks[i] == null) { continue; }
			ticks[i].anchoredPosition = dirs[i] * currentGap;
		}
	}

	private void ApplyTint(Color c)
	{
		if (tintTargets == null) { return; }
		foreach (Graphic g in tintTargets)
		{
			if (g != null) { g.color = c; }
		}
	}
}
