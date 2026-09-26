using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터의 몸(뇌 1). 눈·귀·이동·감전·점프와 상태 전환만 한다.
/// 누가 추격하고 누가 우회할지는 감독(<see cref="MonsterDirector"/>, 뇌 2)이 정한다.
/// 몸은 본 것·도착·놓침·감전을 감독에게 알리고, 감독이 준 명령(추격·우회·수색·복귀)을 수행한다.
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
		Flank = 5,
		Idle = 7
	}

	/// <summary>AI 테스트 씬: 몬스터가 플레이어를 보지 못하게 한다.</summary>
	public static bool DebugPlayerInvisible;

	public static List<MonsterAI> activeMonsters = new List<MonsterAI>();

	[Header("1. Identity & Role")]
	public MonsterRole role;

	public Transform player;

	[Header("2. Movement & Zone")]
	public bool useGlobalNavMesh;

	public Transform zoneCenter;

	public float zoneRadius = 35f;

	[Header("3. Speed Settings")]
	public float patrolSpeed = 3.5f;

	[Tooltip("사냥 중이 아닐 때 쓰는 추격 속도. 사냥 중에는 감독이 거리에 따라 정한다(6. 감독 — 사냥 속도)")]
	public float chaseSpeed = 9f;

	[Tooltip("수색 지점으로 걸어갈 때 속도")]
	public float investigateSpeed = 6f;

	public float patrolWaitTime;

	[Tooltip("구역으로 돌아갈 때 속도. 매우 빠르게 — 구역을 꽤 벗어나기 때문")]
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

	[Tooltip("구역 몬스터는 이 비율만큼만 듣는다")]
	[Range(0.05f, 1f)]
	public float zoneHearingScale = 0.45f;

	[Tooltip("수색 지점 도착 후 두리번거리는 시간(초)")]
	public float investigateLookTime = 3f;

	[Tooltip("제자리 수색 시 회전 속도(도/초)")]
	public float investigateTurnSpeed = 120f;

	[Header("4-2. 멈춤 (Idle)")]
	[Tooltip("할 수 있는 일이 없을 때(길이 없음, 발밑에 NavMesh 없음) 제자리에서 기다리다 이 주기(초)마다 다시 할 일을 찾는다")]
	public float idleRetryInterval = 3f;

	[Tooltip("멈춤 중 천천히 둘러보는 회전 속도(도/초). 멈춰 있어도 눈은 뜨고 있다")]
	public float idleLookSpeed = 45f;

	[Header("4-3. Debug")]
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

	[Tooltip("스턴이 끝났는데 플레이어와 몸이 겹쳐 있으면 충돌 복구를 이 시간까지 미룬다(초)")]
	public float stunRestoreMaxDelay = 3f;

	[Tooltip("전기 아크 연출을 켤지 여부")]
	public bool showStunSparks = true;

	[Tooltip("동시에 그릴 아크 개수")]
	[Range(1, 12)]
	public int stunArcCount = 5;

	[Tooltip("아크가 튀는 폭. 몸집에 맞춰 조절")]
	public float stunArcJaggedness = 0.16f;

	[Header("6. 감독(Director) — 전역 몬스터의 값만 사용됨")]
	[Tooltip("사냥 인원(추격 1 + 우회). 1~3")]
	public int huntTeamSize = 3;

	[Tooltip("마지막으로 보거나 들은 뒤 이 시간(초)이 지나면 사냥 끝")]
	public float huntMemory = 8f;

	[Tooltip("끈질김: 시야를 처음 놓쳤을 때 동료가 아는 위치로 계속 쫓는 시간(초)")]
	public float pursuitPersistence = 3f;

	[Tooltip("끈질김이 끝나도 마지막으로 본 자리(모퉁이)까지는 가 본다. 그 확인에 쓰는 최대 시간(초)")]
	public float cornerCheckTime = 4f;

	[Tooltip("끈질김: 놓칠 때마다 다음 끈질김에 곱하는 비율. 사냥이 끝나면 처음 값으로 돌아온다")]
	[Range(0.1f, 1f)]
	public float persistenceDecay = 0.7f;

	[Tooltip("끈질김: 줄어들어도 이 아래로는 내려가지 않는다(초)")]
	public float persistenceMin = 2f;

	[Tooltip("끈질김이 다 떨어졌을 때 그 자리에서 두리번거리는 시간(초)")]
	public float giveUpLookTime = 2f;

	[Tooltip("사냥 속도: 플레이어와 가까울 때(플레이어 달리기 11보다 느리게 — 똑바로 달리면 떨칠 수 있다)")]
	public float huntNearSpeed = 10f;

	[Tooltip("사냥 속도: 플레이어와 멀 때(먼 몬스터는 금방 따라붙는다)")]
	public float huntFarSpeed = 14f;

	[Tooltip("사냥 속도: 이 거리(m) 안이면 가까운 속도")]
	public float huntNearDistance = 15f;

	[Tooltip("사냥 속도: 이 거리(m) 밖이면 먼 속도. 사이는 부드럽게 바뀐다")]
	public float huntFarDistance = 40f;

	[Header("6-1. 감독 — 우회(포위)")]
	[Tooltip("우회 목표: 플레이어가 이 시간(초) 뒤에 있을 곳을 예상해 그 둘레에 목표를 잡는다")]
	public float flankLeadTime = 2f;

	[Tooltip("우회 목표: 예상 위치에서 이 거리(m) 떨어진 옆·앞 지점")]
	public float flankRadius = 16f;

	[Tooltip("우회 몬스터로 부를 수 있는 최대 거리(m). 이보다 먼 몬스터는 자기 구역을 지킨다 — 5마리가 맵 곳곳에 있어야 여러 마리처럼 보인다")]
	public float flankRecruitRange = 90f;

	[Tooltip("우회 몬스터가 이 거리(m) 안에서 플레이어를 보면 협공 추격으로 바뀐다")]
	public float flankEngageDistance = 18f;

	[Tooltip("우회 지점에 도착했는데 안 보이면 이 시간(초) 둘러본 뒤 감독에게 다음 지점을 받는다")]
	public float flankSearchTime = 1.5f;

	[Tooltip("우회에서 연달아 못 찾으면 이 횟수 뒤 구역으로 돌아간다")]
	public int flankMaxMisses = 3;

	[Tooltip("우회 한 번의 최대 시간(초). 넘기면 도착한 것으로 친다")]
	public float flankTimeout = 10f;

	[Tooltip("교대 순환: 우회를 이 시간(초) 넘게 했는데 협공에 못 들어갔으면 빈 구역으로 돌려보내고 다른 몬스터로 바꾼다. 0이면 끔")]
	public float flankRotateSeconds = 15f;

	[Header("6-2. 감독 — 줄줄이 방지")]
	[Tooltip("추격자 뒤 이 거리(m) 안에서 같은 쪽으로 따라오면 '줄줄이'")]
	public float queueDistance = 10f;

	[Tooltip("줄줄이가 이 시간(초) 이어지면 뒤쪽 몬스터를 반대쪽 우회로 돌린다")]
	public float queueSeconds = 2.5f;

	[Tooltip("인원 초과로 구역에 돌아가는 몬스터는 이 시간(초) 동안 멀리 보이는 플레이어를 무시한다(8m 안은 예외)")]
	public float benchSeconds = 5f;

	[Tooltip("협공 추격자가 플레이어에게 다가가는 최소 거리(m). 여기서 멈추고 추격자와 겹치지 않는 옆쪽으로 벌어져 에워싼다 — 한 덩어리로 붙지 않게")]
	public float assistSpacing = 3.2f;

	[Header("7. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	private const float SightFlickerGrace = 0.5f;
	private const float TouchSightRange = 2f;
	private const int MaxPathFailures = 3;
	private const float IdleSnapDistance = 2f;

	private NavMeshAgent agent;
	private Animator animator;
	private Rigidbody rb;
	private Collider myCollider;
	private Collider playerCollider;
	private State currentState;
	private State stateBeforeStun;
	private static NavMeshTriangulation navMeshData;
	private static bool isNavMeshDataLoaded;
	private float stateTimer;
	private float damageTimer;
	private bool isJumping;
	private bool isStunned;
	private bool playerPassThrough;
	private Vector3 startPosition;
	private Quaternion startRotation;
	private Transform generatedHome;
	private Transform originalZoneCenter;
	private float originalZoneRadius;
	private int patrolFailures;
	private int returnFailures;

	// 추격: 마지막으로 본 자리, 끈질김
	private Vector3 lastKnownPos;
	private float huntSpeed = -1f;
	private float pursuitBudget = 3f;
	private float pursuitTimer;
	private bool lostSight;
	private float lostSightAt;
	private float budgetBeforeLoss;
	private Vector3 cornerPos;
	private float cornerTimer;
	private bool checkingCorner;
	private bool givingUp;

	// 우회: 감독이 준 길을 꼭짓점 순서로 따라간다
	private Vector3[] flankCorners;
	private int flankIndex;
	private Vector3 flankGoal;
	private float flankTimer;

	// 수색이 끝나면 감독에게 무엇을 알릴지
	private enum SearchReason { None, Noise, FlankMiss, GiveUp }
	private SearchReason searchReason;

	public State CurrentState => currentState;
	public bool IsInStun => currentState == State.Stun;
	public bool IsTraversingLink => isJumping;
	public bool IsGivingUp => givingUp;
	public bool IsFlanking => currentState == State.Flank;
	public bool IsSeeingPlayer { get; private set; }
	public float SeenPlayerAt { get; private set; } = float.NegativeInfinity;
	public bool CanTakeOrders => !isStunned && !isJumping && agent != null && agent.enabled && agent.isOnNavMesh;
	public Vector3 FlankGoal => flankGoal;
	public Vector3[] DebugFlankRoute => currentState == State.Flank ? flankCorners : null;
	public Vector3 DebugDestination =>
		(agent != null && agent.isOnNavMesh && agent.hasPath) ? agent.destination : transform.position;
	public float EffectiveHearingRange => role == MonsterRole.Global_Stalker ? hearingRange : hearingRange * zoneHearingScale;
	public Transform OriginalZone => originalZoneCenter;
	public float OriginalZoneRadius => originalZoneRadius;

	public Vector3 PlanarVelocity
	{
		get
		{
			if (agent == null) return Vector3.zero;
			Vector3 v = agent.velocity;
			v.y = 0f;
			return v;
		}
	}

	/// <summary>NavMesh 경로 계산용 필터(이 몬스터의 에이전트 종류·지역 비용).</summary>
	public NavMeshQueryFilter NavigationFilter
	{
		get
		{
			var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
			for (int i = 0; i < 32; i++) filter.SetAreaCost(i, agent.GetAreaCost(i));
			return filter;
		}
	}

	/// <summary>화면·기록용 상태 이름.</summary>
	public string StateLabel
	{
		get
		{
			switch (currentState)
			{
			case State.Patrol: return "순찰";
			case State.Chase:
				if (checkingCorner) return "모퉁이 확인";
				return lostSight ? $"추적 {Mathf.Max(0f, pursuitTimer):F1}초" : "추격";
			case State.Investigate:
				if (givingUp) return "놓침";
				if (searchReason == SearchReason.FlankMiss) return "우회 끝 둘러봄";
				return HasArrived() ? "수색" : "수색 이동";
			case State.Return: return "복귀";
			case State.Stun: return "감전";
			case State.Flank: return "우회";
			case State.Idle: return "멈춤";
			}
			return currentState.ToString();
		}
	}

	private MonsterDirector Director => MonsterDirector.Instance;

	private void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
		animator = GetComponent<Animator>();
		rb = GetComponent<Rigidbody>();
		myCollider = GetComponent<Collider>();
		startPosition = transform.position;
		startRotation = transform.rotation;
	}

	private void OnEnable() { activeMonsters.Add(this); }

	private void OnDisable()
	{
		activeMonsters.Remove(this);
		if (playerPassThrough) SetPlayerPassThrough(false);
	}

	private void OnDestroy()
	{
		if (generatedHome != null) Destroy(generatedHome.gameObject);
	}

	private void Start()
	{
		if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
		if (useGlobalNavMesh && !isNavMeshDataLoaded)
		{
			navMeshData = NavMesh.CalculateTriangulation();
			isNavMeshDataLoaded = true;
		}
		if (zoneCenter == null || zoneCenter.IsChildOf(transform))
		{
			// 일부 씬은 전역 몬스터의 집을 자기 자신(움직이는 transform)으로 가리킨다 — 고정된 집을 만든다
			Vector3 homePosition = zoneCenter != null ? zoneCenter.position : transform.position;
			var home = new GameObject(name + "_Home");
			home.transform.position = homePosition;
			zoneCenter = home.transform;
			generatedHome = zoneCenter;
		}
		originalZoneCenter = zoneCenter;
		originalZoneRadius = zoneRadius;
		agent.acceleration = patrolAcceleration;
		agent.angularSpeed = angularSpeed;
		agent.stoppingDistance = stoppingDistance;
		// Patrol은 enum 기본값이라 ChangeState(Patrol)가 조기 반환한다 — 첫 목적지를 직접 잡는다
		currentState = State.Patrol;
		agent.autoBraking = false;
		PickNewDestination();
		_ = MonsterDirector.Instance;
		ResetPursuit();
	}

	public void ResetMonster()
	{
		if (originalZoneCenter != null) { zoneCenter = originalZoneCenter; zoneRadius = originalZoneRadius; }
		StopAllCoroutines();
		if (playerPassThrough) SetPlayerPassThrough(false);
		agent.updateRotation = true;
		enabled = true;
		if (agent.isOnNavMesh) agent.isStopped = true;
		agent.velocity = Vector3.zero;
		agent.Warp(startPosition);
		transform.rotation = startRotation;
		currentState = State.Patrol;
		isStunned = isJumping = false;
		damageTimer = 0f;
		huntSpeed = -1f;
		checkingCorner = givingUp = lostSight = false;
		flankCorners = null;
		searchReason = SearchReason.None;
		IsSeeingPlayer = false;
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

	/// <summary>끈질김을 처음 값으로 되돌린다(사냥이 끝날 때).</summary>
	public void ResetPursuit()
	{
		pursuitBudget = Director != null ? Director.PursuitPersistence : pursuitPersistence;
	}

	/// <summary>감독이 정한 사냥 속도. 음수면 해제(chaseSpeed 사용).</summary>
	public void SetHuntSpeed(float speed) { huntSpeed = speed; }

	private float HuntMoveSpeed => huntSpeed > 0f ? huntSpeed : chaseSpeed;
	private float TrackingCap => Director != null ? Director.TrackingSpeedCap : huntNearSpeed;

	// ────────────────────────────────────────────────
	//  감독의 명령
	// ────────────────────────────────────────────────

	/// <summary>직접 보고 있는 몬스터만 새 추격을 시작한다.</summary>
	public void CommandChase(Vector3 known)
	{
		if (!CanTakeOrders || !IsSeeingPlayer) return;
		if (currentState == State.Chase) return;
		lastKnownPos = cornerPos = known;
		givingUp = checkingCorner = false;
		ChangeState(State.Chase);
		agent.SetDestination(known);
	}

	/// <summary>우회: 감독이 준 길(꼭짓점)을 따라 목표로 간다. 같은 목표를 다시 받으면 이어서 간다.</summary>
	public void CommandFlank(Vector3[] corners)
	{
		if (!CanTakeOrders || corners == null || corners.Length < 2) return;
		Vector3 goal = corners[corners.Length - 1];
		bool sameGoal = currentState == State.Flank && Vector3.Distance(goal, flankGoal) < 1f;
		flankCorners = corners;
		flankGoal = goal;
		if (!sameGoal)
		{
			flankIndex = 1;
			flankTimer = Director != null ? Director.FlankTimeout : 10f;
		}
		givingUp = checkingCorner = false;
		searchReason = SearchReason.None;
		if (currentState != State.Flank) ChangeState(State.Flank);
		else UpdateFlankDestination();
	}

	/// <summary>수색: 지점으로 가서 둘러본다(소리를 들었을 때).</summary>
	public void CommandSearch(Vector3 point)
	{
		if (!CanTakeOrders || currentState == State.Chase) return;
		lastKnownPos = point;
		givingUp = false;
		searchReason = SearchReason.Noise;
		if (currentState == State.Investigate)
		{
			stateTimer = investigateLookTime;
			agent.SetDestination(point);
		}
		else ChangeState(State.Investigate);
	}

	/// <summary>복귀: 감독이 정한 구역으로 돌아간다(가장 가까운 빈 구역).</summary>
	public void CommandReturn(Transform home, float radius)
	{
		if (isStunned) return;
		if (home != null) { zoneCenter = home; zoneRadius = radius; }
		givingUp = checkingCorner = false;
		if (currentState == State.Return)
		{
			if (agent.isOnNavMesh && zoneCenter != null) agent.SetDestination(zoneCenter.position);
			return;
		}
		ChangeState(State.Return);
	}

	/// <summary>사냥과 상관없이 순찰로 돌아간다(구역 안에 있을 때).</summary>
	public void CommandPatrol()
	{
		if (isStunned) return;
		givingUp = checkingCorner = false;
		ChangeState(State.Patrol);
	}

	// ────────────────────────────────────────────────

	private void Update()
	{
		if (agent == null) return;
		if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning)
		{
			if (agent.isOnNavMesh && !agent.isStopped) agent.isStopped = true;
			return;
		}
		// 발밑에 NavMesh가 없으면 어떤 상태도 길을 찾을 수 없다 — 멈춤으로 기다린다
		if (!agent.isOnNavMesh)
		{
			if (!isStunned && !isJumping)
			{
				if (currentState != State.Idle) EnterIdle("off_navmesh");
				ProcessIdle(false);
			}
			return;
		}
		if (agent.isStopped && !isStunned && !isJumping) agent.isStopped = false;
		if (damageTimer > 0f) damageTimer -= Time.deltaTime;
		if (currentState != State.Stun && !isJumping && agent.isOnOffMeshLink) StartCoroutine(PerformJump());
		if (currentState == State.Stun || isJumping) return;

		bool canSee = CheckSight();
		IsSeeingPlayer = canSee;
		if (canSee)
		{
			SeenPlayerAt = Time.time;
			Director?.ReportSighting(this, player.position);
			// 감독이 이 보고로 역할을 바꿨을 수 있다(합류·협공)
		}

		ApplySpeed();
		switch (currentState)
		{
		case State.Patrol: ProcessPatrol(canSee); break;
		case State.Chase: ProcessChase(canSee); break;
		case State.Flank: ProcessFlank(canSee); break;
		case State.Investigate: ProcessInvestigate(canSee); break;
		case State.Return: ProcessReturn(canSee); break;
		case State.Idle: ProcessIdle(canSee); break;
		}
	}

	private void ApplySpeed()
	{
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
			// 보고 있으면 거리에 따른 사냥 속도, 못 본 채 쫓을 때는 플레이어보다 느리게 — 똑바로 달리면 떨칠 수 있다
			agent.speed = lostSight ? Mathf.Min(HuntMoveSpeed, TrackingCap) : HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Flank:
			agent.speed = HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Investigate:
			// 소리 확인은 빠른 걸음 — 플레이어(11)보다 느려야 계속 쏘며 뛰는 플레이어가 소리만으로 잡히지 않는다
			agent.speed = searchReason == SearchReason.Noise ? Mathf.Clamp(investigateSpeed + 1f, investigateSpeed, TrackingCap - 1f) : investigateSpeed;
			agent.acceleration = investigateAcceleration;
			break;
		}
	}

	/// <summary>눈에 띈 플레이어를 쫓을지 감독에게 묻는다. 허락하면 추격에 들어간다.</summary>
	private bool TryJoinChase()
	{
		if (Director != null && !Director.RequestJoin(this)) return false;
		if (currentState != State.Chase)
		{
			lastKnownPos = player.position;
			givingUp = checkingCorner = false;
			ChangeState(State.Chase);
		}
		return true;
	}

	private void ProcessPatrol(bool canSee)
	{
		if (canSee && TryJoinChase()) return;
		// 갈 수 있는 순찰 지점을 연달아 못 찾으면 매 프레임 길을 다시 묻지 않고 멈춤으로 기다린다
		if (agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathComplete) patrolFailures = 0;
		if (patrolFailures >= MaxPathFailures) { EnterIdle("patrol_unreachable"); return; }
		if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			patrolFailures++;
			PickNewDestination();
			return;
		}
		if (agent.pathPending || (agent.hasPath && agent.remainingDistance > agent.stoppingDistance)) return;
		stateTimer -= Time.deltaTime;
		if (patrolWaitTime <= 0f || stateTimer <= 0f) PickNewDestination();
	}

	/// <summary>
	/// 추격. 보이면 정확한 위치로, 놓치면 자신이 마지막으로 본 자리까지만 간다.
	/// 총소리나 다른 몬스터의 제보는 감독의 배치에 쓰이지만 이 몬스터의 추격 목적지를 바꾸지 않는다.
	/// </summary>
	private void ProcessChase(bool canSee)
	{
		if (canSee)
		{
			// 한순간 깜빡 끊겼다 다시 본 것은 '놓침'으로 치지 않는다
			if (lostSight && Time.time - lostSightAt < SightFlickerGrace) pursuitBudget = budgetBeforeLoss;
			lostSight = false;
			checkingCorner = false;
			lastKnownPos = player.position;
			// 협공이면 추격자와 겹치지 않는 옆자리로 — 추격자만 몸으로 부딪힌다
			agent.SetDestination(Director != null && Director.TryGetSpacingSlot(this, out Vector3 slot) ? slot : player.position);
			return;
		}
		if (!lostSight) BeginLostSight();

		pursuitTimer -= Time.deltaTime;
		if (pursuitTimer <= 0f)
		{
			// 끈질김이 끝나도 마지막으로 본 자리까지는 가 본다
			cornerTimer -= Time.deltaTime;
			if (Vector3.Distance(transform.position, cornerPos) <= 3f || cornerTimer <= 0f)
			{
				GiveUpChase();
				return;
			}
			checkingCorner = true;
			agent.SetDestination(cornerPos);
			return;
		}
		agent.SetDestination(cornerPos);
	}

	private void BeginLostSight()
	{
		lostSight = true;
		lostSightAt = Time.time;
		budgetBeforeLoss = pursuitBudget;
		pursuitTimer = pursuitBudget;
		cornerPos = lastKnownPos;
		cornerTimer = cornerCheckTime;
		checkingCorner = false;
		float decay = Director != null ? Director.PersistenceDecay : persistenceDecay;
		float min = Director != null ? Director.PersistenceMin : persistenceMin;
		pursuitBudget = Mathf.Max(min, pursuitBudget * decay);
	}

	private void GiveUpChase()
	{
		lostSight = false;
		givingUp = true;
		lastKnownPos = transform.position;
		searchReason = SearchReason.GiveUp;
		ChangeState(State.Investigate);
		stateTimer = Director != null ? Director.GiveUpLookTime : giveUpLookTime;
		Director?.ReportLostPlayer(this);
		if (showDebugLog) Debug.Log($"<color=grey><b>[추격 포기]</b></color> {name} — 끈질김이 다 떨어짐 (다음 끈질김 {pursuitBudget:F1}초)");
	}

	/// <summary>
	/// 우회. 감독이 준 길로 목표까지 간다. 서서 기다리지 않는다.
	/// 가까이서 플레이어를 보면 감독에게 협공을 청하고, 도착해서 안 보이면 잠깐 둘러본 뒤 다음 지점을 받는다.
	/// </summary>
	private void ProcessFlank(bool canSee)
	{
		if (flankCorners == null) { FinishFlank(canSee); return; }
		if (canSee && Director != null && Director.RequestEngage(this))
		{
			lastKnownPos = player.position;
			ChangeState(State.Chase);
			return;
		}
		flankTimer -= Time.deltaTime;
		if (flankIndex < flankCorners.Length - 1 && Vector3.Distance(transform.position, flankCorners[flankIndex]) < 1.25f)
			UpdateFlankDestination();
		if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			Director?.ReportFlankBlocked(this);
			return;
		}
		bool arrived = flankIndex >= flankCorners.Length - 1 && HasArrived();
		if (arrived || flankTimer <= 0f) FinishFlank(canSee);
	}

	private void FinishFlank(bool canSee)
	{
		if (canSee && Director != null && Director.RequestEngage(this, true))
		{
			lastKnownPos = player.position;
			ChangeState(State.Chase);
			return;
		}
		// 도착 지점을 짧게 둘러본다 — 그 사이 보이면 합류
		lastKnownPos = transform.position;
		searchReason = SearchReason.FlankMiss;
		ChangeState(State.Investigate);
		stateTimer = Director != null ? Director.FlankSearchTime : flankSearchTime;
	}

	private void UpdateFlankDestination()
	{
		if (flankCorners == null || flankIndex >= flankCorners.Length) return;
		// 꼭짓점은 다음 꼭짓점이 벽 없이 보일 때만 지난 것으로 친다(벽 모서리를 깎지 않게)
		while (flankIndex < flankCorners.Length - 1 &&
			(Vector3.Distance(transform.position, flankCorners[flankIndex]) < .4f ||
			(Vector3.Distance(transform.position, flankCorners[flankIndex]) < 1.25f &&
			!NavMesh.Raycast(transform.position, flankCorners[flankIndex + 1], out _, NavigationFilter))))
		{
			flankIndex++;
		}
		bool last = flankIndex >= flankCorners.Length - 1;
		agent.stoppingDistance = last ? stoppingDistance : .15f;
		agent.autoBraking = last;
		agent.SetDestination(flankCorners[flankIndex]);
	}

	private void ProcessInvestigate(bool canSee)
	{
		if (canSee)
		{
			bool joined = searchReason == SearchReason.FlankMiss
				? Director != null && Director.RequestEngage(this, true) && JoinFromSearch()
				: TryJoinChase();
			if (joined) return;
		}
		if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			// 수색 지점까지 길이 없다 — 도착을 영영 기다리지 않는다
			FinishSearch();
			return;
		}
		if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + .5f) return;
		stateTimer -= Time.deltaTime;
		transform.Rotate(Vector3.up, investigateTurnSpeed * Time.deltaTime);
		if (stateTimer <= 0f) FinishSearch();
	}

	private bool JoinFromSearch()
	{
		lastKnownPos = player.position;
		ChangeState(State.Chase);
		return true;
	}

	private void FinishSearch()
	{
		var reason = searchReason;
		searchReason = SearchReason.None;
		givingUp = false;
		if (reason == SearchReason.FlankMiss && Director != null) { Director.ReportFlankMissed(this); return; }
		if (Director != null) { Director.ReportSearchDone(this); return; }
		ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
	}

	private void ProcessReturn(bool canSee)
	{
		if (canSee && TryJoinChase()) return;
		if (zoneCenter != null && Vector3.Distance(transform.position, zoneCenter.position) < 3f)
		{
			ChangeState(State.Patrol);
			return;
		}
		stateTimer += Time.deltaTime;
		if (!agent.pathPending && (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath) && stateTimer > 1f)
		{
			stateTimer = 0f;
			// 집까지 길이 계속 없으면 영원히 재시도하지 않고 멈춤으로 기다린다
			if (++returnFailures >= MaxPathFailures) { EnterIdle("home_unreachable"); return; }
			if (zoneCenter != null) agent.SetDestination(zoneCenter.position);
		}
	}

	/// <summary>
	/// 멈춤: 할 수 있는 일이 없을 때의 안전한 기본 상태. 제자리에서 천천히 둘러보며 눈은 뜨고 있고,
	/// 일정 주기마다 할 일(집으로 가기 → 순찰)을 다시 찾는다. 감독의 명령은 언제든 받는다.
	/// </summary>
	private void EnterIdle(string reason)
	{
		PlaytestRecorder.Record("monster_idle", name, transform.position, reason);
		if (showDebugLog) Debug.Log($"<color=grey><b>[멈춤]</b></color> {name} — {reason}");
		givingUp = checkingCorner = false;
		ChangeState(State.Idle);
		stateTimer = Mathf.Max(0.5f, idleRetryInterval);
		Director?.ReportIdle(this);
	}

	private void ProcessIdle(bool canSee)
	{
		if (canSee && agent.isOnNavMesh && TryJoinChase()) return;
		transform.Rotate(Vector3.up, idleLookSpeed * Time.deltaTime);
		stateTimer -= Time.deltaTime;
		if (stateTimer > 0f) return;
		stateTimer = Mathf.Max(0.5f, idleRetryInterval);
		if (!agent.isOnNavMesh)
		{
			// 발밑 가까이에 길이 있으면 그 위로 올려놓는다. 멀면 계속 기다린다
			if (agent.enabled && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, IdleSnapDistance, NavMesh.AllAreas))
				agent.Warp(hit.position);
			return;
		}
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

	// ────────────────────────────────────────────────
	//  감전
	// ────────────────────────────────────────────────

	public void OnHitByLaser(Vector3 shooterPosition)
	{
		if (!isStunned) StartCoroutine(ProcessStunReaction(shooterPosition));
	}

	/// <summary>플레이어와의 충돌만 켜고 끈다. 지형·동료 충돌은 그대로다.</summary>
	private void SetPlayerPassThrough(bool on)
	{
		if (!stunLetPlayerPass) return;
		if (myCollider == null) myCollider = GetComponent<Collider>();
		if (playerCollider == null && player != null) playerCollider = player.GetComponent<Collider>();
		if (myCollider == null || playerCollider == null) return;
		if (!myCollider.enabled || !playerCollider.enabled) return;
		Physics.IgnoreCollision(myCollider, playerCollider, on);
		playerPassThrough = on;
	}

	private bool OverlapsPlayer()
	{
		if (myCollider == null || playerCollider == null) return false;
		if (!myCollider.enabled || !playerCollider.enabled) return false;
		return Physics.ComputePenetration(
			myCollider, myCollider.transform.position, myCollider.transform.rotation,
			playerCollider, playerCollider.transform.position, playerCollider.transform.rotation,
			out Vector3 _, out float _);
	}

	/// <summary>몸이 겹친 채 충돌을 되살리면 서로 튕겨나간다 — 떨어질 때까지(상한 있음) 미룬다.</summary>
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
		IsSeeingPlayer = false;
		PlaytestRecorder.Record("monster_stunned", name, transform.position);
		stateBeforeStun = currentState;
		ChangeState(State.Stun);
		Director?.ReportStunned(this);
		agent.isStopped = true;
		agent.velocity = Vector3.zero;
		agent.updateRotation = false;
		// 좌클릭으로 만든 틈 — 굳어 있는 동안 플레이어만 몸을 통과한다
		SetPlayerPassThrough(true);
		if (animator != null) animator.SetTrigger("Hit");
		if (showStunSparks) StunSparkEffect.Play(transform, stunFreezeTime, arcCount: stunArcCount, jagged: stunArcJaggedness);

		yield return new WaitForSeconds(stunFreezeTime);
		if (stateBeforeStun == State.Patrol || stateBeforeStun == State.Return || stateBeforeStun == State.Investigate)
		{
			// 몰랐던 몬스터는 맞은 쪽을 돌아본다
			Vector3 look = shooterPosition - transform.position;
			look.y = 0f;
			if (look != Vector3.zero) transform.rotation = Quaternion.LookRotation(look);
		}
		yield return new WaitForSeconds(stunRecoverTime);
		agent.updateRotation = true;
		agent.isStopped = false;
		isStunned = false;
		// 충돌 복구만 따로 기다린다. AI는 곧바로 깨어난다
		if (playerPassThrough) StartCoroutine(RestoreCollisionWhenClear());
		currentState = State.Investigate;   // 감독이 곧바로 다음 일을 정한다(보이면 합류, 아니면 복귀)
		searchReason = SearchReason.None;
		PlaytestRecorder.Record("monster_state", name, transform.position, "Stun -> Recovered");
		bool sees = CheckSight();
		IsSeeingPlayer = sees;
		if (Director != null) Director.ReportRecovered(this, sees);
		else ChangeState(State.Return);
	}

	// ────────────────────────────────────────────────

	private bool IsOutsideZone()
	{
		return !useGlobalNavMesh && role == MonsterRole.Zone_Defender && zoneCenter != null
			&& Vector3.Distance(transform.position, zoneCenter.position) > zoneRadius;
	}

	private bool HasArrived()
	{
		return agent != null && agent.isOnNavMesh && !agent.pathPending
			&& agent.remainingDistance <= agent.stoppingDistance + 0.5f;
	}

	private void PickNewDestination()
	{
		Vector3 destination = transform.position;
		if (useGlobalNavMesh)
		{
			if (!isNavMeshDataLoaded || navMeshData.vertices == null || navMeshData.vertices.Length == 0)
			{
				navMeshData = NavMesh.CalculateTriangulation();
				isNavMeshDataLoaded = true;
			}
			if (navMeshData.vertices.Length != 0)
				destination = navMeshData.vertices[UnityEngine.Random.Range(0, navMeshData.vertices.Length)];
		}
		else
		{
			Vector3 center = zoneCenter != null ? zoneCenter.position : transform.position;
			destination = center;
			for (int i = 0; i < 5; i++)
			{
				Vector2 offset = UnityEngine.Random.insideUnitCircle * zoneRadius;
				if (NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y), out var hit, 5f, NavMesh.AllAreas))
				{
					destination = hit.position;
					break;
				}
			}
		}
		if (!agent.SetDestination(destination)) patrolFailures++;
		stateTimer = patrolWaitTime;
	}

	private void ChangeState(State newState)
	{
		if (currentState == newState) return;
		PlaytestRecorder.Record("monster_state", name, transform.position, currentState + " -> " + newState);
		currentState = newState;
		if (newState != State.Flank)
		{
			flankCorners = null;
			agent.stoppingDistance = stoppingDistance;
		}
		if (newState != State.Chase) lostSight = false;
		if (newState != State.Investigate && newState != State.Chase) givingUp = false;
		if (newState != State.Investigate && newState != State.Stun) searchReason = SearchReason.None;
		switch (newState)
		{
		case State.Flank:
			agent.autoBraking = false;
			UpdateFlankDestination();
			break;
		case State.Patrol:
			agent.autoBraking = false;
			patrolFailures = 0;
			PickNewDestination();
			break;
		case State.Idle:
			if (agent.isOnNavMesh) agent.ResetPath();
			break;
		case State.Chase:
			agent.autoBraking = true;
			break;
		case State.Investigate:
			agent.autoBraking = true;
			stateTimer = investigateLookTime;
			if (agent.isOnNavMesh) agent.SetDestination(lastKnownPos);
			break;
		case State.Return:
			stateTimer = 0f;
			returnFailures = 0;
			agent.autoBraking = true;
			if (zoneCenter != null && agent.isOnNavMesh) agent.SetDestination(zoneCenter.position);
			break;
		}
	}

	private bool CheckSight()
	{
		if (player == null || DebugPlayerInvisible) return false;
		Vector3 eye = transform.position + Vector3.up * 1.5f;
		Vector3 target = player.position + Vector3.up * 1.5f;
		Vector3 to = target - eye;
		// 바로 옆(2m 안)이면 방향과 상관없이(벽 가림은 검사) 보고 있는 것으로 친다
		if (to.magnitude <= TouchSightRange)
		{
			return !Physics.Linecast(eye, target, out var nearHit, obstacleMask, QueryTriggerInteraction.Ignore)
				|| nearHit.transform == player || nearHit.transform.IsChildOf(player);
		}
		if (to.magnitude > sightRange) return false;
		if (Vector3.Angle(transform.forward, to) > fovAngle * 0.5f) return false;
		if (Physics.Linecast(eye, target, out var hitInfo, obstacleMask) && hitInfo.collider.transform != player) return false;
		return true;
	}

	private IEnumerator PerformJump()
	{
		isJumping = true;
		agent.isStopped = true;
		OffMeshLinkData link = agent.currentOffMeshLinkData;
		Vector3 start = transform.position;
		Vector3 end = link.endPos + Vector3.up * agent.baseOffset;
		float time = 0f;
		float duration = Mathf.Max(0.05f, jumpDuration);
		while (time < duration)
		{
			time += Time.deltaTime;
			float t = time / duration;
			Vector3 position = Vector3.Lerp(start, end, t);
			position.y += Mathf.Sin(t * MathF.PI) * jumpHeight;
			transform.position = position;
			yield return null;
		}
		agent.CompleteOffMeshLink();
		agent.isStopped = false;
		isJumping = false;
	}

	private void OnCollisionEnter(Collision col)
	{
		if (damageTimer > 0f || currentState == State.Stun || !col.gameObject.CompareTag("Player")) return;
		damageTimer = damageCooldown;
		agent.velocity = Vector3.zero;
		var health = col.gameObject.GetComponent<PlayerHealth>();
		if (health != null) health.TakeDamage(transform.position, contactDamageHearts);
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		if (!useGlobalNavMesh && zoneCenter != null) Gizmos.DrawWireSphere(zoneCenter.position, zoneRadius);
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, sightRange);
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(transform.position, EffectiveHearingRange);
	}
}
