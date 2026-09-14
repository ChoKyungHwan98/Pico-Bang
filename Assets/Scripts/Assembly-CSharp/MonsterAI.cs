using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 한 마리의 행동. 플랫 FSM.
///
/// 몬스터는 전역 1 + 구역 4. <b>모두 이 같은 FSM을 쓴다.</b> 전역 몬스터의 차이는 넓은 순찰과 좋은 청각뿐이다.
/// 여러 마리가 어떻게 흩어질지는 <see cref="MonsterDirector"/>(보이지 않는 조정자)가 정하고,
/// 이 클래스는 명령(수색 / 차단 / 해제)과 목적지만 받아 기존 상태로 수행한다.
///
/// 행동 규칙 (기획 2026-09-12):
///   평상시              → 순찰 (구역 몬스터는 자기 구역, 전역 몬스터는 넓게)
///   플레이어를 직접 봄  → 추격 + 조정자에게 목격 보고 (누가 봤든 포위가 시작된다)
///   시야를 놓침         → 마지막으로 본 위치로 이동 → 수색 (조정자가 흩어진 수색 지점으로 바꿔 줄 수 있음)
///   소리를 들음         → "여기서 소리가 났다" — 그 근처로 이동 → 수색 (플레이어 위치로 취급하지 않는다)
///   (조정자) 차단       → 앞길에 가서 대기, 가까이 오면 덮침
///   발견 실패           → 복귀 → 순찰
///   레이저 피격         → 기절
///
/// 정직성 원칙: 플레이어의 현재 위치는 <b>보고 있는 동안에만</b> 쓴다(시야 판정 통과 시).
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

	[Tooltip("사용하지 않음. 추격 방식은 전부 같다 — 옆길·앞길 차단은 조정자가 맡는다")]
	public ChaseStyle style;

	public Transform player;

	[Header("2. Movement & Zone")]
	public bool useGlobalNavMesh;

	public Transform zoneCenter;

	public float zoneRadius = 35f;

	[Header("3. Speed Settings")]
	public float patrolSpeed = 3.5f;

	public float chaseSpeed = 9f;

	public float investigateSpeed = 6f;

	public float patrolWaitTime;

	// 수색 지점·차단 지점으로 달려갈 때는 추격 속도(chaseSpeed)를 쓴다 (기획: "빠르게 = 추격 속도와 같게")

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

	[Tooltip("시야에서 놓친 뒤 마지막 목격 지점을 쫓는 시간")]
	public float memoryTime = 4f;

	[Tooltip("이 거리보다 멀어지면 추격을 포기한다")]
	public float giveUpRange = 40f;

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

	[Tooltip("전기 아크 연출을 켤지 여부")]
	public bool showStunSparks = true;

	[Tooltip("동시에 그릴 아크 개수")]
	[Range(1, 12)]
	public int stunArcCount = 5;

	[Tooltip("아크가 튀는 폭. 몸집에 맞춰 조절")]
	public float stunArcJaggedness = 0.16f;

	[Header("6. 조정자(Director) — 전역 몬스터의 값만 사용됨")]
	[Tooltip("동시에 직접 쫓는 최대 마릿수. 넘치면 뒤처진 구역 몬스터를 차단으로 돌린다")]
	public int maxSimultaneousChasers = 2;

	[Tooltip("작은 포위(구역 몬스터가 발견): 차단 보낼 마릿수")]
	public int smallHuntCutters = 1;

	[Tooltip("큰 포위(전역 몬스터가 발견): 차단 보낼 마릿수")]
	public int largeHuntCutters = 2;

	[Tooltip("차단 후보: 마지막 목격 위치에서 8방향으로 바닥을 따라 최대 이만큼 뻗어 본다(m)")]
	public float cutCandidateMaxDistance = 18f;

	[Tooltip("차단 후보: 이만큼도 못 가고 막히는 방향은 버린다(m). 복도에서는 양 끝만 남는다")]
	public float cutCandidateMinDistance = 6f;

	[Tooltip("차단 후보: 서로 이 거리 안이면 하나로 합친다(m)")]
	public float cutCandidateMergeDistance = 8f;

	[Tooltip("차단 후보: 추격자가 이 거리 안에 있으면 감점 — 같은 자리에 겹치지 않게(m)")]
	public float cutAvoidChaserRadius = 10f;

	[Tooltip("차단 대기 몬스터가 덮치려 할 때, 가장 먼 추격자보다 이만큼 이상 가까워야 교대한다(m)")]
	public float swapDistanceMargin = 3f;

	[Tooltip("차단하러 가는 길이 목격 위치 이 반경 안을 지나면 가산점 — 플레이어를 뚫고 앞으로 가지 않게")]
	public float crossingAvoidRadius = 6f;

	[Tooltip("위 관통 가산점(m 단위 거리로 환산)")]
	public float crossingPenalty = 25f;

	[Tooltip("놓쳤을 때 수색 지점 분산: 가까운 거리(m)")]
	public float searchSpreadNear = 8f;

	[Tooltip("놓쳤을 때 수색 지점 분산: 먼 거리(m)")]
	public float searchSpreadFar = 18f;

	[Tooltip("마지막 목격 후 이 시간이 지나면 사냥 종료(초)")]
	public float huntMemory = 8f;

	[Tooltip("보이는 동안 차단 배치를 다시 계산하는 주기(초)")]
	public float layoutRefreshInterval = 1f;

	[Tooltip("목격이 이 시간 끊기면 '놓쳤다'로 보고 수색을 흩어놓는다(초). 한두 프레임 깜빡임 무시용")]
	public float lostSightGrace = 0.5f;

	[Header("7. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	[Header("8. 차단 대기 (조정자가 지시)")]
	[Tooltip("차단 대기 중 플레이어가 이 거리 안에 보이면 덮친다. 멀리서 보고 뛰쳐나가면 추격·차단을 오가며 떨게 된다")]
	public float ambushEngageRange = 8f;

	[Tooltip("차단 대기를 이만큼 유지해도 덮치지 못하면 포기하고 복귀한다(초)")]
	public float interceptTimeout = 12f;

	[Tooltip("추격↔차단 역할이 바뀐 직후 다시 바뀌지 않는 시간(초)")]
	public float roleLockTime = 2f;

	public static List<MonsterAI> activeMonsters = new List<MonsterAI>();

	private NavMeshAgent agent;

	private Animator animator;

	private Rigidbody rb;

	private State currentState;

	private State stateBeforeStun;

	// 이 몬스터가 "안다고 믿는" 목표 위치: 보고 있던 위치, 들은 소리 위치, 조정자가 준 수색 지점
	private Vector3 lastKnownPos;

	private static NavMeshTriangulation navMeshData;

	private static bool isNavMeshDataLoaded = false;

	private float stateTimer;

	private float memoryTimer;

	private float damageTimer;

	private bool isJumping;

	private bool isStunned;

	private Vector3 startPosition;

	private Quaternion startRotation;

	// 지점을 향해 추격 속도로 달려가는 중인가. 수색 상태를 벗어나면 꺼진다.
	private bool isRushing;

	// 차단 대기
	private Vector3 interceptPoint;
	private float interceptTimer;
	private float roleLockUntil;

	public bool IsInStun => currentState == State.Stun;

	public State CurrentState => currentState;

	public bool IsIntercepting => currentState == State.Intercept;

	/// <summary>조정자가 이 개체를 추격에서 차단으로 돌려도 되는가. 전역 몬스터는 제외, 방금 역할이 바뀌었으면 보류.</summary>
	public bool CanBeDemoted =>
		currentState == State.Chase && role == MonsterRole.Zone_Defender && Time.time >= roleLockUntil;

	/// <summary>조정자의 명령(수색·차단)을 받을 수 있는가. 직접 쫓는 중·기절·점프 중이면 제외.</summary>
	public bool IsAvailableForOrders =>
		currentState != State.Chase && currentState != State.Stun && !isJumping;

	/// <summary>AI 테스트 씬 표시용 상태 이름.</summary>
	public string StateLabel
	{
		get
		{
			switch (currentState)
			{
			case State.Patrol: return "순찰";
			case State.Chase: return "추격";
			case State.Investigate:
				return (isRushing && !HasArrived()) ? "이동" : "수색";
			case State.Return: return "복귀";
			case State.Stun: return "기절";
			case State.Intercept: return HasArrived() ? "차단 대기" : "차단 이동";
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
		ChangeState(State.Patrol);

		// 조정자는 첫 목격 전에도 추격 인원을 세야 하므로 미리 깨운다
		_ = MonsterDirector.Instance;
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
		if (animator != null)
		{
			animator.Rebind();
			animator.Update(0f);
		}
		if (agent.isOnNavMesh)
		{
			agent.isStopped = true;
		}
	}

	// ────────────────────────────────────────────────
	//  청각
	// ────────────────────────────────────────────────

	/// <summary>
	/// <see cref="NoiseSystem"/>이 호출한다. 들리는지 여부는 여기서 판정한다.
	///
	/// 소리는 <b>"플레이어가 여기 있다"가 아니라 "여기서 소리가 났다"</b>다.
	/// 조정자의 목격 기록에는 넣지 않고, 들은 몬스터만 그 근처로 확인하러 간다.
	/// 멀리서 난 소리일수록 어긋난 지점으로 향한다 — 그래야 플레이어에게 빠져나갈 여지가 생긴다.
	/// </summary>
	public void OnHearNoise(Vector3 soundPosition, float noiseRadius, NoiseKind kind)
	{
		// 쫓는 중·차단 중에는 소리에 한눈팔지 않는다 (시킨 일만 한다)
		if (currentState == State.Chase || currentState == State.Intercept || isStunned)
		{
			return;
		}

		float distance = Vector3.Distance(base.transform.position, soundPosition);
		float audibleRange = Mathf.Min(EffectiveHearingRange, noiseRadius);
		if (distance > audibleRange)
		{
			return;
		}

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

		RushToInvestigate(guessed);
	}

	/// <summary>
	/// 한 지점으로 추격 속도로 달려가 수색한다.
	/// 이미 수색 중이어도 새 지점으로 갱신해야 한다 — <see cref="ChangeState"/>는 같은 상태로의 전환을 무시한다.
	/// </summary>
	private void RushToInvestigate(Vector3 position)
	{
		lastKnownPos = position;
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
	//  조정자(Director)의 명령 — 목적지만 받아 기존 상태로 수행한다
	// ────────────────────────────────────────────────

	/// <summary>
	/// 수색 명령: 그 지점으로 가서 둘러본다.
	/// 조정자가 "아무도 못 보고 있다"고 판단했을 때 내리므로, 추격 중(기억으로 쫓는 중)이어도 받는다.
	/// </summary>
	public void CommandSearch(Vector3 point)
	{
		if (isStunned) { return; }
		RushToInvestigate(point);
	}

	/// <summary>차단 명령: 앞길 지점으로 가서 기다린다. 이미 차단 중이면 지점만 옮긴다.</summary>
	public void CommandAmbush(Vector3 point)
	{
		if (isStunned) { return; }
		interceptPoint = point;
		if (currentState == State.Intercept)
		{
			agent.SetDestination(point);
			return;
		}
		interceptTimer = interceptTimeout;
		roleLockUntil = Time.time + roleLockTime;
		ChangeState(State.Intercept);
	}

	/// <summary>해제 명령: 사냥이 끝났다 — 차단·수색을 멈추고 구역으로 돌아간다. 직접 쫓는 중이면 무시.</summary>
	public void CommandRelease()
	{
		if (currentState == State.Intercept || currentState == State.Investigate)
		{
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
		switch (currentState)
		{
		case State.Patrol:
		case State.Return:
			agent.speed = patrolSpeed;
			agent.acceleration = patrolAcceleration;
			break;
		case State.Chase:
		case State.Intercept:
			agent.speed = chaseSpeed;
			agent.acceleration = chaseAcceleration;
			break;
		case State.Investigate:
			agent.speed = isRushing ? chaseSpeed : investigateSpeed;
			agent.acceleration = isRushing ? chaseAcceleration : investigateAcceleration;
			break;
		}

		bool canSee = player != null && CheckSight();

		// 보고 있으면 누구든 조정자에게 보고한다 — 이것이 조정자가 아는 유일한 플레이어 정보다
		if (canSee)
		{
			MonsterDirector director = MonsterDirector.Instance;
			if (director != null) { director.ReportSighting(this, player.position); }
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

	private IEnumerator ProcessStunReaction(Vector3 shooterPosition)
	{
		isStunned = true;
		stateBeforeStun = currentState;
		ChangeState(State.Stun);
		agent.isStopped = true;
		agent.velocity = Vector3.zero;
		agent.updateRotation = false;
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
		else if (stateBeforeStun == State.Chase)
		{
			// 맞은 순간 쏜 방향은 알 수 있다 — 그 자리를 확인하러 간다
			lastKnownPos = shooterPosition;
		}
		yield return new WaitForSeconds(stunRecoverTime);
		agent.updateRotation = true;
		agent.isStopped = false;
		isStunned = false;
		if (stateBeforeStun == State.Chase)
		{
			ChangeState(State.Investigate);
			isRushing = true;
		}
		else if (CheckSight())
		{
			TryStartChase();
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

	private void ProcessChase(bool canSee)
	{
		if (canSee)
		{
			// 보고 있는 동안에만 현재 위치를 안다 — 곧장 쫓는다 (모든 몬스터 동일)
			lastKnownPos = player.position;
			memoryTimer = memoryTime;
			if (Vector3.Distance(base.transform.position, player.position) > giveUpRange)
			{
				ChangeState(State.Return);
				return;
			}
			agent.SetDestination(player.position);
		}
		else
		{
			// 놓쳤다: 마지막으로 본 위치까지만 간다. 진짜 현재 위치는 모른다
			memoryTimer -= Time.deltaTime;
			if (!(memoryTimer > 0f))
			{
				ChangeState(State.Investigate);
				isRushing = true;
				return;
			}
			agent.SetDestination(lastKnownPos);
		}
		if (!useGlobalNavMesh && role == MonsterRole.Zone_Defender && zoneCenter != null && Vector3.Distance(base.transform.position, zoneCenter.position) > zoneRadius * 2.5f)
		{
			ChangeState(State.Return);
		}
	}

	private void ProcessInvestigate(bool canSee)
	{
		if (canSee)
		{
			TryStartChase();
		}
		else if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
		{
			stateTimer -= Time.deltaTime;
			base.transform.Rotate(Vector3.up, investigateTurnSpeed * Time.deltaTime);
			if (stateTimer <= 0f)
			{
				// 발견 실패: 구역 밖이면 구역으로 복귀, 구역 안이면 그대로 순찰
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
	/// 차단 대기. 지점에 가서 기다린다.
	/// 기다리는 동안 플레이어를 봐도 멀면 자리를 지키고(보고만 한다), 가까이 오면 덮친다.
	/// </summary>
	private void ProcessIntercept(bool canSee)
	{
		if (canSee && Vector3.Distance(base.transform.position, player.position) <= ambushEngageRange)
		{
			// 덮치기도 허가제: 추격자가 가득 차 있으면 조정자가 교대 여부를 정하고, 아니면 자리를 지킨다
			lastKnownPos = player.position;
			memoryTimer = memoryTime;
			if (TryStartChase())
			{
				return;
			}
		}

		if (HasArrived())
		{
			// 보이면 그쪽을, 안 보이면 차단 지점을 향한 채(오던 방향 반대) 기다린다
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
		switch (newState)
		{
		case State.Intercept:
			agent.autoBraking = true;
			agent.SetDestination(interceptPoint);
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
		ChangeState(State.Chase);
	}

	/// <summary>
	/// 추격 허가를 받고 들어간다. 동시에 쫓는 수가 가득 차 있으면 조정자가 거부하고
	/// 곧바로 아직 안 막힌 길로 차단 명령을 내린다 — 추격 상태를 거치지 않으므로 한 순간도 초과되지 않는다.
	/// </summary>
	private bool TryStartChase()
	{
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
