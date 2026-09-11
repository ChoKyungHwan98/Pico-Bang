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
/// 맵 전체를 위에서 내려다보며 몬스터 5마리가 무엇을 하는지 본다.
///   - 머리 위 상태 글자 (순찰 / 이동 / 수색 / 추격 / 매복 / 복귀 / 기절)
///   - 시야 부채꼴, 청각 원, 구역 원, 지금 향하는 목적지
///   - 감독이 보낸 흐린 지점, 매복 지점, 플레이어 진행 방향
///
/// 조작 (사용자 요청으로 단순화)
///   WASD   지도 기준 이동 (W = 지도 위쪽). 캐릭터가 보는 방향과 무관
///   Shift  달리기
///   좌클릭 플레이어 자리에서 소리 (게임 속 발사음과 같은 반경)
///
/// 테스트에 필요한 것만 남긴다 (사용자 요청):
///   남김 — 지도, 몬스터, 플레이어, 오버레이, WASD·Shift·좌클릭, 상태 패널
///   뺌   — 홈 화면·카메라 이동·카운트다운, 모든 소리, 게임 UI, 과녁, 연출(속도선 등), 사격
/// 무적·제한 시간 정지는 자동으로 켜진다. 오버레이 선은 전용 레이어(31)에 그린다.
/// </summary>
public class AITestDebug : MonoBehaviour
{
	private const int OverlayLayer = 31;

	// 필드 이름을 바꾸면 씬에 저장된 옛 값이 무시되고 아래 기본값이 적용된다.
	// (첫 버전의 noiseRadius 45가 씬에 남아 좌클릭 소리가 30m가 아니라 45m로 나가던 문제를 이렇게 끊었다)
	[Header("조작")]
	[Tooltip("WASD 기본 이동 속도. 11 = 실제 게임의 달리기 속도라 추격 테스트가 실제와 같다")]
	[SerializeField] private float testMoveSpeed = 11f;
	[Tooltip("Shift 누를 때 속도. 맵을 빨리 가로지르는 용도")]
	[SerializeField] private float testFastSpeed = 30f;
	[Tooltip("좌클릭 소리 반경 (게임 속 발사음과 같게)")]
	[SerializeField] private float testNoiseRadius = 30f;

	[Header("표시")]
	[SerializeField] private float lineWidth = 0.6f;

	private Camera overviewCam;
	private Camera mainCam;
	private float overlayY;
	private Material lineMat;

	private Transform player;
	private Rigidbody playerBody;
	private PlayerController playerController;
	private PlayerShooter playerShooter;
	private Vector3 moveInput;

