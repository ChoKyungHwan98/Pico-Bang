using System.Collections;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
	/// <summary>AI 테스트 씬: 잡혀도 체력이 줄지 않는다.</summary>
	public static bool DebugGodMode;

	[Header("Health Settings")]
	[SerializeField]
	private int maxHealth = 5;

	[SerializeField]
	private float knockbackForce = 15f;

	[SerializeField]
	private float stunDuration = 0.5f;

	[SerializeField]
	private float invincibilityDuration = 1.5f;

	[Header("Visual Effects")]
	[SerializeField]
	private Renderer[] bodyRenderers;

	[SerializeField]
	private float blinkInterval = 0.1f;

	[Header("UI References")]
	[SerializeField]
	private GameObject[] heartIcons;

	private int currentHealth;

	private bool isInvincible;

	private Rigidbody rb;

	private PlayerController playerController;

	private void Awake()
	{
		rb = GetComponent<Rigidbody>();
		playerController = GetComponent<PlayerController>();
		if (bodyRenderers == null || bodyRenderers.Length == 0)
		{
			bodyRenderers = GetComponentsInChildren<Renderer>();
		}
	}

	private void Start()
	{
		InitializeHealth();
	}

	public void InitializeHealth()
	{
		currentHealth = maxHealth;
		isInvincible = false;
		if (rb != null)
		{
			rb.isKinematic = false;
		}
		UpdateHeartUI();
		if (bodyRenderers != null)
		{
			Renderer[] array = bodyRenderers;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].enabled = true;
			}
		}
	}

	public void ResetHealth()
	{
		InitializeHealth();
	}

	public void TakeDamage(Vector3 attackerPos)
	{
		TakeDamage(attackerPos, 1);
	}

	/// <summary>
	/// 피격. 깎는 하트 수는 공격자가 정한다 (<see cref="MonsterAI.contactDamageHearts"/>).
	/// </summary>
	public void TakeDamage(Vector3 attackerPos, int hearts)
	{
		if (DebugGodMode) { return; }
		if (!isInvincible && currentHealth > 0)
		{
			currentHealth -= Mathf.Max(1, hearts);
			if (currentHealth < 0) { currentHealth = 0; }
			UpdateHeartUI();
			Debug.Log($"플레이어 피격! 남은 체력: {currentHealth}");
			if (currentHealth <= 0)
			{
				Die();
			}
			else
			{
				StartCoroutine(HitRoutine(attackerPos));
			}
		}
	}

	private IEnumerator HitRoutine(Vector3 attackerPos)
	{
		isInvincible = true;
		if (playerController != null)
		{
			playerController.enabled = false;
		}
		Vector3 normalized = (base.transform.position - attackerPos).normalized;
		normalized.y = 0.2f;
		if (rb != null)
		{
			rb.linearVelocity = Vector3.zero;
			rb.AddForce(normalized * knockbackForce, ForceMode.Impulse);
		}
		StartCoroutine(BlinkEffect());
		yield return new WaitForSeconds(stunDuration);
		if (playerController != null)
		{
			playerController.enabled = true;
		}
		yield return new WaitForSeconds(invincibilityDuration - stunDuration);
		isInvincible = false;
	}

	private IEnumerator BlinkEffect()
	{
		Renderer[] array;
		for (float timer = 0f; timer < invincibilityDuration; timer += blinkInterval)
		{
			array = bodyRenderers;
			foreach (Renderer obj in array)
			{
				obj.enabled = !obj.enabled;
			}
			yield return new WaitForSeconds(blinkInterval);
		}
		array = bodyRenderers;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].enabled = true;
		}
	}

	private void UpdateHeartUI()
	{
		for (int i = 0; i < heartIcons.Length; i++)
		{
			if (heartIcons[i] != null)
			{
				heartIcons[i].SetActive(i < currentHealth);
			}
		}
	}

	private void Die()
	{
		Debug.Log("\ud83d\udc80 플레이어 사망!");
		if (playerController != null)
		{
			playerController.enabled = false;
		}
		if (rb != null)
		{
			rb.isKinematic = true;
		}
		if (GameFlowManager.Instance != null)
		{
			GameFlowManager.Instance.TriggerGameOver();
		}
	}

	public int GetCurrentHealth()
	{
		return currentHealth;
	}
}
