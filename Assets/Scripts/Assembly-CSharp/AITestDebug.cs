using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// AI 테스트 씬(ShooterInGame_AITest) 전용 도구. 본 게임 씬에는 넣지 않는다.
///
/// 맵 전체를 위에서 내려다보며 몬스터 5마리와 조정자가 무엇을 하는지 본다.
///   - 머리 위 상태 글자 (순찰 / 이동 / 수색 / 추격 / 차단 / 복귀 / 기절) + 받은 명령 [차단]/[수색]
///   - 시야 부채꼴, 청각 원, 구역 원, 지금 향하는 목적지
///   - 조정자의 <b>마지막 목격 기록</b>(✕ 위치 + 이동 방향 화살표 + 몇 초 전) — 몬스터가 컨닝하지 않는지 확인용
///   - 차단 지점(보라 원), 수색 지점(노랑 원)
///
/// 조작: WASD 지도 기준 이동(W = 지도 위쪽) · Shift 빠르게 · 좌클릭 = 플레이어 자리에서 소리
/// 테스트에 필요한 것만 남긴다: 홈 화면·카운트다운·소리·게임 UI·과녁·연출·사격은 모두 뺀다.
/// 무적·제한 시간 정지는 자동. 오버레이 선은 전용 레이어(31)에 그린다.
/// </summary>
public class AITestDebug : MonoBehaviour
{
	private const int OverlayLayer = 31;

	// 필드 이름을 바꾸면 씬에 저장된 옛 값이 무시되고 아래 기본값이 적용된다.
	[Header("조작")]
	[Tooltip("WASD 기본 이동 속도. 11 = 실제 게임의 달리기 속도라 추격 테스트가 실제와 같다")]
	[SerializeField] private float testMoveSpeed = 11f;
	[Tooltip("Shift 누를 때 속도. 맵을 빨리 가로지르는 용도")]
	[SerializeField] private float testFastSpeed = 30f;
	[Tooltip("좌클릭 소리 반경 (게임 속 발사음과 같게)")]
	[SerializeField] private float testNoiseRadius = 30f;

	[Header("표시")]
	[SerializeField] private float lineWidth = 0.6f;

	private static readonly Color CutColor = new Color(0.85f, 0.4f, 1f, 1f);
	private static readonly Color SearchColor = new Color(1f, 0.9f, 0.2f, 1f);
	private static readonly Color SightColor = new Color(1f, 0.3f, 0.3f, 1f);

	private Camera overviewCam;
	private Camera mainCam;
	private float overlayY;
	private Material lineMat;

	private Transform player;
	private Rigidbody playerBody;
	private PlayerController playerController;
	private PlayerShooter playerShooter;
	private Vector3 moveInput;
	private Vector3 playerPrevPos;
	private Vector3 playerVel;

	private class MonsterLines
	{
		public LineRenderer body, zone, hearing, sight, dest, intercept;
	}
	private readonly Dictionary<MonsterAI, MonsterLines> monsterLines = new Dictionary<MonsterAI, MonsterLines>();
	private readonly List<LineRenderer> orderMarkers = new List<LineRenderer>();
	private LineRenderer playerMarker, playerVelocity, noiseRing;
	private LineRenderer sightCrossA, sightCrossB, sightArrow;
	private readonly List<LineRenderer> candidateLines = new List<LineRenderer>();
	private static readonly Color CandidateColor = new Color(0.6f, 0.6f, 0.65f, 0.8f);
	private static readonly Color RejectedColor = new Color(0.45f, 0.45f, 0.5f, 0.45f);
	private float noiseRingUntil;

	private GUIStyle labelStyle, panelStyle;

