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

	[Tooltip("벽 과녁이 붙는 높이 범위(바닥 기준, m) — 눈높이 근처")]
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

	// 기획 결정(2026-09-11): 포탈은 무작위 구역(A~D)에 열린다. 단, 걸어서 닿을 수 있는 곳만.
	[Tooltip("구역 중심에서 이 반경 안에서 포탈 자리를 찾는다(m)")]
	[SerializeField]
	private float portalSearchRadius = 25f;

	[Tooltip("포탈 둘레로 이만큼은 평평하고 트여 있어야 한다(m). 작은 발판·벽 틈을 막는다")]
	[SerializeField]
	private float portalClearRadius = 3f;

	private Vector3 portalHomePosition;
	private bool portalHomeSaved;

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
		ClearAllTargets();
		if (portalNoticeText != null)
		{
			portalNoticeText.gameObject.SetActive(value: false);
		}
		if (portalObject != null)
		{
			portalObject.SetActive(value: false);
		}
		UpdateUI();
		SpawnTargets(totalTargetsToSpawn);
	}

	private void ClearAllTargets()
	{
		foreach (GameObject spawnedTarget in spawnedTargets)
		{
			if (spawnedTarget != null)
			{
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
			countText.text = $"Target: {destroyedCount}";
		}
	}

	private void OpenPortal()
	{
		portalOpened = true;
		if (portalObject != null)
		{
			PlacePortalInRandomZone();
			portalObject.SetActive(value: true);
		}
		StartCoroutine(ShowPortalNotice());
	}

	private IEnumerator ShowPortalNotice()
	{
		if (portalNoticeText != null)
		{
			portalNoticeText.gameObject.SetActive(value: true);
			portalNoticeText.text = "Portal Created!";
			yield return new WaitForSeconds(5f);
			portalNoticeText.gameObject.SetActive(value: false);
		}
	}

	/// <summary>
	/// 과녁을 무작위로 뿌린다. 공중 과녁을 먼저 채우고, 나머지를 벽에 붙인다.
	/// 기획: 과녁은 점수를 모으는 수집품이다. 재미는 몬스터가 맡는다 — 과녁에 특별한 규칙을 붙이지 않는다.
	/// </summary>
	private void SpawnTargets(int count)
	{
		if (spawnAreas == null || spawnAreas.Length == 0)
		{
			return;
		}
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

			Vector3 origin = floor.position + Vector3.up * Random.Range(wallHeightRange.x, wallHeightRange.y);
			Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
			if (!Physics.Raycast(origin, dir, out RaycastHit wall, wallSearchDistance, ~0, QueryTriggerInteraction.Ignore)) { continue; }
			// 몸이 있는 것(플레이어·몬스터)이나 다른 과녁에는 붙이지 않는다
			if (wall.collider.attachedRigidbody != null || wall.collider.GetComponent<Target>() != null) { continue; }
			if (Mathf.Abs(Vector3.Dot(wall.normal, Vector3.up)) >= 0.3f) { continue; }

			Vector3 position = wall.point + wall.normal * 0.1f;
			if (!IsFarFromOthers(position)) { continue; }

			GameObject item = Object.Instantiate(targetPrefab, position, Quaternion.LookRotation(wall.normal));
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

	/// <summary>
	/// 포탈을 무작위 구역(A~D) 안의, 걸어서 닿을 수 있는 자리로 옮긴다.
	/// 구역을 무작위 순서로 돌며 조건에 맞는 자리를 찾고, 어디서도 못 찾으면 원래 자리에 둔다.
	/// </summary>
	private void PlacePortalInRandomZone()
	{
		Transform portal = portalObject.transform;
		if (!portalHomeSaved)
		{
			portalHomePosition = portal.position;
			portalHomeSaved = true;
		}

		List<Vector3> zoneCenters = new List<Vector3>();
		foreach (MonsterAI m in MonsterAI.activeMonsters)
		{
			if (m != null && m.role == MonsterAI.MonsterRole.Zone_Defender && m.zoneCenter != null)
			{
				zoneCenters.Add(m.zoneCenter.position);
			}
		}
		// 무작위 순서
		for (int i = zoneCenters.Count - 1; i > 0; i--)
		{
			int j = Random.Range(0, i + 1);
			Vector3 tmp = zoneCenters[i]; zoneCenters[i] = zoneCenters[j]; zoneCenters[j] = tmp;
		}

		GameObject start = GameObject.Find("PlayerStartPos");
		Vector3 startPos = (start != null) ? start.transform.position : portalHomePosition;
		if (!NavMesh.SamplePosition(startPos, out NavMeshHit startHit, 3f, NavMesh.AllAreas))
		{
			portal.position = portalHomePosition;
			return;
		}

		foreach (Vector3 center in zoneCenters)
		{
			for (int attempt = 0; attempt < 60; attempt++)
			{
				Vector2 offset = Random.insideUnitCircle * portalSearchRadius;
				if (TryPortalSpot(center + new Vector3(offset.x, 0f, offset.y), startHit.position, out Vector3 spot))
				{
					portal.position = spot;
					portal.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
					return;
				}
			}
		}

		Debug.LogWarning("[TargetManager] 포탈을 놓을 구역 자리를 찾지 못해 원래 위치에 엽니다.");
		portal.position = portalHomePosition;
	}

	/// <summary>
	/// 포탈 자리 조건:
	///   1) 걸을 수 있는 바닥
	///   2) 둘레 portalClearRadius가 같은 높이로 평평하게 트여 있음 — 작은 발판·벽 틈 제외
	///   3) 시작 지점에서 점프 없이 걸어서 닿는 경로가 있음 — 점프로만 닿는 높은 곳 제외
	///
	/// "시작 지점과 높이가 비슷해야 한다"는 조건은 두지 않는다. 이 맵은 구역 바닥 자체가 시작 지점보다
	/// 1~2m 높은 넓은 비탈이라, 그 조건을 걸면 구역 A·C에서 자리를 하나도 못 찾았다(시험 결과 60개 중 0개).
	/// 작은 발판은 2), 점프로만 가는 곳은 3)이 이미 걸러낸다.
	/// </summary>
	private bool TryPortalSpot(Vector3 candidate, Vector3 startOnNav, out Vector3 spot)
	{
		spot = Vector3.zero;
		if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) { return false; }

		for (int i = 0; i < 8; i++)
		{
			Vector3 dir = Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward;
			Vector3 around = hit.position + dir * portalClearRadius;
			// 가는 길에 바닥 가장자리(벽·낭떠러지)가 있으면 트인 곳이 아니다
			if (NavMesh.Raycast(hit.position, around, out NavMeshHit _, NavMesh.AllAreas)) { return false; }
			if (!NavMesh.SamplePosition(around, out NavMeshHit aroundHit, 0.5f, NavMesh.AllAreas)) { return false; }
			if (Mathf.Abs(aroundHit.position.y - hit.position.y) > 0.3f) { return false; }
		}

		// 점프 링크(Jump 영역)를 빼고 걸어서만 닿는지 본다
		int walkOnly = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Jump"));
		NavMeshPath path = new NavMeshPath();
		if (!NavMesh.CalculatePath(startOnNav, hit.position, walkOnly, path) || path.status != NavMeshPathStatus.PathComplete) { return false; }

		// NavMesh 표면은 바닥보다 살짝 떠 있으므로 실제 바닥 높이에 맞춘다
		spot = hit.position;
		if (Physics.Raycast(hit.position + Vector3.up, Vector3.down, out RaycastHit ground, 3f, ~0, QueryTriggerInteraction.Ignore)
			&& ground.collider.attachedRigidbody == null)
		{
			spot.y = ground.point.y;
		}
		return true;
	}

	/// <summary>
	/// 공중 과녁: 구역 안 무작위 지점의 바닥을 찾아 그 위 허공에 띄운다.
	/// 걸어갈 수 있는 바닥(NavMesh) 위에만 두어 장애물 지붕 위나 벽 속에 생기지 않게 한다.
	/// </summary>
	private void SpawnFloatingTargets(int count)
	{
		if (count <= 0) { return; }
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
			if (t != null && (t.transform.position - position).sqrMagnitude < min) { return false; }
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
