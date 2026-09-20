using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 한 마리의 행동. 플랫 FSM.
///
/// 몬스터는 전역 1 + 구역 4. <b>모두 이 같은 FSM을 쓴다.</b> 전역 몬스터의 차이는 넓은 순찰과 좋은 청각뿐이다.
/// 무리를 어떻게 움직일지는 <see cref="MonsterDirector"/>(보이지 않는 감독, 두 번째 뇌)가 정하고,
/// 이 클래스는 명령(수색 / 차단 / 해제)과 흐린 목적지만 받아 기존 상태로 수행한다.
///
/// 행동 규칙 (기획 2026-09-14):
///   평상시              → 순찰 (구역 몬스터는 자기 구역, 전역 몬스터는 맵 전체)
///   플레이어를 직접 봄  → 추격 + 감독에게 보고 → 사냥 팀 3마리(발견자 포함)
///   시야를 놓침(모퉁이)  → 곧바로 수색하지 않고 감독의 흐린 힌트로 <b>끈질김 시간</b>만큼 더 쫓는다.
///                         놓칠 때마다 끈질김이 줄어든다(6 → 4.2 → 2.9초…). 다 떨어지면 잠깐 두리번 → 빠르게 복귀
///   소리를 들음         → 멈칫하며 소리 쪽을 본 뒤 그 근처로 달려간다 (한 소리에 가까운 2마리까지)
///   (감독) 차단         → 플레이어가 향하는 쪽 길에 가서 막는다, 가까이 오면 덮침
///   사냥 종료           → 구역 밖이면 매우 빠르게 복귀
///   레이저 피격         → 기절
///
/// 정보는 한 방향: 감독은 진짜 위치를 알지만 몬스터에게는 흐린 목적지만 준다.
/// 몬스터가 플레이어의 정확한 현재 위치를 쓰는 것은 <b>자기 눈으로 보고 있을 때</b>뿐이다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody))]
public class MonsterAI : MonoBehaviour
{
	public enum MonsterRole
	{
		Zone_Defender = 0,
		Global_Stalker = 1
	}

	/// <summary>사용하지 않는다. 씬 호환을 위해 남겨 둠 — 모든 몬스터는 "보이면 곧장 쫓기"로 통일(기획: 같은 AI).</summary>
	public enum ChaseStyle
	{
		Blinky_Direct = 0,
		Pinky_Predict = 1,
		Inky_Tactical = 2,
		Rusher_Berserk = 3
	}

	public enum State
	{
		Patrol = 0,
		Chase = 1,
		Investigate = 2,
		Return = 3,
		Stun = 4,
		Intercept = 5
	}

	/// <summary>AI 테스트 씬: 몬스터가 플레이어를 보지 못하게 한다.</summary>
	public static bool DebugPlayerInvisible;

	[Header("1. Identity & Role")]
	public MonsterRole role;

	[Tooltip("사용하지 않음. 추격 방식은 전부 같다 — 앞길·옆길 차단은 감독이 맡는다")]
	public ChaseStyle style;

	public Transform player;

	[Header("2. Movement & Zone")]
	public bool useGlobalNavMesh;

	public Transform zoneCenter;

	public float zoneRadius = 35f;

	[Header("3. Speed Settings")]
	public float patrolSpeed = 3.5f;

	[Tooltip("사냥 중이 아닐 때 쓰는 추격 속도. 사냥 중(추격·차단·달려가는 수색)에는 감독이 거리에 따라 정한다(6. 조정자 — 사냥 속도)")]
	public float chaseSpeed = 9f;

	public float investigateSpeed = 6f;

	public float patrolWaitTime;

	[Tooltip("사냥이 끝나 구역으로 돌아갈 때 속도. 매우 빠르게 — 구역을 꽤 벗어나기 때문(기획 2026-09-14)")]
	public float returnSpeed = 16f;

	[Header("3-1. Acceleration")]
	public float patrolAcceleration = 30f;

	public float chaseAcceleration = 80f;

	public float investigateAcceleration = 50f;

	[Header("3-2. Agent Base")]
	public float angularSpeed = 600f;

	public float stoppingDistance = 1.2f;

	[Header("4. Senses — Sight")]
	public float sightRange = 25f;

	public float fovAngle = 160f;

	public LayerMask obstacleMask;

	[Header("4-1. Senses — Hearing")]
	[Tooltip("전역 몬스터 기준 청각 반경")]
	public float hearingRange = 50f;

	[Tooltip("구역 몬스터는 이 비율만큼만 듣는다 (기획: 전역보다 좁다)")]
	[Range(0.05f, 1f)]
	public float zoneHearingScale = 0.45f;

	[Tooltip("소리로 아는 위치의 부정확도. 멀리서 난 소리일수록 어긋난 지점으로 향한다. 0이면 항상 정확")]
	[Range(0f, 1f)]
	public float noiseAccuracyFalloff = 0.35f;

	[Tooltip("소리를 들으면 이 시간 동안 멈칫하며 소리 쪽으로 몸을 돌린 뒤 달려간다(초). 반응이 눈에 보여야 소리가 주의를 끈다")]
	public float noiseReactTime = 0.5f;

	[Tooltip("멈칫은 '몰랐다가 처음 알아챌 때' 한 번만. 한 번 멈칫하면 이 시간(초) 동안 다시 멈칫하지 않는다. " +
		"계속 쏘는 플레이어 앞에서 멈칫이 반복되면 몬스터가 제자리에 서 있게 된다(기획 2026-09-17)")]
	public float noiseReactCooldown = 8f;

	[Tooltip("수색 지점 도착 후 두리번거리는 시간")]
	public float investigateLookTime = 3f;

	[Tooltip("제자리 수색 시 회전 속도(도/초)")]
	public float investigateTurnSpeed = 120f;

	[Header("4-2. Debug")]
	public bool showDebugLog = true;

	public bool drawDebugLine = true;

	[Header("5. Combat")]
	[Tooltip("접촉 시 깎는 하트 수")]
	public int contactDamageHearts = 1;

	public float damageCooldown = 1f;

	[Header("5-1. Stun (레이저 피격)")]
	[Tooltip("완전히 굳어 있는 시간")]
	public float stunFreezeTime = 1.5f;

	[Tooltip("굳은 뒤 자세를 되찾는 데 걸리는 시간")]
	public float stunRecoverTime = 1.5f;

	[Tooltip("굳어 있는 동안 플레이어가 몸을 통과할 수 있게 한다. 좌클릭으로 만든 틈으로 빠져나가기 위함")]
	public bool stunLetPlayerPass = true;

	[Tooltip("스턴이 끝났는데 플레이어와 몸이 겹쳐 있으면 충돌 복구를 이 시간까지 미룬다(초). " +
		"겹친 채 복구하면 서로 튕겨나가거나 벽 밖으로 밀린다")]
	public float stunRestoreMaxDelay = 3f;

