using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

public class GameFlowManager : MonoBehaviour
{
	public static GameFlowManager Instance;

	[Header("1. UI Panels")]
	[SerializeField]
	private GameObject homePanel;

	[SerializeField]
	private GameObject inGamePanel;

	[SerializeField]
	private GameObject gameOverPanel;

	[SerializeField]
	private GameObject gameClearPanel;

	[SerializeField]
	private GameObject crosshairUI;

	[Header("2. Intro Elements")]
	[SerializeField]
	private TextMeshProUGUI centerCountdownText;

	[SerializeField]
	private RectTransform irisImageRect;

	[Header("3. Camera & Player")]
	[SerializeField]
	private Transform mainCamera;

	[SerializeField]
	private Transform introCamPos;

	[SerializeField]
	private Transform tpsCamPos;

	[SerializeField]
	private Transform playerStartPoint;

	[Header("4. Controllers")]
	[SerializeField]
	private PlayerController playerController;

	[SerializeField]
	private PlayerHealth playerHealth;

	[SerializeField]
	private Animator playerAnimator;

	[SerializeField]
	private PlayerShooter playerShooter;

	[Header("5. Character Face")]
	[SerializeField]
	private SkinnedMeshRenderer faceRenderer;

	[SerializeField]
	private int smileIndex = 6;

	/// <summary>블렌드셰이프 하나의 가중치. 표정은 여러 개를 겹쳐야 자연스럽다.</summary>
	[System.Serializable]
	public struct FaceShape
	{
		[Tooltip("블렌드셰이프 인덱스 (0 Blink / 1~5 mouth_A,I,U,E,O / 6 Joy / 7 Angry / 8 Sorrow / 9 Fun)")]
		public int index;

		[Range(0f, 100f)]
		public float weight;
	}

	[Header("3-1. 홈 화면 표정")]
	[Tooltip("비어 있으면 기존 방식(smileIndex 100)을 쓴다. 값을 넣으면 이 조합이 우선한다")]
	[SerializeField]
	private FaceShape[] homeExpression;

	[Header("6. Result Sequence Objects")]
	[SerializeField]
	private CanvasGroup resTimeGroup;

	[SerializeField]
	private CanvasGroup resKillGroup;

	[SerializeField]
	private CanvasGroup resHeartGroup;

	[SerializeField]
	private CanvasGroup resScoreGroup;

	[SerializeField]
	private CanvasGroup resTotalGroup;

	[SerializeField]
	private CanvasGroup restartBtnGroup;

	[SerializeField]
	private TextMeshProUGUI resTimeText;

	[SerializeField]
	private TextMeshProUGUI resKillText;

	[SerializeField]
	private TextMeshProUGUI resHeartText;

	[SerializeField]
	private TextMeshProUGUI resScoreText;

	[SerializeField]
	private TextMeshProUGUI resTotalText;

	private bool isGameRunning;

	public bool IsGameRunning => isGameRunning;

	private void Awake()
	{
		Instance = this;
	}

	private void Start()
	{
		// 초기 상태를 한 번 기록해 둬야 재시작 때 되돌릴 수 있다.
		foreach (IGameResettable r in FindResettables())
		{
			r.SaveInitialState();
		}
		ResetToHomeState();
	}

	/// <summary>
	/// 홈 화면 표정을 적용한다.
	/// 여러 블렌드셰이프를 겹칠 수 있어야 "눈 뜨고 웃는" 표정을 만들 수 있다 —
	/// Joy 하나만 100으로 올리면 눈이 감겨버린다.
	/// </summary>
	private void ApplyHomeExpression()
	{
		if (faceRenderer == null) { return; }

		ClearExpression();

		if (homeExpression != null && homeExpression.Length > 0)
		{
			int count = (faceRenderer.sharedMesh != null) ? faceRenderer.sharedMesh.blendShapeCount : 0;
			foreach (FaceShape s in homeExpression)
			{
				if (s.index >= 0 && s.index < count)
				{
					faceRenderer.SetBlendShapeWeight(s.index, s.weight);
				}
			}
			return;
		}

		// 설정이 비어 있으면 기존 동작 유지
		faceRenderer.SetBlendShapeWeight(smileIndex, 100f);
	}

