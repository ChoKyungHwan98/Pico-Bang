using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감독(뇌 2). 발견은 몸의 눈으로만 — 감독은 몬스터가 본 것·들은 것을 모아 사냥을 짠다.
///
/// 포위망(2026-09-26 재계획):
/// - 사냥: 누가 보든 사냥 시작, 본 몬스터는 모두 사냥에 들어간다. 아무도 huntMemory초 동안 못 보면 사냥 끝.
/// - 출구 막기: 0.5초마다 감독이 아는 플레이어 위치에서 길로 exitMin~exitMax m 떨어진 지점을 방향별로 묶어 '출구'로 삼는다.
///   보고 있는 몬스터는 곧장 달려들고(추격), 못 보는 몬스터는 추격자 쪽이 아닌 출구를 하나씩 맡는다. 예측은 하지 않는다.
/// - 한 규칙: 한 방향에 추격자 하나. 플레이어 기준 sameSideAngle° 안에서 다른 추격자 뒤를 따르는 몬스터는 추격하지 않고 빈 출구로 간다.
/// - 둘레 순찰: 사냥에 안 낀 몬스터는 플레이어 둘레 ringMin~ringMax m, 방향을 나눈 자리 근처를 걷는다.
/// - 재배치: 사냥에 안 낀 몬스터가 어느 카메라에도 안 보이고 relocateDistance m 넘게 떨어지면, 안 보이는 둘레 자리로 옮긴다.
/// - 소리: 쏘면 사냥에 안 낀 몬스터 중 들은 1~2마리가 서로 다른 쪽에서 확인하러 온다(사냥 시작은 아님).
/// 둘레 순찰·재배치는 실제 플레이어 위치를 쓴다(보이지 않는 곳에서의 배치일 뿐, 발견은 여전히 눈으로만).
/// 모든 판단은 <see cref="Decide"/>로 기록된다 — 플레이 기록(decision)과 포트폴리오 지도의 판단 목록.
/// </summary>
public class MonsterDirector : MonoBehaviour
{
    private static MonsterDirector instance;
    private static bool isQuitting;
    public static MonsterDirector Instance
    {
        get
        {
            if (isQuitting) return null;
            if (instance == null) instance = new GameObject("~MonsterDirector").AddComponent<MonsterDirector>();
            return instance;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; isQuitting = false; DecisionMade = null; NoiseReported = null; Relocated = null; }

    public enum Role { None, Chase, Trail, Block }

    /// <summary>지도 표시용 경로 정보.</summary>
    public class RouteInfo
    {
        public Vector3[] corners;
        public Vector3 goal;
        public bool directChaser;
        public int colorIndex;
        public float eta;
    }

    /// <summary>출구 하나: 플레이어가 빠져나갈 수 있는 길목.</summary>
    public struct ExitInfo
    {
        public Vector3 point;
        public Vector3 direction;
        public int bin;
        public float weight;
        public bool blocked;
    }

    /// <summary>소리 한 번의 결과: 누가 들었나, 무시됐나.</summary>
    public struct NoiseReport
    {
        public Vector3 position;
        public float radius;
        public NoiseKind kind;
        public MonsterAI[] listeners;
        public bool ignored;
    }

    /// <summary>판단 기록(몬스터, 문장). 몬스터가 null이면 감독 자신의 판단.</summary>
    public static event System.Action<MonsterAI, string> DecisionMade;
    public static event System.Action<NoiseReport> NoiseReported;
    /// <summary>재배치(몬스터, 옮기기 전, 옮긴 뒤) — 지도에 점선으로.</summary>
    public static event System.Action<MonsterAI, Vector3, Vector3> Relocated;

    [Header("사냥")]
    [Tooltip("아무도 이 시간(초) 동안 못 보면 사냥 끝")]
    public float huntMemory = 6f;
    [Tooltip("사냥에 들어갈 수 있는 최대 수(못 본 몬스터를 부를 때). 직접 본 몬스터는 넘어도 들어간다")]
    public int maxHunters = 4;
    [Tooltip("누가 본 지 이 시간(초) 안일 때만 못 본 몬스터를 사냥에 부른다")]
    public float recruitWindow = 3f;

    [Header("출구 막기")]
    [Tooltip("출구 = 감독이 아는 위치에서 길로 이 거리(m) 사이")]
    public float exitMin = 12f;
    public float exitMax = 20f;
    [Tooltip("출구로 달려가는 속도")]
    public float blockSpeed = 14f;
    [Tooltip("출구 도착 후 조여 드는 속도")]
    public float closeInSpeed = 6f;
    [Tooltip("다른 출구가 이만큼(초) 더 빨라야 맡은 출구를 바꾼다")]
    public float switchGain = 1.5f;
    [Tooltip("한 방향에 추격자 하나: 플레이어 기준 이 각도(°) 안에서 다른 추격자 뒤에 있으면 추격 대신 출구로")]
    public float sameSideAngle = 50f;

    [Header("추격 속도")]
    [Tooltip("보고 쫓는 몬스터가 가까울 때(플레이어 달리기 11보다 느리게 — 똑바로 달리면 조금씩 벌어진다)")]
    public float nearSpeed = 10f;
    [Tooltip("보고 쫓는 몬스터가 멀 때")]
    public float farSpeed = 14f;
    public float nearDistance = 15f;
    public float farDistance = 40f;

    [Header("둘레 순찰 · 재배치")]
    public float ringMin = 30f;
    public float ringMax = 60f;
    [Tooltip("둘레 자리를 다시 잡는 주기(초)")]
    public float ringInterval = 4f;
    [Tooltip("안 보일 때 재배치를 켠다")]
    public bool useRelocation = true;
    [Tooltip("이보다 멀면(m) 재배치 대상")]
    public float relocateDistance = 60f;
    [Tooltip("재배치 자리는 플레이어와 최소 이 거리(m)")]
    public float relocateMin = 25f;
    [Tooltip("한 몬스터를 다시 재배치하기까지(초)")]
    public float relocateCooldown = 8f;

    [Header("소리")]
    [Tooltip("총소리에 확인하러 오는 최대 수")]
    public int noiseCallers = 2;

    private const float PlanInterval = .5f;
    private const float SeeGrace = 1f;
    private const float ReleaseCooldown = 4f;
    private const float SplitCommit = 2f;

    private readonly List<MonsterAI> hunters = new List<MonsterAI>();
    private readonly Dictionary<MonsterAI, Role> roles = new Dictionary<MonsterAI, Role>();
    private readonly Dictionary<MonsterAI, RouteInfo> routes = new Dictionary<MonsterAI, RouteInfo>();
    private readonly Dictionary<MonsterAI, Vector3> ringSlots = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, float> relocatedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> releasedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> splitAt = new Dictionary<MonsterAI, float>();
    private readonly List<MonsterAI> noiseCheckers = new List<MonsterAI>();
    private readonly List<ExitInfo> exits = new List<ExitInfo>();
    private readonly Dictionary<string, float> decisionThrottle = new Dictionary<string, float>();
    private readonly List<Vector3> anchors = new List<Vector3>();
    private NavMeshPath pathBuffer;

    private MonsterAI spotter;
    private Transform player;
    private bool hunting, hasSighting;
    private Vector3 knownPosition, sightPosition, heading;
    private float knowledgeTime = float.NegativeInfinity, sightTime = float.NegativeInfinity, headingTime = float.NegativeInfinity;
    private Vector3 headingSamplePos;
    private float headingSampleTime = float.NegativeInfinity;
    private float nextPlan, nextRing, nextRelocate;
    private string summary = "순찰 중";
    private int huntCount;

    // ── 몸이 쓰는 값 ──
    public float CloseInSpeed => closeInSpeed;

    // ── 화면·기록용 ──
    public bool IsHunting => hunting;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => sightPosition;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => heading;
    public string SightingSpotterName => spotter != null ? spotter.name : "-";
    public Vector3 DebugKnownPosition => knownPosition;
    public IReadOnlyList<MonsterAI> DebugTeam => hunters;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugRoutes => routes;
    public IReadOnlyList<ExitInfo> DebugExits => exits;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugRingSlots => ringSlots;
    public string DebugLayoutSummary => summary;
    public int DebugChaserCount
    {
        get
        {
            int count = 0;
            foreach (var m in hunters) if (m != null && m.CurrentState == MonsterAI.State.Chase) count++;
            return count;
        }
    }
    public Role RoleOf(MonsterAI m) => m != null && roles.TryGetValue(m, out var r) ? r : Role.None;
    /// <summary>사냥 중인 몬스터만 이름이 있다(기록 분석이 이 값으로 사냥 몬스터를 센다).</summary>
    public string DebugOrderLabel(MonsterAI m)
    {
        if (m == null || !hunters.Contains(m)) return "";
        switch (RoleOf(m))
        {
        case Role.Chase: return "추격";
        case Role.Trail: return "흔적";
        case Role.Block: return "출구";
        }
        return "사냥";
    }
    public string DebugRouteLabel(MonsterAI m)
    {
        if (!routes.TryGetValue(m, out var r) || r.directChaser) return "";
        return "출구 ETA~" + r.eta.ToString("F1") + "초";
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        pathBuffer = new NavMeshPath();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void OnApplicationQuit() { isQuitting = true; }

    // ────────────────────────────────────────────────
    //  판단 기록
    // ────────────────────────────────────────────────

    /// <summary>
    /// 감독의 판단 한 건. code는 분석용 짧은 이름(영문), text는 사람이 읽는 문장.
    /// 같은 몬스터·같은 code는 throttle초 안에 다시 기록하지 않는다(매 프레임 반복 방지).
    /// </summary>
    private void Decide(MonsterAI m, string code, string text, float throttle = 0f)
    {
        string key = (m != null ? m.name : "director") + "|" + code;
        if (throttle > 0f && decisionThrottle.TryGetValue(key, out float until) && Time.time < until) return;
        if (throttle > 0f) decisionThrottle[key] = Time.time + throttle;
        Vector3 at = m != null ? m.transform.position : knownPosition;
        PlaytestRecorder.Record("decision", m != null ? m.name : "director", at, code + "|" + text, knownPosition);
        DecisionMade?.Invoke(m, text);
    }

    private static string Short(MonsterAI m) => m == null ? "-" : m.name.Replace("Monster_", "");

    // ────────────────────────────────────────────────
    //  몸(MonsterAI)이 알려 오는 것
    // ────────────────────────────────────────────────

    /// <summary>플레이어를 보고 있다. 무리의 지식이 되고, 본 몬스터는 사냥에 들어간다(인원과 상관없이).</summary>
    public void ReportSighting(MonsterAI observer, Vector3 position)
    {
        if (observer == null || observer.IsInStun) return;
        UpdateHeading(position);
        hasSighting = true;
        sightPosition = knownPosition = position;
        sightTime = knowledgeTime = Time.time;
        spotter = observer;
        if (!hunting) StartHunt();
        if (hunters.Contains(observer)) return;
        noiseCheckers.Remove(observer);
        ringSlots.Remove(observer);
        hunters.Add(observer);
        roles[observer] = Role.Chase;
        Decide(observer, "spotted", "플레이어를 봄 → 사냥 합류, 곧장 달려듦 (사냥 " + hunters.Count + "마리)");
        nextPlan = 0f;
    }

    /// <summary>출구로 가던 몬스터가 플레이어를 봤다: 달려들어도 되는가. 다른 추격자 뒤 같은 쪽이면 출구를 지킨다.</summary>
    public bool RequestEngage(MonsterAI m)
    {
        if (m == null || !m.CanTakeOrders) return false;
        // 방금 같은 쪽이라 출구로 돌린 몬스터는 2초 동안 돌아 나가는 것을 마친다(바로 되돌아서면 와리가리가 된다). 코앞(6m)은 예외
        if (splitAt.TryGetValue(m, out float split) && Time.time - split < SplitCommit && DistanceToPlayer(m) > 6f) return false;
        var front = FrontChaser(m);
        if (front != null)
        {
            Decide(m, "engage_denied_same_side", "봤지만 " + Short(front) + " 뒤 같은 쪽 → 출구 지킴", 2f);
            return false;
        }
        roles[m] = Role.Chase;
        routes.Remove(m);
        Decide(m, "engage", (m.IsClosingIn ? "출구에서 조여 들다" : "출구로 가다") + " " + DistanceToPlayer(m).ToString("F0") + "m 앞에서 봄 → 달려듦");
        return true;
    }

    /// <summary>소리 확인·감전 뒤 둘러보기가 끝났다.</summary>
    public void ReportSearchDone(MonsterAI m)
    {
        noiseCheckers.Remove(m);
        if (hunting && hunters.Contains(m)) { nextPlan = 0f; return; }
        SendToRing(m, "확인 끝, 아무것도 없음");
    }

    /// <summary>출구까지 길이 없다.</summary>
    public void ReportBlockFailed(MonsterAI m)
    {
        roles[m] = Role.None;
        Decide(m, "block_failed", "출구까지 길 막힘 → 다시 배정", 1f);
        nextPlan = 0f;
    }

    public void ReportStunned(MonsterAI m)
    {
        noiseCheckers.Remove(m);
        if (!hunters.Contains(m)) return;
        roles[m] = Role.None;
        routes.Remove(m);
        Decide(m, "stunned", "감전 → 풀리면 다시 배정 (사냥에서 빠지지 않음)");
    }

    /// <summary>감전이 풀렸는데 플레이어가 안 보인다. 사냥 중이면 다시 배정, 아니면 맞은 쪽을 확인하러 간다.</summary>
    public void ReportRecovered(MonsterAI m, Vector3 shooterPosition)
    {
        if (hunting && hunters.Contains(m)) { nextPlan = 0f; return; }
        m.CommandSearch(shooterPosition);
        if (!noiseCheckers.Contains(m)) noiseCheckers.Add(m);
        Decide(m, "recovered_check", "감전 풀림, 안 보임 → 맞은 쪽 확인");
    }

    public void ReportIdle(MonsterAI m)
    {
        noiseCheckers.Remove(m);
        ringSlots.Remove(m);
        if (!hunters.Contains(m)) return;
        Decide(m, "idle", "갈 길이 없음 → 멈춤, 사냥에서 빠짐");
        RemoveHunter(m);
    }

    /// <summary>못 보고 쫓을 때·조여 들 때 갈 곳: 무리가 아는 최신 위치.</summary>
    public bool TryGetPursuitHint(out Vector3 hint)
    {
        hint = knownPosition;
        return hunting;
    }

    /// <summary>
    /// 소리. 몬스터 귀(자기 청각과 소리 범위 중 작은 쪽)에 닿으면 들은 것이다.
    /// 사냥 중인 몬스터가 들으면 위치 힌트만 새로 고친다(사냥 시작·연장은 눈으로만).
    /// 사냥에 안 낀 몬스터 중 들은 몬스터 최대 noiseCallers마리가 서로 다른 쪽에서 확인하러 온다.
    /// 과녁 소리는 사냥 중이면 무시한다(플레이어 위치가 아니므로).
    /// </summary>
    public void ReportNoise(Vector3 position, float radius, NoiseKind kind)
    {
        var listeners = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || m.IsInStun) continue;
            if (Vector3.Distance(m.transform.position, position) <= Mathf.Min(radius, m.EffectiveHearingRange)) listeners.Add(m);
        }
        bool ignored = hunting && kind == NoiseKind.TargetDestroyed;
        NoiseReported?.Invoke(new NoiseReport { position = position, radius = radius, kind = kind, listeners = listeners.ToArray(), ignored = ignored });
        if (ignored || listeners.Count == 0) return;
        PlaytestRecorder.Record("noise_evidence", "player", position,
            kind + ":listeners=" + listeners.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        bool huntHeard = false;
        foreach (var m in listeners) if (hunters.Contains(m)) { huntHeard = true; break; }
        if (hunting && huntHeard) { knownPosition = position; knowledgeTime = Time.time; }

        // 이미 확인하러 오는 몬스터는 목적지만 옮긴다 — 쏠 때마다 새 몬스터를 부르지 않는다
        noiseCheckers.RemoveAll(c => c == null || c.CurrentState != MonsterAI.State.Investigate || hunters.Contains(c));
        foreach (var c in noiseCheckers) if (listeners.Contains(c)) c.CommandSearch(position);
        int need = Mathf.Max(0, noiseCallers) - noiseCheckers.Count;
        if (need <= 0) return;
        var free = new List<MonsterAI>();
        foreach (var m in listeners)
        {
            if (!m.CanTakeOrders || hunters.Contains(m) || noiseCheckers.Contains(m)) continue;
            var st = m.CurrentState;
            if (st != MonsterAI.State.Patrol && st != MonsterAI.State.Return && st != MonsterAI.State.Idle && st != MonsterAI.State.Investigate) continue;
            free.Add(m);
        }
        free.Sort((a, b) => Vector3.Distance(a.transform.position, position).CompareTo(Vector3.Distance(b.transform.position, position)));
        foreach (var m in free)
        {
            if (need <= 0) break;
            // 서로 다른 쪽에서 오게: 이미 오는 몬스터와 소리 기준 70° 넘게 벌어진 몬스터만 더 부른다
            Vector3 bearing = Vector3.ProjectOnPlane(m.transform.position - position, Vector3.up);
            bool apart = true;
            foreach (var c in noiseCheckers)
            {
                Vector3 other = Vector3.ProjectOnPlane(c.transform.position - position, Vector3.up);
                if (bearing.sqrMagnitude > .01f && other.sqrMagnitude > .01f && Vector3.Angle(bearing, other) < 70f) { apart = false; break; }
            }
            if (!apart) continue;
            if (float.IsInfinity(PathLength(m, position))) continue;
            m.CommandSearch(position);
            noiseCheckers.Add(m);
            ringSlots.Remove(m);
            need--;
            Decide(m, "noise_call", (kind == NoiseKind.Shot ? "총소리" : "과녁 소리") + " 들음 (" +
                Vector3.Distance(m.transform.position, position).ToString("F0") + "m) → 확인하러 감 (" + noiseCheckers.Count + "번째, 발견은 눈으로)");
        }
    }

    /// <summary>과녁 경보: 과녁을 쏜 순간의 플레이어 위치. 사냥 중일 때만 위치를 새로 고친다(사냥을 시작하지는 않는다).</summary>
    public void ReportTargetAlarm(Vector3 shotPosition, float shotTime)
    {
        if (hunting && Time.time - shotTime <= huntMemory) { knownPosition = shotPosition; knowledgeTime = Time.time; }
    }

    public void AbortHunt()
    {
        EndHunt(false);
        hasSighting = false;
        knowledgeTime = sightTime = headingTime = headingSampleTime = float.NegativeInfinity;
        heading = Vector3.zero;
        decisionThrottle.Clear(); ringSlots.Clear(); relocatedAt.Clear(); releasedAt.Clear(); splitAt.Clear(); noiseCheckers.Clear(); exits.Clear();
        summary = "순찰 중";
    }

    // ────────────────────────────────────────────────
    //  사냥
    // ────────────────────────────────────────────────

    private void StartHunt()
    {
        hunting = true;
        huntCount++;
        nextPlan = 0f;
        Decide(null, "hunt_start", "사냥 시작 #" + huntCount);
    }

    private void EndHunt(bool announce)
    {
        if (announce && hunting) Decide(null, "hunt_end", huntMemory.ToString("F0") + "초 동안 아무도 못 봄 → 사냥 끝, 둘레로 흩어짐");
        var members = new List<MonsterAI>(hunters);
        hunters.Clear(); roles.Clear(); routes.Clear(); exits.Clear();
        hunting = false;
        summary = "순찰 중";
        foreach (var m in members)
        {
            if (m == null) continue;
            m.SetHuntSpeed(-1f);
        }
        if (!announce) return;
        PlaceRing(members);
        // 둘레 자리를 못 받은 몬스터도 추격 상태로 남겨 두지 않는다
        foreach (var m in members)
            if (m != null && !m.IsInStun && (m.CurrentState == MonsterAI.State.Chase || m.CurrentState == MonsterAI.State.Block))
                m.CommandPatrolAt(m.transform.position);
    }

    private void RemoveHunter(MonsterAI m)
    {
        hunters.Remove(m);
        roles.Remove(m);
        routes.Remove(m);
        if (m != null) m.SetHuntSpeed(-1f);
    }

    private void UpdateHeading(Vector3 position)
    {
        float dt = Time.time - headingSampleTime;
        if (dt < .25f) return;
        if (dt <= 1.5f)
        {
            Vector3 v = Vector3.ProjectOnPlane(position - headingSamplePos, Vector3.up) / dt;
            if (v.magnitude > 2f)
            {
                heading = heading.sqrMagnitude < .01f ? v.normalized : Vector3.Slerp(heading, v.normalized, .6f).normalized;
                headingTime = Time.time;
            }
        }
        headingSamplePos = position;
        headingSampleTime = Time.time;
    }

    private void Update()
    {
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) return;
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) return;
        if (Time.time >= nextRing) { nextRing = Time.time + ringInterval; PlaceRing(null); }
        if (useRelocation && Time.time >= nextRelocate) { nextRelocate = Time.time + 1f; RelocateHidden(); }
        if (!hunting) return;
        if (Time.time - sightTime > huntMemory) { EndHunt(true); return; }
        if (Time.time >= nextPlan) Plan();
    }

    private void Plan()
    {
        nextPlan = Time.time + PlanInterval;
        hunters.RemoveAll(m => m == null || !m.isActiveAndEnabled);
        BuildExits();
        EnforceCap();
        AssignRoles();
        ApplySpeeds();
        RebuildSummary();
    }

    private bool Sees(MonsterAI m) => m.IsSeeingPlayer || Time.time - m.SeenPlayerAt < SeeGrace;

    /// <summary>
    /// 부르기(출구 배정 뒤): 누가 본 지 recruitWindow초 안이고 사냥이 maxHunters보다 적으면, 아직 비어 있는 출구마다
    /// 그 출구에 플레이어 곁을 지나지 않고 가장 빨리 닿는 사냥 밖 몬스터를 불러 곧바로 맡긴다.
    /// 막을 출구가 있는 몬스터만 부른다 — 불렀다가 바로 돌려보내는 일이 없게(09-26 15:14 판 부름 64 / 빠짐 69).
    /// 방금(ReleaseCooldown초) 사냥에서 빠진 몬스터는 부르지 않는다.
    /// </summary>
    private void RecruitIntoOpenExits(List<int> open, HashSet<int> taken)
    {
        if (Time.time - sightTime > recruitWindow) return;
        Vector3 p = PlayerPosition;
        var free = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || hunters.Contains(m) || !m.CanTakeOrders) continue;
            if (releasedAt.TryGetValue(m, out float released) && Time.time - released < ReleaseCooldown) continue;
            var st = m.CurrentState;
            if (st != MonsterAI.State.Patrol && st != MonsterAI.State.Return && st != MonsterAI.State.Idle && st != MonsterAI.State.Investigate) continue;
            free.Add(m);
        }
        while (hunters.Count < maxHunters && free.Count > 0)
        {
            MonsterAI bestM = null;
            int bestI = -1;
            Vector3[] bestPath = null;
            float bestC = float.PositiveInfinity;
            foreach (int i in open)
            {
                if (taken.Contains(i)) continue;
                foreach (var m in free)
                {
                    var path = ComputePath(m, exits[i].point);
                    if (path == null || PassesNear(path, p, 5f)) continue;
                    float c = Length(path) / Mathf.Max(1f, blockSpeed) - exits[i].weight;
                    if (c < bestC) { bestC = c; bestM = m; bestI = i; bestPath = (Vector3[])path.Clone(); }
                }
            }
            if (bestM == null) return;
            free.Remove(bestM);
            taken.Add(bestI);
            hunters.Add(bestM);
            noiseCheckers.Remove(bestM);
            ringSlots.Remove(bestM);
            roles[bestM] = Role.None;
            Decide(bestM, "recruit", "빈 출구로 부름 (" + Length(bestPath).ToString("F0") + "m, 사냥 " + hunters.Count + "마리)");
            SendToExit(bestM, bestI, bestPath);
        }
    }

    /// <summary>인원 초과(직접 본 몬스터가 더 들어온 경우): 못 보고 있는 몬스터 중 가장 먼 몬스터를 둘레로.</summary>
    private void EnforceCap()
    {
        while (hunters.Count > maxHunters)
        {
            MonsterAI farthest = null;
            float d = -1f;
            foreach (var m in hunters)
            {
                if (m == null || Sees(m)) continue;
                float distance = DistanceToPlayer(m);
                if (distance > d) { d = distance; farthest = m; }
            }
            if (farthest == null) break;
            RemoveHunter(farthest);
            SendToRing(farthest, "사냥 " + maxHunters + "마리 초과, 못 보고 있는 가장 먼 몬스터");
        }
    }

    /// <summary>
    /// 역할(0.5초마다):
    /// ① 보고 있는 몬스터 = 추격(몸이 스스로 달려든다). 단 다른 추격자 뒤 같은 쪽이면 출구로.
    /// ② 아무도 안 보면 아는 위치에 가장 가까운 몬스터 하나 = 흔적 추적.
    /// ③ 나머지 = 출구 막기. 추격자 쪽이 아닌 출구를, 가장 빨리 닿는 몬스터에게 하나씩.
    /// </summary>
    private void AssignRoles()
    {
        var active = new List<MonsterAI>();
        foreach (var m in hunters) if (m.CanTakeOrders) active.Add(m);
        active.Sort((a, b) => DistanceToPlayer(a).CompareTo(DistanceToPlayer(b)));

        var chasers = new List<MonsterAI>();
        var rest = new List<MonsterAI>();
        foreach (var m in active)
        {
            if (!Sees(m) || (m.CurrentState != MonsterAI.State.Chase && m.CurrentState != MonsterAI.State.Block)) { rest.Add(m); continue; }
            if (m.CurrentState == MonsterAI.State.Block) { rest.Add(m); continue; }   // 출구에서 본 몬스터는 RequestEngage로 스스로 바꾼다
            MonsterAI front = null;
            foreach (var c in chasers)
                if (SameSideBehind(m, c)) { front = c; break; }
            if (front != null)
            {
                Decide(m, "same_side_split", Short(front) + " 뒤 같은 쪽에서 따라감 → 비어 있는 출구로", 2f);
                splitAt[m] = Time.time;
                rest.Add(m);
                continue;
            }
            chasers.Add(m);
            roles[m] = Role.Chase;
            routes.Remove(m);
        }

        if (chasers.Count == 0 && rest.Count > 0)
        {
            // 흔적: 아는 위치까지 길이 가장 짧은 몬스터(이미 흔적을 쫓던 몬스터를 조금 우선)
            MonsterAI trail = null;
            float best = float.PositiveInfinity;
            foreach (var m in rest)
            {
                float length = PathLength(m, knownPosition) - (RoleOf(m) == Role.Trail || m.CurrentState == MonsterAI.State.Chase ? 6f : 0f);
                if (length < best) { best = length; trail = m; }
            }
            if (trail != null)
            {
                rest.Remove(trail);
                if (RoleOf(trail) != Role.Trail) Decide(trail, "trail", "아무도 못 봄 → 아는 위치에 가장 가까움, 흔적 추적", 2f);
                roles[trail] = Role.Trail;
                routes.Remove(trail);
                chasers.Add(trail);
                trail.CommandChase(knownPosition);
            }
        }

        MarkCoveredExits(chasers);
        AssignExits(rest, chasers);
    }

    /// <summary>추격자가 있는 쪽 출구는 이미 막힌 것으로 친다(그쪽으로 보내면 뒤따라가기다).</summary>
    private void MarkCoveredExits(List<MonsterAI> chasers)
    {
        for (int i = 0; i < exits.Count; i++)
        {
            var e = exits[i];
            e.blocked = false;
            foreach (var c in chasers)
            {
                Vector3 side = Vector3.ProjectOnPlane(c.transform.position - knownPosition, Vector3.up);
                if (side.sqrMagnitude > 4f && Vector3.Angle(side, e.direction) < 50f) { e.blocked = true; break; }
            }
            exits[i] = e;
        }
    }

    private void AssignExits(List<MonsterAI> blockers, List<MonsterAI> chasers)
    {
        Vector3 p = PlayerPosition;
        var open = new List<int>();
        for (int i = 0; i < exits.Count; i++) if (!exits[i].blocked) open.Add(i);

        // 비용 = 닿는 시간 − 출구 가중치(가는 쪽) − 지금 맡은 출구 보너스. 가는 길이 플레이어 곁을 지나면 제외(뒤따라가기)
        var cost = new Dictionary<(MonsterAI, int), float>();
        var paths = new Dictionary<(MonsterAI, int), Vector3[]>();
        foreach (var m in blockers)
            foreach (int i in open)
            {
                var path = ComputePath(m, exits[i].point);
                if (path == null || PassesNear(path, p, 5f)) continue;
                float eta = Length(path) / Mathf.Max(1f, blockSpeed);
                float c = eta - exits[i].weight;
                if (Holds(m, exits[i].point)) c -= switchGain;
                cost[(m, i)] = c;
                paths[(m, i)] = (Vector3[])path.Clone();
            }

        var left = new List<MonsterAI>(blockers);
        var taken = new HashSet<int>();
        while (left.Count > 0)
        {
            MonsterAI bestM = null;
            int bestI = -1;
            float bestC = float.PositiveInfinity;
            foreach (var pair in cost)
            {
                if (!left.Contains(pair.Key.Item1) || taken.Contains(pair.Key.Item2)) continue;
                if (pair.Value < bestC) { bestC = pair.Value; bestM = pair.Key.Item1; bestI = pair.Key.Item2; }
            }
            if (bestM == null) break;
            left.Remove(bestM);
            taken.Add(bestI);
            SendToExit(bestM, bestI, paths[(bestM, bestI)]);
        }

        // 막을 출구가 없다(빈 출구는 다른 몬스터가 맡았거나, 뒤에서 플레이어 곁을 지나야만 갈 수 있다).
        // 보고 있으면 계속 쫓는다(보면서 돌아서지 않는다). 못 보면 사냥에서 빠져 둘레로 — 뒤에 줄 서는 대신 재배치로 앞에서 다시 나타난다
        foreach (var m in left)
        {
            if (Sees(m))
            {
                if (RoleOf(m) != Role.Trail) Decide(m, "no_exit_seeing", "막을 출구 없음, 보고 있음 → 계속 쫓음", 2f);
                roles[m] = Role.Trail;
                routes.Remove(m);
                m.CommandChase(knownPosition);
                continue;
            }
            RemoveHunter(m);
            releasedAt[m] = Time.time;
            SendToRing(m, "막을 출구 없음(뒤에서만 갈 수 있음) → 사냥에서 빠짐");
        }
        RecruitIntoOpenExits(open, taken);
    }

    private void SendToExit(MonsterAI m, int index, Vector3[] path)
    {
        var e = exits[index];
        bool newGoal = !Holds(m, e.point);
        roles[m] = Role.Block;
        e.blocked = true;
        exits[index] = e;
        float eta = Length(path) / Mathf.Max(1f, blockSpeed);
        routes[m] = new RouteInfo { corners = path, goal = e.point, eta = eta, colorIndex = e.bin % 2 + 1 };
        // 같은 출구에서 이미 조여 드는 중이면, 출구가 플레이어를 따라 크게(8m+) 움직였을 때만 다시 간다
        if (!newGoal && m.IsClosingIn && Vector3.Distance(m.transform.position, e.point) < 8f) return;
        m.CommandBlock(e.point);
        if (newGoal) Decide(m, "exit_assign", "출구 막기 · " + DirectionName(e.direction) + " · 약 " + eta.ToString("F1") + "초");
    }

    private void ApplySpeeds()
    {
        foreach (var m in hunters)
        {
            if (m == null) continue;
            switch (RoleOf(m))
            {
            case Role.Chase:
                m.SetHuntSpeed(Mathf.Lerp(nearSpeed, farSpeed, Mathf.InverseLerp(nearDistance, farDistance, DistanceToPlayer(m))));
                break;
            case Role.Trail:
                m.SetHuntSpeed(nearSpeed);
                break;
            default:
                m.SetHuntSpeed(blockSpeed);
                break;
            }
        }
    }

    // ────────────────────────────────────────────────
    //  출구
    // ────────────────────────────────────────────────

    private void BuildAnchors()
    {
        anchors.Clear();
        var mesh = NavMesh.CalculateTriangulation();
        var cells = new HashSet<Vector3Int>();
        for (int i = 0; i + 2 < mesh.indices.Length; i += 3)
        {
            Vector3 c = (mesh.vertices[mesh.indices[i]] + mesh.vertices[mesh.indices[i + 1]] + mesh.vertices[mesh.indices[i + 2]]) / 3f;
            var cell = new Vector3Int(Mathf.FloorToInt(c.x / 6f), Mathf.FloorToInt(c.y / 3f), Mathf.FloorToInt(c.z / 6f));
            if (cells.Add(cell)) anchors.Add(c);
        }
    }

    /// <summary>
    /// 출구: 감독이 아는 위치에서 길로 exitMin~exitMax m인 걸을 수 있는 지점을, 그 길이 출발하는 방향(45° 칸)으로 묶는다.
    /// 칸마다 길이가 가운데에 가장 가까운 지점 하나. 예측은 하지 않는다 — 지금 위치에서 나갈 수 있는 길목일 뿐이다.
    /// 가중치: 플레이어가 달리던 쪽 출구일수록 크다(먼저 막는다).
    /// </summary>
    private void BuildExits()
    {
        exits.Clear();
        if (anchors.Count == 0) BuildAnchors();
        if (!NavMesh.SamplePosition(knownPosition, out var origin, 4f, NavMesh.AllAreas)) return;
        float mid = (exitMin + exitMax) * .5f;
        bool headingKnown = Time.time - headingTime <= 2f && heading.sqrMagnitude > .01f;
        var best = new ExitInfo?[8];
        var bestError = new float[8];
        var filter = new NavMeshQueryFilter { agentTypeID = hunters.Count > 0 && hunters[0] != null ? hunters[0].NavigationFilter.agentTypeID : 0, areaMask = NavMesh.AllAreas };
        foreach (var a in anchors)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a - origin.position, Vector3.up);
            float d = flat.magnitude;
            if (d < exitMin * .6f || d > exitMax + 4f || Mathf.Abs(a.y - origin.position.y) > 4f) continue;
            if (!NavMesh.CalculatePath(origin.position, a, filter, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
            var route = pathBuffer.corners;
            float length = Length(route);
            if (length < exitMin || length > exitMax || length > d * 1.6f + 4f) continue;
            Vector3 dir = RouteStartDirection(route);
            if (dir.sqrMagnitude < .01f) continue;
            int bin = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f) / 45f), 0, 7);
            float error = Mathf.Abs(length - mid);
            if (best[bin] != null && error >= bestError[bin]) continue;
            bestError[bin] = error;
            float forward = headingKnown ? Mathf.Max(0f, Vector3.Dot(dir, heading)) : 0f;
            best[bin] = new ExitInfo { point = a, direction = dir, bin = bin, weight = forward * 2f };
        }
        foreach (var e in best) if (e != null) exits.Add(e.Value);
    }

    private static Vector3 RouteStartDirection(Vector3[] route)
    {
        for (int i = 1; i < route.Length; i++)
        {
            Vector3 d = Vector3.ProjectOnPlane(route[i] - route[0], Vector3.up);
            if (d.magnitude >= 3f) return d.normalized;
        }
        return Vector3.ProjectOnPlane(route[route.Length - 1] - route[0], Vector3.up).normalized;
    }

    private string DirectionName(Vector3 dir)
    {
        if (Time.time - headingTime > 2f || heading.sqrMagnitude < .01f) return "옆길";
        float angle = Vector3.SignedAngle(heading, dir, Vector3.up);
        if (Mathf.Abs(angle) <= 45f) return "앞";
        if (Mathf.Abs(angle) >= 135f) return "뒤";
        return angle > 0f ? "오른쪽" : "왼쪽";
    }

    // ────────────────────────────────────────────────
    //  둘레 순찰 · 재배치
    // ────────────────────────────────────────────────

    private Vector3 PlayerPosition => player != null ? player.position : knownPosition;

    private Vector3 PlayerForward
    {
        get
        {
            if (player == null) return Vector3.forward;
            var body = player.GetComponent<Rigidbody>();
            Vector3 v = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) : Vector3.zero;
            if (v.magnitude > 2f) return v.normalized;
            Vector3 f = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            return f.sqrMagnitude > .01f ? f.normalized : Vector3.forward;
        }
    }

    private bool IsFree(MonsterAI m)
    {
        if (m == null || hunters.Contains(m) || noiseCheckers.Contains(m) || !m.CanTakeOrders) return false;
        var st = m.CurrentState;
        return st == MonsterAI.State.Patrol || st == MonsterAI.State.Return || st == MonsterAI.State.Idle;
    }

    /// <summary>
    /// 둘레 자리(ringInterval초마다, 사냥 끝에도): 사냥에 안 낀 몬스터에게 플레이어 둘레 ringMin~ringMax m,
    /// 방향을 고르게 나눈 자리를 준다(첫 자리는 플레이어가 가는 쪽). 자리는 플레이어 눈에 안 보이는 곳을 먼저 고른다.
    /// 자리가 12m 넘게 바뀔 때만 명령한다.
    /// </summary>
    private void PlaceRing(List<MonsterAI> alsoInclude)
    {
        if (player == null) return;
        if (anchors.Count == 0) BuildAnchors();
        var free = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters) if (IsFree(m)) free.Add(m);
        if (alsoInclude != null) foreach (var m in alsoInclude) if (m != null && !free.Contains(m) && !m.IsInStun && !hunters.Contains(m)) free.Add(m);
        if (free.Count == 0) return;

        Vector3 p = PlayerPosition;
        Vector3 fwd = PlayerForward;
        float baseAngle = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        int n = free.Count;
        float step = 360f / n;
        // 사냥 중이면 사냥 몬스터가 있는 쪽을 피해 돌린다
        if (hunting && hunters.Count > 0)
        {
            Vector3 huntSide = Vector3.zero;
            foreach (var h in hunters) if (h != null) huntSide += Vector3.ProjectOnPlane(h.transform.position - p, Vector3.up).normalized;
            if (huntSide.sqrMagnitude > .01f) baseAngle = Mathf.Atan2(-huntSide.x, -huntSide.z) * Mathf.Rad2Deg;
        }
        var slots = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            if (FindRingSlot(p, baseAngle + i * step, Mathf.Min(40f, step * .5f), out Vector3 slot)) slots.Add(slot);
        }
        // 가까운 짝부터 맺는다
        while (free.Count > 0 && slots.Count > 0)
        {
            MonsterAI bestM = null;
            int bestS = -1;
            float bestD = float.PositiveInfinity;
            foreach (var m in free)
                for (int s = 0; s < slots.Count; s++)
                {
                    float d = Vector3.Distance(m.transform.position, slots[s]);
                    if (d < bestD) { bestD = d; bestM = m; bestS = s; }
                }
            Vector3 chosen = slots[bestS];
            free.Remove(bestM);
            slots.RemoveAt(bestS);
            bool moved = !ringSlots.TryGetValue(bestM, out Vector3 old) || Vector3.Distance(old, chosen) > 12f ||
                (bestM.CurrentState != MonsterAI.State.Patrol && bestM.CurrentState != MonsterAI.State.Return);
            ringSlots[bestM] = chosen;
            if (!moved) continue;
            bestM.CommandPatrolAt(chosen);
            Decide(bestM, "ring_place", "둘레 자리 (플레이어에서 " + Vector3.Distance(p, chosen).ToString("F0") + "m, " +
                Mathf.RoundToInt(Mathf.Repeat(Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(chosen - p, Vector3.up), Vector3.up), 360f)) + "°)", 3f);
        }
    }

    /// <summary>플레이어 둘레 한 방향의 자리: 거리 ringMin~ringMax, 방향 ±spread°, 플레이어에게 안 보이는 곳 우선, 45m에 가까운 곳.</summary>
    private bool FindRingSlot(Vector3 p, float angle, float spread, out Vector3 slot)
    {
        slot = Vector3.zero;
        Vector3 eye = p + Vector3.up * 1.5f;
        float mid = (ringMin + ringMax) * .5f;
        var scored = new List<KeyValuePair<float, Vector3>>();
        foreach (var a in anchors)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a - p, Vector3.up);
            float d = flat.magnitude;
            if (d < ringMin || d > ringMax) continue;
            float diff = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg));
            if (diff > spread) continue;
            bool seen = !Physics.Linecast(eye, a + Vector3.up * 1.3f, ObstacleMask, QueryTriggerInteraction.Ignore);
            scored.Add(new KeyValuePair<float, Vector3>(Mathf.Abs(d - mid) + diff * .2f + (seen ? 30f : 0f), a));
        }
        scored.Sort((x, y) => x.Key.CompareTo(y.Key));
        // 플레이어에게서 길로 이어진 자리만(떨어진 지붕·섬에 두면 몬스터가 갈 길이 없어 멈춘다 — 09-26 15:14 판 Global 멈춤 3번).
        // 길이가 곧은 거리의 2배를 넘는 자리(벽 너머 멀리 돌아가야 하는 곳)도 뺀다
        if (!NavMesh.SamplePosition(p, out var from, 3f, NavMesh.AllAreas)) return false;
        for (int i = 0; i < scored.Count && i < 6; i++)
        {
            Vector3 a = scored[i].Value;
            if (!NavMesh.CalculatePath(from.position, a, NavMesh.AllAreas, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
            if (Length(pathBuffer.corners) > Vector3.Distance(from.position, a) * 2f + 10f) continue;
            slot = a;
            return true;
        }
        return false;
    }

    private LayerMask ObstacleMask
    {
        get
        {
            foreach (var m in MonsterAI.activeMonsters) if (m != null) return m.obstacleMask;
            return Physics.DefaultRaycastLayers;
        }
    }

    /// <summary>
    /// 재배치(1초마다): 사냥에 안 낀 몬스터가 플레이어에게서 relocateDistance m 넘게 떨어지고 어느 카메라에도 안 보이면,
    /// 둘레 자리 중 카메라에 안 보이고 플레이어와 relocateMin m 넘게 떨어진 곳으로 옮긴다. 한 번에 한 마리.
    /// </summary>
    private void RelocateHidden()
    {
        if (player == null) return;
        Vector3 p = PlayerPosition;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (!IsFree(m)) continue;
            if (relocatedAt.TryGetValue(m, out float at) && Time.time - at < relocateCooldown) continue;
            Vector3 from = m.transform.position;
            float distance = Vector3.Distance(from, p);
            if (distance < relocateDistance || VisibleToAnyCamera(from)) continue;
            if (!ringSlots.TryGetValue(m, out Vector3 slot) || Vector3.Distance(slot, p) < relocateMin || Vector3.Distance(slot, p) > ringMax + 10f)
            {
                Vector3 fwd = PlayerForward;
                if (!FindRingSlot(p, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + Random.Range(-90f, 90f), 45f, out slot)) continue;
            }
            if (Vector3.Distance(slot, p) < relocateMin || VisibleToAnyCamera(slot)) continue;
            if (!NavMesh.SamplePosition(slot, out var hit, 2f, NavMesh.AllAreas)) continue;
            relocatedAt[m] = Time.time;
            ringSlots[m] = hit.position;
            m.Relocate(hit.position);
            Relocated?.Invoke(m, from, hit.position);
            Decide(m, "relocate", "안 보이고 " + distance.ToString("F0") + "m 떨어짐 → 둘레 자리로 재배치 (플레이어에서 " +
                Vector3.Distance(hit.position, p).ToString("F0") + "m)");
            return;
        }
    }

    /// <summary>게임 화면을 그리는 카메라(지도처럼 직교 카메라는 뺀다) 중 하나라도 그 자리를 볼 수 있나.</summary>
    private bool VisibleToAnyCamera(Vector3 point)
    {
        Vector3 target = point + Vector3.up * 1.3f;
        var mask = ObstacleMask;
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null || cam.orthographic) continue;
            Vector3 view = cam.WorldToViewportPoint(target);
            if (view.z <= 0f || view.z > cam.farClipPlane || view.x < -.05f || view.x > 1.05f || view.y < -.05f || view.y > 1.05f) continue;
            if (!Physics.Linecast(cam.transform.position, target, mask, QueryTriggerInteraction.Ignore)) return true;
        }
        // 후방 미러(렌더 텍스처로 그려 꺼져 있는 카메라)도 본다
        var mirror = GameObject.Find("~ThreatCutInCamera");
        if (mirror != null && mirror.TryGetComponent(out Camera cut))
        {
            Vector3 view = cut.WorldToViewportPoint(target);
            if (view.z > 0f && view.z <= cut.farClipPlane && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f &&
                !Physics.Linecast(cut.transform.position, target, mask, QueryTriggerInteraction.Ignore)) return true;
        }
        return false;
    }

    private void SendToRing(MonsterAI m, string why)
    {
        if (m == null) return;
        m.SetHuntSpeed(-1f);
        noiseCheckers.Remove(m);
        Vector3 p = PlayerPosition;
        Vector3 away = Vector3.ProjectOnPlane(m.transform.position - p, Vector3.up);
        float angle = away.sqrMagnitude > .01f ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        if (!FindRingSlot(p, angle, 60f, out Vector3 slot)) { m.CommandPatrolAt(m.transform.position); return; }
        ringSlots[m] = slot;
        m.CommandPatrolAt(slot);
        Decide(m, "go_ring", why + " → 둘레 자리로 (" + Vector3.Distance(p, slot).ToString("F0") + "m)");
    }

    // ────────────────────────────────────────────────
    //  도우미
    // ────────────────────────────────────────────────

    /// <summary>m 앞(플레이어에 더 가까이, 같은 방향)에서 보고 쫓는 추격자.</summary>
    private MonsterAI FrontChaser(MonsterAI m)
    {
        foreach (var c in hunters)
        {
            if (c == null || c == m || c.CurrentState != MonsterAI.State.Chase || !Sees(c)) continue;
            if (SameSideBehind(m, c)) return c;
        }
        return null;
    }

    /// <summary>
    /// a가 b 뒤 같은 방향에 있나: 플레이어 기준 방향 차이가 sameSideAngle° 안이고 더 멀다.
    /// 플레이어에 6m 안으로 붙은 몬스터는 예외(이미 코앞 — 돌려보내면 돌아서는 모습이 된다).
    /// </summary>
    private bool SameSideBehind(MonsterAI a, MonsterAI b)
    {
        Vector3 p = PlayerPosition;
        Vector3 toA = Vector3.ProjectOnPlane(a.transform.position - p, Vector3.up);
        Vector3 toB = Vector3.ProjectOnPlane(b.transform.position - p, Vector3.up);
        if (toA.magnitude <= 6f || toB.magnitude < 1f) return false;
        return Vector3.Angle(toA, toB) < sameSideAngle && toA.magnitude >= toB.magnitude - 1f;
    }

    /// <summary>m이 이미 이 출구(8m 안)를 맡고 있나. 출구 지점은 플레이어를 따라 조금씩 움직이므로 거리로 같은 출구를 알아본다.</summary>
    private bool Holds(MonsterAI m, Vector3 exitPoint) => RoleOf(m) == Role.Block && Vector3.Distance(m.BlockGoal, exitPoint) < 8f;

    private static bool PassesNear(Vector3[] path, Vector3 playerAt, float radius)
    {
        float total = Length(path), walked = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float segment = Vector3.Distance(path[i - 1], path[i]);
            // 마지막 8m는 플레이어에게 다가가는 구간이라 검사하지 않는다
            if (total - walked > 8f && DistanceToSegment(playerAt, path[i - 1], path[i]) < radius) return true;
            walked += segment;
        }
        return false;
    }

    private float DistanceToPlayer(MonsterAI m) => Vector3.Distance(m.transform.position, PlayerPosition);

    private Vector3[] ComputePath(MonsterAI m, Vector3 to)
    {
        var filter = m.NavigationFilter;
        if (!NavMesh.SamplePosition(m.transform.position, out var a, 2f, filter) ||
            !NavMesh.SamplePosition(to, out var b, 3f, filter) ||
            !NavMesh.CalculatePath(a.position, b.position, filter, pathBuffer) ||
            pathBuffer.status != NavMeshPathStatus.PathComplete) return null;
        var corners = pathBuffer.corners;
        return corners.Length >= 2 ? corners : new[] { a.position, b.position };
    }

    private float PathLength(MonsterAI m, Vector3 to)
    {
        var path = ComputePath(m, to);
        return path != null ? Length(path) : float.PositiveInfinity;
    }

    public static float Length(Vector3[] points)
    {
        float length = 0f;
        if (points != null) for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var d = b - a;
        return Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude)));
    }

    /// <summary>기록·화면용 한 줄. "출구 k/n"은 QA 도구가 막힌 출구 비율을 셀 때 쓴다.</summary>
    private void RebuildSummary()
    {
        int chase = 0, trail = 0, block = 0, blocked = 0;
        foreach (var m in hunters)
        {
            switch (RoleOf(m))
            {
            case Role.Chase: chase++; break;
            case Role.Trail: trail++; break;
            case Role.Block: block++; break;
            }
        }
        foreach (var e in exits) if (e.blocked) blocked++;
        summary = "추격 " + chase + " · 흔적 " + trail + " · 출구 막기 " + block + " · 출구 " + blocked + "/" + exits.Count;
    }

    private void OnDrawGizmos()
    {
        if (!hunting) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(knownPosition, 1.5f);
        foreach (var e in exits)
        {
            Gizmos.color = e.blocked ? Color.red : Color.green;
            Gizmos.DrawWireSphere(e.point, 1.2f);
        }
        Gizmos.color = Color.cyan;
        foreach (var r in routes.Values)
            if (r.corners != null)
                for (int i = 1; i < r.corners.Length; i++) Gizmos.DrawLine(r.corners[i - 1], r.corners[i]);
    }
}