	[Tooltip("전기 아크 연출을 켤지 여부")]
	public bool showStunSparks = true;

	[Tooltip("동시에 그릴 아크 개수")]
	[Range(1, 12)]
	public int stunArcCount = 5;

	[Tooltip("아크가 튀는 폭. 몸집에 맞춰 조절")]
	public float stunArcJaggedness = 0.16f;

	[Header("6. 조정자(Director) — 전역 몬스터의 값만 사용됨")]
	[Tooltip("동시에 직접 쫓는 최대 마릿수")]
	public int maxSimultaneousChasers = 2;

	[Tooltip("사냥 팀 마릿수(발견자 포함). 발견자가 쫓고 나머지는 앞길·옆길을 막는다")]
	public int huntTeamSize = 3;

	[Tooltip("팀 밖 몬스터가 가장 먼 차단 팀원보다 이 비율만큼 가까우면 교대(0~1). 도망친 쪽 구역 몬스터가 앞길로 올라온다")]
	[Range(0.1f, 1f)]
	public float teamSwapRatio = 0.6f;

	[Tooltip("경로 겹침이 이 비율을 넘으면 대체 경로(경유지)를 찾는다. 0=늘 우회, 1=절대 우회 안 함")]
	[Range(0f, 1f)]
	public float routeOverlapThreshold = 0.45f;

	[Tooltip("두 경로가 이 거리 안이면 '같은 길'로 본다(m)")]
	public float routeNearDistance = 3f;

	[Tooltip("플레이어 이 반경 안은 겹침 판정에서 제외한다(m). 모든 경로가 플레이어에서 만나므로 " +
		"빼지 않으면 지워지지 않는 바닥값이 생긴다. 다만 넓게 빼면 공유 통로가 묻히므로 주의")]
	public float convergenceExcludeRadius = 8f;

	[Tooltip("차단 후보: 플레이어에게서 8방향으로 바닥을 따라 최대 이만큼 뻗어 본다(m)")]
	public float cutCandidateMaxDistance = 18f;

	[Tooltip("차단 후보: 이만큼도 못 가고 막히는 방향은 버린다(m)")]
	public float cutCandidateMinDistance = 6f;

	[Tooltip("차단 후보: 서로 이 거리 안이면 하나로 합친다(m). 너무 크면 막을 방향이 서너 개로 줄어 포위가 한쪽으로 몰린다")]
	public float cutCandidateMergeDistance = 6f;

	[Tooltip("차단 대기 몬스터가 덮치려 할 때, 가장 먼 추격자보다 이만큼 이상 가까워야 교대한다(m)")]
	public float swapDistanceMargin = 3f;

	[Tooltip("차단하러 가는 길이 플레이어 이 반경 안을 지나면 비싸게 친다 — 뚫고 가면 결국 뒤를 쫓는 꼴")]
	public float crossingAvoidRadius = 6f;

	[Tooltip("감독이 차단 목적지를 흐리는 반경(m). 몬스터에게 정확한 좌표를 주지 않는다")]
	public float hintBlurRadius = 4f;

	[Tooltip("추격자가 놓쳤을 때 감독이 주는 힌트의 흐림 반경(m)")]
	public float pursuitHintBlur = 3f;

	[Tooltip("추격자가 아무도 없고 마지막 목격 후 이 시간이 지나면 사냥 종료(초)")]
	public float huntMemory = 8f;

	[Tooltip("차단 배치를 다시 계산하는 주기(초)")]
	public float layoutRefreshInterval = 1f;

	[Tooltip("줄줄이 감지: 같은 방향으로 가는 몬스터 바로 뒤 이 거리(m) 안에 붙어 가면 '따라가는 중'")]
	public float followDistance = 7f;

	[Tooltip("줄줄이 감지: 위 상태가 이 시간(초) 이어지면 뒤쪽 몬스터를 떼어낸다")]
	public float followHoldTime = 1f;

	[Tooltip("끈질김: 시야를 처음 놓쳤을 때 감독의 흐린 힌트로 계속 쫓는 시간(초). 업계 권장 2~3초")]
	public float pursuitPersistence = 3f;

	[Tooltip("끈질김이 끝나도 마지막으로 본 자리(모퉁이)까지는 가 본다. 그 확인에 쓰는 최대 시간(초)")]
	public float cornerCheckTime = 4f;

	[Tooltip("끈질김: 놓칠 때마다 다음 끈질김에 곱하는 비율. 모퉁이를 여러 번 돌면 결국 떨어져 나간다")]
	[Range(0.1f, 1f)]
	public float persistenceDecay = 0.7f;

	[Tooltip("끈질김: 줄어들어도 이 아래로는 내려가지 않는다(초)")]
	public float persistenceMin = 2f;

	[Tooltip("끈질김이 다 떨어졌을 때 그 자리에서 두리번거리는 시간(초). 이후 복귀")]
	public float giveUpLookTime = 2f;

	[Tooltip("사냥 속도: 플레이어와 가까울 때(플레이어 달리기 11보다 약간 느리게 — 똑바로 달리면 떨칠 수 있다)")]
	public float huntNearSpeed = 10f;

	[Tooltip("사냥 속도: 플레이어와 멀 때(먼 몬스터는 금방 따라붙는다)")]
	public float huntFarSpeed = 14f;

	[Tooltip("사냥 속도: 이 거리(m) 안이면 가까운 속도")]
	public float huntNearDistance = 15f;

	[Tooltip("사냥 속도: 이 거리(m) 밖이면 먼 속도. 사이는 부드럽게 바뀐다")]
	public float huntFarDistance = 40f;

	[Tooltip("빙 돌아가기: 돌아가는 데 이 시간(초)을 넘으면 우회하지 않고 자기 쪽에서 접근한다. 사냥 유지 시간과 맞춰 8초")]
	public float detourTimeLimit = 8f;

	[Tooltip("빙 돌아가기: 플레이어에게서 이 거리(m)에 경유 지점을 잡는다. 여기를 먼저 들른 뒤 막을 자리로 간다")]
	public float detourWaypointDistance = 22f;

	[Tooltip("들어오는 쪽을 가르는 기준 각도. 차단 몬스터는 추격자와, 그리고 서로 이 각도 이상 다른 쪽에서 들어온다")]
	[Range(30f, 150f)]
	public float sideAngleMin = 90f;

	[Tooltip("흩어짐: 이 시간(초) 동안 조인 뒤 흩어진다. 팩맨의 스캐터 — 추격이 길어져도 다 같이 몰리지 않게")]
	public float scatterAfter = 20f;

	[Tooltip("흩어짐: 물러나 있는 시간(초). 이 동안 추격자만 계속 쫓고, 막던 몬스터는 자기 구역으로 돌아간다")]
	public float scatterTime = 6f;

	[Tooltip("흩어짐: 물러난 몬스터를 다시 부르지 않는 시간(초). 왔다 갔다 방지")]
	public float scatterRejoinBlock = 8f;

