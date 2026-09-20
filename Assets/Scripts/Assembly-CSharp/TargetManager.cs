using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

public class TargetManager : MonoBehaviour
{
	public static TargetManager Instance;

	/// <summary>AI 테스트 씬: 제한 시간이 줄지 않는다.</summary>
	public static bool DebugFreezeTimer;

	[Header("UI References")]
	[SerializeField]
	private TextMeshProUGUI timeText;

	[SerializeField]
	private TextMeshProUGUI scoreText;

	[SerializeField]
	private TextMeshProUGUI countText;

	[SerializeField]
	private TextMeshProUGUI portalNoticeText;

	[Header("Game Settings")]
	[SerializeField]
	private float gameTime = 150f;

	[SerializeField]
	private int targetGoal = 30;

	[Header("Spawning Settings")]
	[SerializeField]
	private GameObject targetPrefab;

	[SerializeField]
	private Transform[] spawnAreas;

	[SerializeField]
	private float spawnRadius = 20f;

	[SerializeField]
	private int totalTargetsToSpawn = 100;

	[Tooltip("과녁끼리 최소 간격(m). 한곳에 뭉치지 않게 한다")]
	[SerializeField]
	private float minSpacing = 3f;

	[Header("Even Spread — 맵 전체에 골고루")]
	[Tooltip("걸을 수 있는 영역을 N×N칸으로 나눠 칸마다 같은 수를 둔다. 3이면 9칸")]
	[SerializeField]
	private int gridDivisions = 3;

	[HideInInspector] // Legacy serialized range. Height rhythm bands below replace it.
	[SerializeField]
	private Vector2 wallHeightRange = new Vector2(1f, 2.3f);

	[Tooltip("바닥 지점에서 벽을 찾는 최대 거리(m)")]
	[SerializeField]
	private float wallSearchDistance = 12f;

	[Header("Floating Targets — 공중에 떠 있는 구체 과녁")]
	// 기획 결정(2026-09-11): 과녁은 벽에만. 3D에서는 공중 과녁이 시야와 동선을 가린다.
	// 코드는 남겨두고 비율 0으로 꺼둔다 — 다시 시험해 보려면 이 값만 올리면 된다.
	[Tooltip("전체 과녁 중 공중 과녁의 비율. 0이면 전부 벽 과녁 (기획: 0)")]
	[Range(0f, 1f)]
	[SerializeField]
	private float floatingRatio = 0f;

	// 동선을 막지 않도록 점프해도 닿지 않는 높이에 둔다.
	// 캐릭터 키 약 1.6m + 점프 약 1.3m(jumpForce 5) ≈ 머리 최고점 3m, 여기에 구체 반지름 0.5m를 더했다.
	[Tooltip("바닥에서 최소 높이(m). 점프한 머리 높이(약 3m)보다 높아야 동선을 막지 않는다")]
	[SerializeField]
	private float floatMinHeight = 3.2f;

	[Tooltip("바닥에서 최대 높이(m)")]
	[SerializeField]
	private float floatMaxHeight = 4.5f;

	[Tooltip("구체 지름(m)")]
	[SerializeField]
	private float floatDiameter = 1f;

	[Header("Portal")]
	[SerializeField]
	private GameObject portalObject;

	private ProceduralExitPortal exitPortal;
	private Coroutine portalNoticeRoutine;
	public Vector3 PortalPosition => exitPortal != null ? exitPortal.transform.position : Vector3.zero;
	public int TargetGoal => targetGoal;
	public bool PortalOpened => portalOpened;
	public int SpawnedTargetCount => spawnedTargets.Count;

	[Header("Wall target rhythm (floor-relative metres)")]
	[SerializeField] private Vector2 lowTargetHeight = new Vector2(1.1f, 1.65f);
	[SerializeField] private Vector2 normalTargetHeight = new Vector2(2.1f, 2.8f);
	[SerializeField] private Vector2 highTargetHeight = new Vector2(3.4f, 4.2f);
	private NavMeshPath reachablePath;
	private Vector3 spawnStart;
	private bool spawnStartReady;

	// 걸을 수 있는 영역의 범위. 칸 나누기에 쓴다
	private Bounds walkableBounds;
	private bool walkableBoundsReady;

	private float currentTime;

	private int currentScore;

	private int destroyedCount;

	private bool isGameActive;

	private bool portalOpened;

	private List<GameObject> spawnedTargets = new List<GameObject>();