	/// <summary>인게임에서는 표정을 전부 초기화한다.</summary>
	private void ClearExpression()
	{
		if (faceRenderer == null || faceRenderer.sharedMesh == null) { return; }
		int count = faceRenderer.sharedMesh.blendShapeCount;
		for (int i = 0; i < count; i++)
		{
			faceRenderer.SetBlendShapeWeight(i, 0f);
		}
	}

	/// <summary>씬 안의 IGameResettable 구현체를 모두 찾는다(비활성 포함).</summary>
	private static IGameResettable[] FindResettables()
	{
		return Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
			.OfType<IGameResettable>()
			.ToArray();
	}

	private void ResetToHomeState()
	{
		isGameRunning = false;
		Time.timeScale = 1f;

		// 포위가 진행 중인 상태로 재시작하면 지휘관이 사냥을 계속하고 있다고 착각한다.
		if (MonsterDirector.Instance != null)
		{
			MonsterDirector.Instance.AbortHunt();
		}

		// 인터페이스 구현체들의 자체 초기화 (카메라 상하 각도, 속도선 강도 등).
		// 아래의 명시적 위치 지정보다 먼저 돌려야 playerStartPoint가 최종적으로 이긴다.
		foreach (IGameResettable r in FindResettables())
		{
			r.ResetToInitialState();
		}
		if (SoundManager.Instance != null)
		{
			SoundManager.Instance.PlayHomeBGM();
		}
		if ((bool)homePanel)
		{
			homePanel.SetActive(value: true);
		}
		if ((bool)inGamePanel)
		{
			inGamePanel.SetActive(value: false);
		}
		if ((bool)gameOverPanel)
		{
			gameOverPanel.SetActive(value: false);
		}
		if ((bool)gameClearPanel)
		{
			gameClearPanel.SetActive(value: false);
		}
		if ((bool)crosshairUI)
		{
			crosshairUI.SetActive(value: false);
		}
		if ((bool)centerCountdownText)
		{
			centerCountdownText.gameObject.SetActive(value: false);
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		if ((bool)playerController)
		{
			playerController.enabled = false;

			// 보간이 켜진 리지드바디는 transform만 옮기면 되돌아간다 —
			// TeleportTo가 rb.position과 SyncTransforms까지 처리한다.
			if (playerStartPoint != null)
			{
				playerController.TeleportTo(playerStartPoint.position, playerStartPoint.rotation);
			}
			else if (introCamPos != null)
			{
				playerController.TeleportTo(introCamPos.position, playerController.transform.rotation);
			}
		}
		if ((bool)playerShooter)
		{
			playerShooter.enabled = false;
		}
		if ((bool)playerHealth)
		{
			playerHealth.ResetHealth();
		}
		if ((bool)introCamPos && (bool)mainCamera)
		{
			mainCamera.SetParent(null);
			mainCamera.position = introCamPos.position;
			mainCamera.rotation = introCamPos.rotation;
		}
		if ((bool)playerAnimator)
		{
			playerAnimator.SetFloat("Speed", 0f);
			playerAnimator.SetFloat("Move X", 0f);
			playerAnimator.SetFloat("Move Y", 0f);
			playerAnimator.SetFloat("VerticalVelocity", 0f);
			playerAnimator.SetBool("IsGrounded", value: true);
			playerAnimator.SetBool("IsJumping", value: false);
			playerAnimator.SetBool("IsFalling", value: false);
			playerAnimator.SetBool("IsDancing", value: true);
		}
		ApplyHomeExpression();
		if (TargetManager.Instance != null)
		{
			TargetManager.Instance.ResetGame();
		}
		MonsterAI[] array = Object.FindObjectsByType<MonsterAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (MonsterAI monsterAI in array)
		{
			if (monsterAI != null)
			{
				monsterAI.ResetMonster();
			}
		}
		if (irisImageRect != null)
		{
			irisImageRect.gameObject.SetActive(value: true);
			irisImageRect.sizeDelta = Vector2.zero;
		}
	}

	public void OnStartButtonClicked()
	{
		StartCoroutine(StartSequence());
	}

	/// <summary>
	/// AI 테스트 씬 전용: 홈 화면·카메라 이동·카운트다운·BGM 없이 곧바로 게임 진행 상태로 들어간다.
	/// 몬스터와 감독은 IsGameRunning만 보고 움직이므로 이것만 켜면 된다.
	/// </summary>
	public void DebugBeginTest()
	{
		StopAllCoroutines();
		Time.timeScale = 1f;
		isGameRunning = true;
	}

	public void OnExitClicked()
	{
		Application.Quit();
	}

	public void OnRestartClicked()
	{
		StartCoroutine(ReturnToHomeSequence());
	}

	private IEnumerator ReturnToHomeSequence()
	{
		if (SoundManager.Instance != null)
		{
			SoundManager.Instance.StopBGMWithFade();
		}
		Cursor.visible = false;
		if (irisImageRect != null)
		{
			irisImageRect.gameObject.SetActive(value: true);
			float duration = 1f;
			float time = 0f;
			while (time < duration)
			{
				time += Time.unscaledDeltaTime;
				float num = Mathf.Lerp(3000f, 0f, time / duration);
				irisImageRect.sizeDelta = new Vector2(num, num);
				yield return null;
			}
			irisImageRect.sizeDelta = Vector2.zero;
		}
		yield return new WaitForSecondsRealtime(0.5f);
		ResetToHomeState();
		if (irisImageRect != null)
		{
			float time = 1f;
			float duration = 0f;
			while (duration < time)
			{
				duration += Time.unscaledDeltaTime;
				float num2 = Mathf.Lerp(0f, 3000f, duration / time);
				irisImageRect.sizeDelta = new Vector2(num2, num2);
				yield return null;
			}
			irisImageRect.gameObject.SetActive(value: false);
		}
	}

	private IEnumerator StartSequence()
	{
		if ((bool)homePanel)
		{
			homePanel.SetActive(value: false);
		}
		if (SoundManager.Instance != null)
		{
			SoundManager.Instance.StopBGMWithFade();
		}
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		if ((bool)playerAnimator)
		{
			playerAnimator.SetBool("IsDancing", value: false);
		}
		ClearExpression();
		float duration = 1.5f;
		float time = 0f;
		Vector3 startPos = mainCamera.position;
		Quaternion startRot = mainCamera.rotation;
		while (time < duration)
		{
			time += Time.deltaTime;
			float t = time / duration;
			if ((bool)tpsCamPos)
			{
				mainCamera.position = Vector3.Lerp(startPos, tpsCamPos.position, t);
				mainCamera.rotation = Quaternion.Lerp(startRot, tpsCamPos.rotation, t);
			}
			yield return null;
		}
		if ((bool)tpsCamPos)
		{
			mainCamera.SetParent(tpsCamPos);
			mainCamera.localPosition = Vector3.zero;
			mainCamera.localRotation = Quaternion.identity;
		}
		if ((bool)inGamePanel)
		{
			inGamePanel.SetActive(value: true);
		}
		// 크로스헤어는 카운트다운이 끝난 뒤에 켠다.
		// 아직 못 쏘는 동안 조준선이 떠 있으면 "지금 쏠 수 있다"는 잘못된 신호를 준다.
		if ((bool)centerCountdownText)
		{
			centerCountdownText.gameObject.SetActive(value: true);
			if (SoundManager.Instance != null)
			{
				SoundManager.Instance.PlayFullCountdownSFX();
			}
			for (int i = 3; i > 0; i--)
			{
				centerCountdownText.text = i.ToString();
				yield return new WaitForSeconds(1f);
			}
			centerCountdownText.text = "START!";
			yield return new WaitForSeconds(0.5f);
			centerCountdownText.gameObject.SetActive(value: false);
		}
		if (SoundManager.Instance != null)
		{
			SoundManager.Instance.PlayInGameBGM();
		}
		isGameRunning = true;

		// 실제로 조작이 열리는 이 시점에 조준선을 켠다
		if ((bool)crosshairUI)
		{
			crosshairUI.SetActive(value: true);
		}

		if ((bool)playerController)
		{
			playerController.enabled = true;
		}
		if ((bool)playerShooter)
		{
			playerShooter.enabled = true;
		}
	}

	public void TriggerGameOver()
	{
		if (isGameRunning)
		{
			isGameRunning = false;
			if (SoundManager.Instance != null)
			{
				SoundManager.Instance.StopBGMWithFade();
			}
			if ((bool)TargetManager.Instance)
			{
				TargetManager.Instance.StopGame();
			}
			StartCoroutine(GameOverSequence());
		}
	}

	private IEnumerator GameOverSequence()
	{
		if ((bool)crosshairUI)
		{
			crosshairUI.SetActive(value: false);
		}
		if ((bool)gameOverPanel)
		{
			gameOverPanel.SetActive(value: true);
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		yield return null;
	}

	public void TriggerGameClear()
	{
		if (isGameRunning)
		{
			isGameRunning = false;
			if (SoundManager.Instance != null)
			{
				SoundManager.Instance.StopBGMWithFade();
			}
			if ((bool)TargetManager.Instance)
			{
				TargetManager.Instance.StopGame();
			}
			MonsterAI[] array = Object.FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
			for (int i = 0; i < array.Length; i++)
			{
				array[i].enabled = false;
			}
			if ((bool)playerController)
			{
				playerController.enabled = false;
			}
			if ((bool)playerShooter)
			{
				playerShooter.enabled = false;
			}
			if ((bool)crosshairUI)
			{
				crosshairUI.SetActive(value: false);
			}
			if ((bool)inGamePanel)
			{
				inGamePanel.SetActive(value: false);
			}
			StartCoroutine(ShowGameClearSequence());
		}
	}

	private IEnumerator ShowGameClearSequence()
	{
		if (introCamPos != null)
		{
			mainCamera.SetParent(null);
			float duration = 1f;
			float time = 0f;
			Vector3 startPos = mainCamera.position;
			Quaternion startRot = mainCamera.rotation;
			while (time < duration)
			{
				time += Time.deltaTime;
				float t = time / duration;
				mainCamera.position = Vector3.Lerp(startPos, introCamPos.position, t);
				mainCamera.rotation = Quaternion.Lerp(startRot, introCamPos.rotation, t);
				yield return null;
			}
		}
		if ((bool)playerAnimator)
		{
			playerAnimator.SetFloat("Speed", 0f);
			playerAnimator.SetBool("IsDancing", value: true);
		}
		ApplyHomeExpression();
		CalculateAndSetScores();
		if ((bool)gameClearPanel)
		{
			gameClearPanel.SetActive(value: true);
		}
		SetAlpha(resTimeGroup, 0f);
		SetAlpha(resKillGroup, 0f);
		SetAlpha(resHeartGroup, 0f);
		SetAlpha(resScoreGroup, 0f);
		SetAlpha(resTotalGroup, 0f);
		SetAlpha(restartBtnGroup, 0f);
		yield return new WaitForSeconds(0.5f);
		SetAlpha(resTimeGroup, 1f);
		yield return new WaitForSeconds(0.5f);
		SetAlpha(resKillGroup, 1f);
		yield return new WaitForSeconds(0.5f);
		SetAlpha(resHeartGroup, 1f);
		yield return new WaitForSeconds(0.5f);
		SetAlpha(resScoreGroup, 1f);
		yield return new WaitForSeconds(0.8f);
		SetAlpha(resTotalGroup, 1f);
		yield return new WaitForSeconds(1f);
		SetAlpha(restartBtnGroup, 1f);
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
	}

	private void SetAlpha(CanvasGroup cg, float alpha)
	{
		if (cg != null)
		{
			cg.alpha = alpha;
		}
	}

	private void CalculateAndSetScores()
	{
		int num = 0;
		int num2 = 0;
		float num3 = 0f;
		int num4 = 0;
		if (TargetManager.Instance != null)
		{
			num = TargetManager.Instance.CurrentScore;
			num2 = TargetManager.Instance.DestroyedCount;
			num3 = TargetManager.Instance.CurrentTime;
		}
		if (playerHealth != null)
		{
			num4 = playerHealth.GetCurrentHealth();
		}
		int num5 = num + num4 * 50 + (int)num3 * 10;
		if ((bool)resTimeText)
		{
			resTimeText.text = $"<size=70%>TIME</size>\n<size=150%><b>{num3:F1}s</b></size>";
		}
		if ((bool)resKillText)
		{
			resKillText.text = $"<size=70%>TARGET</size>\n<size=150%><b>{num2}</b></size>";
		}
		if ((bool)resHeartText)
		{
			resHeartText.text = $"<size=70%>HEART</size>\n<size=150%><b>{num4}</b></size>";
		}
		if ((bool)resScoreText)
		{
			resScoreText.text = $"<size=70%>SCORE</size>\n<size=150%><b>{num}</b></size>";
		}
		if ((bool)resTotalText)
		{
			resTotalText.text = $"<size=80%><color=yellow>TOTAL SCORE</color></size>\n<size=200%><b>{num5}</b></size>";
		}
	}
}