	private void Start()
	{
		PlayerHealth.DebugGodMode = true;
		TargetManager.DebugFreezeTimer = true;
		// 소리 전부 끔 (BGM·효과음). 몬스터의 "청각"은 NoiseSystem이라 영향 없음
		AudioListener.volume = 0f;

		lineMat = new Material(Shader.Find("Sprites/Default"));
		GameObject p = GameObject.FindGameObjectWithTag("Player");
		if (p != null)
		{
			player = p.transform;
			playerBody = p.GetComponent<Rigidbody>();
			playerController = p.GetComponent<PlayerController>();
			playerShooter = p.GetComponent<PlayerShooter>();
			playerPrevPos = player.position;
		}

		BuildOverviewCamera();
		playerMarker = NewLine("Player", Color.white, true);
		playerVelocity = NewLine("PlayerVelocity", Color.white, false);
		noiseRing = NewLine("Noise", new Color(1f, 0.55f, 0.1f, 0.9f), true);
		noiseRing.enabled = false;
		sightCrossA = NewLine("SightA", SightColor, false);
		sightCrossB = NewLine("SightB", SightColor, false);
		sightArrow = NewLine("SightDir", SightColor, false);

		KeepTestMode();
		StartCoroutine(BeginTest());
	}

	/// <summary>
	/// 홈 화면·카운트다운을 건너뛰고 곧바로 게임 상태로. GameFlowManager가 Start에서 홈 상태를 세팅한 뒤에
	/// 덮어써야 하므로 두 프레임 기다린다. 과녁은 AI 테스트에 필요 없어 치운다.
	/// </summary>
	private System.Collections.IEnumerator BeginTest()
	{
		yield return null;
		yield return null;
		if (GameFlowManager.Instance != null) { GameFlowManager.Instance.DebugBeginTest(); }
		foreach (Target t in FindObjectsByType<Target>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			Destroy(t.gameObject);
		}
		KeepTestMode();
	}

	private void OnDestroy()
	{
		PlayerHealth.DebugGodMode = false;
		MonsterAI.DebugPlayerInvisible = false;
		TargetManager.DebugFreezeTimer = false;
		AudioListener.volume = 1f;
		if (mainCam != null) { mainCam.enabled = true; }
		if (playerBody != null) { playerBody.isKinematic = false; }
	}

	// ────────────────────────────────────────────────
	//  카메라·화면
	// ────────────────────────────────────────────────

	private void BuildOverviewCamera()
	{
		NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
		Bounds b = new Bounds(Vector3.zero, Vector3.one * 200f);
		if (tri.vertices != null && tri.vertices.Length > 0)
		{
			b = new Bounds(tri.vertices[0], Vector3.zero);
			foreach (Vector3 v in tri.vertices) { b.Encapsulate(v); }
		}
		// 벽 꼭대기보다 위에 선을 그려 가려지지 않게 한다 (위에서 수직으로 보므로 높이는 위치에 영향 없음)
		overlayY = b.max.y + 10f;

		GameObject go = new GameObject("~OverviewCamera");
		go.transform.SetParent(transform, false);
		overviewCam = go.AddComponent<Camera>();
		// URP 카메라 데이터를 붙인다 (없으면 매 프레임 경고). 후처리·그림자는 필요 없다
		UniversalAdditionalCameraData data = overviewCam.GetUniversalAdditionalCameraData();
		data.renderPostProcessing = false;
		data.renderShadows = false;

		overviewCam.orthographic = true;
		overviewCam.transform.position = new Vector3(b.center.x, overlayY + 30f, b.center.z);
		overviewCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
		overviewCam.orthographicSize = Mathf.Max(b.size.z * 0.5f, b.size.x * 0.5f / aspect) * 1.05f;
		overviewCam.nearClipPlane = 0.3f;
		overviewCam.farClipPlane = overlayY + 200f;
		overviewCam.clearFlags = CameraClearFlags.SolidColor;
		overviewCam.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
		overviewCam.cullingMask = ~0;
		overviewCam.depth = 100f;
	}

