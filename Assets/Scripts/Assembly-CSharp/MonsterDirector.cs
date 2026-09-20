using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조정자(Director) — 몬스터가 아니라 뒤에서 무리를 움직이는 보이지 않는 "두 번째 뇌".
///
/// 기획 (2026-09-14 확정):
///   - 이 게임에 숨기는 없다. 플레이어는 <b>달려서만</b> 피한다. 목표는 "포위망이 닫히기 전에 빠져나가기".
///   - <b>감독은 플레이어의 진짜 위치를 안다.</b> 대신 정보는 한 방향으로만 흐른다:
///     감독 → 몬스터에게는 <b>흐린 목적지</b>만 내려보낸다. 몬스터는 자기 눈에 보일 때만 정확한 위치로 쫓는다.
///   - 누가 발견하든 <b>사냥 팀은 3마리</b>(발견자 포함). 나머지는 평소대로 순찰.
///     발견자(추격) 1 + 가까운 2마리(차단). 탈출로 4개 중 3개를 막고 1개를 열어두는 느낌.
///   - 차단 몬스터는 뒤를 따라가지 않는다. 플레이어가 향하는 쪽 길을 <b>각자 다른 길로 돌아서</b> 막는다.
///   - 사냥 중 더 가까운 몬스터가 생기면 가장 먼 차단 팀원과 교대한다(플레이어가 도망친 쪽 구역 몬스터가 앞길로 올라온다).
///   - 속도는 거리에 따라: 가까우면 플레이어(11)보다 약간 느리게(10), 멀면 빠르게(최대 14). 똑바로 달리면 항상 떨칠 수 있다.
///   - 추격자는 모퉁이를 돌아도 바로 수색하지 않고 감독의 흐린 힌트로 <b>끈질김 시간</b>만큼 더 쫓는다(MonsterAI).
///   - 직접 추격은 최대 2마리. 같은 방향으로 붙어 가는 줄줄이는 계속 끊는다.
///
/// 씬에 배치할 필요 없음 — 첫 호출 시 자동 생성. 수치는 전역 몬스터 인스펙터 "6. 조정자" 칸을 따른다.
/// </summary>
public class MonsterDirector : MonoBehaviour
{
    private static MonsterDirector instance;
    private static bool isQuitting;

    public static MonsterDirector Instance
    {
        get
        {
            if (isQuitting) { return null; }
            if (instance == null)
            {
                GameObject go = new GameObject("~MonsterDirector");
                instance = go.AddComponent<MonsterDirector>();
            }
            return instance;
        }
    }

    /// <summary>차단 후보 한 길. AI 테스트 씬에 그대로 그린다.</summary>
    /// <summary>한 몬스터에게 배정된 "플레이어까지 가는 길". 목적지는 언제나 플레이어, 다른 것은 경로뿐이다.</summary>
    public class RouteInfo
    {
        public Vector3[] corners;      // 전체 경로(경유지가 있으면 두 구간을 이어 붙인 것)
        public bool detour;            // 경유지를 거치는가
        public Vector3 waypoint;
        public float overlap;          // 확정된 다른 경로들과의 최대 겹침률(수렴 구간 제외)
        public bool directChaser;      // 감독이 경로를 주지 않는 직행 추격자
        public int colorIndex;
    }

    public struct CutCandidate
    {
        public Vector3 point;       // 그 방향으로 바닥을 따라 가다 막히는 곳
        public Vector3 direction;   // 플레이어 → point
        public float reach;
        public float score;         // 이미 막힌 방향과 멀수록, 플레이어가 향하는 쪽일수록 높다
        public bool rejected;       // 너무 짧게 막혀 버려진 방향(벽·막다른 곳)
        public bool chosen;
        public string note;
    }

    // ── 감독만 아는 진짜 정보 (몬스터에게 그대로 넘기지 않는다) ──
    private Transform player;
    private Vector3 playerPos;
    private Vector3 playerVelocity;
    private Vector3 prevPlayerPos;
    private bool hasPrevPlayerPos;

    // ── 마지막 목격 (표시 + 사냥 종료 판단) ──
    private bool hasSighting;
    private Vector3 sightPosition;
    private Vector3 sightVelocity;
    private float sightTime;
    private MonsterAI sightSpotter;

    // ── 사냥 팀 ──
    private bool hunting;
    private readonly List<MonsterAI> team = new List<MonsterAI>();
    private readonly Dictionary<MonsterAI, Vector3> cutPoints = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, Vector3> cutWaypoints = new Dictionary<MonsterAI, Vector3>();   // 빙 돌아가는 경유 지점
    private readonly Dictionary<MonsterAI, float> rejoinBlockedUntil = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, Vector3> blurOffsets = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, float> blurRefreshAt = new Dictionary<MonsterAI, float>();
    private readonly List<MonsterAI> chasers = new List<MonsterAI>();
    private readonly Dictionary<MonsterAI, RouteInfo> routes = new Dictionary<MonsterAI, RouteInfo>();
    private int routeColorSeq;
    private string routeSummary = "";
    private readonly List<CutCandidate> lastCandidates = new List<CutCandidate>();
    private Vector3 candidateOrigin;
    private NavMeshPath pathBuffer;
    private float nextCoordinateTime;
    private float nextLayoutTime;
    private float nextTeamReviewTime;

    // ── 흩어짐(팩맨 스캐터) · 인계 · 복귀 ──
    private float huntPhaseStart;        // 지금 "조이기" 구간이 시작된 시각
    private float scatterUntil;          // 이 시각까지 흩어짐
    private int scatterCount;
    private int handovers;
    private int teleports;
    private float chaserOutOfZoneSince;
    private readonly Dictionary<MonsterAI, float> unseenSince = new Dictionary<MonsterAI, float>();
    private readonly List<Vector3> searchedSpots = new List<Vector3>();   // 이미 뒤진 곳 — 서로 공유
    private bool searchIssued;

    // ── 줄줄이 감지 ──
    private readonly Dictionary<MonsterAI, float> followTimers = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> followCooldownUntil = new Dictionary<MonsterAI, float>();
    private int followBreaks;
    private string layoutSummary = "";

    private const float CoordinateInterval = 0.25f;
    private const float TeamReviewInterval = 2f;
    private const float RejoinBlockTime = 5f;           // 팀에서 빠진 개체는 이 시간 동안 다시 부르지 않는다(들락날락 방지)
    private const float BlurRefreshTime = 4f;           // 흐림 방향을 바꾸는 주기 — 매번 바꾸면 몬스터가 흔들린다
    private const float MovingSpeedThreshold = 1.5f;
    private const float FollowMinSpeed = 2f;
    private const float FollowLateral = 3f;
    private const float FollowCooldown = 3f;
    private const float OverlapSampleStep = 2f;
    private const float OverlapNearDistance = 3f;
    private const float InfoGrace = 0.5f;                // 누군가 이 시간 안에 봤으면 정보가 살아 있다
    private const float SearchNear = 8f;                 // 수색 분산: 가까운 지점 거리
    private const float SearchFar = 18f;                 // 수색 분산: 먼 지점 거리
    private const float StopShortDistance = 20f;        // 알맞은 길이 없을 때: 자기 쪽 길에서 플레이어 이 거리 앞까지 다가간다

    // 차단 배정 비용 가중치
    private const float LengthWeight = 0.3f;            // 경로 1m당
    private const float AngleWeight = 20f;              // 각도 점수(0~1.5)
    private const float CrossingPenalty = 30f;          // 플레이어를 뚫고 가는 길
    private const float OverlapPenalty = 25f;           // 다른 팀원 길과 겹치는 비율 × 이 값
    private const float KeepBonus = 8f;                 // 지금 가던 지점이면 우대(우왕좌왕 방지)

    // ── AI 테스트 씬 표시용 ──
    public bool IsHunting => hunting;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => sightPosition;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => sightVelocity.magnitude > MovingSpeedThreshold ? sightVelocity.normalized : Vector3.zero;
    public string SightingSpotterName => sightSpotter != null ? sightSpotter.name : "-";
    public int DebugChaserCount => chasers.Count;
    public IReadOnlyList<MonsterAI> DebugTeam => team;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugOrderPoints => cutPoints;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugWaypoints => cutWaypoints;
    public IReadOnlyList<CutCandidate> DebugCutCandidates => lastCandidates;
    public Vector3 DebugCandidateOrigin => candidateOrigin;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugRoutes => routes;
    public float DebugConvergenceRadius => ConvergenceRadius;

    /// <summary>테스트 화면용 한 줄 요약: 직행/우회 · 겹침 · 최종 접근 여부.</summary>
    public string DebugRouteLabel(MonsterAI m)
    {
        if (m == null || !routes.TryGetValue(m, out RouteInfo r)) { return ""; }
        string kind = r.directChaser ? "직행(추격)" : (r.detour ? "우회" : "직행");
        string fin = (m.IsFinalApproach ? " ▶최종" : "");
        return kind + " ov" + r.overlap.ToString("F2") + fin;
    }
    public int DebugFollowBreaks => followBreaks;
    public bool IsScattering => Time.time < scatterUntil;
    public float ScatterRemaining => Mathf.Max(0f, scatterUntil - Time.time);
    public float HuntPhaseElapsed => hunting ? Time.time - huntPhaseStart : 0f;
    public int DebugScatterCount => scatterCount;
    public int DebugHandovers => handovers;
    public int DebugTeleports => teleports;
    public string DebugLayoutSummary => layoutSummary;