	// 공중 과녁 원본. 벽 과녁 프리팹을 복제해 겉모습만 구체로 바꾼 것 (숨겨 둔 채 재사용)
	private GameObject floatingTemplate;

	public int CurrentScore => currentScore;

	public int DestroyedCount => destroyedCount;

	public float CurrentTime => currentTime;

	private void Awake()
	{
		Instance = this;
		reachablePath = new NavMeshPath();
	}

	private void Start()
	{
	}

	public void ResetGame()
	{
		currentTime = gameTime;
		currentScore = 0;
		destroyedCount = 0;
		isGameActive = true;
		portalOpened = false;
		if (portalNoticeRoutine != null)
		{
			StopCoroutine(portalNoticeRoutine);
			portalNoticeRoutine = null;
		}
		ClearAllTargets();
		if (portalNoticeText != null)
		{
			portalNoticeText.gameObject.SetActive(value: false);
		}
		if (portalObject != null)
		{
			portalObject.SetActive(value: false);
		}
		EnsureExitPortal();
		UpdateUI();
		SpawnTargets(totalTargetsToSpawn);
	}

	private void ClearAllTargets()
	{
		foreach (GameObject spawnedTarget in spawnedTargets)
		{
			if (spawnedTarget != null)
			{
				spawnedTarget.SetActive(false);
				Object.Destroy(spawnedTarget);
			}
		}
		spawnedTargets.Clear();
	}

	private void Update()
	{
		if (!isGameActive || (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning))
		{
			return;
		}
		if (currentTime > 0f && !DebugFreezeTimer)
		{
			currentTime -= Time.deltaTime;
			if (currentTime <= 0f)
			{
				currentTime = 0f;
				EndGame(isWin: false);
			}
		}
		UpdateUI();
	}

	public void OnTargetDestroyed()
	{
		destroyedCount++;
		currentScore += 10;
		if (!portalOpened && destroyedCount >= targetGoal)
		{
			OpenPortal();
		}
		UpdateUI();
	}

	public void StopGame()
	{
		isGameActive = false;
	}

	private void EndGame(bool isWin)
	{
		isGameActive = false;
		if (!isWin && GameFlowManager.Instance != null)
		{
			GameFlowManager.Instance.TriggerGameOver();
		}
	}

	private void UpdateUI()
	{
		int num = Mathf.FloorToInt(currentTime / 60f);
		int num2 = Mathf.FloorToInt(currentTime % 60f);
		if (timeText != null)
		{
			timeText.text = $"{num:00}:{num2:00}";
		}
		if (scoreText != null)
		{
			scoreText.text = $"SCORE: {currentScore}";
		}
		if (countText != null)
		{
			countText.text = portalOpened ? "EXIT: RETURN TO START" : $"Target: {destroyedCount} / {targetGoal}";
		}
	}

	private void OpenPortal()
	{
		portalOpened = true;
		EnsureExitPortal();
		PlaytestRecorder.Record("portal_open", "exit", PortalPosition, "return_to_start");
		portalNoticeRoutine = StartCoroutine(ShowPortalNotice());
	}

	private IEnumerator ShowPortalNotice()
	{
		if (portalNoticeText != null)
		{
			portalNoticeText.gameObject.SetActive(value: true);
			portalNoticeText.text = "중앙에 포탈이 생성되었습니다";
			yield return new WaitForSeconds(5f);
			portalNoticeText.gameObject.SetActive(value: false);
		}
		portalNoticeRoutine = null;
	}

	/// <summary>
	/// 과녁을 무작위로 뿌린다. 공중 과녁을 먼저 채우고, 나머지를 벽에 붙인다.
	/// 기획: 과녁은 점수를 모으는 수집품이다. 재미는 몬스터가 맡는다 — 과녁에 특별한 규칙을 붙이지 않는다.
	/// </summary>
	private void SpawnTargets(int count)
	{
		if (targetPrefab == null) { Debug.LogError("[TargetManager] Target prefab is missing."); return; }
		var start = GameFlowManager.Instance != null ? GameFlowManager.Instance.PlayerStartPoint : null;
		spawnStartReady = start != null && NavMesh.SamplePosition(start.position, out _, 3f, NavMesh.AllAreas);
		if (spawnStartReady) { NavMesh.SamplePosition(start.position, out var hit, 3f, NavMesh.AllAreas); spawnStart = hit.position; }
		int floating = Mathf.RoundToInt(count * floatingRatio);
		SpawnFloatingTargets(floating);
		SpawnWallTargets(count - floating);
	}