	[Tooltip("추격 인계: 추격자가 자기 구역 밖(반경 ×1.2)에 이 시간(초) 이상 머무르면, 플레이어가 있는 구역 몬스터가 이어받는다")]
	public float handoverDelay = 3f;

	[Tooltip("복귀 순간이동: 플레이어에게 보이지 않는 상태가 이 시간(초) 이어지면 구역으로 옮긴다")]
	public float teleportUnseenTime = 3f;

	[Tooltip("복귀 순간이동: 플레이어와 이 거리(m) 밖일 때만. 출발·도착 모두 보이지 않아야 한다")]
	public float teleportMinDistance = 40f;

	[Tooltip("복귀 순간이동 판정에 쓰는 플레이어 시야각(도). 이 안이고 가려지지 않았으면 '보인다'로 친다")]
	public float playerViewAngle = 90f;

	[Header("7. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	[Header("8. 차단 대기 (감독이 지시)")]
	[Tooltip("차단 대기 중 플레이어가 이 거리 안에 보이면 덮친다")]
	public float ambushEngageRange = 8f;

	[Tooltip("차단 명령이 이 시간 동안 갱신되지 않으면 포기하고 복귀한다(초)")]
	public float interceptTimeout = 12f;

	[Tooltip("추격↔차단 역할이 바뀐 직후 다시 바뀌지 않는 시간(초)")]
	public float roleLockTime = 2f;

	[Tooltip("경유지에 이 거리 안으로 들어오면 지난 것으로 보고 최종 접근으로 넘어간다(m)")]
	public float waypointReachedDistance = 3f;

	[Tooltip("최종 접근 중 플레이어 위치를 다시 조준하는 간격(초). 0이면 매 프레임")]
	public float finalApproachRefresh = 0.15f;

	[Tooltip("플레이어에게 이 거리 안까지 붙었으면 감독이 경로를 갈아엎지 않는다(m). " +
		"거의 닿았는데 갑자기 딴 길로 돌아가는 것을 막는다. 멀리 있을 때는 잠기지 않아야 겹침 배정이 계속 돈다")]
	public float routeLockDistance = 12f;

	public static List<MonsterAI> activeMonsters = new List<MonsterAI>();

	private NavMeshAgent agent;

	private Animator animator;

	private Rigidbody rb;

	private State currentState;

	private State stateBeforeStun;

	// 이 몬스터가 향하는 목표: 보고 있던 위치, 들은 소리 위치, 감독이 준 흐린 지점
	private Vector3 lastKnownPos;

	private static NavMeshTriangulation navMeshData;

	private static bool isNavMeshDataLoaded = false;

	private float stateTimer;

	private float damageTimer;

	private bool isJumping;

	private bool isStunned;

	private Vector3 startPosition;

	private Quaternion startRotation;

	// 지점을 향해 달려가는 중인가. 수색 상태를 벗어나면 꺼진다.
	private bool isRushing;

	// 차단 대기
	private Vector3 interceptPoint;
	private Vector3 interceptWaypoint;      // 빙 돌아가기: 여기를 먼저 들른 뒤 차단 지점으로
	private bool hasWaypoint;
	private float interceptTimer;

	// 최종 접근: 경유지를 지났고, 이제 플레이어의 현재 위치를 계속 따라간다
	private bool finalApproach;

	private float nextFinalRefresh;

	private Collider myCollider;

	private Collider playerCollider;

	private bool playerPassThrough;
	private float roleLockUntil;
	private float noiseReactReadyAt;        // 이 시각 전에는 다시 멈칫하지 않는다
	private float WaypointReached => waypointReachedDistance;

	// 사냥 속도 (감독이 정함, 음수면 chaseSpeed)
	private float huntSpeed = -1f;

	// 끈질김
	private float pursuitBudget = 6f;   // 다음에 놓쳤을 때 쓸 끈질김
	private float pursuitTimer;         // 지금 남은 끈질김
	private bool lostSight;
	private float nextHintTime;
	private bool givingUp;
	private float lostSightAt;
	private float budgetBeforeLoss;
	private Vector3 cornerPos;          // 마지막으로 본 자리 — 끈질김이 끝나도 여기는 확인한다
	private float cornerTimer;
	private bool checkingCorner;
	private float homeLockUntil;        // 복귀 명령 뒤 이 시간까지는 다시 추격하지 않는다(흩어짐·인계)
	private const float HintInterval = 1f;
	private const float SightFlickerGrace = 0.5f;
	private const float TouchSightRange = 2f;

	// 소리 반응(멈칫)
	private bool pendingNoise;
	private float noiseReactUntil;
	private Vector3 noiseTarget;

	public bool IsInStun => currentState == State.Stun;

	public bool IsRushing => currentState == State.Investigate && isRushing;

	/// <summary>끈질김이 떨어져 두리번거리는 중. 감독은 이 개체를 팀에서 뺀다.</summary>
	public bool IsGivingUp => givingUp;

	/// <summary>수평 이동 속도. 감독의 줄줄이 감지용.</summary>
	public Vector3 PlanarVelocity
	{
		get
		{
			if (agent == null) { return Vector3.zero; }
			Vector3 v = agent.velocity;
			v.y = 0f;
			return v;
		}
	}

	/// <summary>지금 위치 + 현재 계획 경로. 몬스터끼리 같은 길을 쓰는지 비교할 때 쓴다.</summary>
	public Vector3[] SpacingRoute()
	{
		List<Vector3> points = new List<Vector3>();
		points.Add(base.transform.position);
		if (agent != null && agent.isOnNavMesh && agent.hasPath)
		{
			points.AddRange(agent.path.corners);
		}
		return points.ToArray();
	}

	public State CurrentState => currentState;

	public bool IsIntercepting => currentState == State.Intercept;

	/// <summary>감독이 이 개체를 추격에서 차단으로 돌려도 되는가. 전역 몬스터는 제외, 방금 역할이 바뀌었으면 보류.</summary>
	public bool CanBeDemoted =>
		currentState == State.Chase && role == MonsterRole.Zone_Defender && Time.time >= roleLockUntil;

	/// <summary>감독의 명령(수색·차단)을 받을 수 있는가. 직접 쫓는 중·기절·점프 중이면 제외.</summary>
	public bool IsAvailableForOrders =>
		currentState != State.Chase && currentState != State.Stun && !isJumping;

	/// <summary>경유지를 지나 플레이어에게 곧장 들어가는 중인가. 이 동안은 멈추지 않는다.</summary>
	public bool IsFinalApproach => finalApproach && currentState == State.Intercept;

	/// <summary>
	/// 감독이 경로를 갈아엎으면 안 되는 상태인가. 최종 접근 중이고 <b>플레이어에게 충분히 붙었을 때만</b> 잠근다.
	/// 멀리서부터 잠그면 겹침 배정이 영영 돌지 않는다(QA 2026-09-20에서 잡힌 문제).
	/// </summary>
	public bool IsRouteLocked
	{
		get
		{
			if (!IsFinalApproach || player == null) { return false; }
			Vector3 gap = player.position - base.transform.position;
			gap.y = 0f;
			return gap.magnitude <= routeLockDistance;
		}
	}

	/// <summary>AI 테스트 씬 표시용 상태 이름.</summary>
	public string StateLabel
	{
		get
		{
			if (pendingNoise) { return "멈칫"; }
			switch (currentState)
			{
			case State.Patrol: return "순찰";
			case State.Chase:
				if (checkingCorner) { return "모퉁이 확인"; }
				return lostSight ? $"추적 {Mathf.Max(0f, pursuitTimer):F1}초" : "추격";
			case State.Investigate:
				if (givingUp) { return "놓침"; }
				return (isRushing && !HasArrived()) ? "이동" : "수색";
			case State.Return: return "복귀";
			case State.Stun: return "기절";
			case State.Intercept: return hasWaypoint ? "우회 이동" : (finalApproach ? "최종 접근" : "접근");
			}
			return currentState.ToString();
		}
	}

	/// <summary>AI 테스트 씬 표시용: 지금 향하고 있는 목적지.</summary>
	public Vector3 DebugDestination =>
		(agent != null && agent.isOnNavMesh && agent.hasPath) ? agent.destination : base.transform.position;

	public Vector3 DebugInterceptPoint => interceptPoint;

	/// <summary>이 몬스터의 실제 청각 반경. 역할에 따라 달라진다.</summary>
	public float EffectiveHearingRange =>
		(role == MonsterRole.Global_Stalker) ? hearingRange : hearingRange * zoneHearingScale;

	private void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
		animator = GetComponent<Animator>();
		rb = GetComponent<Rigidbody>();
		myCollider = GetComponent<Collider>();
		startPosition = base.transform.position;
		startRotation = base.transform.rotation;
	}

	private void OnEnable()
	{
		activeMonsters.Add(this);
	}

	private void OnDisable()
	{
		activeMonsters.Remove(this);
		if (playerPassThrough) { SetPlayerPassThrough(false); }
	}

	private void Start()
	{
		if (player == null)
		{
			player = GameObject.FindGameObjectWithTag("Player")?.transform;
		}
		if (useGlobalNavMesh && !isNavMeshDataLoaded)
		{
			navMeshData = NavMesh.CalculateTriangulation();
			isNavMeshDataLoaded = true;
		}
		if (zoneCenter == null)
		{
			GameObject gameObject = new GameObject(base.name + "_Home");
			gameObject.transform.position = base.transform.position;
			zoneCenter = gameObject.transform;
		}
		agent.acceleration = patrolAcceleration;
		agent.angularSpeed = angularSpeed;
		agent.stoppingDistance = stoppingDistance;
		// State.Patrol은 enum 기본값(0)이라 ChangeState(Patrol)은 조기 반환한다.
		// 초기 목적지를 여기서 명시적으로 잡아야 첫 프레임부터 순찰한다.
		currentState = State.Patrol;
		isRushing = false;
		agent.autoBraking = false;
		PickNewDestination();

		// 감독은 첫 목격 전에도 추격 인원을 세야 하므로 미리 깨운다
		_ = MonsterDirector.Instance;
		ResetPursuit();
	}

	public void ResetMonster()
	{
		base.enabled = true;
		if (agent.isOnNavMesh)
		{
			agent.isStopped = true;
		}
		agent.velocity = Vector3.zero;
		agent.Warp(startPosition);
		base.transform.rotation = startRotation;
		currentState = State.Patrol;
		isStunned = false;
		isJumping = false;
		isRushing = false;
		roleLockUntil = 0f;
		damageTimer = 0f;
		huntSpeed = -1f;
		pendingNoise = false;
		hasWaypoint = false;
		finalApproach = false;
		noiseReactReadyAt = 0f;
		homeLockUntil = 0f;
		checkingCorner = false;
		givingUp = false;
		lostSight = false;
		ResetPursuit();
		if (animator != null)
		{
			animator.Rebind();
			animator.Update(0f);
		}
		if (agent.isOnNavMesh)
		{
			agent.ResetPath();
			agent.autoBraking = false;
			PickNewDestination();
			agent.isStopped = true;
		}
	}

	/// <summary>사냥이 끝나면 끈질김을 처음 값으로 되돌린다.</summary>
	public void ResetPursuit()
	{
		MonsterDirector director = MonsterDirector.Instance;
		pursuitBudget = director != null ? director.PursuitPersistence : pursuitPersistence;
	}

	/// <summary>감독이 정한 사냥 속도. 음수면 해제(chaseSpeed 사용).</summary>
	public void SetHuntSpeed(float speed)
	{
		huntSpeed = speed;
	}

	private float HuntMoveSpeed => huntSpeed > 0f ? huntSpeed : chaseSpeed;

	/// <summary>못 본 채 추적할 때의 속도 상한. 감독의 "가까울 때 속도"를 쓴다.</summary>
	private float TrackingCap
	{
		get
		{
			MonsterDirector d = MonsterDirector.Instance;
			return d != null ? d.TrackingSpeedCap : huntNearSpeed;
		}
	}

	// ────────────────────────────────────────────────
	//  청각
	// ────────────────────────────────────────────────

	/// <summary>이 소리가 들리는가. 추격·차단·기절 중이면 소리에 한눈팔지 않는다(시킨 일만 한다).</summary>
	public bool CanHearNoise(Vector3 soundPosition, float noiseRadius, out float distance)
	{
		distance = Vector3.Distance(base.transform.position, soundPosition);
		if (currentState == State.Chase || currentState == State.Intercept || isStunned || givingUp)
		{
			return false;
		}
		return distance <= Mathf.Min(EffectiveHearingRange, noiseRadius);
	}

	/// <summary>
	/// <see cref="NoiseSystem"/>이 들은 개체 중 가까운 순서로 골라 호출한다.
	///
	/// 소리는 <b>"플레이어가 여기 있다"가 아니라 "여기서 소리가 났다"</b>다.
	/// 멀리서 난 소리일수록 어긋난 지점으로 향한다. 들으면 잠깐 멈칫하며 소리 쪽을 본 뒤 달려간다.
	/// </summary>
	public void OnHearNoise(Vector3 soundPosition, float noiseRadius, NoiseKind kind)
	{
		if (!CanHearNoise(soundPosition, noiseRadius, out float distance)) { return; }
		float audibleRange = Mathf.Min(EffectiveHearingRange, noiseRadius);

		// 거리 비율만큼 위치를 흐린다
		Vector3 guessed = soundPosition;
		if (noiseAccuracyFalloff > 0f && audibleRange > 0.01f)
		{
			float ratio = Mathf.Clamp01(distance / audibleRange);
			float jitter = ratio * audibleRange * noiseAccuracyFalloff;
			Vector2 offset = UnityEngine.Random.insideUnitCircle * jitter;
			Vector3 candidate = soundPosition + new Vector3(offset.x, 0f, offset.y);
			if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, jitter + 2f, NavMesh.AllAreas))
			{
				guessed = hit.position;
			}
		}