    public string DebugOrderLabel(MonsterAI m)
    {
        if (m == null || !team.Contains(m)) { return ""; }
        return cutPoints.ContainsKey(m) ? "팀·차단" : "팀";
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnApplicationQuit() { isQuitting = true; }

    /// <summary>게임 재시작 등으로 사냥을 강제 종료하고 기록도 지운다.</summary>
    public void AbortHunt()
    {
        EndHunt("게임 초기화");
        hasSighting = false;
        sightSpotter = null;
        lastCandidates.Clear();
        followTimers.Clear();
        followCooldownUntil.Clear();
        rejoinBlockedUntil.Clear();
        layoutSummary = "";
    }

    // ────────────────────────────────────────────────
    //  보고
    // ────────────────────────────────────────────────

    /// <summary>몬스터가 플레이어를 시야로 보고 있는 동안 매 프레임 호출한다. 발견자는 반드시 팀에 들어간다.</summary>
    public void ReportSighting(MonsterAI spotter, Vector3 seenPosition)
    {
        if (spotter == null) { return; }
        // 흩어지는 동안 팀 밖(또는 물러나는) 몬스터의 발견은 무시한다 — 팩맨의 스캐터와 같은 규칙(기획 2026-09-18)
        if (IsScattering && !team.Contains(spotter)) { return; }
        hasSighting = true;
        sightPosition = seenPosition;
        sightVelocity = playerVelocity;
        sightTime = Time.time;
        sightSpotter = spotter;
        searchIssued = false;

        if (!hunting)
        {
            StartHunt(spotter);
        }
        else if (!team.Contains(spotter))
        {
            AddToTeam(spotter, "직접 발견");
        }
    }

    /// <summary>
    /// 과녁 파괴 경보. 사냥 중이 아니면 가장 가까운 2마리를 발사 위치 근처(흐리게)로 확인 보낸다.
    /// 경보만으로 사냥을 시작하지는 않는다 — 몬스터가 실제로 봐야 팀이 꾸려진다.
    /// </summary>
    public void ReportTargetAlarm(Vector3 shotPosition, float shotTime)
    {
        if (hunting) { return; }
        foreach (MonsterAI m in NearestAvailable(2, null, shotPosition))
        {
            m.CommandSearch(BlurOnNav(shotPosition, HintBlurRadius * 1.5f));
        }
        Log("<color=orange><b>[과녁 경보]</b></color> 가까운 2마리가 발사 위치 근처로 확인하러 감");
    }

    /// <summary>추격자가 플레이어를 놓쳤을 때(모퉁이 등) 따라갈 흐린 목적지. 감독 → 몬스터 한 방향.</summary>
    public bool TryGetPursuitHint(MonsterAI m, out Vector3 hint)
    {
        hint = Vector3.zero;
        if (m == null || player == null) { return false; }
        hint = BlurOnNav(playerPos, PursuitHintBlur);
        return true;
    }

    // ────────────────────────────────────────────────
    //  추격 허가 (최대 2마리)
    // ────────────────────────────────────────────────

    /// <summary>
    /// 몬스터가 플레이어를 보고 추격에 들어가려 할 때 먼저 묻는다. 허가되면 true.
    ///   - 자리가 남아 있으면 허가
    ///   - 전역 몬스터는 가장 먼 구역 추격자를 차단으로 돌리고 허가
    ///   - 차단 대기 중 덮치려는 개체: 가장 먼 구역 추격자보다 swapDistanceMargin 이상 가까울 때만 교대
    ///   - 그 밖: 거부하고 차단 역할로 — 추격을 거치지 않으니 한 순간도 3마리가 되지 않는다
    /// </summary>
    public bool RequestChase(MonsterAI m)
    {
        if (m == null) { return false; }
        // 흩어지는 동안에는 새로 추격에 들어가지 않는다(이미 쫓고 있던 개체만 계속)
        if (IsScattering && m.CurrentState != MonsterAI.State.Chase) { return false; }
        RefreshChasers();

        int others = 0;
        foreach (MonsterAI c in chasers) { if (c != m) { others++; } }
        if (others < MaxChasers) { return true; }

        MonsterAI farthest = FarthestZoneChaser(m);
        if (m.role == MonsterAI.MonsterRole.Global_Stalker)
        {
            if (farthest != null && farthest.CanBeDemoted)
            {
                MakeCutter(farthest, "전역 몬스터에게 추격 자리 양보");
                return true;
            }
        }
        else if (m.CurrentState == MonsterAI.State.Intercept)
        {
            if (farthest != null && farthest.CanBeDemoted
                && HorizontalDistance(m.transform.position, playerPos) + SwapMargin
                   < HorizontalDistance(farthest.transform.position, playerPos))
            {
                MakeCutter(farthest, "더 가까운 차단 몬스터와 교대");
                return true;
            }
            return false;   // 자리를 지킨다
        }

        if (team.Contains(m) && m.CurrentState != MonsterAI.State.Intercept)
        {
            MakeCutter(m, "추격 인원 가득 — 막는 역할로");
        }
        return false;
    }

    // ────────────────────────────────────────────────
    //  갱신
    // ────────────────────────────────────────────────

    private void Update()
    {
        if (!TrackPlayer()) { return; }
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) { return; }

        float now = Time.time;
        if (now >= nextCoordinateTime)
        {
            nextCoordinateTime = now + CoordinateInterval;
            RefreshChasers();
            UpdateHuntSpeeds();
            UpdateTeleportReturns();
            if (hunting)
            {
                CleanTeam();
                EnforceChaserCap();
                BreakFollowing();
                TryHandoverChase();
            }
        }

        if (!hunting) { return; }

        if (ShouldEndHunt())
        {
            EndHunt("팀 전원이 놓침");
            return;
        }

        // 흩어짐: 조이기 20초 → 흩어짐 6초 (팩맨 스캐터). 추격자만 남고 나머지는 자기 구역으로 물러난다
        UpdateScatter();
        if (IsScattering)
        {
            layoutSummary = $"흩어짐 {ScatterRemaining:F1}초 남음 · 추격자만 계속 쫓는 중";
            return;
        }

        // 기획 A안(2026-09-15): 아무도 못 보고 추격자도 모두 포기했으면 감독은 진짜 위치를 더 이상 알려주지 않는다.
        // 막는 팀원은 마지막으로 받은 곳을 확인하고, 사냥 종료와 함께 돌아간다 — 끝까지 달리면 떨칠 수 있다.
        // (QA S6: 막는 팀원이 진짜 위치를 계속 받아 따돌린 곳까지 찾아와 사냥이 끝나지 않았다)
        if (!InfoLive())
        {
            layoutSummary = $"정보 끊김 — 팀 {team.Count}마리가 마지막 지점 확인 중 (사냥 종료까지 {Mathf.Max(0f, HuntMemory - (now - sightTime)):F1}초)";
            return;
        }
        if (now >= nextTeamReviewTime)
        {
            nextTeamReviewTime = now + TeamReviewInterval;
            ReviewTeam();
        }
        if (now >= nextLayoutTime)
        {
            nextLayoutTime = now + LayoutRefresh;
            LayoutTeamByRoute();
        }
    }