	/// <summary>
	/// 벽 과녁을 맵 전체에 골고루 뿌린다.
	///
	/// 예전 방식은 배치 기준점(SpawnArea)을 무작위로 골랐는데, 기준점 자체가 한쪽에 몰려 있어서 과녁도 몰렸다.
	/// 이제 걸을 수 있는 영역을 N×N칸으로 나누고 칸마다 같은 수를 둔다 → 모으려면 여러 곳을 돌아다녀야 한다.
	/// 칸 안의 걸을 수 있는 바닥에서 눈높이로 옆을 비춰 가장 가까운 벽에 붙이므로,
	/// 플레이어가 갈 수 있는 곳의 보이는 높이에만 생긴다.
	/// </summary>
	private void SpawnWallTargets(int count)
	{
		if (count <= 0 || !EnsureWalkableBounds()) { return; }

		int n = Mathf.Max(1, gridDivisions);
		int cells = n * n;
		int[] quota = new int[cells];
		for (int i = 0; i < cells; i++) { quota[i] = count / cells; }
		// 나누어떨어지지 않는 나머지는 무작위 칸에 하나씩
		for (int r = 0; r < count % cells; r++) { quota[Random.Range(0, cells)]++; }

		int shortfall = 0;
		for (int cx = 0; cx < n; cx++)
		{
			for (int cz = 0; cz < n; cz++)
			{
				int want = quota[cx * n + cz];
				int got = SpawnWallTargetsInCell(cx, cz, n, want);
				shortfall += want - got;
			}
		}

		// Empty edge cells must not reduce the playable supply: spread their missing quota over valid cells.
		for (int pass = 0; pass < 3 && shortfall > 0; pass++)
		{
			for (int cell = 0; cell < cells && shortfall > 0; cell++)
			{
				shortfall -= SpawnWallTargetsInCell(cell / n, cell % n, n, Mathf.Min(2, shortfall));
			}
		}
		if (shortfall > 0)
		{
			Debug.LogWarning($"[TargetManager] 벽 과녁 {shortfall}개를 놓을 자리를 찾지 못했습니다 (목표 {count}개).");
		}
	}

	private int SpawnWallTargetsInCell(int cx, int cz, int n, int want)
	{
		Vector3 min = walkableBounds.min;
		Vector3 size = walkableBounds.size;
		float cellW = size.x / n;
		float cellD = size.z / n;

		int placed = 0;
		int attempts = 0;
		while (placed < want && attempts < want * 80)
		{
			attempts++;
			float x = min.x + (cx + Random.value) * cellW;
			float z = min.z + (cz + Random.value) * cellD;
			Vector3 probe = new Vector3(x, walkableBounds.center.y, z);

			if (!NavMesh.SamplePosition(probe, out NavMeshHit floor, size.y, NavMesh.AllAreas)) { continue; }
			// 가장 가까운 바닥이 옆 칸으로 튀어나가면 칸 나누기가 무의미해진다
			if (floor.position.x < min.x + cx * cellW || floor.position.x > min.x + (cx + 1) * cellW) { continue; }
			if (floor.position.z < min.z + cz * cellD || floor.position.z > min.z + (cz + 1) * cellD) { continue; }

			// Deliberate 20/60/20 rhythm instead of small, visually indistinguishable eye-height jitter.
			int slot = spawnedTargets.Count % 10;
			Vector2 band = slot == 0 || slot == 5 ? lowTargetHeight : slot == 3 || slot == 8 ? highTargetHeight : normalTargetHeight;
			if (attempts > want * 60) { band = normalTargetHeight; } // Short walls must not make the goal impossible.
			Vector3 origin = floor.position + Vector3.up * Random.Range(band.x, band.y);
			Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
			if (!Physics.Raycast(origin, dir, out RaycastHit wall, wallSearchDistance, ~0, QueryTriggerInteraction.Ignore)) { continue; }
			// 몸이 있는 것(플레이어·몬스터)이나 다른 과녁에는 붙이지 않는다
			if (wall.collider.attachedRigidbody != null || wall.collider.GetComponentInParent<Target>() != null) { continue; }
			if (Mathf.Abs(Vector3.Dot(wall.normal, Vector3.up)) >= 0.3f) { continue; }

			Vector3 position = wall.point + wall.normal * 0.1f;
			if (!IsFarFromOthers(position)) { continue; }
			Vector3 eye = floor.position + Vector3.up * 1.5f;
			Vector3 aim = position - eye;
			float horizontal = new Vector2(aim.x, aim.z).magnitude;
			if (horizontal < 3f || Mathf.Abs(Mathf.Atan2(aim.y, horizontal) * Mathf.Rad2Deg) > 35f) { continue; }
			if (Physics.Raycast(eye, aim.normalized, out var obstruction, aim.magnitude - .12f, ~0, QueryTriggerInteraction.Ignore)) { continue; }
			if (!HasWallBacking(wall, .78f)) { continue; }
			if (spawnStartReady && (!NavMesh.CalculatePath(spawnStart, floor.position, NavMesh.AllAreas, reachablePath) || reachablePath.status != NavMeshPathStatus.PathComplete)) { continue; }

			GameObject item = Object.Instantiate(targetPrefab, position, Quaternion.LookRotation(wall.normal));
			var target = item.GetComponent<Target>();
			if (target != null) { target.SetPlacement(position.y - floor.position.y, floor.position); }
			spawnedTargets.Add(item);
			placed++;
		}
		return placed;
	}

