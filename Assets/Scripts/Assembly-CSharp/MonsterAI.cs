using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터의 몸(뇌 1). 눈·귀·이동·감전·점프와 상태 전환만 한다.
/// 추격은 몸만의 것: 눈으로 보면 쫓고, 시야가 끊기면 마지막으로 본 자리까지만 가 보고 끝낸다.
/// 추격 대상·위치를 쓰는 곳은 시야 판정 하나뿐이다 — 감독·소리는 추격을 시작하거나 늘리지 못한다.
/// 쫓지 않을 때는 감독(<see cref="MonsterDirector"/>, 뇌 2)이 준 자리(길목·소리 확인·둘레 순찰)로 간다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Rigidbody))]
public class MonsterAI : MonoBehaviour
{
	public enum State
	{
		Patrol = 0,
		Chase = 1,
		Investigate = 2,
		Return = 3,
		Stun = 4,
		Block = 5,
		Idle = 7
	}

	/// <summary>AI 테스트 씬: 몬스터가 플레이어를 보지 못하게 한다.</summary>
	public static bool DebugPlayerInvisible;

	public static List<MonsterAI> activeMonsters = new List<MonsterAI>();

	public Transform player;

	[Header("1. Speed Settings")]
	[Tooltip("둘레 자리 근처를 걸을 때 속도")]
	public float patrolSpeed = 3.5f;

	[Tooltip("감독 없이 쫓을 때 속도. 사냥 중에는 감독이 정한다")]
	public float chaseSpeed = 9f;

	[Tooltip("소리 확인하러 갈 때 속도(플레이어 달리기 11보다 느리게 — 소리만으로는 잡히지 않는다)")]
	public float investigateSpeed = 6f;

	public float patrolWaitTime;

	[Tooltip("멀리 떨어진 둘레 자리로 옮겨 갈 때 속도")]
	public float returnSpeed = 16f;

	[Header("1-1. Acceleration")]
	public float patrolAcceleration = 30f;

	public float chaseAcceleration = 80f;

	public float investigateAcceleration = 50f;

	[Header("1-2. Agent Base")]
	public float angularSpeed = 600f;

	public float stoppingDistance = 1.2f;

	[Header("2. Senses — Sight")]
	public float sightRange = 25f;

	public float fovAngle = 160f;

	public LayerMask obstacleMask;

	[Header("2-1. Senses — Hearing")]
	public float hearingRange = 50f;

	[Tooltip("소리 확인 지점 도착 후 두리번거리는 시간(초)")]
	public float investigateLookTime = 3f;

	[Tooltip("제자리 수색 시 회전 속도(도/초)")]
	public float investigateTurnSpeed = 120f;

	[Header("2-2. 멈춤 (Idle)")]
	[Tooltip("할 수 있는 일이 없을 때(길이 없음, 발밑에 NavMesh 없음) 제자리에서 기다리다 이 주기(초)마다 다시 할 일을 찾는다")]
	public float idleRetryInterval = 3f;

	[Tooltip("멈춤 중 천천히 둘러보는 회전 속도(도/초). 멈춰 있어도 눈은 뜨고 있다")]
	public float idleLookSpeed = 45f;

	[Header("2-3. Debug")]
	public bool showDebugLog = true;

	public bool drawDebugLine = true;

	[Header("3. Combat")]
	[Tooltip("접촉 시 깎는 하트 수")]
	public int contactDamageHearts = 1;

	public float damageCooldown = 1f;

	[Header("3-1. Stun (레이저 피격)")]
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