	private class MonsterLines
	{
		public LineRenderer body, zone, hearing, sight, dest, intercept;
	}
	private readonly Dictionary<MonsterAI, MonsterLines> monsterLines = new Dictionary<MonsterAI, MonsterLines>();
	private readonly List<LineRenderer> dispatchMarkers = new List<LineRenderer>();
	private LineRenderer playerMarker, playerVelocity, noiseRing;
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
		}

		BuildOverviewCamera();
		playerMarker = NewLine("Player", Color.white, true);
		playerVelocity = NewLine("PlayerVelocity", Color.white, false);
		noiseRing = NewLine("Noise", new Color(1f, 0.55f, 0.1f, 0.9f), true);
		noiseRing.enabled = false;

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
	/// 바닥이 없는 곳(벽)으로는 못 가고, 벽에 비스듬히 닿으면 벽을 따라 미끄러진다.
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
			|| TryWalk(current, new Vector3(0f, 0f, step.z), out next))
		{
			playerBody.MovePosition(next);
		}
		// 몬스터 시야 판정은 위치만 보지만, 지도에서 방향이 읽히도록 이동 방향을 바라보게 한다
		playerBody.MoveRotation(Quaternion.LookRotation(new Vector3(moveInput.x, 0f, moveInput.z)));
	}

	private static bool TryWalk(Vector3 from, Vector3 step, out Vector3 result)
	{
		result = from;
		if (step.sqrMagnitude < 0.0001f) { return false; }
		Vector3 target = from + step;
		// 위아래로 넉넉히 찾아서 단차를 흡수한다
		if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { return false; }
		// 찾은 바닥이 옆으로 멀리 떨어져 있으면 그 방향은 벽이다
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
					intercept = NewLine(m.name + "_Intercept", new Color(0.85f, 0.4f, 1f, 1f), true)
				};
				monsterLines[m] = ml;
			}
			DrawMonster(m, ml);
		}

		DrawDispatchPoints();

		if (player != null)
		{
			SetCircle(playerMarker, player.position, 1.6f);
			Vector3 v = (MonsterDirector.Instance != null) ? MonsterDirector.Instance.DebugPlayerVelocity : Vector3.zero;
			SetSegment(playerVelocity, player.position, player.position + v * 1.5f);
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

	private void DrawDispatchPoints()
	{
		int i = 0;
		if (MonsterDirector.Instance != null)
		{
			foreach (KeyValuePair<MonsterAI, Vector3> pair in MonsterDirector.Instance.DebugDispatchPoints)
			{
				if (i >= dispatchMarkers.Count) { dispatchMarkers.Add(NewLine("Dispatch" + i, new Color(1f, 0.6f, 0.1f, 1f), true)); }
				dispatchMarkers[i].enabled = true;
				SetCircle(dispatchMarkers[i], pair.Value, 2f);
				i++;
			}
		}
		for (; i < dispatchMarkers.Count; i++) { dispatchMarkers[i].enabled = false; }
	}

	private static Color StateColor(MonsterAI m)
	{
		switch (m.CurrentState)
		{
		case MonsterAI.State.Patrol: return new Color(0.55f, 0.85f, 0.55f);
		case MonsterAI.State.Chase: return new Color(1f, 0.25f, 0.25f);
		case MonsterAI.State.Investigate: return m.StateLabel == "이동" ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.9f, 0.2f);
		case MonsterAI.State.Return: return new Color(0.3f, 0.8f, 1f);
		case MonsterAI.State.Stun: return Color.white;
		case MonsterAI.State.Intercept: return new Color(0.85f, 0.4f, 1f);
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

		// 지도 위 이름표
		if (overviewCam != null)
		{
			MonsterDirector director = MonsterDirector.Instance;
			foreach (MonsterAI m in MonsterAI.activeMonsters)
			{
				if (m == null) { continue; }
				string tag = (director != null && Contains(director.DebugSquad, m)) ? " [파견]" : "";
				DrawLabel(m.transform.position, m.name.Replace("Monster_", "") + tag + "\n" + m.StateLabel, StateColor(m));
			}
			if (player != null) { DrawLabel(player.position, "플레이어", Color.white); }
		}

		// 좌상단 패널
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("<b>AI 테스트</b>  WASD 이동(" + testMoveSpeed + ") · Shift 빠르게(" + testFastSpeed + ") · 좌클릭 소리(" + testNoiseRadius + "m)");
		MonsterDirector d = MonsterDirector.Instance;
		if (d != null)
		{
			sb.Append("사냥 <b>" + (d.IsHunting ? "진행 중" : "대기") + "</b> · 파견 ");
			if (d.DebugSquad.Count == 0) { sb.Append("-"); }
			for (int i = 0; i < d.DebugSquad.Count; i++)
			{
				if (d.DebugSquad[i] != null) { sb.Append((i > 0 ? ", " : "") + d.DebugSquad[i].name.Replace("Monster_", "")); }
			}
			sb.AppendLine(" · 추격 중 <b>" + d.DebugChaserCount + "</b>마리 (최대 2)");
		}
		foreach (MonsterAI m in MonsterAI.activeMonsters)
		{
			if (m == null) { continue; }
			sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(StateColor(m)) + ">■</color> " + m.name + "  " + m.StateLabel);
		}
		sb.Append("<color=#8CD98C>순찰</color> <color=#FF9A1A>이동</color> <color=#FFE633>수색</color> <color=#FF4040>추격</color> "
			+ "<color=#D966FF>매복</color> <color=#4DCCFF>복귀</color> 기절 · <color=#FF9A1A>○</color> 감독이 보낸 지점");
		GUI.Box(new Rect(10, 10, 480, 60 + MonsterAI.activeMonsters.Count * 20 + 20), sb.ToString(), panelStyle);
	}

	private void DrawLabel(Vector3 world, string text, Color color)
	{
		Vector3 sp = overviewCam.WorldToScreenPoint(Lift(world));
		if (sp.z < 0f) { return; }
		Rect rect = new Rect(sp.x + 12f, Screen.height - sp.y - 18f, 160f, 40f);
		Color prev = labelStyle.normal.textColor;
		labelStyle.normal.textColor = Color.black;
		GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, labelStyle);
		labelStyle.normal.textColor = color;
		GUI.Label(rect, text, labelStyle);
		labelStyle.normal.textColor = prev;
	}

	private static bool Contains(IReadOnlyList<MonsterAI> list, MonsterAI m)
	{
		for (int i = 0; i < list.Count; i++) { if (list[i] == m) { return true; } }
		return false;
	}
}