	/// <summary>걸을 수 있는 영역(NavMesh)의 범위를 한 번 계산해 둔다.</summary>
	private bool EnsureWalkableBounds()
	{
		if (walkableBoundsReady) { return true; }
		NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
		if (tri.vertices == null || tri.vertices.Length == 0) { return false; }
		Bounds b = new Bounds(tri.vertices[0], Vector3.zero);
		foreach (Vector3 v in tri.vertices) { b.Encapsulate(v); }
		walkableBounds = b;
		walkableBoundsReady = true;
		return true;
	}

	private void EnsureExitPortal()
	{
		if (portalObject != null) { portalObject.SetActive(false); }
		Transform start = GameFlowManager.Instance != null ? GameFlowManager.Instance.PlayerStartPoint : null;
		if (start == null) { start = GameObject.Find("PlayerStartPos")?.transform; }
		if (start == null) { Debug.LogError("[TargetManager] Player start is required for the fixed exit."); return; }
		Vector3 position = start.position;
		// Keep the exact start X/Z; only settle the visual base onto its floor.
		if (Physics.Raycast(position + Vector3.up * .4f, Vector3.down, out var ground, 2f, ~0, QueryTriggerInteraction.Ignore)
			&& ground.collider.attachedRigidbody == null) { position.y = ground.point.y; }
		if (exitPortal == null) { exitPortal = ProceduralExitPortal.Create(position, Quaternion.Euler(0, start.eulerAngles.y, 0)); }
		else { exitPortal.transform.SetPositionAndRotation(position, Quaternion.Euler(0, start.eulerAngles.y, 0)); }
		exitPortal.SetUnlocked(portalOpened);
	}

	private bool HasWallBacking(RaycastHit wall, float radius)
	{
		Vector3 right = Vector3.Cross(Vector3.up, wall.normal).normalized;
		Vector3[] offsets = { Vector3.zero, Vector3.up * radius, Vector3.down * radius, right * radius, -right * radius };
		foreach (var offset in offsets)
		{
			if (!Physics.Raycast(wall.point + offset + wall.normal * .25f, -wall.normal, out var hit, .6f, ~0, QueryTriggerInteraction.Ignore)
				|| hit.collider != wall.collider || Vector3.Dot(hit.normal, wall.normal) < .9f) { return false; }
		}
		return true;
	}

