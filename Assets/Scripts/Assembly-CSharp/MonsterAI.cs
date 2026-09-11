using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터 개체 행동. 플랫 FSM — 두 개의 뇌 중 아래쪽.
///
/// 이 클래스는 <b>한 마리가 무엇을 하는가</b>만 담당한다.
/// 누구를 어디로 보낼지, 누가 쫓고 누가 매복할지는 <see cref="MonsterDirector"/>(전역 몬스터의 지휘)가 정한다.
/// 구역 몬스터 4마리는 지휘를 따르는 "눈먼 수족"이다 — 스스로 머리를 쓰지 않고 시킨 대로 움직여서 읽힌다.
///
/// 행동 규칙 (기획 2026-09-11):
///   평상시              → 구역 순찰
///   감독 제보 / 소리    → 그 지점까지 추격 속도로 이동 → 주변 수색
///   플레이어를 직접 봄  → 추격
///   시야를 놓침         → 마지막으로 본 위치까지 추적 → 주변 수색 → 발견 실패 시 구역 복귀
///   레이저 피격         → 기절
///   (감독 지시) 매복    → 플레이어가 가는 쪽으로 앞질러 가서 대기, 가까이 오면 덮침
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

	// 제보·소리를 받고 달려갈 때는 추격 속도(chaseSpeed)를 쓴다 (기획: "빠르게 = 추격 속도와 같게")

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

	[Header("5-2. Berserk (Rusher 전용)")]
	public float berserkTriggerRange = 12f;

	public float berserkDuration = 3f;

	[Tooltip("광폭화 시 추격 속도 배율")]
	public float berserkSpeedScale = 1.5f;

	public float berserkAcceleration = 120f;

	[Header("5-3. Chase Style 세부")]
	[Tooltip("Pinky: 플레이어 진행 방향으로 몇 초 앞을 노릴 것인가")]
	public float predictLeadTime = 0.5f;

	[Tooltip("Inky: 플레이어 기준 옆으로 파고드는 거리")]
	public float flankOffset = 4f;

	[Header("6. 무리 호출 — 무리를 부른 전역 몬스터의 값만 사용됨")]
	[Tooltip("감독이 무리를 다시 보내는 주기(초). 짧을수록 플레이어를 바짝 따라붙고, 길수록 헛다리를 짚는다")]
	public float dispatchInterval = 2.5f;

	[Tooltip("플레이어에게서 최소 이만큼 떨어진 지점으로 보낸다")]
	public float dispatchBlurMin = 6f;

	[Tooltip("플레이어에게서 최대 이만큼 떨어진 지점으로 보낸다. 클수록 흐리다")]
	public float dispatchBlurMax = 14f;

	[Tooltip("배정된 방향에서 매번 흔들리는 각도(도)")]
	public float dispatchAngleJitter = 25f;

	[Tooltip("전역 몬스터가 플레이어를 놓친 뒤 호출을 유지하는 시간")]
	public float callMemory = 6f;

	[Tooltip("플레이어에게서 이보다 먼 개체는 부르지 않는다. 0이면 거리 제한 없음")]
	public float dispatchRange = 0f;

	[Tooltip("제보를 받으면 제보 지점에서 가장 가까운 구역 몬스터 몇 마리를 보낼 것인가 (기획: 2). 나머지는 순찰 유지")]
	public int dispatchSquadSize = 2;

	[Header("6-1. 추격 인원 제한 — 전역 몬스터의 값만 사용됨")]
	[Tooltip("동시에 직접 쫓는 최대 마릿수 (기획: 2). 넘치면 뒤처진 구역 몬스터가 매복으로 돌린다")]
	public int maxSimultaneousChasers = 2;

	[Tooltip("매복 지점: 플레이어 진행 방향으로 이만큼 앞(m)")]
	public float interceptLeadDistance = 15f;

	[Tooltip("매복 지점을 다시 계산하는 주기(초). 플레이어가 방향을 바꾸면 따라 옮긴다")]
	public float interceptRefreshInterval = 1.5f;

	[Header("7. Jump (단차 이동)")]
	public float jumpDuration = 0.5f;

	public float jumpHeight = 1.5f;

	[Header("8. 매복 (감독이 지시)")]
	[Tooltip("매복 중 플레이어가 이 거리 안에 보이면 덮친다. 멀리서 보고 뛰쳐나가면 추격·매복을 오가며 떨게 된다")]
	public float ambushEngageRange = 8f;

	[Tooltip("매복을 이만큼 유지해도 덮치지 못하면 포기하고 복귀한다(초)")]
	public float interceptTimeout = 12f;

	[Tooltip("추격↔매복 역할이 바뀐 직후 다시 바뀌지 않는 시간(초)")]
	public float roleLockTime = 2f;

	public static List<MonsterAI> activeMonsters = new List<MonsterAI>();

	private NavMeshAgent agent;

	private Animator animator;

	private Rigidbody rb;

	private State currentState;

	private State stateBeforeStun;

	private Vector3 lastKnownPos;

	private static NavMeshTriangulation navMeshData;

	private static bool isNavMeshDataLoaded = false;

	private float stateTimer;

	private float memoryTimer;

	private float damageTimer;

	private float berserkTimer;

	private bool isJumping;

	private bool isBerserking;

	private Vector3 playerPrevPos;

	private Vector3 playerVelocity;

	private bool isStunned;

	private Vector3 startPosition;

	private Quaternion startRotation;

	// 제보·소리·놓친 위치를 향해 추격 속도로 달려가는 중인가. 수색 상태를 벗어나면 꺼진다.
	private bool isRushing;

	// 매복
	private Vector3 interceptPoint;
	private float interceptTimer;
	private float roleLockUntil;

	public bool IsInStun => currentState == State.Stun;

	public State CurrentState => currentState;

	public bool IsIntercepting => currentState == State.Intercept;

	/// <summary>감독이 이 개체를 추격에서 매복으로 돌려도 되는가. 방금 역할이 바뀌었으면 잠시 보류.</summary>
	public bool CanBeDemoted =>
		currentState == State.Chase && role == MonsterRole.Zone_Defender && Time.time >= roleLockUntil;

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
			case State.Intercept: return HasArrived() ? "매복 대기" : "매복 이동";
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

	/// <summary>감독이 보낼 수 있는 상태인가. 이미 직접 쫓고 있거나 기절 중이면 제외.</summary>
	public bool CanReceiveDispatch =>
		currentState != State.Chase && currentState != State.Stun && currentState != State.Intercept && !isJumping;

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

		// 추격 인원 조율은 사냥(제보)과 무관하게 항상 돌아야 하므로 지휘관을 미리 깨운다
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
		isBerserking = false;
		isJumping = false;
		isRushing = false;
		roleLockUntil = 0f;
		damageTimer = 0f;
		berserkTimer = 0f;
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
	/// 기획: 전역 몬스터는 넓게, 구역 몬스터는 좁게 듣는다.
	/// 폴리싱: 소리는 <b>정확한 좌표를 알려주지 않는다.</b> 멀리서 난 소리일수록
	/// 어긋난 지점으로 향한다 — 그래야 플레이어에게 빠져나갈 여지가 생긴다.
	/// </summary>
	public void OnHearNoise(Vector3 soundPosition, float noiseRadius, NoiseKind kind)
	{
		// 쫓는 중·매복 중에는 소리에 한눈팔지 않는다 (시킨 일만 한다)
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
	/// 소리를 듣거나 감독의 호출을 받아 한 지점으로 달려간다.
	///
	/// 이미 수색 중이어도 새 지점으로 갱신해야 한다. <see cref="ChangeState"/>는 같은 상태로의
	/// 전환을 무시하므로, 그대로 두면 호출을 연달아 받아도 첫 지점에 머문다.
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
		if (player != null && Time.deltaTime > 0f)
		{
			playerVelocity = (player.position - playerPrevPos) / Time.deltaTime;
			playerPrevPos = player.position;
		}
		if (currentState == State.Stun)
		{
			return;
		}
		if (!isBerserking)
		{
			switch (currentState)
			{
			case State.Patrol:
				agent.speed = patrolSpeed;
				agent.acceleration = patrolAcceleration;
				break;
			case State.Chase:
				agent.speed = chaseSpeed;
				agent.acceleration = chaseAcceleration;
				break;
			case State.Investigate:
				// 제보·소리·놓친 위치로 달려가는 동안은 추격 속도
				agent.speed = isRushing ? chaseSpeed : investigateSpeed;
				agent.acceleration = isRushing ? chaseAcceleration : investigateAcceleration;
				break;
			case State.Return:
				agent.speed = patrolSpeed;
				agent.acceleration = patrolAcceleration;
				break;
			case State.Intercept:
				// 앞질러 가야 하므로 추격 속도
				agent.speed = chaseSpeed;
				agent.acceleration = chaseAcceleration;
				break;
			}
		}
		bool canSee = player != null && CheckSight();
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
		// 회복 구간까지 끌면 "언제 다시 움직이는지"가 안 읽힌다.
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
			lastKnownPos = shooterPosition;
		}
		yield return new WaitForSeconds(stunRecoverTime);
		agent.updateRotation = true;
		agent.isStopped = false;
		isStunned = false;
		if (stateBeforeStun == State.Chase)
		{
			// 쏜 자리(마지막으로 안 위치)까지 추격 속도로 추적 → 수색
			ChangeState(State.Investigate);
			isRushing = true;
		}
		else if (CheckSight())
		{
			StartChase();
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
			StartChase();
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
		if (player != null && Vector3.Distance(base.transform.position, player.position) > giveUpRange)
		{
			ChangeState(State.Return);
			return;
		}
		if (canSee)
		{
			lastKnownPos = player.position;
			memoryTimer = memoryTime;

			// 전역 몬스터는 보고 있는 동안 계속 지휘관에게 보고한다.
			// 실제 호출·대형 유지는 MonsterDirector가 결정한다.
			if (role == MonsterRole.Global_Stalker && MonsterDirector.Instance != null)
			{
				MonsterDirector.Instance.ReportSpotted(this, player);
			}

			if (style == ChaseStyle.Rusher_Berserk)
			{
				CheckBerserk();
			}
			MoveByStyle(player.position);
		}
		else
		{
			memoryTimer -= Time.deltaTime;
			if (!(memoryTimer > 0f))
			{
				// 마지막으로 본 위치까지 추격 속도로 가서 수색
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
			StartChase();
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

	// ────────────────────────────────────────────────
	//  매복 (MonsterDirector가 지시)
	// ────────────────────────────────────────────────

	/// <summary>
	/// 추격에서 빠져 플레이어가 가는 쪽으로 앞질러 간다.
	/// 기획: 동시에 쫓는 건 최대 2마리. 셋째부터는 줄줄이 따라가지 않고 앞에서 기다린다.
	/// </summary>
	public void BeginIntercept(Vector3 point)
	{
		if (isStunned) { return; }
		interceptPoint = point;
		interceptTimer = interceptTimeout;
		roleLockUntil = Time.time + roleLockTime;
		ChangeState(State.Intercept);
	}

	/// <summary>플레이어가 방향을 바꾸면 매복 지점을 옮긴다.</summary>
	public void UpdateInterceptPoint(Vector3 point)
	{
		if (currentState != State.Intercept) { return; }
		interceptPoint = point;
		agent.SetDestination(point);
	}

	/// <summary>쫓던 무리가 모두 놓쳤다 — 매복을 풀고 구역으로 돌아간다.</summary>
	public void EndIntercept()
	{
		if (currentState != State.Intercept) { return; }
		ChangeState(IsOutsideZone() ? State.Return : State.Patrol);
	}

	private void ProcessIntercept(bool canSee)
	{
		if (canSee && player != null
			&& Vector3.Distance(base.transform.position, player.position) <= ambushEngageRange)
		{
			// 덮친다. 쫓는 수가 셋이 되면 감독이 가장 뒤처진 개체를 대신 매복으로 돌린다
			lastKnownPos = player.position;
			memoryTimer = memoryTime;
			StartChase();
			return;
		}

		// 도착했으면 플레이어 쪽을 보고 기다린다
		if (HasArrived() && player != null)
		{
			Vector3 look = player.position - base.transform.position;
			look.y = 0f;
			if (look.sqrMagnitude > 0.01f)
			{
				base.transform.rotation = Quaternion.Slerp(base.transform.rotation,
					Quaternion.LookRotation(look), Time.deltaTime * 6f);
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
			StartChase();
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
			// 막 추격을 시작한 개체를 곧바로 매복으로 돌리지 않도록
			roleLockUntil = Time.time + roleLockTime;
		}
		ChangeState(State.Chase);
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

	/// <summary>감독이 흐린 지점으로 보낼 때 쓴다. 이미 쫓고 있거나 매복·기절 중이면 무시한다.</summary>
	public void ReceiveSignal(Vector3 signalPos)
	{
		if (currentState != State.Chase && currentState != State.Stun && currentState != State.Intercept)
		{
			RushToInvestigate(signalPos);
		}
	}

	private void MoveByStyle(Vector3 target)
	{
		if (isBerserking)
		{
			agent.SetDestination(target);
			return;
		}
		Vector3 destination = target;
		switch (style)
		{
		case ChaseStyle.Pinky_Predict:
			// 플레이어가 가고 있는 쪽 앞을 노린다
			destination = target + playerVelocity * predictLeadTime;
			break;
		case ChaseStyle.Inky_Tactical:
			// 플레이어 기준 측면으로 파고든다 (몬스터 기준이 아님)
			{
				Vector3 side = Vector3.Cross(Vector3.up, playerVelocity.sqrMagnitude > 0.1f
					? playerVelocity.normalized
					: (target - base.transform.position).normalized);
				float sign = (Vector3.Dot(side, base.transform.position - target) >= 0f) ? 1f : -1f;
				destination = target + side * (flankOffset * sign);
			}
			break;
		}
		agent.SetDestination(destination);
	}

	private void HandlePhysicsAndTimers()
	{
		if (damageTimer > 0f)
		{
			damageTimer -= Time.deltaTime;
		}
		if (isBerserking)
		{
			berserkTimer -= Time.deltaTime;
			if (berserkTimer <= 0f)
			{
				EndBerserk();
			}
		}
		if (currentState != State.Stun && !isJumping && agent.isOnOffMeshLink)
		{
			StartCoroutine(PerformJump());
		}
	}

	private void CheckBerserk()
	{
		if (!isBerserking && !(damageTimer > 0f) && Vector3.Distance(base.transform.position, player.position) < berserkTriggerRange)
		{
			StartBerserk();
		}
	}

	private void StartBerserk()
	{
		isBerserking = true;
		agent.speed = chaseSpeed * berserkSpeedScale;
		agent.acceleration = berserkAcceleration;
		berserkTimer = berserkDuration;
	}

	private void EndBerserk()
	{
		isBerserking = false;
		agent.speed = chaseSpeed;
		agent.acceleration = chaseAcceleration;
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