	[Header("4. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	private const float TouchSightRange = 2f;
	private const int MaxPathFailures = 3;
	private const float IdleSnapDistance = 2f;
	private const float WanderRadius = 10f;
	private const float FarFromPatrol = 20f;
	private const float DefaultLostChaseTime = 2f;
	private const float LastSeenReach = 1.5f;

	private NavMeshAgent agent;
	private Animator animator;
	private Rigidbody rb;
	private Collider myCollider;
	private Collider playerCollider;
	private State currentState;
	private State stateBeforeStun;
	private float stateTimer;
	private float damageTimer;
	private bool isJumping;
	private bool isStunned;
	private bool playerPassThrough;
	private Vector3 startPosition;
	private Quaternion startRotation;
	private int patrolFailures;
	private int returnFailures;

	// 둘레 순찰: 감독이 준 자리 근처를 걷는다
	private Vector3 patrolCenter;
	private bool hasPatrolCenter;

	// 추격: 마지막으로 "본" 자리(소리로는 바뀌지 않는다)
	private Vector3 lastSeenPos;
	private float lostSightAt = float.NegativeInfinity;
	private bool reportedBlindChase;
	private float huntSpeed = -1f;

	// 소리 확인 지점
	private Vector3 searchPoint;

	// 길목: 도착하면 플레이어가 올 쪽을 보고 기다린다
	private Vector3 blockGoal;
	private Vector3 blockFace;
	private bool holding;

	public State CurrentState => currentState;
	public bool IsInStun => currentState == State.Stun;
	public bool IsTraversingLink => isJumping;
	public bool IsBlocking => currentState == State.Block;
	public bool IsHolding => currentState == State.Block && holding;
	public bool IsSeeingPlayer { get; private set; }
	public float SeenPlayerAt { get; private set; } = float.NegativeInfinity;
	public bool CanTakeOrders => !isStunned && !isJumping && agent != null && agent.enabled && agent.isOnNavMesh;
	public Vector3 BlockGoal => blockGoal;
	public Vector3 PatrolCenter => hasPatrolCenter ? patrolCenter : transform.position;
	public Vector3 DebugDestination =>
		(agent != null && agent.isOnNavMesh && agent.hasPath) ? agent.destination : transform.position;
	public float EffectiveHearingRange => hearingRange;

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
			case State.Chase: return IsSeeingPlayer ? "추격" : "본 자리 확인";
			case State.Investigate: return HasArrived() ? "둘러봄" : "소리 확인";
			case State.Return: return "자리 이동";
			case State.Stun: return "감전";
			case State.Block: return holding ? "길목에서 대기" : "길목으로";
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

	private void Start()
	{
		if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
		agent.acceleration = patrolAcceleration;
		agent.angularSpeed = angularSpeed;
		agent.stoppingDistance = stoppingDistance;
		// Patrol은 enum 기본값이라 ChangeState(Patrol)가 조기 반환한다 — 첫 목적지를 직접 잡는다
		currentState = State.Patrol;
		agent.autoBraking = false;
		PickNewDestination();
		_ = MonsterDirector.Instance;
	}

	public void ResetMonster()
	{
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
		holding = false;
		hasPatrolCenter = false;
		IsSeeingPlayer = false;
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

	/// <summary>감독이 정한 사냥 속도. 음수면 해제(chaseSpeed 사용).</summary>
	public void SetHuntSpeed(float speed) { huntSpeed = speed; }

	private float HuntMoveSpeed => huntSpeed > 0f ? huntSpeed : chaseSpeed;

	// ────────────────────────────────────────────────
	//  감독의 명령
	// ────────────────────────────────────────────────

	/// <summary>길목: 감독이 정한 가려진 자리로 달려가, 도착하면 face 쪽(플레이어가 올 쪽)을 보고 기다린다. 보면 그때 스스로 쫓는다.</summary>
	public void CommandBlock(Vector3 goal, Vector3 face)
	{
		if (!CanTakeOrders) return;
		bool sameGoal = currentState == State.Block && Vector3.Distance(goal, blockGoal) < 3f;
		blockGoal = goal;
		blockFace = face;
		if (currentState != State.Block) { ChangeState(State.Block); return; }
		if (sameGoal) return;
		holding = false;
		agent.isStopped = false;
		agent.SetDestination(goal);
	}

	/// <summary>소리 확인: 지점으로 가서 둘러본다.</summary>
	public void CommandSearch(Vector3 point)
	{
		if (!CanTakeOrders || currentState == State.Chase || currentState == State.Block) return;
		searchPoint = point;
		if (currentState == State.Investigate)
		{
			stateTimer = investigateLookTime;
			agent.SetDestination(point);
		}
		else ChangeState(State.Investigate);
	}

	/// <summary>둘레 순찰: 감독이 준 자리 근처를 걷는다. 멀면 먼저 빠르게 옮겨 간다.</summary>
	public void CommandPatrolAt(Vector3 center)
	{
		if (isStunned) return;
		patrolCenter = center;
		hasPatrolCenter = true;
		huntSpeed = -1f;
		bool far = Vector3.Distance(transform.position, center) > FarFromPatrol;
		if (far)
		{
			if (currentState == State.Return) { if (agent.isOnNavMesh) agent.SetDestination(center); }
			else ChangeState(State.Return);
			return;
		}
		if (currentState == State.Patrol) return;
		ChangeState(State.Patrol);
	}

	/// <summary>재배치: 아무도 보지 않을 때 감독이 둘레 자리로 옮긴다.</summary>
	public void Relocate(Vector3 point)
	{
		if (!CanTakeOrders) return;
		agent.Warp(point);
		patrolCenter = point;
		hasPatrolCenter = true;
		PlaytestRecorder.Record("monster_state", name, point, currentState + " -> Relocated");
		if (currentState == State.Patrol) PickNewDestination();
		else ChangeState(State.Patrol);
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
		if (IsSeeingPlayer && !canSee) lostSightAt = Time.time;
		IsSeeingPlayer = canSee;
		if (canSee)
		{
			SeenPlayerAt = Time.time;
			lastSeenPos = player.position;
			Director?.ReportSighting(this, player.position);
		}

		ApplySpeed();
		switch (currentState)
		{
		case State.Patrol: ProcessPatrol(canSee); break;
		case State.Chase: ProcessChase(canSee); break;
		case State.Block: ProcessBlock(canSee); break;
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
			agent.speed = HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Block:
			agent.speed = HuntMoveSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Investigate:
			// 소리 확인은 빠른 걸음 — 플레이어(11)보다 느려야 계속 쏘며 뛰는 플레이어가 소리만으로 잡히지 않는다
			agent.speed = investigateSpeed;
			agent.acceleration = investigateAcceleration;
			break;
		}
	}

	/// <summary>
	/// 추격에 들어가는 유일한 길: 지금 눈으로 보고 있을 때. 감독에게는 ReportSighting으로 이미 알렸다.
	/// </summary>
	private bool StartChaseOnSight(bool canSee)
	{
		if (!canSee) return false;
		lastSeenPos = player.position;
		PlaytestRecorder.Record("chase_start", name, transform.position, "sight;from=" + currentState, player.position);
		ChangeState(State.Chase);
		return true;
	}

	private void ProcessPatrol(bool canSee)
	{
		if (StartChaseOnSight(canSee)) return;
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
	/// 추격. 보이면 플레이어에게 곧장. 시야가 끊기면 마지막으로 본 자리까지만 가 보고(최대 lostChaseTime초),
	/// 거기서도 못 보면 추격 끝 — 감독에게 알리고 감독이 다음 자리를 준다. 소리는 이 자리를 바꾸지 않는다.
	/// </summary>
	private void ProcessChase(bool canSee)
	{
		if (canSee)
		{
			reportedBlindChase = false;
			agent.SetDestination(player.position);
			return;
		}
		agent.SetDestination(lastSeenPos);
		float lostFor = Time.time - lostSightAt;
		float limit = Director != null ? Director.LostChaseTime : DefaultLostChaseTime;
		bool reached = Vector3.Distance(transform.position, lastSeenPos) <= LastSeenReach;
		if (lostFor > limit + .5f && !reportedBlindChase)
		{
			// 여기까지 오면 버그다: 못 본 채 추격이 이어지고 있다(QA 도구가 0인지 센다)
			reportedBlindChase = true;
			PlaytestRecorder.Record("chase_without_sight", name, transform.position, "lost=" + lostFor.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
		}
		if (!reached && lostFor < limit) return;
		PlaytestRecorder.Record("chase_end", name, transform.position,
			"lost=" + lostFor.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + (reached ? ";reached" : ";timeout"), lastSeenPos);
		if (Director != null) Director.ReportChaseEnded(this, lostFor);
		else ChangeState(State.Patrol);
		// 감독이 새 자리를 못 줬다면(예: 길 없음) 추격 상태로 남지 않는다
		if (currentState == State.Chase) ChangeState(State.Patrol);
	}

	/// <summary>
	/// 길목. 감독이 준 가려진 자리까지 달려가고, 도착하면 멈춰서 플레이어가 올 쪽을 보고 기다린다(조여 들지 않는다).
	/// 보이면 그때 스스로 쫓는다.
	/// </summary>
	private void ProcessBlock(bool canSee)
	{
		if (StartChaseOnSight(canSee)) return;
		if (!holding && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			Director?.ReportBlockFailed(this);
			return;
		}
		if (!holding)
		{
			if (Vector3.Distance(transform.position, blockGoal) > 2f && !HasArrived()) return;
			holding = true;
			agent.ResetPath();
		}
		if (blockFace.sqrMagnitude > .01f)
			transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(blockFace), investigateTurnSpeed * Time.deltaTime);
	}

	private void ProcessInvestigate(bool canSee)
	{
		if (StartChaseOnSight(canSee)) return;
		if (!agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid)
		{
			// 확인 지점까지 길이 없다 — 도착을 영영 기다리지 않는다
			FinishSearch();
			return;
		}
		if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + .5f) return;
		stateTimer -= Time.deltaTime;
		transform.Rotate(Vector3.up, investigateTurnSpeed * Time.deltaTime);
		if (stateTimer <= 0f) FinishSearch();
	}

	private void FinishSearch()
	{
		if (Director != null) { Director.ReportSearchDone(this); return; }
		ChangeState(State.Patrol);
	}

	private void ProcessReturn(bool canSee)
	{
		if (StartChaseOnSight(canSee)) return;
		if (Vector3.Distance(transform.position, patrolCenter) < 4f)
		{
			ChangeState(State.Patrol);
			return;
		}
		stateTimer += Time.deltaTime;
		if (!agent.pathPending && (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath) && stateTimer > 1f)
		{
			stateTimer = 0f;
			// 자리까지 길이 계속 없으면 영원히 재시도하지 않고 멈춤으로 기다린다
			if (++returnFailures >= MaxPathFailures) { EnterIdle("slot_unreachable"); return; }
			agent.SetDestination(patrolCenter);
		}
	}

	/// <summary>
	/// 멈춤: 할 수 있는 일이 없을 때의 안전한 기본 상태. 제자리에서 천천히 둘러보며 눈은 뜨고 있고,
	/// 일정 주기마다 순찰을 다시 시도한다. 감독의 명령은 언제든 받는다.
	/// </summary>
	private void EnterIdle(string reason)
	{
		PlaytestRecorder.Record("monster_idle", name, transform.position, reason);
		if (showDebugLog) Debug.Log($"<color=grey><b>[멈춤]</b></color> {name} — {reason}");
		hasPatrolCenter = false;
		ChangeState(State.Idle);
		stateTimer = Mathf.Max(0.5f, idleRetryInterval);
		Director?.ReportIdle(this);
	}

	private void ProcessIdle(bool canSee)
	{
		if (agent.isOnNavMesh && StartChaseOnSight(canSee)) return;
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
		if (stateBeforeStun == State.Patrol || stateBeforeStun == State.Return || stateBeforeStun == State.Investigate || stateBeforeStun == State.Idle)
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
		// 일단 제자리에서 둘러본다 — 보이면 쫓고, 아니면 감독이 둘레로 보낸다(맞은 쪽으로 가는 것도 흔적 추적이라 하지 않는다)
		searchPoint = transform.position;
		currentState = State.Investigate;
		stateTimer = investigateLookTime;
		if (agent.isOnNavMesh) agent.SetDestination(transform.position);
		PlaytestRecorder.Record("monster_state", name, transform.position, "Stun -> Recovered");
		bool sees = CheckSight();
		IsSeeingPlayer = sees;
		if (sees)
		{
			SeenPlayerAt = Time.time;
			Director?.ReportSighting(this, player.position);
			StartChaseOnSight(true);
		}
		else Director?.ReportRecovered(this);
	}

	// ────────────────────────────────────────────────

	private bool HasArrived()
	{
		return agent != null && agent.isOnNavMesh && !agent.pathPending
			&& agent.remainingDistance <= agent.stoppingDistance + 0.5f;
	}

	/// <summary>순찰 목적지: 감독이 준 둘레 자리 근처(없으면 지금 자리 근처).</summary>
	private void PickNewDestination()
	{
		Vector3 center = hasPatrolCenter ? patrolCenter : transform.position;
		Vector3 destination = center;
		for (int i = 0; i < 5; i++)
		{
			Vector2 offset = UnityEngine.Random.insideUnitCircle * WanderRadius;
			if (NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y), out var hit, 4f, NavMesh.AllAreas))
			{
				destination = hit.position;
				break;
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
		holding = false;
		agent.stoppingDistance = stoppingDistance;
		switch (newState)
		{
		case State.Block:
			agent.autoBraking = true;
			agent.SetDestination(blockGoal);
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
			reportedBlindChase = false;
			break;
		case State.Investigate:
			agent.autoBraking = true;
			stateTimer = investigateLookTime;
			if (agent.isOnNavMesh) agent.SetDestination(searchPoint);
			break;
		case State.Return:
			stateTimer = 0f;
			returnFailures = 0;
			agent.autoBraking = true;
			if (agent.isOnNavMesh) agent.SetDestination(patrolCenter);
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
		if (hasPatrolCenter) Gizmos.DrawWireSphere(patrolCenter, WanderRadius);
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, sightRange);
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(transform.position, EffectiveHearingRange);
	}
}
