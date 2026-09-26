using System.Collections;
using UnityEngine;

public class Target : MonoBehaviour
{
	[SerializeField]
	private GameObject hitEffectPrefab;

	[SerializeField]
	private AudioClip hitSound;

	[Header("Noise — 몬스터에게 들리는 소리")]
	[Tooltip("과녁이 부서지는 소리의 반경. 발사음보다 크게 두면 '무엇을 쐈는가'가 위험도를 가른다")]
	[SerializeField]
	private float destroyNoiseRadius = 45f;

	[Header("파괴 연출")]
	[Tooltip("터지기 직전 부풀었다 사라지는 시간. 0이면 즉시 사라진다")]
	[SerializeField]
	private float shatterTime = 0.12f;

	[Tooltip("터지기 직전 부푸는 배율")]
	[SerializeField]
	private float shatterPunchScale = 1.35f;

	[SerializeField]
	private Color burstColor = new Color(0.45f, 0.85f, 1f, 1f);

	[SerializeField]
	private int burstCount = 30;

	private bool isDestroyed;
	public float PlacementHeight { get; private set; } = -1f;
	public Vector3 ShootingPosition { get; private set; }
	public void SetPlacement(float height, Vector3 shootingPosition) { PlacementHeight = height; ShootingPosition = shootingPosition; }

	public void OnHit()
	{
		ResolveHit(false, Vector3.zero, 0f);
	}

	/// <summary>플레이어 사격으로 맞았을 때. 최초 파괴 시 발사 순간 위치를 무리 경보로 전달한다.</summary>
	public void OnHit(Vector3 shotPosition, float shotTime)
	{
		ResolveHit(true, shotPosition, shotTime);
	}

	private void ResolveHit(bool hasShotContext, Vector3 shotPosition, float shotTime)
	{
		// 발사체가 날아오는 사이 두 번 맞을 수 있다
		if (isDestroyed) { return; }
		isDestroyed = true;
		PlaytestRecorder.Record("target_destroyed", "target_" + GetInstanceID(), transform.position, "height=" + PlacementHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), shotPosition);

		if (TargetManager.Instance != null)
		{
			TargetManager.Instance.OnTargetDestroyed();
		}

		// 파괴음은 과녁 자리에서 난다 — 플레이어 위치가 아니다.
		// 덕분에 "멀리 있는 과녁을 쏴서 몬스터를 유인한다"는 수가 성립한다.
		NoiseSystem.Emit(base.transform.position, destroyNoiseRadius, NoiseKind.TargetDestroyed);

		if (hitEffectPrefab != null)
		{
			Object.Instantiate(hitEffectPrefab, base.transform.position, base.transform.rotation);
		}
		if (hitSound != null)
		{
			AudioSource.PlayClipAtPoint(hitSound, base.transform.position, GameSettings.SfxVolume);
		}

		BurstVfx.Play(base.transform.position, burstColor, burstCount, speed: 7f, size: 0.22f, lifetime: 0.6f);

		if (shatterTime > 0f)
		{
			// 충돌은 즉시 끄고 연출만 남긴다 — 이미 부순 과녁을 또 맞히면 안 된다
			foreach (Collider c in GetComponentsInChildren<Collider>())
			{
				c.enabled = false;
			}
			StartCoroutine(ShatterRoutine());
		}
		else
		{
			Object.Destroy(base.gameObject);
		}
	}

	/// <summary>부풀었다가 순식간에 쪼그라들며 사라진다. 짧아야 시원하다.</summary>
	private IEnumerator ShatterRoutine()
	{
		Vector3 baseScale = base.transform.localScale;
		float elapsed = 0f;

		while (elapsed < shatterTime)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / shatterTime);

			// 앞의 30%는 부풀고, 나머지는 0으로 수축
			float s = (t < 0.3f)
				? Mathf.Lerp(1f, shatterPunchScale, t / 0.3f)
				: Mathf.Lerp(shatterPunchScale, 0f, (t - 0.3f) / 0.7f);

			base.transform.localScale = baseScale * s;
			base.transform.Rotate(Vector3.up, 720f * Time.deltaTime, Space.Self);
			yield return null;
		}

		Object.Destroy(base.gameObject);
	}
}