	/// <summary>게임 카메라·게임 UI·원래 조작을 끈다. 게임 흐름이 다시 켤 수 있어 매 프레임 확인한다.</summary>
	private void KeepTestMode()
	{
		if (mainCam == null) { mainCam = Camera.main; }
		if (mainCam != null && mainCam != overviewCam && mainCam.enabled) { mainCam.enabled = false; }

		if (playerController != null && playerController.enabled)
		{
			// 달리기 속도선 등 연출은 테스트에 필요 없다 — 조작기를 끄면서 효과 강도도 0으로
			playerController.StopSpeedEffect();
			playerController.enabled = false;
		}
		if (playerShooter != null && playerShooter.enabled) { playerShooter.enabled = false; }

		foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
		{
			if (c.isRootCanvas && c.enabled && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.enabled = false; }
		}

		if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; }
		Cursor.visible = true;
	}

	// ────────────────────────────────────────────────
	//  조작: 지도 기준 WASD + 좌클릭 소리
	// ────────────────────────────────────────────────

	private void Update()
	{
		// 게임 흐름이 화면·조작을 다시 켤 수 있으므로 항상 확인한다
		KeepTestMode();
		bool running = GameFlowManager.Instance == null || GameFlowManager.Instance.IsGameRunning;

		moveInput = Vector3.zero;
		Keyboard kb = Keyboard.current;
		if (running && kb != null)
		{
			if (kb.wKey.isPressed) { moveInput.z += 1f; }
			if (kb.sKey.isPressed) { moveInput.z -= 1f; }
			if (kb.dKey.isPressed) { moveInput.x += 1f; }
			if (kb.aKey.isPressed) { moveInput.x -= 1f; }
			if (moveInput.sqrMagnitude > 1f) { moveInput.Normalize(); }
			moveInput *= kb.leftShiftKey.isPressed ? testFastSpeed : testMoveSpeed;
		}

		Mouse mouse = Mouse.current;
		if (running && mouse != null && mouse.leftButton.wasPressedThisFrame && player != null)
		{
			NoiseSystem.Emit(player.position, testNoiseRadius, NoiseKind.Shot);
			SetCircle(noiseRing, player.position, testNoiseRadius);
			noiseRing.enabled = true;
			noiseRingUntil = Time.time + 1.5f;
		}
	}

	/// <summary>
	/// 테스트용 이동: 물리 대신 걸을 수 있는 바닥(NavMesh)을 따라 미끄러지듯 움직인다.
	/// 계단·턱·높은 곳을 점프 없이 타고 넘는다. 맵과 NavMesh는 그대로라 몬스터는 실제 게임과 같은 지형에서 움직인다.
	/// </summary>
	private void FixedUpdate()
	{
		if (playerBody == null) { return; }
		bool running = GameFlowManager.Instance == null || GameFlowManager.Instance.IsGameRunning;
		if (!running) { return; }

		if (!playerBody.isKinematic) { playerBody.isKinematic = true; }
		if (moveInput.sqrMagnitude < 0.01f) { return; }

		Vector3 current = playerBody.position;
		Vector3 step = moveInput * Time.fixedDeltaTime;
		if (TryWalk(current, step, out Vector3 next)
			|| TryWalk(current, new Vector3(step.x, 0f, 0f), out next)
			|| TryWalk(current, new Vector3(0f, 0f, step.z), out next)
			|| TryHop(current, moveInput.normalized, out next))
		{
			playerBody.MovePosition(next);
		}
		playerBody.MoveRotation(Quaternion.LookRotation(new Vector3(moveInput.x, 0f, moveInput.z)));
	}

	/// <summary>
	/// 턱 넘기: 이어진 바닥이 끊겨 막혔을 때, 가려는 방향 최대 3m 앞의 바닥으로 바로 옮긴다.
	/// 이 맵에는 높이 2m 장애물 블록이 많고, 몬스터는 그 사이를 점프 링크로 넘는다 — 테스트 플레이어도 넘을 수 있어야 한다.
	/// 단, 사이에 있는 물체가 높은 쪽 바닥보다 1m 넘게 솟아 있으면 벽으로 보고 넘지 않는다(2m 블록은 넘고 10m 벽은 못 넘음).
	/// </summary>
	private static bool TryHop(Vector3 from, Vector3 dir, out Vector3 result)
	{
		result = from;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.01f) { return false; }
		dir.Normalize();

		for (float d = 0.75f; d <= 3f; d += 0.25f)
		{
			Vector3 target = from + dir * d;
			if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { continue; }
			Vector2 off = new Vector2(hit.position.x - target.x, hit.position.z - target.z);
			if (off.magnitude > 0.6f) { continue; }
			// 가려는 방향으로 실제로 나아가야 한다 — 바닥 끝으로 도로 붙는 것(뒤쪽 가장자리)은 넘기가 아니다
			Vector3 moved = hit.position - from;
			moved.y = 0f;
			if (Vector3.Dot(moved, dir) < d * 0.8f) { continue; }

			float allowedTop = Mathf.Max(from.y, hit.position.y) + 1f;
			if (IsBlockedByTallObject(from, hit.position, allowedTop)) { return false; }

			result = hit.position;
			return true;
		}
		return false;
	}

	/// <summary>
	/// from→to 사이에 높은 쪽 바닥보다 솟은 물체(벽)가 있는지 옆으로 비춰 본다.
	/// 높은 쪽 바닥 기준 +1.2m, +2.5m 두 높이에서 수평 광선을 양방향으로 쏜다.
	/// (위에서 내려다보는 방식은 이 맵 벽에 윗면이 없어 광선이 벽을 통과해 버렸다 — 외벽 60곳 중 12곳을 넘었음)
	/// 2m 블록은 두 높이보다 낮아 넘을 수 있고, 10m 벽은 걸린다. 한쪽 면만 있는 메시도 잡도록 양방향으로 쏜다.
	/// </summary>
	private static bool IsBlockedByTallObject(Vector3 from, Vector3 to, float allowedTop)
	{
		float baseY = allowedTop - 1f;   // 높은 쪽 바닥 높이
		foreach (float h in new[] { 1.2f, 2.5f })
		{
			Vector3 a = new Vector3(from.x, baseY + h, from.z);
			Vector3 b = new Vector3(to.x, baseY + h, to.z);
			if (HitsSolid(a, b) || HitsSolid(b, a)) { return true; }
		}
		return false;
	}

	private static bool HitsSolid(Vector3 a, Vector3 b)
	{
		Vector3 dir = b - a;
		float len = dir.magnitude;
		if (len < 0.01f) { return false; }
		foreach (RaycastHit hit in Physics.RaycastAll(a, dir / len, len, ~0, QueryTriggerInteraction.Ignore))
		{
			// 몸이 있는 것(플레이어·몬스터)은 벽이 아니다
			if (hit.collider.attachedRigidbody == null) { return true; }
		}
		return false;
	}

	private static bool TryWalk(Vector3 from, Vector3 step, out Vector3 result)
	{
		result = from;
		if (step.sqrMagnitude < 0.0001f) { return false; }
		Vector3 target = from + step;
		if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { return false; }
		Vector2 off = new Vector2(hit.position.x - target.x, hit.position.z - target.z);
		if (off.magnitude > 0.6f) { return false; }
		result = hit.position;
		return true;
	}

	// ────────────────────────────────────────────────
	//  오버레이
	// ────────────────────────────────────────────────

	private void LateUpdate()
	{
		foreach (MonsterAI m in MonsterAI.activeMonsters)
		{
			if (m == null) { continue; }
			if (!monsterLines.TryGetValue(m, out MonsterLines ml))
			{
				ml = new MonsterLines
				{
					body = NewLine(m.name + "_Body", Color.white, true),
					zone = NewLine(m.name + "_Zone", new Color(1f, 1f, 1f, 0.35f), true),
					hearing = NewLine(m.name + "_Hearing", new Color(0.3f, 0.85f, 1f, 0.35f), true),
					sight = NewLine(m.name + "_Sight", Color.white, false),
					dest = NewLine(m.name + "_Dest", Color.white, false),
					intercept = NewLine(m.name + "_Intercept", CutColor, true)
				};
				monsterLines[m] = ml;
			}
			DrawMonster(m, ml);
		}

		DrawOrderPoints();
		DrawSighting();
		DrawCutCandidates();

		if (player != null)
		{
			// 플레이어 속도는 이 도구가 직접 계산한다 (조정자는 진짜 플레이어를 모른다)
			if (Time.deltaTime > 0f)
			{
				Vector3 v = (player.position - playerPrevPos) / Time.deltaTime;
				v.y = 0f;
				playerVel = Vector3.Lerp(playerVel, v, 0.2f);
				playerPrevPos = player.position;
			}
			SetCircle(playerMarker, player.position, 1.6f);
			SetSegment(playerVelocity, player.position, player.position + playerVel * 1.5f);
		}

		if (noiseRing.enabled && Time.time > noiseRingUntil) { noiseRing.enabled = false; }
	}

	private void DrawMonster(MonsterAI m, MonsterLines ml)
	{
		Color c = StateColor(m);
		Vector3 pos = m.transform.position;

		ml.body.startColor = ml.body.endColor = c;
		SetCircle(ml.body, pos, 1.8f);

		bool hasZone = m.role == MonsterAI.MonsterRole.Zone_Defender && m.zoneCenter != null && !m.useGlobalNavMesh;
		ml.zone.enabled = hasZone;
		if (hasZone) { SetCircle(ml.zone, m.zoneCenter.position, m.zoneRadius); }

		SetCircle(ml.hearing, pos, m.EffectiveHearingRange);

		// 시야 부채꼴
		Color sc = c; sc.a = 0.7f;
		ml.sight.startColor = ml.sight.endColor = sc;
		const int arcSeg = 16;
		ml.sight.positionCount = arcSeg + 3;
		ml.sight.SetPosition(0, Lift(pos));
		Vector3 fwd = m.transform.forward; fwd.y = 0f; fwd.Normalize();
		for (int i = 0; i <= arcSeg; i++)
		{
			float a = Mathf.Lerp(-m.fovAngle * 0.5f, m.fovAngle * 0.5f, (float)i / arcSeg);
			ml.sight.SetPosition(i + 1, Lift(pos + Quaternion.Euler(0f, a, 0f) * fwd * m.sightRange));
		}
		ml.sight.SetPosition(arcSeg + 2, Lift(pos));

		// 목적지까지
		Color dc = c; dc.a = 0.9f;
		ml.dest.startColor = ml.dest.endColor = dc;
		SetSegment(ml.dest, pos, m.DebugDestination);

		ml.intercept.enabled = m.IsIntercepting;
		if (m.IsIntercepting) { SetCircle(ml.intercept, m.DebugInterceptPoint, 2.5f); }
	}

	/// <summary>조정자가 내린 명령 지점: 차단(보라), 수색(노랑).</summary>
	private void DrawOrderPoints()
	{
		int i = 0;
		MonsterDirector d = MonsterDirector.Instance;
		if (d != null)
		{
			foreach (KeyValuePair<MonsterAI, Vector3> pair in d.DebugOrderPoints)
			{
				if (i >= orderMarkers.Count) { orderMarkers.Add(NewLine("Order" + i, Color.white, true)); }
				Color c = d.DebugOrderLabel(pair.Key) == "차단" ? CutColor : SearchColor;
				orderMarkers[i].startColor = orderMarkers[i].endColor = c;
				orderMarkers[i].enabled = true;
				SetCircle(orderMarkers[i], pair.Value, 2f);
				i++;
			}
		}
		for (; i < orderMarkers.Count; i++) { orderMarkers[i].enabled = false; }
	}

	/// <summary>
	/// 차단 후보 길: 마지막 목격 위치에서 8방향으로 뻗은 선.
	/// 선택된 길 = 보라, 선택 안 된 길 = 회색, 금방 막혀 버려진 방향 = 흐린 회색. 점수는 글자로 표시(OnGUI).
	/// </summary>
	private void DrawCutCandidates()
	{
		MonsterDirector d = MonsterDirector.Instance;
		int i = 0;
		if (d != null && d.IsHunting && d.HasSighting)
		{
			foreach (MonsterDirector.CutCandidate c in d.DebugCutCandidates)
			{
				if (i >= candidateLines.Count) { candidateLines.Add(NewLine("Candidate" + i, CandidateColor, false)); }
				LineRenderer lr = candidateLines[i];
				Color col = c.chosen ? CutColor : (c.rejected ? RejectedColor : CandidateColor);
				lr.startColor = lr.endColor = col;
				lr.widthMultiplier = c.chosen ? lineWidth * 1.4f : lineWidth * 0.7f;
				lr.enabled = true;
				SetSegment(lr, d.SightingPosition, c.point);
				i++;
			}
		}
		for (; i < candidateLines.Count; i++) { candidateLines[i].enabled = false; }
	}

	/// <summary>조정자의 마지막 목격 기록: ✕ 위치 + 알고 있는 이동 방향 화살표.</summary>
	private void DrawSighting()
	{
		MonsterDirector d = MonsterDirector.Instance;
		bool show = d != null && d.HasSighting && (d.IsHunting || d.SightingAge < 10f);
		sightCrossA.enabled = sightCrossB.enabled = sightArrow.enabled = show;
		if (!show) { return; }

		Vector3 p = d.SightingPosition;
		const float s = 2.2f;
		SetSegment(sightCrossA, p + new Vector3(-s, 0f, -s), p + new Vector3(s, 0f, s));
		SetSegment(sightCrossB, p + new Vector3(-s, 0f, s), p + new Vector3(s, 0f, -s));
		Vector3 dir = d.SightingDirection;
		sightArrow.enabled = dir.sqrMagnitude > 0.01f;
		if (sightArrow.enabled) { SetSegment(sightArrow, p, p + dir * 10f); }
	}

	private static Color StateColor(MonsterAI m)
	{
		switch (m.CurrentState)
		{
		case MonsterAI.State.Patrol: return new Color(0.55f, 0.85f, 0.55f);
		case MonsterAI.State.Chase: return new Color(1f, 0.25f, 0.25f);
		case MonsterAI.State.Investigate: return m.StateLabel == "이동" ? new Color(1f, 0.6f, 0.1f) : SearchColor;
		case MonsterAI.State.Return: return new Color(0.3f, 0.8f, 1f);
		case MonsterAI.State.Stun: return Color.white;
		case MonsterAI.State.Intercept: return CutColor;
		}
		return Color.gray;
	}

	private LineRenderer NewLine(string name, Color color, bool loop)
	{
		GameObject go = new GameObject("~AI_" + name);
		go.layer = OverlayLayer;
		go.transform.SetParent(transform, false);
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.useWorldSpace = true;
		lr.loop = loop;
		lr.sharedMaterial = lineMat;
		lr.startColor = lr.endColor = color;
		lr.widthMultiplier = lineWidth;
		lr.numCapVertices = 2;
		lr.shadowCastingMode = ShadowCastingMode.Off;
		lr.receiveShadows = false;
		return lr;
	}

	private Vector3 Lift(Vector3 p) { return new Vector3(p.x, overlayY, p.z); }

	private void SetCircle(LineRenderer lr, Vector3 center, float radius)
	{
		const int seg = 40;
		lr.positionCount = seg;
		for (int i = 0; i < seg; i++)
		{
			float a = (float)i / seg * Mathf.PI * 2f;
			lr.SetPosition(i, Lift(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius));
		}
	}

	private void SetSegment(LineRenderer lr, Vector3 a, Vector3 b)
	{
		lr.positionCount = 2;
		lr.SetPosition(0, Lift(a));
		lr.SetPosition(1, Lift(b));
	}

	// ────────────────────────────────────────────────
	//  글자
	// ────────────────────────────────────────────────

	private void OnGUI()
	{
		if (labelStyle == null)
		{
			labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
			labelStyle.normal.textColor = Color.white;
			panelStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, richText = true };
			panelStyle.normal.textColor = Color.white;
			panelStyle.padding = new RectOffset(10, 10, 8, 8);
		}

		MonsterDirector d = MonsterDirector.Instance;

		// 지도 위 이름표
		if (overviewCam != null)
		{
			foreach (MonsterAI m in MonsterAI.activeMonsters)
			{
				if (m == null) { continue; }
				string order = d != null ? d.DebugOrderLabel(m) : "";
				string tag = order.Length > 0 ? " [" + order + "]" : "";
				DrawLabel(m.transform.position, m.name.Replace("Monster_", "") + tag + "\n" + m.StateLabel, StateColor(m));
			}
			if (player != null) { DrawLabel(player.position, "플레이어", Color.white); }
			if (d != null && d.HasSighting && (d.IsHunting || d.SightingAge < 10f))
			{
				DrawLabel(d.SightingPosition, "마지막 목격\n" + d.SightingAge.ToString("F1") + "초 전", SightColor);
			}
			// 차단 후보 길의 점수 — 왜 그 길을 골랐는지
			if (d != null && d.IsHunting && d.HasSighting)
			{
				foreach (MonsterDirector.CutCandidate c in d.DebugCutCandidates)
				{
					if (c.rejected) { continue; }
					DrawLabel(c.point, (c.chosen ? "▶ " : "") + c.score.ToString("F2"), c.chosen ? CutColor : CandidateColor);
				}
			}
		}

		// 좌상단 패널
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("<b>AI 테스트</b>  WASD 이동(" + testMoveSpeed + ") · Shift 빠르게(" + testFastSpeed + ") · 좌클릭 소리(" + testNoiseRadius + "m)");
		if (d != null)
		{
			string hunt = !d.IsHunting ? "대기" : (d.IsLargeHunt ? "큰 포위" : "작은 포위");
			sb.Append("사냥 <b>" + hunt + "</b> · 추격 중 <b>" + d.DebugChaserCount + "</b>마리 (최대 2)");
			if (d.HasSighting)
			{
				sb.Append(" · 마지막 목격 <b>" + d.SightingAge.ToString("F1") + "초 전</b> (" + d.SightingSpotterName.Replace("Monster_", "") + ")");
			}
			sb.AppendLine();
		}
		foreach (MonsterAI m in MonsterAI.activeMonsters)
		{
			if (m == null) { continue; }
			string order = d != null ? d.DebugOrderLabel(m) : "";
			sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(StateColor(m)) + ">■</color> " + m.name + "  " + m.StateLabel
				+ (order.Length > 0 ? "  [" + order + "]" : ""));
		}
		sb.Append("<color=#8CD98C>순찰</color> <color=#FF9A1A>이동</color> <color=#FFE633>수색</color> <color=#FF4040>추격</color> "
			+ "<color=#D966FF>차단</color> <color=#4DCCFF>복귀</color> 기절 · <color=#D966FF>○</color> 차단 지점 <color=#FFE633>○</color> 수색 지점 <color=#FF4D4D>✕</color> 마지막 목격");
		GUI.Box(new Rect(10, 10, 560, 60 + MonsterAI.activeMonsters.Count * 20 + 20), sb.ToString(), panelStyle);
	}

	private void DrawLabel(Vector3 world, string text, Color color)
	{
		Vector3 sp = overviewCam.WorldToScreenPoint(Lift(world));
		if (sp.z < 0f) { return; }
		Rect rect = new Rect(sp.x + 12f, Screen.height - sp.y - 18f, 180f, 40f);
		Color prev = labelStyle.normal.textColor;
		labelStyle.normal.textColor = Color.black;
		GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, labelStyle);
		labelStyle.normal.textColor = color;
		GUI.Label(rect, text, labelStyle);
		labelStyle.normal.textColor = prev;
	}
}