    /// <summary>감독은 진짜 위치·속도를 매 프레임 안다. 이 값은 흐린 목적지를 만드는 데만 쓴다.</summary>
    private bool TrackPlayer()
    {
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) { return false; }
            player = go.transform;
            hasPrevPlayerPos = false;
        }
        Vector3 pos = player.position;
        if (hasPrevPlayerPos && Time.deltaTime > 0f)
        {
            Vector3 v = (pos - prevPlayerPos) / Time.deltaTime;
            v.y = 0f;
            playerVelocity = Vector3.Lerp(playerVelocity, v, 0.2f);
        }
        prevPlayerPos = pos;
        hasPrevPlayerPos = true;
        playerPos = pos;
        return true;
    }

    /// <summary>
    /// 사냥 속도(기획 A안): 추격·차단·달려가는 수색 중인 몬스터는 플레이어와의 거리에 따라
    /// 가까우면 느리게(huntNearSpeed), 멀면 빠르게(huntFarSpeed). 똑바로 달리면 떨칠 수 있지만 먼 몬스터는 금방 따라붙는다.
    /// </summary>
    private void UpdateHuntSpeeds()
    {
        float nearD = NearDistance, farD = Mathf.Max(NearDistance + 0.1f, FarDistance);
        // 정보가 끊긴 뒤에는 속도 계산도 진짜 위치가 아니라 마지막 목격 위치 기준
        Vector3 reference = (hunting && !InfoLive()) ? sightPosition : playerPos;
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m == null) { continue; }
            MonsterAI.State s = m.CurrentState;
            bool huntMove = s == MonsterAI.State.Chase || s == MonsterAI.State.Intercept
                || (s == MonsterAI.State.Investigate && m.IsRushing);
            if (!huntMove) { m.SetHuntSpeed(-1f); continue; }
            float t = Mathf.InverseLerp(nearD, farD, HorizontalDistance(m.transform.position, reference));
            m.SetHuntSpeed(Mathf.Lerp(NearSpeed, FarSpeed, t));
        }
    }

    // ────────────────────────────────────────────────
    //  사냥 팀 (발견자 포함 3마리)
    // ────────────────────────────────────────────────

    private void StartHunt(MonsterAI spotter)
    {
        hunting = true;
        huntPhaseStart = Time.time;
        scatterUntil = 0f;
        chaserOutOfZoneSince = 0f;
        searchedSpots.Clear();
        searchIssued = false;
        team.Clear();
        cutPoints.Clear();
        cutWaypoints.Clear();
        team.Add(spotter);
        foreach (MonsterAI m in NearestAvailable(TeamSize - 1, team, playerPos)) { team.Add(m); }
        nextLayoutTime = 0f;
        nextTeamReviewTime = Time.time + TeamReviewInterval;
        Log($"<color=orange><b>[사냥 시작]</b></color> {spotter.name} 발견 → 팀 {TeamNames()}");
    }

    private void AddToTeam(MonsterAI m, string reason)
    {
        if (team.Count >= TeamSize)
        {
            // 가득: 쫓고 있지 않은 팀원 중 플레이어에게서 가장 먼 개체가 자리를 비킨다
            MonsterAI worst = null;
            float worstLen = -1f;
            foreach (MonsterAI t in team)
            {
                if (t == null || t.CurrentState == MonsterAI.State.Chase) { continue; }
                float len = RouteLengthToPlayer(t);
                if (len > worstLen) { worstLen = len; worst = t; }
            }
            if (worst == null) { return; }
            ReleaseMember(worst, $"{m.name}에게 자리 넘김");
        }
        team.Add(m);
        nextLayoutTime = 0f;
        Log($"<color=orange><b>[팀 합류]</b></color> {m.name} — {reason} → 팀 {TeamNames()}");
    }

    private void ReleaseMember(MonsterAI m, string reason)
    {
        team.Remove(m);
        cutPoints.Remove(m);
        cutWaypoints.Remove(m);
        rejoinBlockedUntil[m] = Time.time + RejoinBlockTime;
        if (m.CurrentState == MonsterAI.State.Intercept || m.CurrentState == MonsterAI.State.Investigate) { m.CommandRelease(); }
        Log($"<color=grey><b>[팀 제외]</b></color> {m.name} — {reason}");
    }

    /// <summary>없어진 개체·포기한 개체를 팀에서 뺀다. 빈자리는 가장 가까운 순찰 몬스터로 채운다 — 팀은 늘 3마리.</summary>
    private void CleanTeam()
    {
        for (int i = team.Count - 1; i >= 0; i--)
        {
            MonsterAI m = team[i];
            if (m == null || !m.isActiveAndEnabled)
            {
                team.RemoveAt(i);
                continue;
            }
            if (m.IsGivingUp)
            {
                team.RemoveAt(i);
                cutPoints.Remove(m);
                cutWaypoints.Remove(m);
                rejoinBlockedUntil[m] = Time.time + RejoinBlockTime;
                Log($"<color=grey><b>[팀 제외]</b></color> {m.name} — 끈질김이 다 떨어져 포기");
                if (!searchIssued)
                {
                    searchIssued = true;
                    SpreadSearch();
                }
            }
        }
        if (team.Count > 0 && team.Count < TeamSize && InfoLive())
        {
            foreach (MonsterAI m in NearestAvailable(TeamSize - team.Count, team, playerPos))
            {
                team.Add(m);
                nextLayoutTime = 0f;
                Log($"<color=orange><b>[팀 합류]</b></color> {m.name} — 빈자리 채움 → 팀 {TeamNames()}");
            }
        }
    }

    /// <summary>
    /// 플레이어가 도망친 쪽에 더 가까운 순찰 몬스터가 생기면, 가장 먼 차단 팀원과 교대한다.
    /// 예: 남쪽으로 도망치면 남쪽 구역 몬스터가 팀에 들어와 앞길로 올라온다.
    /// </summary>
    private void ReviewTeam()
    {
        MonsterAI farCutter = null;
        float farLen = -1f;
        foreach (MonsterAI t in team)
        {
            if (t == null || t.CurrentState == MonsterAI.State.Chase || t.IsInStun) { continue; }
            float len = RouteLengthToPlayer(t);
            if (len > farLen) { farLen = len; farCutter = t; }
        }
        if (farCutter == null) { return; }

        List<MonsterAI> outsiders = NearestAvailable(1, team, playerPos);
        if (outsiders.Count == 0) { return; }
        float outLen = RouteLengthToPlayer(outsiders[0]);
        if (outLen < farLen * TeamSwapRatio)
        {
            ReleaseMember(farCutter, $"{outsiders[0].name}가 더 가까움 ({outLen:F0}m < {farLen:F0}m)");
            team.Add(outsiders[0]);
            nextLayoutTime = 0f;
            Log($"<color=orange><b>[팀 교대]</b></color> {outsiders[0].name} 합류 → 팀 {TeamNames()}");
        }
    }

    /// <summary>
    /// 감독이 진짜 위치를 몬스터에게 흘려보내도 되는 상태인가:
    /// 팀에 추격자(보고 있거나 끈질김으로 추적 중)가 있거나, 누군가 방금(0.5초 안) 봤을 때만.
    /// </summary>
    private bool InfoLive()
    {
        foreach (MonsterAI t in team)
        {
            if (t != null && t.CurrentState == MonsterAI.State.Chase) { return true; }
        }
        return hasSighting && Time.time - sightTime <= InfoGrace;
    }

    /// <summary>
    /// 흩어짐(팩맨 스캐터): 조이기 구간이 scatterAfter를 넘으면 scatterTime 동안 흩어진다.
    /// 추격자는 계속 쫓고, 막던 팀원은 자기 구역으로 빠르게 물러나며 scatterRejoinBlock 동안 다시 불려오지 않는다.
    /// 추격이 길어질수록 다 같이 한 구역으로 몰리던 문제를 주기적으로 리셋한다.
    /// </summary>
    private void UpdateScatter()
    {
        float now = Time.time;
        if (now < scatterUntil) { return; }
        if (scatterUntil > 0f)
        {
            // 방금 끝났다 → 다시 조이기 시작
            scatterUntil = 0f;
            huntPhaseStart = now;
            nextLayoutTime = 0f;
            nextTeamReviewTime = now;
            Log("<color=orange><b>[재소집]</b></color> 흩어짐 끝 — 가까운 몬스터로 다시 포위");
            return;
        }
        if (now - huntPhaseStart < ScatterAfter) { return; }

        scatterUntil = now + ScatterTime;
        scatterCount++;
        List<string> names = new List<string>();
        foreach (MonsterAI m in new List<MonsterAI>(team))
        {
            if (m == null || m.CurrentState == MonsterAI.State.Chase) { continue; }   // 추격자는 계속 쫓는다
            names.Add(m.name.Replace("Monster_", ""));
            team.Remove(m);
            cutPoints.Remove(m);
            cutWaypoints.Remove(m);
            rejoinBlockedUntil[m] = now + ScatterRejoinBlock;
            m.CommandGoHome(ScatterRejoinBlock);
        }
        Log($"<color=cyan><b>[흩어짐]</b></color> {ScatterTime:F0}초 — 물러남: {string.Join(", ", names.ToArray())}");
    }

    /// <summary>
    /// 추격 인계(기획 2026-09-18): 구역 몬스터가 자기 구역 밖까지 쫓아가면, 플레이어가 있는 구역의 몬스터가 추격을 이어받고
    /// 원래 추격자는 자기 구역으로 돌아간다. 한 구역이 오래 비는 것을 막는다. 전역 몬스터는 인계하지 않는다.
    /// </summary>
    private void TryHandoverChase()
    {
        MonsterAI chaser = null;
        foreach (MonsterAI t in team)
        {
            if (t != null && t.CurrentState == MonsterAI.State.Chase) { chaser = t; break; }
        }
        if (chaser == null || chaser.role == MonsterAI.MonsterRole.Global_Stalker || chaser.zoneCenter == null)
        {
            chaserOutOfZoneSince = 0f;
            return;
        }
        float zoneLimit = Mathf.Min(chaser.zoneRadius, 60f);
        bool outside = HorizontalDistance(playerPos, chaser.zoneCenter.position) > zoneLimit;
        if (!outside) { chaserOutOfZoneSince = 0f; return; }
        if (chaserOutOfZoneSince <= 0f) { chaserOutOfZoneSince = Time.time; return; }
        if (Time.time - chaserOutOfZoneSince < HandoverDelay) { return; }

        MonsterAI heir = ZoneOwner(playerPos, chaser);
        if (heir == null || !heir.IsAvailableForOrders || heir.IsGivingUp) { return; }

        chaserOutOfZoneSince = 0f;
        if (!team.Contains(heir)) { AddToTeam(heir, "추격 인계"); }
        heir.CommandSearch(BlurOnNav(playerPos, PursuitHintBlur));
        team.Remove(chaser);
        cutPoints.Remove(chaser);
        cutWaypoints.Remove(chaser);
        rejoinBlockedUntil[chaser] = Time.time + RejoinBlockTime;
        chaser.CommandGoHome(RejoinBlockTime);
        handovers++;
        nextLayoutTime = 0f;
        Log($"<color=orange><b>[추격 인계]</b></color> {chaser.name} → {heir.name} (플레이어가 구역을 벗어남)");
    }

    /// <summary>그 위치가 속한 구역의 몬스터(구역 중심이 가장 가까운 개체).</summary>
    private static MonsterAI ZoneOwner(Vector3 pos, MonsterAI exclude)
    {
        MonsterAI best = null;
        float bestD = float.MaxValue;
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m == null || m == exclude || m.role != MonsterAI.MonsterRole.Zone_Defender || m.zoneCenter == null) { continue; }
            float dd = HorizontalDistance(pos, m.zoneCenter.position);
            if (dd < bestD) { bestD = dd; best = m; }
        }
        return best;
    }

    /// <summary>
    /// 복귀 순간이동: 구역으로 돌아가는 중인 몬스터가 <b>플레이어에게 보이지 않고</b> 멀리 있으며
    /// 일정 시간 그 상태가 이어지면 자기 구역으로 옮긴다. 도착 지점도 보이지 않는 곳이어야 한다.
    /// 맵 한쪽이 오래 비는 것을 막는 장치(L4D의 활동 구역 재배치와 같은 발상).
    /// </summary>
    private void UpdateTeleportReturns()
    {
        if (player == null) { return; }
        float now = Time.time;
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            bool idle = m != null && (m.CurrentState == MonsterAI.State.Return || m.CurrentState == MonsterAI.State.Patrol);
            Vector3 homeCenter = m != null && m.zoneCenter != null ? m.zoneCenter.position : Vector3.zero;
            bool farFromHome = m != null && m.zoneCenter != null && HorizontalDistance(m.transform.position, homeCenter) > 25f;
            if (m == null || m.IsInStun || team.Contains(m) || !idle || !farFromHome)
            {
                unseenSince.Remove(m);
                continue;
            }
            if (HorizontalDistance(m.transform.position, playerPos) < TeleportMinDistance || PlayerCanSee(m.transform.position + Vector3.up * 1.2f))
            {
                unseenSince.Remove(m);
                continue;
            }
            float since;
            if (!unseenSince.TryGetValue(m, out since)) { unseenSince[m] = now; continue; }
            if (now - since < TeleportUnseenTime) { continue; }

            Vector3 home = m.zoneCenter != null ? m.zoneCenter.position : m.transform.position;
            if (!NavMesh.SamplePosition(home, out NavMeshHit hit, 8f, NavMesh.AllAreas)) { continue; }
            if (HorizontalDistance(hit.position, playerPos) < TeleportMinDistance || PlayerCanSee(hit.position + Vector3.up * 1.2f)) { continue; }

            m.TeleportTo(hit.position);
            unseenSince.Remove(m);
            teleports++;
            Log($"<color=grey><b>[복귀 순간이동]</b></color> {m.name} — 보이지 않는 곳에서 구역으로");
        }
    }

    /// <summary>플레이어가 그 지점을 볼 수 있는가(시야각 + 가림). 순간이동을 들키지 않게 하는 검사.</summary>
    private bool PlayerCanSee(Vector3 point)
    {
        if (player == null) { return false; }
        Vector3 eye = playerPos + Vector3.up * 1.5f;
        Vector3 to = point - eye;
        if (Vector3.Angle(player.forward, to) > PlayerViewAngle * 0.5f) { return false; }
        MonsterAI g = FindGlobal();
        int mask = g != null ? g.obstacleMask.value : ~0;
        return !Physics.Linecast(eye, point, mask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 수색 분산(공유): 추격자가 포기하면 남은 팀원에게 서로 다른 수색 지점을 준다.
    /// 이미 뒤진 곳은 목록으로 공유해 다시 가지 않는다(Game AI Pro의 수색 규칙).
    /// </summary>
    private void SpreadSearch()
    {
        List<MonsterAI> searchers = new List<MonsterAI>();
        foreach (MonsterAI m in team)
        {
            if (m != null && m.IsAvailableForOrders && !m.IsGivingUp) { searchers.Add(m); }
        }
        if (searchers.Count == 0) { return; }

        Vector3 p = sightPosition;
        Vector3 dir = SightingDirection;
        List<Vector3> raw = new List<Vector3>();
        if (dir.sqrMagnitude > 0.01f)
        {
            raw.Add(p + dir * SearchNear);
            raw.Add(p + Quaternion.Euler(0f, 50f, 0f) * dir * SearchNear);
            raw.Add(p + Quaternion.Euler(0f, -50f, 0f) * dir * SearchNear);
            raw.Add(p + dir * SearchFar);
        }
        for (int i = 0; i < 6; i++) { raw.Add(p + Quaternion.Euler(0f, 60f * i, 0f) * Vector3.forward * SearchNear); }

        int given = 0;
        foreach (MonsterAI m in searchers)
        {
            Vector3 best = Vector3.zero;
            float bestCost = float.MaxValue;
            foreach (Vector3 r in raw)
            {
                if (!NavMesh.SamplePosition(r, out NavMeshHit hit, 5f, NavMesh.AllAreas)) { continue; }
                bool old = false;
                foreach (Vector3 done in searchedSpots) { if (HorizontalDistance(done, hit.position) < 10f) { old = true; break; } }
                if (old) { continue; }
                if (!BuildRoute(m.transform.position, hit.position, out _, out float len)) { continue; }
                if (len < bestCost) { bestCost = len; best = hit.position; }
            }
            if (bestCost == float.MaxValue) { continue; }
            m.CommandSearch(BlurOnNav(best, HintBlurRadius));
            cutPoints.Remove(m);
            cutWaypoints.Remove(m);
            searchedSpots.Add(best);
            given++;
        }
        if (given > 0) { Log($"<color=yellow><b>[수색 분산]</b></color> {given}마리에게 서로 다른 지점 (이미 뒤진 {searchedSpots.Count}곳 제외)"); }
    }

    private bool ShouldEndHunt()
    {
        if (team.Count == 0) { return true; }
        foreach (MonsterAI t in team)
        {
            if (t != null && t.CurrentState == MonsterAI.State.Chase) { return false; }
        }
        return Time.time - sightTime > HuntMemory;
    }

    private void EndHunt(string reason)
    {
        if (hunting) { Log($"<color=grey><b>[사냥 종료]</b></color> {reason}"); }
        foreach (MonsterAI m in team)
        {
            if (m == null) { continue; }
            if (m.CurrentState == MonsterAI.State.Intercept || m.CurrentState == MonsterAI.State.Investigate) { m.CommandRelease(); }
        }
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.CurrentState != MonsterAI.State.Chase) { m.ResetPursuit(); }
        }
        team.Clear();
        cutPoints.Clear();
        cutWaypoints.Clear();
        followTimers.Clear();
        searchedSpots.Clear();
        scatterUntil = 0f;
        hunting = false;
    }

    /// <summary>지금 명령을 받을 수 있는 몬스터 중 target까지 실제 경로가 짧은 순서로 count마리.</summary>
    private List<MonsterAI> NearestAvailable(int count, List<MonsterAI> exclude, Vector3 target)
    {
        List<KeyValuePair<float, MonsterAI>> list = new List<KeyValuePair<float, MonsterAI>>();
        if (count <= 0) { return new List<MonsterAI>(); }
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m == null || !m.IsAvailableForOrders || m.IsGivingUp) { continue; }
            if (exclude != null && exclude.Contains(m)) { continue; }
            if (rejoinBlockedUntil.TryGetValue(m, out float until) && Time.time < until) { continue; }
            if (!BuildRoute(m.transform.position, target, out _, out float len)) { continue; }
            list.Add(new KeyValuePair<float, MonsterAI>(len, m));
        }
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        List<MonsterAI> result = new List<MonsterAI>();
        for (int i = 0; i < list.Count && i < count; i++) { result.Add(list[i].Value); }
        return result;
    }

    // ────────────────────────────────────────────────
    //  추격 인원 안전장치
    // ────────────────────────────────────────────────

    private void EnforceChaserCap()
    {
        int guard = 0;
        while (chasers.Count > MaxChasers && guard++ < 5)
        {
            MonsterAI farthest = FarthestZoneChaser(null);
            if (farthest == null || !farthest.CanBeDemoted) { break; }
            MakeCutter(farthest, $"추격 {chasers.Count}마리 초과");
            RefreshChasers();
        }
    }

    private void RefreshChasers()
    {
        chasers.Clear();
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.CurrentState == MonsterAI.State.Chase) { chasers.Add(m); }
        }
    }

    private MonsterAI FarthestZoneChaser(MonsterAI exclude)
    {
        MonsterAI best = null;
        float bestDist = -1f;
        foreach (MonsterAI c in chasers)
        {
            if (c == null || c == exclude || c.role != MonsterAI.MonsterRole.Zone_Defender) { continue; }
            float d = HorizontalDistance(c.transform.position, playerPos);
            if (d > bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    /// <summary>추격자를 막는 역할로 돌린다. 우선 자기 쪽 길로 다가가게 하고, 곧바로 차단 배치를 다시 계산한다.</summary>
    private void MakeCutter(MonsterAI m, string reason)
    {
        if (!team.Contains(m)) { AddToTeam(m, reason); }
        // 강등이 아니다(기획 2026-09-20). 계속 플레이어를 쫓되, 어느 길로 갈지는 다음 배정에서 정한다.
        // 예전처럼 플레이어에게서 떨어진 지점으로 보내지 않는다.
        m.CommandPursue(Vector3.zero, false);
        cutWaypoints.Remove(m);
        cutPoints[m] = playerPos;
        RefreshChasers();
        nextLayoutTime = 0f;
        Log($"<color=magenta><b>[우회 전환]</b></color> {m.name} — {reason}");
    }

    // ────────────────────────────────────────────────
    //  줄줄이 감지
    // ────────────────────────────────────────────────

    /// <summary>
    /// 같은 방향으로 움직이는 몬스터 바로 뒤(followDistance 안, 같은 줄)에 followHoldTime 이상 붙어 가면 뒤쪽을 떼어낸다.
    ///   - 추격자끼리 줄: 뒤쪽 구역 추격자를 막는 역할로(전역은 비키지 않는다)
    ///   - 차단 이동 중: 차단 배치를 즉시 다시 계산한다(다른 팀원 길과 겹치는 길은 비싸다)
    /// </summary>
    private void BreakFollowing()
    {
        List<MonsterAI> all = MonsterAI.activeMonsters;
        float maxDist = FollowDistance;
        for (int i = 0; i < all.Count; i++)
        {
            MonsterAI trail = all[i];
            if (trail == null) { continue; }
            MonsterAI lead = FindLeader(trail, all, maxDist);
            if (lead == null) { followTimers.Remove(trail); continue; }

            float t = (followTimers.TryGetValue(trail, out float v) ? v : 0f) + CoordinateInterval;
            followTimers[trail] = t;
            if (t < FollowHoldTime) { continue; }
            if (followCooldownUntil.TryGetValue(trail, out float until) && Time.time < until) { continue; }

            if (BreakFollower(trail, lead))
            {
                followTimers.Remove(trail);
                followCooldownUntil[trail] = Time.time + FollowCooldown;
                followBreaks++;
            }
        }
    }

    private static MonsterAI FindLeader(MonsterAI trail, List<MonsterAI> all, float maxDist)
    {
        if (trail.IsInStun) { return null; }
        Vector3 tv = trail.PlanarVelocity;
        if (tv.magnitude < FollowMinSpeed) { return null; }
        Vector3 heading = tv.normalized;

        foreach (MonsterAI lead in all)
        {
            if (lead == null || lead == trail || lead.IsInStun) { continue; }
            Vector3 lv = lead.PlanarVelocity;
            if (lv.magnitude < FollowMinSpeed || Vector3.Dot(lv.normalized, heading) < 0.8f) { continue; }
            Vector3 gap = lead.transform.position - trail.transform.position;
            gap.y = 0f;
            float ahead = Vector3.Dot(gap, heading);
            if (ahead <= 0.5f || ahead > maxDist) { continue; }
            if ((gap - heading * ahead).magnitude <= FollowLateral) { return lead; }
        }
        return null;
    }

    private bool BreakFollower(MonsterAI trail, MonsterAI lead)
    {
        if (!team.Contains(trail)) { return false; }
        string who = $"{trail.name}가 {lead.name} 뒤를 따라감";
        if (trail.CurrentState == MonsterAI.State.Chase)
        {
            if (lead.CurrentState != MonsterAI.State.Chase || !trail.CanBeDemoted) { return false; }
            MakeCutter(trail, $"줄줄이 — {who}");
            return true;
        }
        if (trail.CurrentState == MonsterAI.State.Intercept)
        {
            nextLayoutTime = 0f;
            Log($"<color=magenta><b>[줄줄이]</b></color> {who} → 차단 배치 다시 계산");
            return true;
        }
        return false;
    }

    // ────────────────────────────────────────────────
    //  차단 배치 — "내가 향하는 쪽을, 각자 다른 길로"
    // ────────────────────────────────────────────────

    /// <summary>
    /// 추격 중이 아닌 팀원(차단 역할)에게 막을 곳과 가는 길을 준다 — 기획 2026-09-17 "추격 1 · 우회 2".
    ///
    /// 1) 후보: 플레이어에게서 8방향으로 뻗어 막히는 곳.
    /// 2) 각 팀원 × 후보마다 <b>가는 길</b>을 짠다(<see cref="PlanApproach"/>).
    ///    최단 경로가 플레이어를 뚫고 가면, 경유 지점을 들러 <b>빙 돌아가는</b> 길을 찾는다.
    ///    돌아가는 데 detourTimeLimit(기본 8초)을 넘으면 그 조합은 쓰지 않는다.
    /// 3) 들어오는 쪽을 갈라놓는다: 추격자가 오는 쪽과 sideAngleMin(기본 90°) 이상,
    ///    차단 몬스터끼리도 그만큼 다른 쪽. 만족하는 조합이 없으면 한 단계씩 조건을 푼다.
    /// 4) 비용 = 도착 시간 − (플레이어가 향하는 쪽 + 추격자와 다른 쪽 점수) − 가던 지점 유지.
    /// 5) 갈 곳이 없는 팀원은 자기 쪽 길로 플레이어 앞까지 접근.
    /// </summary>
    private void LayoutTeam()
    {
        RefreshChasers();
        List<MonsterAI> cutters = new List<MonsterAI>();
        foreach (MonsterAI m in team)
        {
            if (m != null && m.IsAvailableForOrders && !m.IsGivingUp) { cutters.Add(m); }
        }
        foreach (MonsterAI m in new List<MonsterAI>(cutPoints.Keys))
        {
            if (!cutters.Contains(m)) { cutPoints.Remove(m); cutWaypoints.Remove(m); }
        }
        if (cutters.Count == 0)
        {
            layoutSummary = $"팀 {team.Count}마리 모두 추격 중";
            return;
        }

        candidateOrigin = NavMesh.SamplePosition(playerPos, out NavMeshHit ph, 3f, NavMesh.AllAreas) ? ph.position : playerPos;
        BuildCandidates(candidateOrigin);
        Vector3 heading = PlayerHeading();
        Vector3 chaserSide = ChaserSide();

        int n = cutters.Count;
        int s = lastCandidates.Count;
        bool[] reachable = new bool[n * s];
        bool[] viaWay = new bool[n * s];
        Vector3[] ways = new Vector3[n * s];
        float[] eta = new float[n * s];
        int valid = 0, unreachable = 0;
        for (int ci = 0; ci < s; ci++)
        {
            if (lastCandidates[ci].rejected) { continue; }
            valid++;
            for (int mi = 0; mi < n; mi++)
            {
                int idx = mi * s + ci;
                reachable[idx] = PlanApproach(cutters[mi], lastCandidates[ci].point, out ways[idx], out viaWay[idx], out eta[idx]);
                if (!reachable[idx]) { unreachable++; }
            }
        }

        List<Vector3> takenSides = new List<Vector3>();
        bool[] mTaken = new bool[n];
        int assigned = 0, detours = 0, relaxed = 0;
        for (int pick = 0; pick < n; pick++)
        {
            int bm = -1, bc = -1, bestStrict = 3;
            float bestCost = float.MaxValue;
            // 3: 추격자와 90°+ & 서로 90°+ / 2: 추격자 90°+ & 서로 60°+ / 1: 추격자 60°+ / 0: 제한 없음
            // 좁은 곳에서는 후보 방향이 서너 개뿐이라 한 번에 풀지 않고 단계적으로 완화한다(2026-09-17 진단)
            for (int strict = 3; strict >= 0 && bm < 0; strict--)
            {
                float chaserMin = strict >= 2 ? SideAngleMin : (strict == 1 ? SideAngleMin * 0.66f : 0f);
                float peerMin = strict >= 3 ? SideAngleMin : (strict == 2 ? SideAngleMin * 0.66f : 0f);
                for (int ci = 0; ci < s; ci++)
                {
                    CutCandidate c = lastCandidates[ci];
                    if (c.rejected || c.chosen) { continue; }
                    if (chaserMin > 0f && chaserSide.sqrMagnitude > 0.01f && Vector3.Angle(c.direction, chaserSide) < chaserMin) { continue; }
                    if (peerMin > 0f)
                    {
                        bool clash = false;
                        foreach (Vector3 t in takenSides) { if (Vector3.Angle(c.direction, t) < peerMin) { clash = true; break; } }
                        if (clash) { continue; }
                    }
                    for (int mi = 0; mi < n; mi++)
                    {
                        int idx = mi * s + ci;
                        if (mTaken[mi] || !reachable[idx]) { continue; }
                        float score = 0f;
                        if (heading.sqrMagnitude > 0.01f) { score += Vector3.Dot(c.direction, heading) * 2f; }
                        if (chaserSide.sqrMagnitude > 0.01f) { score += Vector3.Angle(c.direction, chaserSide) / 180f * 3f; }
                        bool keeping = cutPoints.TryGetValue(cutters[mi], out Vector3 cur) && HorizontalDistance(cur, c.point) < HintBlurRadius + 4f;
                        float cost = eta[idx] - score - (keeping ? 1.5f : 0f);
                        if (cost < bestCost) { bestCost = cost; bm = mi; bc = ci; bestStrict = strict; }
                    }
                }
            }
            if (bm < 0) { break; }

            CutCandidate chosen = lastCandidates[bc];
            chosen.chosen = true;
            lastCandidates[bc] = chosen;
            takenSides.Add(chosen.direction);
            mTaken[bm] = true;
            assigned++;
            if (bestStrict < 3) { relaxed++; }
            int best = bm * s + bc;
            if (viaWay[best]) { detours++; }
            IssueCut(cutters[bm], chosen.point, ways[best], viaWay[best]);
        }

        int approach = 0;
        for (int mi = 0; mi < n; mi++)
        {
            if (mTaken[mi]) { continue; }
            IssueCut(cutters[mi], StopShortPoint(cutters[mi]), Vector3.zero, false);
            approach++;
        }

        layoutSummary = $"팀 {team.Count} (추격 {chasers.Count}) · 후보 {valid}길 · 막기 {assigned}(빙 돌아 {detours}) / 자기 쪽 접근 {approach}"
            + (relaxed > 0 ? $" · 방향 겹침 허용 {relaxed}" : "") + (unreachable > 0 ? $" · 못 가는 조합 {unreachable}" : "");
    }

    // ────────────────────────────────────────────────
    //  경로 배정 (기획 2026-09-20) — 방위각이 아니라 NavMesh 경로 겹침으로 나눈다
    // ────────────────────────────────────────────────

    /// <summary>
    /// ① 팀 전원의 플레이어까지 경로를 짜고 ② 겹침을 비교해 ③ 겹치는 개체에게 경유지를 물려 다른 길을 주고
    /// ④ 목적지는 언제나 플레이어로 둔다. 도착해서 기다리는 개체는 더 이상 없다.
    ///
    /// 확정은 <b>경로가 짧은 순</b>. 가장 가까운 개체가 직행을 갖고 나머지가 비킨다 — 강등이 아니라 순서다.
    /// 최종 접근에 들어간 개체는 경로를 갈아엎지 않는다(매초 재배치가 마무리를 엎지 않도록).
    /// </summary>
    private void LayoutTeamByRoute()
    {
        RefreshChasers();

        List<MonsterAI> pending = new List<MonsterAI>();
        List<Vector3[]> accepted = new List<Vector3[]>();
        int locked = 0, directs = 0, detours = 0, failed = 0;

        // 팀에서 빠진 개체의 기록은 버린다
        foreach (MonsterAI m in new List<MonsterAI>(routes.Keys))
        {
            if (m == null || !team.Contains(m)) { routes.Remove(m); }
        }

        foreach (MonsterAI m in team)
        {
            if (m == null || m.IsInStun || m.IsGivingUp) { continue; }

            // 직행 추격자: 감독이 길을 주지 않는다. 다만 남이 피해 가도록 그 길을 등록해 둔다
            if (m.CurrentState == MonsterAI.State.Chase)
            {
                if (BuildRoute(m.transform.position, playerPos, out Vector3[] cr, out float _))
                {
                    accepted.Add(cr);
                    RouteInfo ri = Hold(m);
                    ri.corners = cr; ri.detour = false; ri.waypoint = Vector3.zero;
                    ri.overlap = 0f; ri.directChaser = true;
                }
                directs++;
                continue;
            }

            // 최종 접근 중: 건드리지 않되 자리는 잡아 둔다
            if (m.IsRouteLocked)
            {
                if (routes.TryGetValue(m, out RouteInfo held) && held.corners != null) { accepted.Add(held.corners); }
                locked++;
                continue;
            }

            pending.Add(m);
        }

        pending.Sort((a, b) => RouteLengthToPlayer(a).CompareTo(RouteLengthToPlayer(b)));

        foreach (MonsterAI m in pending)
        {
            if (!BuildRoute(m.transform.position, playerPos, out Vector3[] straight, out float straightLen))
            {
                failed++;
                m.CommandPursue(Vector3.zero, false);   // 길을 못 짜도 멈추지 않는다
                continue;
            }

            float ov = MaxOverlapExcl(straight, accepted);
            Vector3[] chosen = straight;
            Vector3 wp = Vector3.zero;
            bool via = false;

            if (ov > OverlapThreshold && accepted.Count > 0
                && TryFindDetour(m, accepted, out Vector3[] alt, out Vector3 altWp, out float altOv)
                && altOv < ov)
            {
                chosen = alt; wp = altWp; via = true; ov = altOv;
            }

            accepted.Add(chosen);
            RouteInfo info = Hold(m);
            info.corners = chosen; info.detour = via; info.waypoint = wp;
            info.overlap = ov; info.directChaser = false;

            m.CommandPursue(wp, via);
            if (via) { detours++; } else { directs++; }

            // 옛 디버그 표시 유지
            cutWaypoints.Remove(m);
            if (via) { cutWaypoints[m] = wp; }
            cutPoints[m] = playerPos;
        }

        routeSummary = $"팀 {team.Count} · 직행 {directs} / 우회 {detours}"
            + (locked > 0 ? $" · 최종접근 {locked}" : "")
            + (failed > 0 ? $" · 길없음 {failed}" : "");
        layoutSummary = routeSummary;
    }

    private RouteInfo Hold(MonsterAI m)
    {
        if (!routes.TryGetValue(m, out RouteInfo r) || r == null)
        {
            r = new RouteInfo { colorIndex = routeColorSeq++ };
            routes[m] = r;
        }
        return r;
    }

    /// <summary>
    /// 플레이어 주변 경유지 후보로 대체 경로를 찾는다(기획 2026-09-20 a안).
    /// <b>겹침은 몬스터 → 경유지 → 플레이어 전체 경로로 잰다</b> — 뒤쪽만 갈라져도 앞 통로를 공유하면 좋은 우회가 아니다.
    /// 기존 규칙은 그대로 둔다: 플레이어를 뚫고 경유지로 가지 않으며, 우회 시간 상한을 넘기지 않고, 가던 경유지를 우대한다.
    /// </summary>
    private bool TryFindDetour(MonsterAI m, List<Vector3[]> accepted, out Vector3[] best, out Vector3 bestWaypoint, out float bestOverlap)
    {
        best = null;
        bestWaypoint = Vector3.zero;
        bestOverlap = 1f;
        float speed = Mathf.Max(1f, FarSpeed);
        float bestCost = float.MaxValue;
        bool hadPrev = routes.TryGetValue(m, out RouteInfo prev) && prev != null && prev.detour;

        for (int k = 0; k < 8; k++)
        {
            Vector3 w = ClampAlongNav(candidateOrigin,
                candidateOrigin + Quaternion.Euler(0f, 45f * k, 0f) * Vector3.forward * WaypointDistance);
            if (HorizontalDistance(w, candidateOrigin) < WaypointDistance * 0.6f) { continue; }

            if (!BuildRoute(m.transform.position, w, out Vector3[] leg1, out float l1)) { continue; }
            if (PassesNear(leg1, candidateOrigin, CrossingRadius)) { continue; }        // 플레이어 관통 방지(유지)
            if (!BuildRoute(w, playerPos, out Vector3[] leg2, out float l2)) { continue; }

            float travel = (l1 + l2) / speed;
            if (travel > DetourTimeLimit) { continue; }                                  // 우회 시간 상한(유지)

            Vector3[] full = ConcatRoute(leg1, leg2);
            float ov = MaxOverlapExcl(full, accepted);

            bool keeping = hadPrev && HorizontalDistance(prev.waypoint, w) < WaypointDistance * 0.35f;
            float cost = (l1 + l2) * LengthWeight + ov * OverlapPenalty - (keeping ? KeepBonus : 0f);
            if (cost < bestCost)
            {
                bestCost = cost; best = full; bestWaypoint = w; bestOverlap = ov;
            }
        }
        return best != null;
    }

    /// <summary>
    /// 이 몬스터가 그 지점까지 가는 길. 최단 경로가 플레이어 근처를 뚫고 가면 경유 지점을 들러 돌아간다.
    /// 돌아가도 detourTimeLimit 안에 못 가면 false — 억지로 맵을 한 바퀴 돌리지 않는다.
    /// </summary>
    private bool PlanApproach(MonsterAI m, Vector3 point, out Vector3 waypoint, out bool viaWaypoint, out float travelTime)
    {
        waypoint = Vector3.zero;
        viaWaypoint = false;
        travelTime = 0f;
        float speed = Mathf.Max(1f, FarSpeed);

        if (BuildRoute(m.transform.position, point, out Vector3[] direct, out float directLen)
            && !PassesNear(direct, candidateOrigin, CrossingRadius))
        {
            travelTime = directLen / speed;
            return travelTime <= DetourTimeLimit;
        }

        // 플레이어를 뚫고 가야 한다 → 옆·반대편으로 도는 경유 지점을 찾는다
        float best = float.MaxValue;
        for (int k = 0; k < 8; k++)
        {
            Vector3 w = ClampAlongNav(candidateOrigin, candidateOrigin + Quaternion.Euler(0f, 45f * k, 0f) * Vector3.forward * WaypointDistance);
            if (HorizontalDistance(w, candidateOrigin) < WaypointDistance * 0.6f) { continue; }
            if (!BuildRoute(m.transform.position, w, out Vector3[] leg1, out float l1)) { continue; }
            if (PassesNear(leg1, candidateOrigin, CrossingRadius)) { continue; }
            if (!BuildRoute(w, point, out Vector3[] leg2, out float l2)) { continue; }
            if (PassesNear(leg2, candidateOrigin, CrossingRadius)) { continue; }
            float t = (l1 + l2) / speed;
            if (t < best) { best = t; waypoint = w; }
        }
        if (best <= DetourTimeLimit)
        {
            viaWaypoint = true;
            travelTime = best;
            return true;
        }
        return false;
    }

    /// <summary>추격자가 플레이어에게 들어오는 쪽(플레이어 → 추격자 방향). 여러 마리면 평균.</summary>
    private Vector3 ChaserSide()
    {
        Vector3 sum = Vector3.zero;
        foreach (MonsterAI ch in chasers)
        {
            if (ch == null) { continue; }
            Vector3 v = ch.transform.position - candidateOrigin;
            v.y = 0f;
            if (v.magnitude < 4f)
            {
                Vector3 back = -ch.PlanarVelocity;
                if (back.sqrMagnitude > 0.25f) { v = back; }
            }
            if (v.sqrMagnitude > 0.25f) { sum += v.normalized; }
        }
        return sum.sqrMagnitude > 0.01f ? sum.normalized : Vector3.zero;
    }

    private void IssueCut(MonsterAI m, Vector3 point, Vector3 waypoint, bool viaWaypoint)
    {
        Vector3 blurred = StableBlur(m, point);
        if (viaWaypoint)
        {
            m.CommandAmbush(blurred, waypoint, true);
            cutWaypoints[m] = waypoint;
        }
        else
        {
            m.CommandAmbush(blurred);
            cutWaypoints.Remove(m);
        }
        cutPoints[m] = blurred;
    }

    /// <summary>개체마다 흐림 방향을 몇 초간 고정한다 — 매번 바꾸면 목적지가 튀어 몬스터가 흔들린다.</summary>
    private Vector3 StableBlur(MonsterAI m, Vector3 point)
    {
        if (!blurRefreshAt.TryGetValue(m, out float at) || Time.time >= at || !blurOffsets.ContainsKey(m))
        {
            Vector2 r = Random.insideUnitCircle * HintBlurRadius;
            blurOffsets[m] = new Vector3(r.x, 0f, r.y);
            blurRefreshAt[m] = Time.time + BlurRefreshTime;
        }
        Vector3 p = point + blurOffsets[m];
        return NavMesh.SamplePosition(p, out NavMeshHit hit, HintBlurRadius + 2f, NavMesh.AllAreas) ? hit.position : point;
    }

    /// <summary>자기 쪽에서 플레이어에게 가는 실제 경로 위, 플레이어 앞 20m(가까우면 절반 거리) 지점. 뒤를 따라가지 않고 자기 방향에서 조인다.</summary>
    private Vector3 StopShortPoint(MonsterAI m)
    {
        Vector3 from = m.transform.position;
        if (!BuildRoute(from, playerPos, out Vector3[] corners, out float length) || corners.Length < 2) { return from; }
        float stopAt = Mathf.Min(StopShortDistance, HorizontalDistance(from, playerPos) * 0.5f);
        for (int i = 1; i < corners.Length; i++)
        {
            float segLen = Vector3.Distance(corners[i - 1], corners[i]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(segLen));
            for (int k = 1; k <= steps; k++)
            {
                Vector3 q = Vector3.Lerp(corners[i - 1], corners[i], (float)k / steps);
                if (HorizontalDistance(q, playerPos) <= stopAt) { return q; }
            }
        }
        return corners[corners.Length - 1];
    }

    /// <summary>플레이어가 향하는 방향. 멈춰 있으면 추격자 반대편, 그것도 없으면 방향 없음.</summary>
    private Vector3 PlayerHeading()
    {
        if (playerVelocity.magnitude > MovingSpeedThreshold) { return playerVelocity.normalized; }
        Vector3 away = Vector3.zero;
        foreach (MonsterAI c in chasers)
        {
            if (c == null) { continue; }
            Vector3 v = playerPos - c.transform.position;
            v.y = 0f;
            if (v.sqrMagnitude > 0.01f) { away += v.normalized; }
        }
        return away.sqrMagnitude > 0.01f ? away.normalized : Vector3.zero;
    }

    /// <summary>origin에서 16방향으로 바닥을 따라 뻗어 본 후보 길. 너무 짧은 방향은 버리고, 가까운 후보는 합친다.</summary>
    private void BuildCandidates(Vector3 origin)
    {
        lastCandidates.Clear();
        float maxDist = CandidateMaxDistance;
        float minDist = CandidateMinDistance;
        float merge = CandidateMergeDistance;

        for (int i = 0; i < 16; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 22.5f * i, 0f) * Vector3.forward;
            Vector3 point = ClampAlongNav(origin, origin + dir * maxDist);
            Vector3 flat = point - origin;
            flat.y = 0f;
            float reach = flat.magnitude;

            CutCandidate c = new CutCandidate
            {
                point = point,
                direction = reach > 0.01f ? flat / reach : dir,
                reach = reach,
                rejected = reach < minDist
            };

            if (!c.rejected)
            {
                int dup = -1;
                for (int k = 0; k < lastCandidates.Count; k++)
                {
                    if (!lastCandidates[k].rejected && HorizontalDistance(lastCandidates[k].point, point) < merge) { dup = k; break; }
                }
                if (dup >= 0)
                {
                    if (lastCandidates[dup].reach >= reach) { continue; }
                    lastCandidates.RemoveAt(dup);
                }
            }
            lastCandidates.Add(c);
        }
    }

    private static float AngleScore(Vector3 direction, List<Vector3> covered, Vector3 heading)
    {
        float minAngle = 180f;
        foreach (Vector3 cov in covered) { minAngle = Mathf.Min(minAngle, Vector3.Angle(direction, cov)); }
        float score = minAngle / 180f;
        if (heading.sqrMagnitude > 0.01f) { score += (Vector3.Dot(direction, heading) + 1f) * 0.25f; }
        return score;
    }

    // ────────────────────────────────────────────────
    //  보조
    // ────────────────────────────────────────────────

    private float RouteLengthToPlayer(MonsterAI m)
    {
        return BuildRoute(m.transform.position, playerPos, out _, out float len) ? len : 9999f;
    }

    private Vector3 BlurOnNav(Vector3 point, float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        Vector3 p = point + new Vector3(r.x, 0f, r.y);
        return NavMesh.SamplePosition(p, out NavMeshHit hit, radius + 2f, NavMesh.AllAreas) ? hit.position : point;
    }

    private string TeamNames()
    {
        List<string> names = new List<string>();
        foreach (MonsterAI m in team) { if (m != null) { names.Add(m.name.Replace("Monster_", "")); } }
        return string.Join(", ", names);
    }

    /// <summary>origin에서 desired 쪽으로 걸을 수 있는 바닥을 따라가다 막히면 막힌 곳 바로 앞.</summary>
    private static Vector3 ClampAlongNav(Vector3 origin, Vector3 desired)
    {
        if (!NavMesh.SamplePosition(origin, out NavMeshHit o, 3f, NavMesh.AllAreas)) { return origin; }
        Vector3 target = desired;
        if (NavMesh.Raycast(o.position, desired, out NavMeshHit edge, NavMesh.AllAreas))
        {
            Vector3 dir = desired - o.position;
            dir.y = 0f;
            target = edge.position - (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.zero) * 1f;
        }
        return NavMesh.SamplePosition(target, out NavMeshHit f, 4f, NavMesh.AllAreas) ? f.position : o.position;
    }

    /// <summary>실제로 걸어갈 수 있는 완전한 경로. 없으면 false.</summary>
    private bool BuildRoute(Vector3 from, Vector3 to, out Vector3[] corners, out float length)
    {
        corners = null;
        length = 0f;
        if (pathBuffer == null) { pathBuffer = new NavMeshPath(); }
        Vector3 start = NavMesh.SamplePosition(from, out NavMeshHit sh, 3f, NavMesh.AllAreas) ? sh.position : from;
        Vector3 end = NavMesh.SamplePosition(to, out NavMeshHit eh, 3f, NavMesh.AllAreas) ? eh.position : to;
        if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }
        corners = pathBuffer.corners;
        for (int i = 1; i < corners.Length; i++) { length += Vector3.Distance(corners[i - 1], corners[i]); }
        return corners.Length > 0;
    }

    /// <summary>경로가 p 반경 안을 지나가는가. 이미 반경 안에서 출발하는 개체(떠나는 길)는 검사하지 않는다.</summary>
    private static bool PassesNear(Vector3[] corners, Vector3 p, float radius)
    {
        if (radius <= 0f || corners == null || corners.Length < 2) { return false; }
        if (HorizontalDistance(corners[0], p) < radius) { return false; }
        for (int i = 1; i < corners.Length; i++)
        {
            if (DistancePointToSegment(p, corners[i - 1], corners[i]) < radius) { return true; }
        }
        return false;
    }

    /// <summary>확정된 길들 중 가장 많이 겹치는 비율.</summary>
    private float MaxOverlapExcl(Vector3[] route, List<Vector3[]> others)
    {
        float max = 0f;
        foreach (Vector3[] o in others) { max = Mathf.Max(max, OverlapExcl(route, o)); }
        return max;
    }

    /// <summary>
    /// route를 2m 간격으로 짚어 다른 길 가까이에 드는 비율(0~1).
    /// <b>플레이어 근처 수렴 구간은 세지 않는다</b> — 모든 길이 플레이어에서 만나므로,
    /// 빼지 않으면 지워지지 않는 바닥값이 생겨 포위가 완성될수록 규칙이 판을 엎는다(기획 2026-09-20).
    /// </summary>
    private float OverlapExcl(Vector3[] route, Vector3[] other)
    {
        if (route == null || other == null || route.Length < 2 || other.Length < 2) { return 0f; }
        float excl = ConvergenceRadius;
        float near = RouteNearDistance;
        int total = 0, hit = 0;
        for (int i = 1; i < route.Length; i++)
        {
            float len = HorizontalDistance(route[i - 1], route[i]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / OverlapSampleStep));
            for (int k = 0; k < steps; k++)
            {
                Vector3 q = Vector3.Lerp(route[i - 1], route[i], (float)k / steps);
                if (HorizontalDistance(q, playerPos) < excl) { continue; }
                total++;
                if (DistanceToPolyline(q, other) < near) { hit++; }
            }
        }
        return total > 0 ? (float)hit / total : 0f;
    }

    /// <summary>두 구간을 한 경로로 잇는다 — 겹침은 반드시 전체 경로로 재야 한다.</summary>
    private static Vector3[] ConcatRoute(Vector3[] a, Vector3[] b)
    {
        if (a == null || a.Length == 0) { return b; }
        if (b == null || b.Length == 0) { return a; }
        List<Vector3> all = new List<Vector3>(a.Length + b.Length);
        all.AddRange(a);
        for (int i = 0; i < b.Length; i++)
        {
            if (i == 0 && all.Count > 0 && HorizontalDistance(all[all.Count - 1], b[i]) < 0.05f) { continue; }
            all.Add(b[i]);
        }
        return all.ToArray();
    }

    private static float MaxOverlap(Vector3[] route, List<Vector3[]> others)
    {
        float max = 0f;
        foreach (Vector3[] o in others) { max = Mathf.Max(max, Overlap(route, o)); }
        return max;
    }

    /// <summary>route를 2m 간격으로 짚어 보며, 다른 길 3m 안에 드는 비율(0~1).</summary>
    private static float Overlap(Vector3[] route, Vector3[] other)
    {
        if (route == null || other == null || route.Length < 2 || other.Length < 2) { return 0f; }
        int total = 0, near = 0;
        for (int i = 1; i < route.Length; i++)
        {
            float len = HorizontalDistance(route[i - 1], route[i]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / OverlapSampleStep));
            for (int k = 0; k < steps; k++)
            {
                Vector3 q = Vector3.Lerp(route[i - 1], route[i], (float)k / steps);
                total++;
                if (DistanceToPolyline(q, other) < OverlapNearDistance) { near++; }
            }
        }
        return total > 0 ? (float)near / total : 0f;
    }

    private static float DistanceToPolyline(Vector3 p, Vector3[] line)
    {
        float best = float.MaxValue;
        for (int i = 1; i < line.Length; i++) { best = Mathf.Min(best, DistancePointToSegment(p, line[i - 1], line[i])); }
        return best;
    }

    private static float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        p.y = a.y = b.y = 0f;
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) { return Vector3.Distance(p, a); }
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static MonsterAI FindGlobal()
    {
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.role == MonsterAI.MonsterRole.Global_Stalker) { return m; }
        }
        return null;
    }

    private void Log(string message)
    {
        MonsterAI g = FindGlobal();
        if (g == null || g.showDebugLog) { Debug.Log(message); }
    }

    // 수치: 전역 몬스터 인스펙터 "6. 조정자" 칸 (없으면 기본값)
    private int MaxChasers { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(1, g.maxSimultaneousChasers) : 2; } }
    private int TeamSize { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(1, g.huntTeamSize) : 3; } }
    private float TeamSwapRatio { get { MonsterAI g = FindGlobal(); return g != null ? g.teamSwapRatio : 0.6f; } }
    private float CandidateMaxDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMaxDistance : 18f; } }
    private float CandidateMinDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMinDistance : 6f; } }
    private float CandidateMergeDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMergeDistance : 8f; } }
    private float CrossingRadius { get { MonsterAI g = FindGlobal(); return g != null ? g.crossingAvoidRadius : 6f; } }
    private float HintBlurRadius { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0f, g.hintBlurRadius) : 4f; } }
    private float PursuitHintBlur { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0f, g.pursuitHintBlur) : 3f; } }
    private float SwapMargin { get { MonsterAI g = FindGlobal(); return g != null ? g.swapDistanceMargin : 3f; } }
    private float HuntMemory { get { MonsterAI g = FindGlobal(); return g != null ? g.huntMemory : 8f; } }
    private float LayoutRefresh { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.2f, g.layoutRefreshInterval) : 1f; } }
    private float FollowDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.followDistance : 7f; } }
    private float FollowHoldTime { get { MonsterAI g = FindGlobal(); return g != null ? g.followHoldTime : 1f; } }
    private float NearSpeed { get { MonsterAI g = FindGlobal(); return g != null ? g.huntNearSpeed : 10f; } }
    private float FarSpeed { get { MonsterAI g = FindGlobal(); return g != null ? g.huntFarSpeed : 14f; } }
    private float NearDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.huntNearDistance : 15f; } }
    private float FarDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.huntFarDistance : 40f; } }
    private float DetourTimeLimit { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(1f, g.detourTimeLimit) : 8f; } }
    private float WaypointDistance { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(5f, g.detourWaypointDistance) : 22f; } }
    private float SideAngleMin { get { MonsterAI g = FindGlobal(); return g != null ? g.sideAngleMin : 90f; } }
    private float ScatterAfter { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(3f, g.scatterAfter) : 20f; } }
    private float ScatterTime { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(1f, g.scatterTime) : 6f; } }
    private float ScatterRejoinBlock { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0f, g.scatterRejoinBlock) : 8f; } }
    private float HandoverDelay { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.5f, g.handoverDelay) : 3f; } }
    private float TeleportUnseenTime { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.5f, g.teleportUnseenTime) : 3f; } }
    private float TeleportMinDistance { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(10f, g.teleportMinDistance) : 40f; } }
    private float PlayerViewAngle { get { MonsterAI g = FindGlobal(); return g != null ? g.playerViewAngle : 90f; } }
    private float OverlapThreshold { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Clamp01(g.routeOverlapThreshold) : 0.45f; } }
    private float RouteNearDistance { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.5f, g.routeNearDistance) : 3f; } }
    private float ConvergenceRadius { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0f, g.convergenceExcludeRadius) : 8f; } }

    /// <summary>못 본 채 추적할 때의 속도 상한(기획 A안). 가까울 때 속도와 같다.</summary>
    public float TrackingSpeedCap => NearSpeed;

    // 몬스터가 읽는 끈질김 수치
    public float PursuitPersistence { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.5f, g.pursuitPersistence) : 6f; } }
    public float PersistenceDecay { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Clamp01(g.persistenceDecay) : 0.7f; } }
    public float PersistenceMin { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.2f, g.persistenceMin) : 2f; } }
    public float GiveUpLookTime { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0f, g.giveUpLookTime) : 2f; } }

    private static readonly Color[] RouteColors =
    {
        new Color(0.30f, 0.85f, 1.00f),   // 하늘
        new Color(1.00f, 0.80f, 0.20f),   // 노랑
        new Color(0.50f, 1.00f, 0.45f),   // 연두
        new Color(1.00f, 0.45f, 0.85f),   // 분홍
        new Color(1.00f, 0.55f, 0.25f),   // 주황
        new Color(0.70f, 0.60f, 1.00f),   // 보라
    };

    private void OnDrawGizmos()
    {
        if (!hunting) { return; }

        Vector3 up = Vector3.up * 0.6f;

        // 겹침 판정에서 빼는 수렴 구간 — 이 안은 모든 길이 만나므로 세지 않는다
        float excl = ConvergenceRadius;
        if (excl > 0.1f)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.25f, 0.5f);
            DrawGizmoCircle(playerPos + up, excl);
        }
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(candidateOrigin, 1f);

        foreach (KeyValuePair<MonsterAI, RouteInfo> pair in routes)
        {
            RouteInfo r = pair.Value;
            if (pair.Key == null || r == null || r.corners == null || r.corners.Length < 2) { continue; }
            Color c = RouteColors[Mathf.Abs(r.colorIndex) % RouteColors.Length];
            // 직행 추격자는 흐리게 — 감독이 준 길이 아니라 참고용이다
            Gizmos.color = r.directChaser ? new Color(c.r, c.g, c.b, 0.35f) : c;
            for (int i = 1; i < r.corners.Length; i++)
            {
                Gizmos.DrawLine(r.corners[i - 1] + up, r.corners[i] + up);
            }
            Gizmos.DrawWireSphere(r.corners[0] + up, 0.5f);
            if (r.detour)
            {
                Gizmos.DrawWireSphere(r.waypoint + up, 1.5f);
                Gizmos.DrawLine(r.waypoint, r.waypoint + Vector3.up * 3f);
            }
            if (pair.Key.IsFinalApproach)
            {
                Gizmos.DrawWireSphere(pair.Key.transform.position + Vector3.up * 2.5f, 0.8f);
            }
        }
    }

    private static void DrawGizmoCircle(Vector3 center, float radius)
    {
        const int seg = 32;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
