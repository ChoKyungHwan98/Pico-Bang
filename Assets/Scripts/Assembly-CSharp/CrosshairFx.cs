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

	[Tooltip("맞혔을 때 색이 유지되는 시간")]
	public float hitFlashTime = 0.18f;

	private float currentGap;
	private float hitFlashRemaining;

	private void Awake()
	{
		Instance = this;
		currentGap = baseGap;
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

	private void Update()
	{
		if (currentGap > baseGap)
		{
			currentGap = Mathf.Lerp(currentGap, baseGap, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
			if (currentGap - baseGap < 0.2f) { currentGap = baseGap; }
			ApplyGap();
		}

		if (hitFlashRemaining > 0f)
		{
			hitFlashRemaining -= Time.deltaTime;
			if (hitFlashRemaining <= 0f) { ApplyTint(idleColor); }
			else { ApplyTint(hitColor); }
		}
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