		if (showDebugLog)
		{
			Debug.Log($"<color=cyan><b>[청각]</b></color> {base.gameObject.name} — {kind} 감지 " +
				$"(거리 {distance:F1}m / 가청 {audibleRange:F1}m)");
		}
		if (drawDebugLine)
		{
			Debug.DrawLine(base.transform.position + Vector3.up, guessed + Vector3.up, Color.cyan, 2.5f);
		}

		noiseTarget = guessed;
		// 멈칫은 "몰랐다가 처음 알아챌 때"만. 이미 확인하러 가는 중이거나 쿨타임이면 멈추지 않고 목적지만 바꾼다
		bool alreadyAlert = currentState == State.Investigate || pendingNoise || Time.time < noiseReactReadyAt;
		if (!alreadyAlert && noiseReactTime > 0f)
		{
			pendingNoise = true;
			noiseReactUntil = Time.time + noiseReactTime;
			noiseReactReadyAt = Time.time + noiseReactCooldown;
		}
		else
		{
			pendingNoise = false;
			RushToInvestigate(guessed);
		}
	}

	/// <summary>
	/// 한 지점으로 달려가 수색한다.
	/// 이미 수색 중이어도 새 지점으로 갱신해야 한다 — <see cref="ChangeState"/>는 같은 상태로의 전환을 무시한다.
	/// </summary>
	private void RushToInvestigate(Vector3 position)
	{
		lastKnownPos = position;
		givingUp = false;
		if (currentState == State.Investigate)
		{
			stateTimer = investigateLookTime;
			agent.SetDestination(position);
		}
		else
		{
			ChangeState(State.Investigate);
		}
		isRushing = true;
	}

	// ────────────────────────────────────────────────
	//  감독(Director)의 명령 — 목적지만 받아 기존 상태로 수행한다
	// ────────────────────────────────────────────────

	/// <summary>수색 명령: 그 지점으로 가서 둘러본다.</summary>
	public void CommandSearch(Vector3 point)
	{
		if (isStunned || currentState == State.Chase) { return; }
		pendingNoise = false;
		RushToInvestigate(point);
	}

	/// <summary>차단 명령: 지점으로 가서 막는다. 이미 차단 중이면 지점만 옮긴다. 명령이 갱신되는 동안은 포기하지 않는다.</summary>
	public void CommandAmbush(Vector3 point)
	{
		CommandAmbush(point, point, false);
	}

	/// <summary>
	/// 빙 돌아가는 차단 명령: 경유 지점을 먼저 들른 뒤 차단 지점으로 간다.
	/// 플레이어를 뚫고 가는 대신 반대편으로 돌아 들어오게 한다(기획 2026-09-17).
	/// </summary>
	public void CommandAmbush(Vector3 point, Vector3 waypoint, bool viaWaypoint)
	{
		if (isStunned) { return; }
		pendingNoise = false;
		givingUp = false;
		interceptPoint = point;
		interceptTimer = interceptTimeout;
		if (viaWaypoint && Vector3.Distance(base.transform.position, waypoint) > WaypointReached)
		{
			interceptWaypoint = waypoint;
			hasWaypoint = true;
		}
		else if (!viaWaypoint)
		{
			hasWaypoint = false;
			finalApproach = false;
		}
		if (currentState == State.Intercept)
		{
			UpdateInterceptDestination();
			return;
		}
		finalApproach = false;
		roleLockUntil = Time.time + roleLockTime;
		ChangeState(State.Intercept);
	}

	/// <summary>
	/// 추적 명령(2026-09-20 개편). 목적지는 언제나 플레이어다 — 감독이 정하는 것은 "어느 길로"뿐이다.
	/// 경유지가 있으면 그쪽을 먼저 들르고, 지나면 최종 접근으로 넘어가 멈추지 않고 플레이어를 따라간다.
	/// 차단 지점에 가서 기다리던 예전 방식(CommandAmbush)을 대체한다.
	/// </summary>
	public void CommandPursue(Vector3 waypoint, bool viaWaypoint)
	{
		if (isStunned) { return; }
		pendingNoise = false;
		givingUp = false;
		interceptTimer = interceptTimeout;
		if (viaWaypoint && Vector3.Distance(base.transform.position, waypoint) > WaypointReached)
		{
			interceptWaypoint = waypoint;
			hasWaypoint = true;
			finalApproach = false;
		}
		else
		{
			hasWaypoint = false;
			finalApproach = true;
		}
		if (player != null) { interceptPoint = player.position; }
		if (currentState == State.Intercept)
		{
			UpdateInterceptDestination();
			return;
		}
		roleLockUntil = Time.time + roleLockTime;
		ChangeState(State.Intercept);
	}

	/// <summary>경유 지점이 남아 있으면 그쪽으로, 다 들렀으면 차단 지점으로.</summary>
	private void UpdateInterceptDestination()
	{
		if (hasWaypoint)
		{
			if (Vector3.Distance(base.transform.position, interceptWaypoint) > WaypointReached)
			{
				agent.SetDestination(interceptWaypoint);
				return;
			}
			hasWaypoint = false;
			finalApproach = true;
		}
		// 최종 접근: 낡은 좌표가 아니라 플레이어의 지금 위치로
		if (finalApproach && player != null)
		{
			interceptPoint = player.position;
			nextFinalRefresh = Time.time + Mathf.Max(0f, finalApproachRefresh);
		}
		agent.SetDestination(interceptPoint);
	}

	/// <summary>AI 테스트 씬 표시용: 지금 돌아가는 중인 경유 지점(없으면 false).</summary>
	public bool TryGetWaypoint(out Vector3 point)
	{
		point = interceptWaypoint;
		return hasWaypoint && currentState == State.Intercept;
	}

	/// <summary>
	/// 복귀 명령: 흩어짐·추격 인계 — 쫓고 있더라도 그만두고 구역으로 돌아간다.
	/// lockSeconds 동안은 플레이어를 봐도 다시 추격하지 않는다(왔다 갔다 방지).
	/// </summary>
	public void CommandGoHome(float lockSeconds)
	{
		if (isStunned) { return; }
		givingUp = false;
		pendingNoise = false;
		checkingCorner = false;
		hasWaypoint = false;
		finalApproach = false;
		homeLockUntil = Time.time + Mathf.Max(0f, lockSeconds);
		ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
	}

	/// <summary>감독이 플레이어에게 보이지 않는 곳에서만 부르는 복귀 순간이동.</summary>
	public void TeleportTo(Vector3 position)
	{
		if (agent == null) { return; }
		agent.Warp(position);
		agent.ResetPath();
		hasWaypoint = false;
		finalApproach = false;
		ChangeState(State.Patrol);
	}

	/// <summary>해제 명령: 사냥이 끝났다 — 차단·수색을 멈추고 구역으로 돌아간다. 직접 쫓는 중이면 무시.</summary>
	public void CommandRelease()
	{
		if (currentState == State.Intercept || currentState == State.Investigate)
		{
			givingUp = false;
			ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
		}
	}

	// ────────────────────────────────────────────────

	private void Update()
	{
		if (agent == null)
		{
			return;
		}
		if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning)
		{
			if (!agent.isStopped)
			{
				agent.isStopped = true;
			}
			return;
		}
		if (agent.isStopped && !isStunned && !isJumping)
		{
			agent.isStopped = false;
		}
		HandlePhysicsAndTimers();
		if (currentState == State.Stun)
		{
			return;
		}

		bool canSee = player != null && CheckSight();

		switch (currentState)
		{
		case State.Patrol:
			agent.speed = patrolSpeed;
			agent.acceleration = patrolAcceleration;
			break;
		case State.Return:
			agent.speed = returnSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Chase:
			// 보고 있으면 거리에 따른 사냥 속도, 못 본 채 추적할 때는 가까운 속도(플레이어보다 느림)로 제한.
			// 그래야 끝까지 달리면 떨칠 수 있다(기획 A안 2026-09-17, QA S6)
			agent.speed = lostSight ? Mathf.Min(HuntMoveSpeed, TrackingCap) : HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Intercept:
			agent.speed = HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Investigate:
			agent.speed = isRushing ? HuntMoveSpeed : investigateSpeed;
			agent.acceleration = isRushing ? chaseAcceleration : investigateAcceleration;
			break;
		}

		// 보고 있으면 누구든 감독에게 보고한다 — 발견자는 사냥 팀에 들어간다
		if (canSee)
		{
			MonsterDirector director = MonsterDirector.Instance;
			if (director != null) { director.ReportSighting(this, player.position); }
		}

		// 소리 반응: 잠깐 멈칫하며 소리 쪽을 본 뒤 달려간다. 그 사이 플레이어가 보이면 곧바로 풀린다
		if (pendingNoise)
		{
			if (canSee || currentState == State.Chase || currentState == State.Intercept)
			{
				pendingNoise = false;
			}
			else if (Time.time < noiseReactUntil)
			{
				agent.isStopped = true;
				Vector3 look = noiseTarget - base.transform.position;
				look.y = 0f;
				if (look.sqrMagnitude > 0.01f)
				{
					base.transform.rotation = Quaternion.Slerp(base.transform.rotation,
						Quaternion.LookRotation(look), Time.deltaTime * 10f);
				}
				return;
			}
			else
			{
				pendingNoise = false;
				RushToInvestigate(noiseTarget);
			}
		}

		switch (currentState)
		{
		case State.Intercept:
			ProcessIntercept(canSee);
			break;
		case State.Patrol:
			ProcessPatrol(canSee);
			break;
		case State.Chase:
			ProcessChase(canSee);
			break;
		case State.Investigate:
			ProcessInvestigate(canSee);
			break;
		case State.Return:
			ProcessReturn(canSee);
			break;
		}
	}

	public void OnHitByLaser(Vector3 shooterPosition)
	{
		if (!isStunned)
		{
			StartCoroutine(ProcessStunReaction(shooterPosition));
		}
	}

	/// <summary>플레이어와의 충돌만 켜고 끈다. 레이어가 아니라 콜라이더 쌍 단위라 지형·동료 충돌은 그대로다.</summary>
	private void SetPlayerPassThrough(bool on)
	{
		if (!stunLetPlayerPass) { return; }
		if (myCollider == null) { myCollider = GetComponent<Collider>(); }
		if (playerCollider == null && player != null) { playerCollider = player.GetComponent<Collider>(); }
		if (myCollider == null || playerCollider == null) { return; }
		if (!myCollider.enabled || !playerCollider.enabled) { return; }
		Physics.IgnoreCollision(myCollider, playerCollider, on);
		playerPassThrough = on;
	}

	/// <summary>플레이어와 몸이 실제로 겹쳐 있는가.</summary>
	private bool OverlapsPlayer()
	{
		if (myCollider == null || playerCollider == null) { return false; }
		if (!myCollider.enabled || !playerCollider.enabled) { return false; }
		return Physics.ComputePenetration(
			myCollider, myCollider.transform.position, myCollider.transform.rotation,
			playerCollider, playerCollider.transform.position, playerCollider.transform.rotation,
			out Vector3 _, out float _);
	}

	/// <summary>
	/// 플레이어와 몸이 겹쳐 있는 동안은 충돌을 되살리지 않는다 — 겹친 채 복구하면 서로 튕겨나가거나 벽 밖으로 밀린다.
	/// 플레이어는 하나뿐이고 빠져나가는 중이라 짧게 끝난다. 상한을 넘으면 그냥 복구한다(무한 대기 방지).
	/// </summary>
	private IEnumerator RestoreCollisionWhenClear()
	{
		float waited = 0f;
		while (playerPassThrough && waited < stunRestoreMaxDelay && OverlapsPlayer())
		{
			waited += Time.deltaTime;
			yield return null;
		}
		SetPlayerPassThrough(false);
	}

	private IEnumerator ProcessStunReaction(Vector3 shooterPosition)
	{
		isStunned = true;
		pendingNoise = false;
		stateBeforeStun = currentState;
		ChangeState(State.Stun);
		agent.isStopped = true;
		agent.velocity = Vector3.zero;
		agent.updateRotation = false;
		// 좌클릭으로 만든 틈 — 굳어 있는 동안 플레이어만 몸을 통과한다(기획 2026-09-20).
		// 몬스터끼리는 열지 않는다: 겹친 채 스턴이 풀리면 서로 튕겨나가거나 끼인다.
		SetPlayerPassThrough(true);
		if (animator != null)
		{
			animator.SetTrigger("Hit");
		}

		// 찌리리 — 굳어 있는 동안만 전기가 흐른다.
		if (showStunSparks)
		{
			StunSparkEffect.Play(base.transform, stunFreezeTime,
				arcCount: stunArcCount, jagged: stunArcJaggedness);
		}

		yield return new WaitForSeconds(stunFreezeTime);
		if (stateBeforeStun == State.Patrol || stateBeforeStun == State.Return || stateBeforeStun == State.Investigate)
		{
			Vector3 normalized = (shooterPosition - base.transform.position).normalized;
			normalized.y = 0f;
			if (normalized != Vector3.zero)
			{
				base.transform.rotation = Quaternion.LookRotation(normalized);
			}
		}
		yield return new WaitForSeconds(stunRecoverTime);
		agent.updateRotation = true;
		agent.isStopped = false;
		isStunned = false;

		// 충돌 복구만 따로 기다린다. AI는 곧바로 깨어난다 —
		// 코루틴 본문에서 기다리면 플레이어가 몸 안에 서 있는 동안 몬스터가 계속 굳어 있다(QA 2026-09-20).
		if (playerPassThrough) { StartCoroutine(RestoreCollisionWhenClear()); }
		if (stateBeforeStun == State.Chase)
		{
			// 기절 전 쫓던 개체는 다시 허가를 받아 추적을 이어간다(끈질김은 남은 만큼). 자리가 없으면 감독이 막는 역할을 준다
			if (!TryStartChase() && currentState == State.Stun)
			{
				ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
			}
		}
		else if (CheckSight())
		{
			if (!TryStartChase() && currentState == State.Stun)
			{
				ChangeState(stateBeforeStun);
			}
		}
		else
		{
			ChangeState(stateBeforeStun);
		}
	}

	private void ProcessPatrol(bool canSee)
	{
		if (canSee)
		{
			TryStartChase();
		}
		else
		{
			if (agent.pathPending || !(agent.remainingDistance <= agent.stoppingDistance))
			{
				return;
			}
			if (patrolWaitTime > 0f)
			{
				stateTimer -= Time.deltaTime;
				if (stateTimer <= 0f)
				{
					PickNewDestination();
				}
			}
			else
			{
				PickNewDestination();
			}
		}
	}

	/// <summary>
	/// 추격. 보고 있으면 정확한 위치로, 놓치면(모퉁이 등) 감독의 흐린 힌트로 끈질김 시간만큼 더 쫓는다.
	/// 놓칠 때마다 다음 끈질김이 줄어든다 — 모퉁이를 여러 번 돌면 결국 떨어져 나간다.
	/// </summary>
	private void ProcessChase(bool canSee)
	{
		if (canSee)
		{
			// 한순간 깜빡 끊겼다 다시 본 것(0.5초 이내)은 "놓침"으로 치지 않는다 — 끈질김을 깎지 않는다
			if (lostSight && Time.time - lostSightAt < SightFlickerGrace) { pursuitBudget = budgetBeforeLoss; }
			lostSight = false;
			checkingCorner = false;
			lastKnownPos = player.position;
			agent.SetDestination(player.position);
			return;
		}

		if (!lostSight)
		{
			lostSight = true;
			lostSightAt = Time.time;
			budgetBeforeLoss = pursuitBudget;
			pursuitTimer = pursuitBudget;
			cornerPos = lastKnownPos;          // 놓친 순간 마지막으로 본 자리
			cornerTimer = cornerCheckTime;
			checkingCorner = false;
			MonsterDirector d = MonsterDirector.Instance;
			float decay = d != null ? d.PersistenceDecay : persistenceDecay;
			float min = d != null ? d.PersistenceMin : persistenceMin;
			pursuitBudget = Mathf.Max(min, pursuitBudget * decay);
			nextHintTime = 0f;
		}

		pursuitTimer -= Time.deltaTime;
		if (pursuitTimer <= 0f)
		{
			// 끈질김이 끝나도 모퉁이는 적어도 확인한다 — 마지막으로 본 자리까지 가 보고 포기(기획 2026-09-18)
			cornerTimer -= Time.deltaTime;
			if (Vector3.Distance(base.transform.position, cornerPos) <= 3f || cornerTimer <= 0f)
			{
				GiveUpChase();
				return;
			}
			checkingCorner = true;
			agent.SetDestination(cornerPos);
			return;
		}

		if (Time.time >= nextHintTime)
		{
			nextHintTime = Time.time + HintInterval;
			MonsterDirector d = MonsterDirector.Instance;
			if (d != null && d.TryGetPursuitHint(this, out Vector3 hint)) { lastKnownPos = hint; }
			agent.SetDestination(lastKnownPos);
		}
	}

	/// <summary>끈질김이 다 떨어졌다: 그 자리에서 잠깐 두리번거린 뒤 복귀한다. 뒤로 돌아가거나 멍하니 순찰하지 않는다.</summary>
	private void GiveUpChase()
	{
		lostSight = false;
		givingUp = true;
		lastKnownPos = base.transform.position;
		ChangeState(State.Investigate);
		isRushing = false;
		MonsterDirector d = MonsterDirector.Instance;
		stateTimer = d != null ? d.GiveUpLookTime : giveUpLookTime;
		if (showDebugLog)
		{
			Debug.Log($"<color=grey><b>[추적 포기]</b></color> {base.name} — 끈질김이 다 떨어짐 (다음 끈질김 {pursuitBudget:F1}초)");
		}
	}

	private void ProcessInvestigate(bool canSee)
	{
		if (canSee)
		{
			givingUp = false;
			TryStartChase();
		}
		else if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
		{
			stateTimer -= Time.deltaTime;
			base.transform.Rotate(Vector3.up, investigateTurnSpeed * Time.deltaTime);
			if (stateTimer <= 0f)
			{
				// 발견 실패: 구역 밖이면 빠르게 복귀, 구역 안이면 그대로 순찰
				givingUp = false;
				ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
			}
		}
	}

	private bool IsOutsideZone()
	{
		return !useGlobalNavMesh && role == MonsterRole.Zone_Defender && zoneCenter != null
			&& Vector3.Distance(base.transform.position, zoneCenter.position) > zoneRadius;
	}

	private bool HasArrived()
	{
		return agent != null && agent.isOnNavMesh && !agent.pathPending
			&& agent.remainingDistance <= agent.stoppingDistance + 0.5f;
	}

	/// <summary>
	/// 차단. 감독이 준 지점으로 가서 막는다.
	/// 기다리는 동안 플레이어를 봐도 멀면 자리를 지키고(보고만 한다), 가까이 오면 덮친다.
	/// </summary>
	private void ProcessIntercept(bool canSee)
	{
		if (hasWaypoint && Vector3.Distance(base.transform.position, interceptWaypoint) <= WaypointReached)
		{
			// 경유지 통과 — 여기서부터 최종 접근이다
			hasWaypoint = false;
			finalApproach = true;
			UpdateInterceptDestination();
		}
		if (canSee && Vector3.Distance(base.transform.position, player.position) <= ambushEngageRange)
		{
			// 덮치기도 허가제: 자리가 없으면 거부되지만, 아래 최종 접근으로 계속 밀고 들어간다
			lastKnownPos = player.position;
			if (TryStartChase())
			{
				return;
			}
		}

		if (finalApproach)
		{
			// 멈추지 않는다. 낡은 좌표가 아니라 플레이어의 지금 위치를 계속 다시 조준한다(기획 2026-09-20)
			if (player != null && Time.time >= nextFinalRefresh)
			{
				nextFinalRefresh = Time.time + Mathf.Max(0f, finalApproachRefresh);
				interceptPoint = player.position;
				agent.SetDestination(interceptPoint);
			}
		}
		else if (HasArrived())
		{
			// 경유지로 가는 길이 막혀 멈춘 경우에만 두리번거린다
			if (canSee)
			{
				Vector3 look = player.position - base.transform.position;
				look.y = 0f;
				if (look.sqrMagnitude > 0.01f)
				{
					base.transform.rotation = Quaternion.Slerp(base.transform.rotation,
						Quaternion.LookRotation(look), Time.deltaTime * 6f);
				}
			}
			else
			{
				base.transform.Rotate(Vector3.up, investigateTurnSpeed * 0.5f * Time.deltaTime);
			}
		}

		interceptTimer -= Time.deltaTime;
		if (interceptTimer <= 0f)
		{
			ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
		}
	}

	private void ProcessReturn(bool canSee)
	{
		if (canSee)
		{
			TryStartChase();
			return;
		}
		if (useGlobalNavMesh)
		{
			ChangeState(State.Patrol);
			return;
		}
		stateTimer += Time.deltaTime;
		if (zoneCenter != null && (Vector3.Distance(base.transform.position, zoneCenter.position) < 5f || stateTimer > 15f))
		{
			ChangeState(State.Patrol);
		}
	}

	private void PickNewDestination()
	{
		Vector3 destination = base.transform.position;
		if (useGlobalNavMesh)
		{
			if (isNavMeshDataLoaded && navMeshData.vertices.Length != 0)
			{
				int num = UnityEngine.Random.Range(0, navMeshData.vertices.Length);
				destination = navMeshData.vertices[num];
			}
			else
			{
				navMeshData = NavMesh.CalculateTriangulation();
				isNavMeshDataLoaded = true;
				destination = base.transform.position;
			}
		}
		else
		{
			Vector3 vector = ((zoneCenter != null) ? zoneCenter.position : base.transform.position);
			bool flag = false;
			for (int i = 0; i < 5; i++)
			{
				Vector2 vector2 = UnityEngine.Random.insideUnitCircle * zoneRadius;
				if (NavMesh.SamplePosition(vector + new Vector3(vector2.x, 0f, vector2.y), out var hit, 5f, -1))
				{
					destination = hit.position;
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				destination = vector;
			}
		}
		agent.SetDestination(destination);
		stateTimer = patrolWaitTime;
	}

	private void ChangeState(State newState)
	{
		if (currentState == newState)
		{
			return;
		}
		currentState = newState;
		if (newState != State.Investigate)
		{
			isRushing = false;
		}
		if (newState != State.Chase)
		{
			lostSight = false;
		}
		switch (newState)
		{
		case State.Intercept:
			agent.autoBraking = true;
			UpdateInterceptDestination();
			break;
		case State.Patrol:
			agent.autoBraking = false;
			PickNewDestination();
			break;
		case State.Chase:
			agent.autoBraking = true;
			break;
		case State.Investigate:
			agent.autoBraking = true;
			stateTimer = investigateLookTime;
			agent.SetDestination(lastKnownPos);
			break;
		case State.Return:
			stateTimer = 0f;
			if (zoneCenter != null)
			{
				agent.SetDestination(zoneCenter.position);
			}
			break;
		case State.Stun:
			break;
		}
	}

	private void StartChase()
	{
		if (currentState != State.Chase)
		{
			// 막 추격을 시작한 개체를 곧바로 차단으로 돌리지 않도록
			roleLockUntil = Time.time + roleLockTime;
		}
		givingUp = false;
		pendingNoise = false;
		ChangeState(State.Chase);
	}

	/// <summary>
	/// 추격 허가를 받고 들어간다. 동시에 쫓는 수가 가득 차 있으면 감독이 거부하고
	/// 곧바로 막는 역할을 준다 — 추격 상태를 거치지 않으므로 한 순간도 초과되지 않는다.
	/// </summary>
	private bool TryStartChase()
	{
		if (Time.time < homeLockUntil) { return false; }
		MonsterDirector director = MonsterDirector.Instance;
		if (director != null && !director.RequestChase(this))
		{
			return false;
		}
		StartChase();
		return true;
	}

	private bool CheckSight()
	{
		if (player == null || DebugPlayerInvisible)
		{
			return false;
		}
		Vector3 vector = base.transform.position + Vector3.up * 1.5f;
		Vector3 vector2 = player.position + Vector3.up * 1.5f;
		Vector3 to = vector2 - vector;
		// 바로 옆(2m 안)이면 방향·가림과 상관없이 보고 있는 것으로 친다.
		// 몸이 겹치면 각도 판정이 흔들려 "놓침"이 섞이고, 잡기 직전의 추격자가 끈질김을 소진해 포기했다(QA 2026-09-15)
		if (to.magnitude <= TouchSightRange)
		{
			return true;
		}
		if (to.magnitude > sightRange)
		{
			return false;
		}
		if (Vector3.Angle(base.transform.forward, to) > fovAngle * 0.5f)
		{
			return false;
		}
		if (Physics.Linecast(vector, vector2, out var hitInfo, obstacleMask) && hitInfo.collider.transform != player)
		{
			return false;
		}
		return true;
	}

	private void HandlePhysicsAndTimers()
	{
		if (damageTimer > 0f)
		{
			damageTimer -= Time.deltaTime;
		}
		if (currentState != State.Stun && !isJumping && agent.isOnOffMeshLink)
		{
			StartCoroutine(PerformJump());
		}
	}

	private IEnumerator PerformJump()
	{
		isJumping = true;
		agent.isStopped = true;
		OffMeshLinkData currentOffMeshLinkData = agent.currentOffMeshLinkData;
		Vector3 start = base.transform.position;
		Vector3 end = currentOffMeshLinkData.endPos + Vector3.up * agent.baseOffset;
		float time = 0f;
		float duration = Mathf.Max(0.05f, jumpDuration);
		while (time < duration)
		{
			time += Time.deltaTime;
			float num = time / duration;
			Vector3 position = Vector3.Lerp(start, end, num);
			position.y += Mathf.Sin(num * MathF.PI) * jumpHeight;
			base.transform.position = position;
			yield return null;
		}
		agent.CompleteOffMeshLink();
		agent.isStopped = false;
		isJumping = false;
	}

	private void OnCollisionEnter(Collision col)
	{
		if (!(damageTimer > 0f) && currentState != State.Stun && col.gameObject.CompareTag("Player"))
		{
			damageTimer = damageCooldown;
			agent.velocity = Vector3.zero;
			PlayerHealth component = col.gameObject.GetComponent<PlayerHealth>();
			if (component != null)
			{
				component.TakeDamage(base.transform.position, contactDamageHearts);
			}
		}
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		if (!useGlobalNavMesh && zoneCenter != null)
		{
			Gizmos.DrawWireSphere(zoneCenter.position, zoneRadius);
		}
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(base.transform.position, sightRange);

		// 청각은 두 역할 모두 표시한다 — 전역이 넓고 구역이 좁은 것이 눈에 보여야 한다
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(base.transform.position, EffectiveHearingRange);
	}
}
