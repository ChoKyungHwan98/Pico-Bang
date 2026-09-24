using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One monster's movement, sight, pursuit memory, return and stun FSM.
/// MonsterDirector jointly selects participants and compatible NavMesh routes.
/// Only the pressure pursuer follows live sightings; supports execute their assigned corridor.
/// Fresh sightings/shots refresh tracking; without evidence the hunt expires.
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

	public enum State
	{
		Patrol = 0,
		Chase = 1,
		Investigate = 2,
		Return = 3,
		Stun = 4,
		Intercept = 5,
		Prepare = 6,
		Idle = 7
	}

	/// <summary>AI 테스트 씬: 몬스터가 플레이어를 보지 못하게 한다.</summary>
	public static bool DebugPlayerInvisible;

	[Header("1. Identity & Role")]
	public MonsterRole role;

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

	[Header("4-3. 멈춤 (Idle)")]
	[Tooltip("할 수 있는 일이 없을 때(길이 없음, 발밑에 NavMesh 없음) 제자리에서 기다리다 이 주기(초)마다 다시 할 일을 찾는다")]
	public float idleRetryInterval = 3f;

	[Tooltip("멈춤 중 천천히 둘러보는 회전 속도(도/초). 멈춰 있어도 눈은 뜨고 있다")]
	public float idleLookSpeed = 45f;

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

	[Tooltip("최대 사냥 팀 크기(1~3). 압박 1 + 서로 다른 진입 경로가 있는 지원 최대 2")]
	public int huntTeamSize = 3;

	[Tooltip("마지막 목격 또는 사격 정보 이후 이 시간이 지나면 사냥 종료(초)")]
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

	[Tooltip("지원 경로의 최대 도착 시간(초). 초과하거나 추격 통로와 겹치면 지원에 뽑지 않는다")]
	public float detourTimeLimit = 8f;

	[Header("7. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	[Header("8. 차단 대기 (감독이 지시)")]

	[Tooltip("차단 명령이 이 시간 동안 갱신되지 않으면 포기하고 복귀한다(초)")]
	public float interceptTimeout = 12f;

	[Header("9. 해산 (기획 2026-09-24)")]
	[Tooltip("전역 몬스터 값만 사용. 추격자 뒤에서 차단·대기 몬스터가 이 거리(m) 안으로 줄지어 따라가면 '줄줄이'로 본다")]
	public float dispersalQueueDistance = 10f;

	[Tooltip("전역 몬스터 값만 사용. 줄줄이가 이 시간(초) 이어지면 감독이 해산 명령을 1회 내린다")]
	public float dispersalQueueSeconds = 1f;

	[Tooltip("추격↔차단 역할이 바뀐 직후 다시 바뀌지 않는 시간(초)")]

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
	private Transform generatedHome;
	private Transform originalZoneCenter;
	private float originalZoneRadius;
	public bool IsGuardingReturnEncounter { get; private set; }

	// 해산: 추격자가 곁에 있어 우회를 받은 개체, 우회에 실패해 집으로 가는 개체
	private bool dispersalDetour;
	private bool returningFromDispersal;
	public bool IsDispersing => dispersalDetour || returningFromDispersal;
	public bool IsDispersalDetour => dispersalDetour && currentState == State.Intercept;
	// 우회 끝 판정 중(도착해서 찾으면 추적) — 감독이 추격을 허가한다
	private bool finishingDetour;
	/// <summary>
	/// 팀 밖에서 직접 본 개체(순찰·수색·일반 복귀 중)이거나 우회 끝에서 찾은 개체 — 발견하면 추격한다(기획 2026-09-24).
	/// 해산·인원 초과로 집에 가는 중인 개체는 집에 닿을 때까지 제외 — 추격↔복귀가 매 프레임 뒤집히지 않게.
	/// </summary>
	public bool IsSightChaseCandidate => finishingDetour ||
		currentState == State.Patrol || currentState == State.Investigate || currentState == State.Idle ||
		(currentState == State.Return && !returningFromDispersal);
	public bool HasCloseVisibleEncounter => !isStunned && !isJumping && player != null &&
		Vector3.Distance(transform.position, player.position) <= 8f && CheckSight();

	public bool ReleaseReturnLockForEncounter()
	{
		if (currentState != State.Return || !HasCloseVisibleEncounter || agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
		homeLockUntil = 0f;
		givingUp = false;
		return true;
	}

	public void RefreshAssignedHome()
	{
		if (currentState == State.Patrol) { CommandGoHome(0f); return; }
		if (currentState == State.Return && agent != null && agent.enabled && agent.isOnNavMesh)
			agent.SetDestination(zoneCenter.position);
	}

	// 지점을 향해 달려가는 중인가. 수색 상태를 벗어나면 꺼진다.
	private bool isRushing;

	// 차단 대기
	private Vector3 interceptPoint;
	private Vector3 interceptWaypoint;      // 빙 돌아가기: 여기를 먼저 들른 뒤 차단 지점으로
	private bool hasWaypoint;
	private float interceptTimer;

	// 최종 접근: 경유지를 지났고, 이제 플레이어의 현재 위치를 계속 따라간다
	private bool finalApproach;

	private Vector3[] tacticalCorners;
	private int tacticalIndex;
	private Vector3 tacticalVia;
	private bool tacticalHasVia;
	private float nextRouteFailureReport;
	private NavMeshQueryFilter cachedNavigationFilter;
	private int navigationFilterFrame = -1;

	public bool IsHomeLocked => Time.time < homeLockUntil;
	public bool IsTraversingLink => isJumping;
	public int NavigationCostKey { get; private set; }
	public bool CanReceiveTactics => !isStunned && !isJumping && !IsHomeLocked && agent != null && agent.enabled && agent.isOnNavMesh;
	public NavMeshQueryFilter NavigationFilter
	{
		get
		{
			if (navigationFilterFrame == Time.frameCount) { return cachedNavigationFilter; }
			var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
			int costKey = 17;
			for (int i = 0; i < 32; i++)
			{
				float cost = agent.GetAreaCost(i);
				filter.SetAreaCost(i, cost);
				costKey = unchecked(costKey * 31 + cost.GetHashCode());
			}
			NavigationCostKey = costKey;
			cachedNavigationFilter = filter;
			navigationFilterFrame = Time.frameCount;
			return filter;
		}
	}

	private Collider myCollider;

	private Collider playerCollider;

	private bool playerPassThrough;
	private float noiseReactReadyAt;        // 이 시각 전에는 다시 멈칫하지 않는다

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

	// 멈춤: 길 찾기에 연달아 실패한 횟수. 이 횟수를 넘으면 멈춤으로 들어간다
	private const int MaxPathFailures = 3;
	private const float IdleSnapDistance = 2f;
	private int patrolFailures;
	private int returnFailures;

	// 소리 반응(멈칫)
	private bool pendingNoise;
	private float noiseReactUntil;
	private Vector3 noiseTarget;

	public bool IsInStun => currentState == State.Stun;

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

	public State CurrentState => currentState;

	public bool IsIntercepting => currentState == State.Intercept;

	/// <summary>경유지를 지나 플레이어에게 곧장 들어가는 중인가. 이 동안은 멈추지 않는다.</summary>
	public bool IsFinalApproach => finalApproach && currentState == State.Intercept;

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
			case State.Return: return IsGuardingReturnEncounter ? "복귀 중 근접 경계" : "복귀";
			case State.Stun: return "기절";
			case State.Intercept: return hasWaypoint ? "우회 이동" : (finalApproach ? "최종 접근" : "접근");
			case State.Prepare: return PreparationAtGoal ? "통로 경계" : "다음 통로 이동";
			case State.Idle: return "멈춤";
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

	private void OnDestroy()
	{
		if (generatedHome != null) { Destroy(generatedHome.gameObject); }
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
		if (zoneCenter == null || zoneCenter.IsChildOf(transform))
		{
			// Some existing scenes point the global monster's home at its own moving transform.
			Vector3 homePosition = zoneCenter != null ? zoneCenter.position : transform.position;
			GameObject gameObject = new GameObject(base.name + "_Home");
			gameObject.transform.position = homePosition;
			zoneCenter = gameObject.transform;
			generatedHome = zoneCenter;
		}
		originalZoneCenter = zoneCenter;
		originalZoneRadius = zoneRadius;
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
		if (originalZoneCenter != null) { zoneCenter = originalZoneCenter; zoneRadius = originalZoneRadius; }
		IsGuardingReturnEncounter = false;
		StopAllCoroutines();
		if (playerPassThrough) { SetPlayerPassThrough(false); }
		agent.updateRotation = true;
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
		damageTimer = 0f;
		huntSpeed = -1f;
		pendingNoise = false;
		hasWaypoint = false;
		finalApproach = false;
		tacticalCorners = null;
		tacticalHasVia = false;
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
		if (currentState == State.Chase || currentState == State.Intercept || currentState == State.Prepare || isStunned || givingUp || IsHomeLocked)
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

	/// <summary>The director alone chooses the pressure pursuer. Replans do not reset lost-sight persistence.</summary>
	public void CommandPressure(Vector3 knownPosition)
	{
		if (!CanReceiveTactics) { return; }
		tacticalCorners = null;
		hasWaypoint = false;
		finalApproach = false;
		if (currentState != State.Chase)
		{
			lastKnownPos = knownPosition;
			StartChase();
			agent.SetDestination(knownPosition);
		}
	}

	/// <summary>New evidence refreshes tracking. Repeating an old plan does not.</summary>
	public void RefreshPursuitEvidence(Vector3 position)
	{
		if (currentState != State.Chase) { return; }
		lastKnownPos = cornerPos = position;
		checkingCorner = false;
		if (lostSight)
		{
			pursuitTimer = Mathf.Max(pursuitTimer, MonsterDirector.Instance.PersistenceMin);
		}
	}

	/// <summary>Follow the assigned corridor corners. Never replace them with a live-player shortest path.</summary>
	public void CommandTacticalRoute(Vector3[] corners, Vector3 via, bool detour)
	{
		if (!CanReceiveTactics || corners == null || corners.Length < 2) { return; }
		pendingNoise = false;
		givingUp = false;
		checkingCorner = false;
		tacticalCorners = corners;
		tacticalIndex = 1;
		tacticalVia = via;
		tacticalHasVia = detour;
		interceptPoint = corners[corners.Length - 1];
		interceptTimer = Mathf.Max(2f, interceptTimeout);
		if (currentState != State.Intercept) { ChangeState(State.Intercept); }
		else { UpdateInterceptDestination(); }
	}

	/// <summary>해산 명령(기획 2026-09-24): 지정 위치로 이동한다. 도착해서 찾으면 추적, 못 찾으면 해산(복귀)한다.</summary>
	public void CommandDispersalDetour(Vector3[] corners, Vector3 via, bool detour)
	{
		returningFromDispersal = false;
		CommandTacticalRoute(corners, via, detour);
		if (currentState == State.Intercept) dispersalDetour = true;
	}

	public bool TryGetTacticalWaypoint(out Vector3 point)
	{
		point = tacticalVia;
		return currentState == State.Intercept && tacticalHasVia && Vector3.Distance(transform.position, tacticalVia) > 2f;
	}

	/// <summary>Walk to a reserved approach. No home lock, teleport, or unchecked pursuit fallback.</summary>
	public void CommandPreparation(Vector3[] corners)
	{
		if (!CanReceiveTactics || corners == null || corners.Length < 2) return;
		pendingNoise = givingUp = checkingCorner = false;
		tacticalCorners = corners; tacticalIndex = 1;
		tacticalHasVia = false;
		interceptPoint = corners[corners.Length - 1];
		if (currentState != State.Prepare) ChangeState(State.Prepare);
		else UpdateInterceptDestination();
		if (Vector3.SqrMagnitude(interceptPoint - transform.position) < .04f)
		{
			agent.ResetPath(); agent.isStopped = true; agent.velocity = Vector3.zero;
		}
	}

	public bool PreparationAtGoal => currentState == State.Prepare && !hasWaypoint && HasArrived();

	private void ProcessPreparation(bool canSee)
	{
		if (tacticalCorners == null) { CommandGoHome(0f); return; }
		if (hasWaypoint && Vector3.Distance(transform.position, interceptWaypoint) < 1.25f) UpdateInterceptDestination();
		// remainingDistance ends at the current corner, not the final preparation point.
		// Stopping within the arrival margin can strand us before a wall corner can be rounded.
		// 우회 2마리(기획 2026-09-24): 지정 위치에 닿으면 서서 기다리지 않는다 — 찾으면 추적, 못 찾으면 해산(복귀)
		if (!hasWaypoint && HasArrived())
		{
			FinishDispersalDetour(canSee);
			return;
		}
		if (canSee && HasCloseVisibleEncounter && Time.time >= nextRouteFailureReport)
		{
			nextRouteFailureReport = Time.time + .3f;
			MonsterDirector.Instance?.InvalidateRoute(this);
		}
	}

	private void UpdateInterceptDestination()
	{
		if (tacticalCorners == null || tacticalIndex >= tacticalCorners.Length) { return; }
		// Crossing a corner only counts when the next corner is directly traversable (no wall cutting).
		while (tacticalIndex < tacticalCorners.Length - 1 &&
			(Vector3.Distance(transform.position, tacticalCorners[tacticalIndex]) < .4f ||
			(Vector3.Distance(transform.position, tacticalCorners[tacticalIndex]) < 1.25f &&
			!NavMesh.Raycast(transform.position, tacticalCorners[tacticalIndex + 1], out _, NavigationFilter))))
		{
			tacticalIndex++;
		}
		if (tacticalHasVia && Vector3.Distance(transform.position, tacticalVia) < 2f) { tacticalHasVia = false; }
		hasWaypoint = tacticalIndex < tacticalCorners.Length - 1;
		finalApproach = !hasWaypoint;
		interceptWaypoint = tacticalCorners[tacticalIndex];
		agent.stoppingDistance = hasWaypoint ? .15f : stoppingDistance;
		agent.autoBraking = !hasWaypoint;
		agent.SetDestination(interceptWaypoint);
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
		tacticalCorners = null;
		ChangeState(State.Return);
		if (zoneCenter != null && agent.isOnNavMesh) { agent.SetDestination(zoneCenter.position); }
	}

	/// <summary>해제 명령: 사냥이 끝났다 — 차단·수색을 멈추고 구역으로 돌아간다. 직접 쫓는 중이면 무시.</summary>
	public void CommandRelease()
	{
		if (currentState == State.Intercept || currentState == State.Prepare || currentState == State.Investigate)
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
			if (agent.isOnNavMesh && !agent.isStopped)
			{
				agent.isStopped = true;
			}
			return;
		}
		// 발밑에 NavMesh가 없으면 어떤 상태도 길을 찾을 수 없다 — 멈춤으로 기다린다
		if (!agent.isOnNavMesh)
		{
			if (!isStunned && !isJumping)
			{
				if (currentState != State.Idle) { EnterIdle("off_navmesh"); }
				ProcessIdle(player != null && CheckSight());
			}
			return;
		}
		if (agent.isStopped && !isStunned && !isJumping)
		{
			agent.isStopped = false;
		}
		HandlePhysicsAndTimers();
		if (currentState == State.Stun || isJumping)
		{
			return;
		}

		bool canSee = player != null && CheckSight();

		switch (currentState)
		{
		case State.Patrol:
		case State.Idle:
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
		case State.Prepare:
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
		case State.Prepare:
			ProcessPreparation(canSee);
			break;
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
		case State.Idle:
			ProcessIdle(canSee);
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
		PlaytestRecorder.Record("monster_stunned", name, transform.position);
		pendingNoise = false;
		stateBeforeStun = currentState;
		ChangeState(State.Stun);
		MonsterDirector.Instance?.InvalidateRoute(this);
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
		ChangeState(State.Return);
		MonsterDirector.Instance?.InvalidateRoute(this);
		if (CheckSight())
		{
			MonsterDirector.Instance?.ReportSighting(this, player.position);
			TryStartChase();
		}
	}

	private void ProcessPatrol(bool canSee)
	{
		if (canSee && TryStartChase()) { return; }
		// 갈 수 있는 순찰 지점을 연달아 못 찾으면 매 프레임 길을 다시 묻지 않고 멈춤으로 기다린다
		if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete) { patrolFailures = 0; }
		if (patrolFailures >= MaxPathFailures) { EnterIdle("patrol_unreachable"); return; }
		if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			patrolFailures++;
			PickNewDestination();
			return;
		}
		if (agent.pathPending || (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)) { return; }
		stateTimer -= Time.deltaTime;
		if (patrolWaitTime <= 0f || stateTimer <= 0f) { PickNewDestination(); }
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
		else if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			// 수색 지점까지 길이 없다 — 도착을 영영 기다리지 않는다
			givingUp = false;
			EnterIdle("search_unreachable");
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
		if (dispersalDetour) { ProcessDispersalDetour(canSee); return; }
		if (canSee && !hasWaypoint && Time.time >= nextRouteFailureReport)
		{
			nextRouteFailureReport = Time.time + .25f;
			MonsterDirector.Instance?.RefreshCloseApproach(this);
		}
		interceptTimer -= Time.deltaTime;
		if (interceptTimer <= 0f || tacticalCorners == null)
		{
			CommandGoHome(2f);
			MonsterDirector.Instance?.InvalidateRoute(this);
			return;
		}
		if (!agent.pathPending && (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath))
		{
			if (Time.time >= nextRouteFailureReport)
			{
				nextRouteFailureReport = Time.time + .5f;
				MonsterDirector.Instance?.InvalidateRoute(this);
			}
		}
		if (hasWaypoint && Vector3.Distance(transform.position, interceptWaypoint) < 1.25f)
		{
			UpdateInterceptDestination();
		}
		// 우회 2마리(기획 2026-09-24): 지정 위치에 닿으면 찾으면 추적, 못 찾으면 해산(복귀)
		if (!hasWaypoint && HasArrived())
		{
			FinishDispersalDetour(canSee);
		}
	}

	/// <summary>
	/// 해산 우회(기획 2026-09-24): 우회 경로 끝(또는 제한 시간 초과)에서
	/// 눈으로 보면 추적, 못 보면 최대한 빨리 복귀한다.
	/// </summary>
	private void ProcessDispersalDetour(bool canSee)
	{
		if (tacticalCorners == null) { FinishDispersalDetour(false); return; }
		interceptTimer -= Time.deltaTime;
		if (hasWaypoint && Vector3.Distance(transform.position, interceptWaypoint) < 1.25f)
		{
			UpdateInterceptDestination();
		}
		bool arrived = !hasWaypoint && HasArrived();
		if (!arrived && interceptTimer > 0f) return;
		FinishDispersalDetour(canSee);
	}

	/// <summary>우회 끝(해산 이동·우회 2마리 공통): 눈으로 보면 추적, 못 찾으면 해산(복귀).</summary>
	private void FinishDispersalDetour(bool canSee)
	{
		finishingDetour = true;
		bool chased = canSee && TryStartChase();
		finishingDetour = false;
		if (chased)
		{
			PlaytestRecorder.Record("dispersal_found", name, transform.position, "chase");
			return;
		}
		PlaytestRecorder.Record("dispersal_return_home", name, transform.position, "not_found");
		CommandDispersalReturn();
	}

	/// <summary>해산 복귀: 최대한 빨리 집으로. 집에 닿아 순찰로 돌아갈 때까지 감독이 다시 끌어들이지 않는다.</summary>
	public void CommandDispersalReturn()
	{
		CommandGoHome(0f);
		if (currentState == State.Return) returningFromDispersal = true;
	}

	private void ProcessReturn(bool canSee)
	{
		IsGuardingReturnEncounter = false;
		if (canSee && HasCloseVisibleEncounter)
		{
			MonsterDirector.Instance?.ReportReturnEncounter(this);
			if (currentState != State.Return) return;
			// 발견하면 추격한다(기획 2026-09-24) — 3마리를 넘으면 감독이 가장 먼 개체를 복귀시킨다
			if (TryStartChase()) return;
			// Keep the encounter dangerous without becoming a second follower in the same corridor.
			IsGuardingReturnEncounter = true;
			agent.isStopped = true;
			agent.velocity = Vector3.zero;
			Vector3 direction = player.position - transform.position; direction.y = 0;
			if (direction.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(
				transform.rotation, Quaternion.LookRotation(direction), angularSpeed * Time.deltaTime);
			return;
		}
		if (canSee && TryStartChase()) { return; }
		// A denied chase must still allow home arrival, including for the global patrol monster.
		stateTimer += Time.deltaTime;
		if (zoneCenter != null && Vector3.Distance(transform.position, zoneCenter.position) < 3f)
		{
			ChangeState(State.Patrol);
		}
		else if (!agent.pathPending && (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath) && stateTimer > 1f)
		{
			stateTimer = 0f;
			// 집까지 길이 계속 없으면 1초마다 영원히 재시도하지 않고 멈춤으로 기다린다
			if (++returnFailures >= MaxPathFailures) { EnterIdle("home_unreachable"); return; }
			if (zoneCenter != null) { agent.SetDestination(zoneCenter.position); }
		}
	}

	/// <summary>
	/// 멈춤: 할 수 있는 일이 없을 때의 안전한 기본 상태.
	/// 길이 없거나 발밑에 NavMesh가 없을 때 들어온다. 제자리에서 천천히 둘러보며 눈은 뜨고 있고,
	/// 일정 주기마다 할 일(집으로 가기 → 순찰)을 다시 찾는다. 감독의 명령은 언제든 받는다.
	/// </summary>
	private void EnterIdle(string reason)
	{
		PlaytestRecorder.Record("monster_idle", name, transform.position, reason);
		if (showDebugLog)
		{
			Debug.Log($"<color=grey><b>[멈춤]</b></color> {base.name} — {reason}");
		}
		givingUp = false;
		pendingNoise = false;
		checkingCorner = false;
		ChangeState(State.Idle);
		stateTimer = Mathf.Max(0.5f, idleRetryInterval);
	}

	private void ProcessIdle(bool canSee)
	{
		if (canSee && agent.isOnNavMesh && TryStartChase()) { return; }
		base.transform.Rotate(Vector3.up, idleLookSpeed * Time.deltaTime);
		stateTimer -= Time.deltaTime;
		if (stateTimer > 0f) { return; }
		stateTimer = Mathf.Max(0.5f, idleRetryInterval);

		if (!agent.isOnNavMesh)
		{
			// 발밑 가까이에 길이 있으면 그 위로 올려놓는다. 멀면 계속 기다린다
			if (agent.enabled && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, IdleSnapDistance, NavMesh.AllAreas))
			{
				agent.Warp(hit.position);
			}
			return;
		}

		// 할 일 다시 찾기: 집이 멀고 갈 수 있으면 집으로, 아니면 순찰
		if (zoneCenter != null && Vector3.Distance(transform.position, zoneCenter.position) >= 3f)
		{
			var path = new NavMeshPath();
			if (NavMesh.CalculatePath(transform.position, zoneCenter.position, NavigationFilter, path) &&
				path.status == NavMeshPathStatus.PathComplete)
			{
				ChangeState(State.Return);
				return;
			}
		}
		ChangeState(State.Patrol);
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
		if (!agent.SetDestination(destination)) { patrolFailures++; }
		stateTimer = patrolWaitTime;
	}

	private void ChangeState(State newState)
	{
		if (currentState == newState)
		{
			return;
		}
		PlaytestRecorder.Record("monster_state", name, transform.position, currentState + " -> " + newState);
		currentState = newState;
		if (newState != State.Intercept && newState != State.Prepare)
		{
			tacticalCorners = null;
			tacticalHasVia = hasWaypoint = finalApproach = false;
			dispersalDetour = false;
			agent.stoppingDistance = stoppingDistance;
		}
		if (newState != State.Investigate)
		{
			isRushing = false;
		}
		if (newState != State.Chase)
		{
			lostSight = false;
		}
		if (newState != State.Return)
		{
			returningFromDispersal = false;
		}
		switch (newState)
		{
		case State.Prepare:
		case State.Intercept:
			agent.autoBraking = true;
			UpdateInterceptDestination();
			break;
		case State.Patrol:
			agent.autoBraking = false;
			patrolFailures = 0;
			PickNewDestination();
			break;
		case State.Idle:
			if (agent.isOnNavMesh) { agent.ResetPath(); }
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
			returnFailures = 0;
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
		// 바로 옆(2m 안)이면 방향과 상관없이 (벽 가림은 검사) 보고 있는 것으로 친다.
		// 몸이 겹치면 각도 판정이 흔들려 "놓침"이 섞이고, 잡기 직전의 추격자가 끈질김을 소진해 포기했다(QA 2026-09-15)
		if (to.magnitude <= TouchSightRange)
		{
			return !Physics.Linecast(vector, vector2, out var nearHit, obstacleMask, QueryTriggerInteraction.Ignore)
				|| nearHit.transform == player || nearHit.transform.IsChildOf(player);
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
