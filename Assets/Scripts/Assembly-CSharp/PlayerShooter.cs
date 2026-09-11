using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(LineRenderer))]
public class PlayerShooter : MonoBehaviour
{
	[Header("Settings")]
	[SerializeField]
	private float range = 100f;

	[SerializeField]
	private LayerMask targetLayer;

	[SerializeField]
	private Animator animator;

	[SerializeField]
	private float effectDuration = 0.2f;

	[SerializeField]
	private float fireCooldown = 1.5f;

	[Header("Projectile")]
	[Tooltip("켜면 발사체가 날아가 닿는 순간 판정 연출이 터진다. 끄면 예전처럼 즉시 레이저")]
	[SerializeField]
	private bool useProjectile = true;

	[Tooltip("초당 이동 거리. 낮추면 회피감이 생기고 높이면 시원해진다")]
	[SerializeField]
	private float projectileSpeed = 55f;

	[SerializeField]
	private float projectileSize = 0.28f;

	[SerializeField]
	private Color projectileColor = new Color(0.55f, 0.9f, 1f, 1f);

	[SerializeField]
	private float projectileTrailTime = 0.12f;

	[Tooltip("발사 순간 총구에서 잠깐 번쩍이는 섬광의 길이(m)")]
	[SerializeField]
	private float muzzleFlashLength = 1.6f;

	[Header("Visual Effects (VFX)")]
	[SerializeField]
	private ParticleSystem lightningMuzzle;

	[SerializeField]
	private GameObject lightningImpactPrefab;

	[Tooltip("명중 지점에서 터지는 스파크 색")]
	[SerializeField]
	private Color impactBurstColor = new Color(0.6f, 0.92f, 1f, 1f);

	[Header("Sound Effects (SFX)")]
	[SerializeField]
	private AudioClip zapSound;

	[SerializeField]
	private AudioClip hitSound;

	[Header("Noise — 몬스터에게 들리는 소리")]
	[Tooltip("발사음이 퍼지는 반경. 쏘는 순간 자기 위치가 노출된다")]
	[SerializeField]
	private float shotNoiseRadius = 30f;

	[Header("References")]
	[SerializeField]
	private Transform firePoint;

	[SerializeField]
	private InputActionReference fireAction;

	private AudioSource audioSource;

	private LineRenderer laserLine;

	private float lastFireTime;

	private void Awake()
	{
		audioSource = GetComponent<AudioSource>();
		laserLine = GetComponent<LineRenderer>();
	}

	private void Start()
	{
		laserLine.enabled = false;
		lastFireTime = 0f - fireCooldown;
	}

	private void OnEnable()
	{
		if (fireAction != null && fireAction.action != null)
		{
			fireAction.action.Enable();
			fireAction.action.performed += OnFire;
		}
	}

	private void OnDisable()
	{
		if (fireAction != null && fireAction.action != null)
		{
			fireAction.action.performed -= OnFire;
			fireAction.action.Disable();
		}
	}

	private void OnFire(InputAction.CallbackContext context)
	{
		if ((!(GameFlowManager.Instance != null) || GameFlowManager.Instance.IsGameRunning) && !Cursor.visible && !(Time.time < lastFireTime + fireCooldown))
		{
			lastFireTime = Time.time;
			StartCoroutine(ShootLightning());
		}
	}