	/// <summary>
	/// 공중 과녁: 구역 안 무작위 지점의 바닥을 찾아 그 위 허공에 띄운다.
	/// 걸어갈 수 있는 바닥(NavMesh) 위에만 두어 장애물 지붕 위나 벽 속에 생기지 않게 한다.
	/// </summary>
	private void SpawnFloatingTargets(int count)
	{
		if (count <= 0 || spawnAreas == null || spawnAreas.Length == 0) { return; }
		GameObject template = GetFloatingTemplate();
		if (template == null) { return; }

		int placed = 0;
		int attempts = 0;
		int maxAttempts = count * 50;
		float clearance = floatDiameter * 0.75f;
		while (placed < count && attempts < maxAttempts)
		{
			attempts++;
			Transform area = spawnAreas[Random.Range(0, spawnAreas.Length)];
			Vector2 offset = Random.insideUnitCircle * spawnRadius;
			Vector3 top = area.position + new Vector3(offset.x, spawnRadius, offset.y);

			if (!Physics.Raycast(top, Vector3.down, out RaycastHit ground, spawnRadius * 3f)) { continue; }
			if (ground.normal.y < 0.7f) { continue; }
			if (ground.collider.CompareTag("Player") || ground.collider.GetComponent<Target>() != null) { continue; }
			if (!NavMesh.SamplePosition(ground.point, out NavMeshHit _, 1f, NavMesh.AllAreas)) { continue; }

			Vector3 position = ground.point + Vector3.up * Random.Range(floatMinHeight, floatMaxHeight);
			if (Physics.CheckSphere(position, clearance, ~0, QueryTriggerInteraction.Ignore)) { continue; }
			if (!IsFarFromOthers(position)) { continue; }

			GameObject item = Object.Instantiate(template, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
			item.name = "FloatingTarget";
			item.SetActive(true);
			spawnedTargets.Add(item);
			placed++;
		}
	}

	private bool IsFarFromOthers(Vector3 position)
	{
		float min = minSpacing * minSpacing;
		foreach (GameObject t in spawnedTargets)
		{
			if (t != null && new Vector2(t.transform.position.x-position.x, t.transform.position.z-position.z).sqrMagnitude < min) { return false; }
		}
		return true;
	}

	/// <summary>
	/// 공중 과녁 원본을 만든다. 벽 과녁 프리팹을 복제하므로 Target의 설정(효과음·이펙트·소음 반경·파괴 연출)과
	/// 레이어(사격 판정 대상)가 그대로 따라온다. 겉모습과 충돌체만 구체로 바꾼다.
	/// 색은 벽 과녁의 머티리얼을 그대로 쓴다 — 빨간 몸통에 흰 띠.
	/// </summary>
	private GameObject GetFloatingTemplate()
	{
		if (floatingTemplate != null) { return floatingTemplate; }
		if (targetPrefab == null) { return null; }

		GameObject root = Object.Instantiate(targetPrefab, base.transform);
		root.SetActive(false);
		root.name = "~FloatingTargetTemplate";
		root.transform.localScale = Vector3.one;

		Material red = null;
		Material white = null;
		foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
		{
			if (r.name == "Ring_1_Outer") { red = r.sharedMaterial; }
			else if (r.name == "Ring_2") { white = r.sharedMaterial; }
		}

		// 원판 모양 겉모습과 상자 충돌체를 걷어낸다 (복제본은 같은 프레임에 또 복제되므로 즉시 제거)
		for (int i = root.transform.childCount - 1; i >= 0; i--)
		{
			Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
		}
		foreach (Collider c in root.GetComponents<Collider>())
		{
			Object.DestroyImmediate(c);
		}
		// 벽 과녁용 원판 회전·맥동은 뺀다 — 공중 과녁의 움직임은 TargetFloat가 맡는다 (둘이 겹치면 띠가 흔들린다)
		TargetSpin spin = root.GetComponent<TargetSpin>();
		if (spin != null)
		{
			Object.DestroyImmediate(spin);
		}

		SphereCollider sphereCollider = root.AddComponent<SphereCollider>();
		sphereCollider.radius = floatDiameter * 0.5f;

		GameObject body = CreateVisual(PrimitiveType.Sphere, root.transform, red);
		body.name = "Body";
		body.transform.localScale = Vector3.one * floatDiameter;

		// 적도를 두르는 흰 띠 — 과녁이라는 게 한눈에 읽히게
		GameObject band = CreateVisual(PrimitiveType.Cylinder, root.transform, white);
		band.name = "Band";
		band.transform.localScale = new Vector3(floatDiameter * 1.08f, 0.04f, floatDiameter * 1.08f);

		root.AddComponent<TargetFloat>();

		floatingTemplate = root;
		return floatingTemplate;
	}

	private static GameObject CreateVisual(PrimitiveType type, Transform parent, Material material)
	{
		GameObject go = GameObject.CreatePrimitive(type);
		Object.DestroyImmediate(go.GetComponent<Collider>());
		go.transform.SetParent(parent, false);
		go.layer = parent.gameObject.layer;
		Renderer r = go.GetComponent<Renderer>();
		if (material != null) { r.sharedMaterial = material; }
		r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		return go;
	}
}