	private IEnumerator ShootLightning()
	{
		if (animator != null)
		{
			animator.SetTrigger("Shoot");
		}
		if (audioSource != null && zapSound != null)
		{
			audioSource.PlayOneShot(zapSound, GameSettings.SfxVolume);
		}

		// 발사음 — 명중 여부와 무관하게, 플레이어 위치에서 발생한다.
		// 기획의 핵심 리스크: 진행하려면 쏴야 하고, 쏘면 위치가 드러난다.
		NoiseSystem.Emit(base.transform.position, shotNoiseRadius, NoiseKind.Shot);

		if (lightningMuzzle != null)
		{
			lightningMuzzle.Play();
		}

		if (CrosshairFx.Instance != null)
		{
			CrosshairFx.Instance.Kick();
		}

		// ── 판정은 지금 끝낸다 ──────────────────────────────
		// 발사체에 판정을 맡기면 빠르게 움직이는 대상에서 빗나가기 시작한다.
		Vector3 origin = firePoint.position;
		Vector3 impactPoint = origin + firePoint.forward * range;
		bool didHit = Physics.Raycast(origin, firePoint.forward, out RaycastHit hitInfo, range, targetLayer);

		MonsterAI hitMonster = null;
		Target hitTarget = null;
		if (didHit)
		{
			impactPoint = hitInfo.point;
			hitMonster = hitInfo.collider.GetComponent<MonsterAI>();
			hitTarget = hitInfo.collider.GetComponent<Target>();
		}

		if (useProjectile)
		{
			yield return StartCoroutine(MuzzleFlash(origin));

			RaycastHit capturedHit = hitInfo;
			bool capturedDidHit = didHit;

			Projectile.Fire(origin, impactPoint, projectileSpeed, projectileSize, projectileColor, projectileTrailTime,
				onArrive: () => ApplyHit(hitMonster, hitTarget, capturedDidHit, capturedHit, impactPoint));
			yield break;
		}

		// ── 예전 방식: 즉시 레이저 ──────────────────────────
		yield return StartCoroutine(DrawBeam(origin, impactPoint));
		ApplyHit(hitMonster, hitTarget, didHit, hitInfo, impactPoint);
	}

	/// <summary>발사체가 닿는 순간 실제 피격 처리와 연출을 한다.</summary>
	private void ApplyHit(MonsterAI monster, Target target, bool didHit, RaycastHit hit, Vector3 impactPoint)
	{
		// 날아가는 동안 파괴됐을 수 있다
		if (monster != null)
		{
			monster.OnHitByLaser(base.transform.position);
		}
		if (target != null)
		{
			target.OnHit();
		}

		BurstVfx.Play(impactPoint, impactBurstColor, count: 18, speed: 5f, size: 0.14f, lifetime: 0.4f);

		// 무언가를 실제로 맞혔을 때만 크로스헤어가 반응해야 확인 신호로 기능한다
		if ((monster != null || target != null) && CrosshairFx.Instance != null)
		{
			CrosshairFx.Instance.Hit();
		}

		if (didHit)
		{
			if (lightningImpactPrefab != null)
			{
				Object.Destroy(Object.Instantiate(lightningImpactPrefab, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
			}
			if (hitSound != null)
			{
				AudioSource.PlayClipAtPoint(hitSound, hit.point, GameSettings.SfxVolume);
			}
		}
	}

	/// <summary>총구에서 잠깐 튀는 짧은 섬광.</summary>
	private IEnumerator MuzzleFlash(Vector3 origin)
	{
		if (muzzleFlashLength <= 0f) { yield break; }

		laserLine.enabled = true;
		laserLine.SetPosition(0, origin);
		laserLine.SetPosition(1, origin + firePoint.forward * muzzleFlashLength);
		SetBeamAlpha(1f);

		yield return new WaitForSeconds(0.04f);
		laserLine.enabled = false;
	}

	private IEnumerator DrawBeam(Vector3 origin, Vector3 end)
	{
		laserLine.enabled = true;
		laserLine.SetPosition(0, origin);
		laserLine.SetPosition(1, end);

		float elapsed = 0f;
		while (elapsed < effectDuration)
		{
			elapsed += Time.deltaTime;
			SetBeamAlpha(Mathf.Lerp(1f, 0f, elapsed / effectDuration));
			yield return null;
		}
		laserLine.enabled = false;
	}

	private void SetBeamAlpha(float a)
	{
		Color s = laserLine.startColor;
		Color e = laserLine.endColor;
		s.a = a;
		e.a = a;
		laserLine.startColor = s;
		laserLine.endColor = e;
	}
}
