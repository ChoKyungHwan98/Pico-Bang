using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감독(뇌 2). 플레이어를 직접 보지 않고, 몬스터가 본 것·들은 것만으로 사냥 팀을 짠다.
///
/// 사냥 팀 = 추격 1 + 우회 최대 2 (huntTeamSize).
/// - 추격: 처음 본 몬스터. 추격을 포기하거나 감전되면 팀에서 다른 몬스터가 이어받는다.
/// - 우회: 플레이어가 곧 도착할 곳의 옆·앞으로 돌아간다. 가까이서 보면 협공 추격으로 바뀐다.
/// - 줄줄이 방지: 추격자 뒤를 같은 쪽에서 따라오면 반대쪽 우회로 돌린다(집으로 보내지 않는다).
/// - 집으로 보내는 경우는 둘뿐: 사냥 끝(기억 만료), 인원 초과(가장 먼 몬스터). 가장 가까운 빈 구역으로 간다.
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
    private static void ResetStatics() { instance = null; isQuitting = false; DecisionMade = null; NoiseReported = null; }

    public enum Role { None, Lead, Assist, Flank }

    /// <summary>지도 표시용 경로 정보.</summary>
    public class RouteInfo
    {
        public Vector3[] corners;
        public Vector3 goal;
        public bool directChaser;
        public int colorIndex;
        public float eta;
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

    private const float PlayerRunSpeed = 11f;
    private const float PlanInterval = .4f;
    private const float FlankReplanInterval = 1f;
    private const float CloseEncounter = 8f;
    private const float BrawlDistance = 12f;

    private readonly Dictionary<MonsterAI, Role> roles = new Dictionary<MonsterAI, Role>();
    private readonly Dictionary<MonsterAI, float> benchUntil = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> queueSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, int> flankMisses = new Dictionary<MonsterAI, int>();
    private readonly Dictionary<MonsterAI, float> nextFlankPlan = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, int> preferredSide = new Dictionary<MonsterAI, int>();
    private readonly Dictionary<MonsterAI, float> flankInvalidSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> flankSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> closeInUntil = new Dictionary<MonsterAI, float>();   // 갈림길에서 조여 드는 중   // 우회 역할을 맡은 시각
    private readonly Dictionary<MonsterAI, RouteInfo> routes = new Dictionary<MonsterAI, RouteInfo>();
    private readonly Dictionary<string, float> decisionThrottle = new Dictionary<string, float>();
    private readonly List<MonsterAI> team = new List<MonsterAI>();
    private NavMeshPath pathBuffer;

    private sealed class Zone
    {
        public Transform center;
        public float radius;
        public MonsterAI owner;
    }
    private readonly List<Zone> zones = new List<Zone>();
    // 우회 후보로 쓰는 걸을 수 있는 지점: NavMesh 삼각형 중심을 6m 격자로 솎은 것
    private readonly List<Vector3> anchors = new List<Vector3>();

    private MonsterAI lead, spotter, global, noiseChecker;
    private Transform player;
    private bool hunting, hasSighting;
    private Vector3 knownPosition, sightPosition, heading;
    private float knowledgeTime = float.NegativeInfinity, sightTime = float.NegativeInfinity, headingTime = float.NegativeInfinity;
    private Vector3 headingSamplePos;
    private float headingSampleTime = float.NegativeInfinity;
    private float nextPlan, nextSpread, nextRotateCheck;
    private float leadChangedAt = float.NegativeInfinity;   // 추격자가 바뀐 시각 — 2초 안에 다시 바꾸지 않는다
    private string summary = "순찰 중";
    private int huntCount;

    // ── 설정: 전역 몬스터의 인스펙터 값 ──
    private MonsterAI Settings
    {
        get
        {
            if (global != null) return global;
            foreach (var m in MonsterAI.activeMonsters)
                if (m != null && m.role == MonsterAI.MonsterRole.Global_Stalker) { global = m; break; }
            return global;
        }
    }
    public float TrackingSpeedCap => Settings != null ? Mathf.Max(1f, Settings.huntNearSpeed) : 10f;
    public float PursuitPersistence => Settings != null ? Mathf.Max(.5f, Settings.pursuitPersistence) : 3f;
    public float PersistenceDecay => Settings != null ? Mathf.Clamp01(Settings.persistenceDecay) : .7f;
    public float PersistenceMin => Settings != null ? Mathf.Max(.2f, Settings.persistenceMin) : 2f;
    public float GiveUpLookTime => Settings != null ? Mathf.Max(0, Settings.giveUpLookTime) : 2f;
    public float FlankTimeout => Settings != null ? Mathf.Max(2f, Settings.flankTimeout) : 10f;
    public float FlankSearchTime => Settings != null ? Mathf.Max(0f, Settings.flankSearchTime) : 1.5f;
    private float FarSpeed => Settings != null ? Mathf.Max(TrackingSpeedCap, Settings.huntFarSpeed) : 14f;
    private float Memory => Settings != null ? Mathf.Max(1, Settings.huntMemory) : 8f;
    private int TeamSize => Settings != null ? Mathf.Clamp(Settings.huntTeamSize, 1, 3) : 3;
    private float LeadTime => Settings != null ? Mathf.Max(0f, Settings.flankLeadTime) : 2f;
    private float FlankRadius => Settings != null ? Mathf.Max(6f, Settings.flankRadius) : 16f;
    private float RecruitRange => Settings != null ? Mathf.Max(10f, Settings.flankRecruitRange) : 130f;
    private float EngageDistance => Settings != null ? Mathf.Max(3f, Settings.flankEngageDistance) : 18f;
    private int MaxMisses => Settings != null ? Mathf.Max(1, Settings.flankMaxMisses) : 3;
    private float QueueDistance => Settings != null ? Mathf.Max(1f, Settings.queueDistance) : 10f;
    private float QueueSeconds => Settings != null ? Mathf.Max(.2f, Settings.queueSeconds) : 2.5f;
    private float BenchSeconds => Settings != null ? Mathf.Max(0f, Settings.benchSeconds) : 5f;

    // ── 화면·기록용 ──
    public bool IsHunting => hunting;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => sightPosition;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => heading;
    public string SightingSpotterName => spotter != null ? spotter.name : "-";
    public Vector3 DebugKnownPosition => knownPosition;
    public MonsterAI DebugPressure => lead;
    public IReadOnlyList<MonsterAI> DebugTeam => team;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugRoutes => routes;
    public string DebugLayoutSummary => summary;
    public int DebugChaserCount
    {
        get
        {
            int count = 0;
            foreach (var pair in roles) if (pair.Key != null && pair.Key.CurrentState == MonsterAI.State.Chase) count++;
            return count;
        }
    }
    public Role RoleOf(MonsterAI m) => m != null && roles.TryGetValue(m, out var r) ? r : Role.None;
    public string DebugOrderLabel(MonsterAI m)
    {
        switch (RoleOf(m))
        {
        case Role.Lead: return "추격";
        case Role.Assist: return "협공";
        case Role.Flank: return "우회";
        }
        return "";
    }
    public string DebugRouteLabel(MonsterAI m)
    {
        if (!routes.TryGetValue(m, out var r) || r.directChaser) return "";
        return "우회 ETA~" + r.eta.ToString("F1") + "초 · 놓침 " + (flankMisses.TryGetValue(m, out int n) ? n : 0);
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

    /// <summary>플레이어를 보고 있다. 누가 봤든 무리의 지식이 된다. 역할은 바꾸지 않는다.</summary>
    public void ReportSighting(MonsterAI observer, Vector3 position)
    {
        if (observer == null || observer.IsInStun) return;
        UpdateHeading(position);
        hasSighting = true;
        sightPosition = position;
        sightTime = Time.time;
        spotter = observer;
        Remember(position, true, observer);
    }

    /// <summary>순찰·복귀·멈춤 중인 몬스터가 플레이어를 봤다: 쫓아도 되는가.</summary>
    public bool RequestJoin(MonsterAI m)
    {
        if (m == null || !m.CanTakeOrders) return false;
        if (roles.TryGetValue(m, out var existing))
        {
            if (existing == Role.Flank) roles[m] = lead == null || lead == m ? Role.Lead : Role.Assist;
            if (roles[m] == Role.Lead) lead = m;
            return true;
        }
        float distance = DistanceToPlayer(m);
        if (benchUntil.TryGetValue(m, out float until) && Time.time < until && distance > CloseEncounter)
        {
            Decide(m, "join_denied_bench", "방금 팀에서 빠져 돌아가는 중 → 잠시 합류 안 함", 2f);
            return false;
        }
        if (!hunting) StartHunt(m.transform.position);
        if (roles.Count >= TeamSize)
        {
            // 내보낼 몬스터: 추격자가 아니고 지금 플레이어를 보고 있지 않은 몬스터 중 가장 먼 몬스터.
            // 보고 있는 몬스터를 돌려보내면 '보면서 돌아서는' 모습이 된다
            MonsterAI farthest = FarthestNonLead(true);
            if (farthest != null && DistanceToPlayer(farthest) > distance && DistanceToPlayer(farthest) > BrawlDistance)
            {
                Decide(farthest, "cap_release", "새로 본 " + Short(m) + "가 더 가까움 → 인원 초과, 못 보고 있는 가장 먼 몬스터 → 빈 구역으로");
                SendHome(farthest, true);
            }
            else if (distance <= CloseEncounter)
            {
                // 코앞에서 본 몬스터가 보면서 돌아서면 안 된다(09-25 F8 04:01 B, 3.7m) — 인원이 넘쳐도 합류한다.
                // 모두 가까이 붙어 싸우는 동안에는 EnforceCap도 누구를 돌려보내지 않는다
                Decide(m, "join_close_over_cap", "코앞(" + distance.ToString("F0") + "m)에서 봄 → 인원 가득이어도 합류", 2f);
            }
            else
            {
                Decide(m, "join_denied_full", "봤지만 인원 " + TeamSize + "마리 가득(내보낼 몬스터가 모두 가까이 붙어 싸우는 중) → 순찰 계속", 2f);
                return false;
            }
        }
        Role role = lead == null ? Role.Lead : Role.Assist;
        SetRole(m, role);
        if (role == Role.Lead) lead = m;
        Decide(m, role == Role.Lead ? "lead_spotted" : "assist_spotted",
            role == Role.Lead ? "플레이어 발견 → 추격자" : "직접 발견 → 협공 추격 (추격자 " + Short(lead) + ")");
        nextPlan = 0f;
        return true;
    }

    /// <summary>우회 중인 몬스터가 플레이어를 봤다: 협공 추격으로 바꿔도 되는가.</summary>
    public bool RequestEngage(MonsterAI m, bool atGoal = false)
    {
        if (m == null || !m.CanTakeOrders) return false;
        if (!roles.ContainsKey(m)) return RequestJoin(m);
        float distance = DistanceToPlayer(m);
        if (!atGoal && distance > EngageDistance) return false;
        if (lead != null && lead != m && lead.CurrentState == MonsterAI.State.Chase && QueuesBehind(m, lead))
        {
            Decide(m, "engage_denied_queue", "봤지만 추격자 " + Short(lead) + "와 같은 쪽 뒤 → 우회 계속", 2f);
            return false;
        }
        Role role = lead == null || lead == m ? Role.Lead : Role.Assist;
        SetRole(m, role);
        if (role == Role.Lead) lead = m;
        flankMisses.Remove(m);
        Decide(m, "engage", (atGoal ? "우회 지점에서" : "우회 중 " + distance.ToString("F0") + "m 앞에서") + " 발견 → 협공 추격");
        return true;
    }

    /// <summary>추격이 끈질김을 다 쓰고 놓쳤다.</summary>
    public void ReportLostPlayer(MonsterAI m)
    {
        if (!roles.ContainsKey(m)) return;
        Decide(m, "lost", "놓침 · 끈질김 소진 → 둘러본 뒤 판단");
        RemoveRole(m);
        if (m == lead) lead = null;
        nextPlan = 0f;
    }

    /// <summary>수색(소리 확인·포기 뒤 둘러보기)이 끝났다.</summary>
    public void ReportSearchDone(MonsterAI m)
    {
        if (hunting && roles.ContainsKey(m)) { nextPlan = 0f; nextFlankPlan[m] = 0f; return; }
        SendHome(m, false);
    }

    /// <summary>우회 지점에 도착해 둘러봤지만 못 찾았다.</summary>
    public void ReportFlankMissed(MonsterAI m)
    {
        if (!hunting || !roles.ContainsKey(m))
        {
            SendHome(m, false);
            return;
        }
        // 무리가 플레이어 위치를 2초 안에 알았다면 '못 찾음'이 아니라 먼저 도착한 것이다 — 다음 지점만 받는다
        if (Time.time - knowledgeTime <= 2f)
        {
            // 갈림길에 먼저 왔는데 플레이어가 다른 가지로 갔다 — 가까우면(길 30m) 새 갈림길로 헤매지 않고
            // 그 자리(추격자와 다른 쪽)에서 조여 들어간다. 추격자와 같은 쪽이면 뒤따라가기라 다음 갈림길로
            var toPlayer = ComputePath(m, knownPosition);
            Vector3 side = Vector3.ProjectOnPlane(m.transform.position - knownPosition, Vector3.up);
            Vector3 leadSide = lead != null && lead != m ? Vector3.ProjectOnPlane(lead.transform.position - knownPosition, Vector3.up) : Vector3.zero;
            bool otherSide = leadSide.sqrMagnitude < 4f || side.sqrMagnitude < .01f || Vector3.Angle(side, leadSide) >= 70f;
            if (toPlayer != null && Length(toPlayer) <= 30f && otherSide && !PassesNear(toPlayer, lead != null ? lead.transform.position : knownPosition + Vector3.up * 999f, 4f))
            {
                SetRole(m, lead == null ? Role.Lead : Role.Assist);
                if (lead == null) lead = m;
                closeInUntil[m] = Time.time + 4f;
                m.CommandChase(knownPosition);
                Decide(m, "flank_close_in", "갈림길에 먼저 도착, 플레이어는 다른 길(" + Length(toPlayer).ToString("F0") + "m) → 이 쪽에서 조여 감", 1.5f);
                return;
            }
            Decide(m, "flank_early", "우회 지점에 먼저 도착 → 플레이어 앞쪽 다음 지점", 1.5f);
            AssignFlank(m, true);
            return;
        }
        int misses = (flankMisses.TryGetValue(m, out int n) ? n : 0) + 1;
        flankMisses[m] = misses;
        if (misses >= MaxMisses)
        {
            Decide(m, "flank_give_up", "우회 " + misses + "번 연속 못 찾음 → 빈 구역으로");
            RemoveRole(m);
            SendHome(m, false);
            nextPlan = 0f;
            return;
        }
        Decide(m, "flank_miss", "우회 지점에 없음 (" + misses + "/" + MaxMisses + ") → 다음 지점");
        preferredSide.Remove(m);
        AssignFlank(m, true);
    }

    /// <summary>우회 길이 막혔다(경로 무효).</summary>
    public void ReportFlankBlocked(MonsterAI m)
    {
        if (!roles.ContainsKey(m)) { SendHome(m, false); return; }
        Decide(m, "flank_blocked", "우회 길 막힘 → 다른 지점", 1f);
        AssignFlank(m, true);
    }

    public void ReportStunned(MonsterAI m)
    {
        if (!roles.ContainsKey(m)) return;
        Decide(m, "stunned", RoleOf(m) == Role.Lead ? "추격자 감전 → 팀에서 빠짐, 추격 교대" : "감전 → 팀에서 빠짐");
        RemoveRole(m);
        if (m == lead) lead = null;
        nextPlan = 0f;
    }

    /// <summary>감전이 풀렸다. 보이면 다시 합류, 아니면 빈 구역으로.</summary>
    public void ReportRecovered(MonsterAI m, bool seesPlayer)
    {
        if (seesPlayer && player != null && RequestJoin(m))
        {
            m.CommandChase(player.position);
            return;
        }
        if (hunting && roles.Count < TeamSize && DistanceToPlayer(m) <= RecruitRange)
        {
            SetRole(m, Role.Flank);
            Decide(m, "recovered_flank", "감전 풀림 → 빈자리 우회");
            AssignFlank(m, true);
            return;
        }
        SendHome(m, false);
    }

    public void ReportIdle(MonsterAI m)
    {
        if (!roles.ContainsKey(m)) return;
        Decide(m, "idle", "갈 길이 없음 → 멈춤, 팀에서 빠짐");
        RemoveRole(m);
        if (m == lead) lead = null;
        nextPlan = 0f;
    }

    /// <summary>추격 중 놓쳤을 때 쫓아갈 곳: 무리가 아는 최신 위치.</summary>
    public bool TryGetPursuitHint(MonsterAI m, out Vector3 hint)
    {
        hint = knownPosition;
        return hunting && Time.time - knowledgeTime <= Memory;
    }

    /// <summary>
    /// 소리. 몬스터 귀(자기 청각과 소리 범위 중 작은 쪽)에 닿으면 무리가 그 위치를 안다.
    /// 사냥 중이 아닐 때 총소리를 들으면, 들은 몬스터 중 가장 가까운 한 마리가 확인하러 간다(추격 · 못 본 상태).
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
        // 사냥 팀이 들으면 위치 힌트만 새로 고친다(놓친 추격자가 소리 쪽으로 간다). 끈질김은 채우지 않는다 —
        // 플레이어는 늘 쏘므로, 소리로 끈질김을 채우면 한 번 붙은 추격자를 절대 떨칠 수 없다
        bool teamHeard = false;
        foreach (var m in listeners) if (roles.ContainsKey(m)) { teamHeard = true; break; }
        if (hunting && teamHeard) Remember(position, false, null);
        // 발견은 눈으로만. 소리는 주의를 끈다: 사냥에 안 낀 몬스터 중 가장 가까운 1마리가 소리 난 곳을 확인하러 간다(걸음, 플레이어보다 느림)
        MonsterAI nearest = null;
        float best = float.PositiveInfinity;
        foreach (var m in listeners)
        {
            if (!m.CanTakeOrders || roles.ContainsKey(m) || m.IsGivingUp) continue;
            if (benchUntil.TryGetValue(m, out float until) && Time.time < until) continue;
            var st = m.CurrentState;
            if (st != MonsterAI.State.Patrol && st != MonsterAI.State.Investigate && st != MonsterAI.State.Idle) continue;
            float length = PathLength(m, position);
            if (length < best) { best = length; nearest = m; }
        }
        if (nearest == null) return;
        // 이미 확인하러 가는 몬스터가 있으면 그 몬스터의 목적지만 옮긴다 — 쏠 때마다 새 몬스터를 부르지 않는다
        if (noiseChecker != null && noiseChecker != nearest && noiseChecker.CurrentState == MonsterAI.State.Investigate &&
            !roles.ContainsKey(noiseChecker) && listeners.Contains(noiseChecker) &&
            PathLength(noiseChecker, position) < best + 15f)
            nearest = noiseChecker;
        bool fresh = nearest != noiseChecker || nearest.CurrentState != MonsterAI.State.Investigate;
        noiseChecker = nearest;
        nearest.CommandSearch(position);
        if (fresh) Decide(nearest, "noise_check", (kind == NoiseKind.Shot ? "총소리" : "과녁 소리") + " 들음 (" + listeners.Count + "마리 중 가장 가까움) → 소리 난 곳 확인 (발견은 눈으로)");
    }

    /// <summary>과녁 경보: 과녁을 쏜 순간의 플레이어 위치. 사냥 중일 때만 위치를 새로 고친다(사냥을 시작하지는 않는다).</summary>
    public void ReportTargetAlarm(Vector3 shotPosition, float shotTime)
    {
        if (hunting && Time.time - shotTime <= Memory) Remember(shotPosition, false, null);
    }

    public void AbortHunt()
    {
        EndHunt(false);
        hasSighting = false;
        knowledgeTime = sightTime = headingTime = headingSampleTime = float.NegativeInfinity;
        heading = Vector3.zero;
        benchUntil.Clear(); decisionThrottle.Clear(); flankInvalidSince.Clear(); flankSince.Clear();
        noiseChecker = null;
        zones.Clear();
        summary = "순찰 중";
    }

    // ────────────────────────────────────────────────
    //  사냥
    // ────────────────────────────────────────────────

    private void StartHunt(Vector3 near)
    {
        hunting = true;
        huntCount++;
        nextPlan = 0f;
        Decide(null, "hunt_start", "사냥 시작 #" + huntCount);
    }

    private void EndHunt(bool announce)
    {
        if (announce && hunting) Decide(null, "hunt_end", Memory.ToString("F0") + "초 동안 아무도 못 봄 → 사냥 끝, 팀 복귀 (총소리는 확인하러 가는 몬스터만 부른다)");
        foreach (var m in new List<MonsterAI>(roles.Keys))
        {
            if (m == null) continue;
            m.SetHuntSpeed(-1f);
            if (!m.IsInStun) SendHome(m, false);
        }
        foreach (var m in MonsterAI.activeMonsters) if (m != null) m.ResetPursuit();
        roles.Clear(); team.Clear(); routes.Clear(); queueSince.Clear(); flankMisses.Clear();
        nextFlankPlan.Clear(); preferredSide.Clear();
        lead = null;
        hunting = false;
        summary = "순찰 중";
    }

    private void Remember(Vector3 point, bool sight, MonsterAI source)
    {
        knownPosition = point;
        knowledgeTime = Time.time;
        if (!sight) return;   // 소리는 위치 힌트일 뿐 — 사냥 시작·끈질김은 눈으로 본 것만
        if (!hunting) StartHunt(point);
        foreach (var pair in roles)
            if (pair.Key != null && pair.Key != source && pair.Key.CurrentState == MonsterAI.State.Chase)
                pair.Key.RefreshPursuitEvidence(point);
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
        if (zones.Count == 0) BuildZones();
        if (Time.time >= nextSpread) { nextSpread = Time.time + 4f; SpreadPatrols(); }
        if (!hunting) return;
        if (Time.time - sightTime > Memory) { EndHunt(true); return; }
        if (Time.time >= nextPlan) Plan();
    }

    private void Plan()
    {
        nextPlan = Time.time + PlanInterval;
        CleanRoles();
        EnsureLead();
        BreakQueues();
        EnforceCap();
        RotateFlanks();
        FillFlanks();
        UpdateFlankGoals();
        ApplySpeeds();
        RebuildDebug();
    }

    private void CleanRoles()
    {
        foreach (var m in new List<MonsterAI>(roles.Keys))
        {
            if (m == null || !m.isActiveAndEnabled) { roles.Remove(m); continue; }
            if (m.IsInStun) { RemoveRole(m); if (m == lead) lead = null; continue; }
            // 명령과 상태가 어긋나면(예: 복귀 중인데 역할이 남음) 역할을 정리한다
            var s = m.CurrentState;
            if (s == MonsterAI.State.Return || s == MonsterAI.State.Patrol || s == MonsterAI.State.Idle)
            {
                RemoveRole(m);
                if (m == lead) lead = null;
            }
        }
        if (lead != null && (!roles.ContainsKey(lead) || lead.CurrentState != MonsterAI.State.Chase))
        {
            if (roles.ContainsKey(lead) && lead.CurrentState == MonsterAI.State.Investigate && lead.IsGivingUp) { /* 포기 중 — ReportLostPlayer가 정리 */ }
            else if (!roles.ContainsKey(lead)) lead = null;
        }
    }

    /// <summary>추격자가 없으면 세운다: 지금 보고 있는 팀원 → 추격 중인 팀원 → 가장 가까운 우회 → 가장 가까운 자유 몬스터.</summary>
    private void EnsureLead()
    {
        if (lead != null && roles.ContainsKey(lead) && lead.CurrentState == MonsterAI.State.Chase && !lead.IsGivingUp) return;
        MonsterAI best = null;
        float bestDistance = float.PositiveInfinity;
        int bestRank = 9;
        foreach (var pair in roles)
        {
            var m = pair.Key;
            if (m == null || !m.CanTakeOrders || m.IsGivingUp) continue;
            int rank = m.IsSeeingPlayer ? 0 : m.CurrentState == MonsterAI.State.Chase ? 1 : 2;
            float d = DistanceToPlayer(m);
            if (rank < bestRank || (rank == bestRank && d < bestDistance)) { best = m; bestRank = rank; bestDistance = d; }
        }
        string why;
        if (best != null)
        {
            why = bestRank == 0 ? "보고 있음" : bestRank == 1 ? "추격 중" : "가장 가까운 팀원";
        }
        else
        {
            // 팀 밖 몬스터를 새 추격자로 부르는 건 방금(2초 안) 누가 봤을 때만 — 소리만으로는 부르지 않는다
            if (Time.time - sightTime > 2f) return;
            best = NearestFree(knownPosition, out float length);
            if (best == null) return;
            why = "가장 가까운 몬스터 (길 " + length.ToString("F0") + "m)";
        }
        var previous = lead;
        lead = best;
        if (previous != null && previous != best && roles.ContainsKey(previous)) SetRole(previous, Role.Assist);
        SetRole(best, Role.Lead);
        if (best.CurrentState != MonsterAI.State.Chase) best.CommandChase(knownPosition);
        Decide(best, "lead_assigned", "추격자 " + (previous != null && previous != best ? "교대 (" + Short(previous) + " → " + Short(best) + ")" : "지정") + " · " + why);
    }

    /// <summary>
    /// 추격 정리(0.4초마다):
    /// ① 추격자가 1초 넘게 못 보고 있고 다른 팀원이 보고 있으면, 보고 있는 가장 가까운 팀원이 추격자가 된다.
    /// ② 협공 추격자가 1.5초 넘게 못 보면 뒤에서 흔적을 쫓지 않고 우회로 돌아간다(뒤따라가기 = 줄줄이).
    /// ③ 줄줄이: 다른 추격자 뒤 같은 쪽 queueDistance 안에서 queueSeconds 동안 따라오면 반대쪽 우회로 돌린다.
    /// 집으로 보내지 않는다 — 포위를 유지한 채 흩어진다.
    /// </summary>
    private void BreakQueues()
    {
        if (lead != null && roles.ContainsKey(lead) && lead.CurrentState == MonsterAI.State.Chase &&
            !lead.IsSeeingPlayer && Time.time - lead.SeenPlayerAt > 1f)
        {
            MonsterAI seer = null;
            float best = float.PositiveInfinity;
            foreach (var pair in roles)
            {
                var m = pair.Key;
                if (m == null || m == lead || !m.IsSeeingPlayer || m.CurrentState != MonsterAI.State.Chase) continue;
                float d = DistanceToPlayer(m);
                if (d < best) { best = d; seer = m; }
            }
            if (seer != null)
            {
                var old = lead;
                SetRole(old, Role.Assist);
                SetRole(seer, Role.Lead);
                lead = seer;
                Decide(seer, "lead_handover", "추격자 " + Short(old) + "가 놓침, 내가 보고 있음 (" + best.ToString("F0") + "m) → 추격자 교대");
            }
        }
        var chasers = new List<MonsterAI>();
        foreach (var pair in roles)
            if (pair.Key != null && pair.Key.CurrentState == MonsterAI.State.Chase && !pair.Key.IsTraversingLink) chasers.Add(pair.Key);
        // 협공 추격자가 추격자보다 앞(플레이어에 더 가까이, 같은 쪽)에서 보고 있으면 그쪽이 추격자다.
        // 그래야 뒤에 남은 쪽이 줄줄이로 잡혀 우회로 빠진다
        if (lead != null && chasers.Contains(lead))
        {
            foreach (var m in chasers)
            {
                if (m == lead || !m.IsSeeingPlayer || !QueuesBehind(lead, m)) continue;
                if (Time.time - leadChangedAt < 2f || DistanceToPlayer(m) + 3f >= DistanceToPlayer(lead)) continue;
                var old = lead;
                SetRole(old, Role.Assist);
                SetRole(m, Role.Lead);
                lead = m;
                Decide(m, "lead_front", Short(old) + "보다 앞에서 보고 있음 → 추격자 교대 (" + Short(old) + "는 뒤에 남음)", 1f);
                break;
            }
        }
        foreach (var m in chasers)
        {
            if (m == lead || RoleOf(m) != Role.Assist) { queueSince.Remove(m); continue; }
            // 갈림길에서 조여 들어가는 중(4초)에는 아직 못 봐도 우회로 되돌리지 않는다 — 두 규칙이 서로 뒤집지 않게
            if (!m.IsSeeingPlayer && Time.time - m.SeenPlayerAt > 1.5f &&
                !(closeInUntil.TryGetValue(m, out float closeIn) && Time.time < closeIn))
            {
                queueSince.Remove(m);
                SetRole(m, Role.Flank);
                Decide(m, "assist_blind", "협공 중 놓침 → 뒤를 쫓지 않고 우회로");
                AssignFlank(m, true);
                continue;
            }
            MonsterAI front = null;
            foreach (var other in chasers)
            {
                if (other == m) continue;
                if (QueuesBehind(m, other) && Vector3.Distance(m.transform.position, other.transform.position) <= QueueDistance)
                { front = other; break; }
            }
            // 플레이어에게 붙은(4m) 협공은 봐준다 — 단, 앞 추격자와 같은 쪽에 겹쳐 있으면 그것도 줄줄이다(09-25 F8 02:56)
            if (front == null || (DistanceToPlayer(m) <= CloseEncounter * .5f && !QueuesBehind(m, front))) { queueSince.Remove(m); continue; }
            if (!queueSince.TryGetValue(m, out float since)) { queueSince[m] = Time.time; continue; }
            if (Time.time - since < QueueSeconds) continue;
            queueSince.Remove(m);
            SetRole(m, Role.Flank);
            preferredSide[m] = -SideOf(front.transform.position);
            Decide(m, "queue_break", Short(front) + " 뒤에 " + QueueSeconds.ToString("F1") + "초 줄지어 따라감 → 반대쪽으로 우회");
            AssignFlank(m, true);
        }
    }

    /// <summary>
    /// 우회 교대(2026-09-26): 우회 중인 몬스터보다 2.5초 이상 빨리 우회 지점에 닿는 몬스터가 있으면 그 자리에서 맞바꾼다.
    /// 우회를 flankRotateSeconds 넘게 했는데 협공에 못 들어갔으면, 대신 들어올 몬스터가 있을 때만 맞바꾼다(빈자리를 만들지 않는다).
    /// 인원 3마리는 그대로 — 늘 가까이 있는 3마리가 되게 한다. 플레이어를 보고 있는 몬스터는 바꾸지 않는다. 1초에 한 번만 본다.
    /// </summary>
    private void RotateFlanks()
    {
        if (Time.time < nextRotateCheck) return;
        nextRotateCheck = Time.time + 1f;
        if (Time.time - sightTime > 3f) return;
        float limit = Settings != null ? Settings.flankRotateSeconds : 15f;
        foreach (var m in new List<MonsterAI>(roles.Keys))
        {
            if (RoleOf(m) != Role.Flank || m.IsSeeingPlayer || m.IsTraversingLink || m.CurrentState != MonsterAI.State.Flank) continue;
            if (!flankSince.TryGetValue(m, out float since) || Time.time - since < 4f) continue;
            var path = ComputePath(m, m.FlankGoal);
            float remaining = path != null ? Length(path) / FarSpeed : float.PositiveInfinity;
            bool tooLong = limit > 0f && Time.time - since >= limit;
            var replacement = BestFreeFlanker(out FlankChoice choice);
            if (replacement == null) continue;
            if (!(choice.eta + 2.5f < remaining) && !tooLong) continue;
            Decide(m, "flank_swap", (tooLong ? "우회 " + limit.ToString("F0") + "초 동안 협공 못 함" : "남은 거리 약 " + remaining.ToString("F1") + "초") +
                " → " + Short(replacement) + "(약 " + choice.eta.ToString("F1") + "초)와 교대, 빈 구역으로");
            SendHome(m, true);
            SetRole(replacement, Role.Flank);
            Decide(replacement, "flank_recruit", Short(m) + " 대신 우회 합류 (약 " + choice.eta.ToString("F1") + "초 거리)");
            AssignFlank(replacement, true);
            return;   // 한 번에 한 마리
        }
    }

    /// <summary>팀 밖 몬스터 중 우회 지점에 가장 알맞게 닿는 몬스터(구역을 비우지 않는 몬스터 먼저, 5초 넘게 걸리면 벌점).</summary>
    private MonsterAI BestFreeFlanker(out FlankChoice bestChoice)
    {
        bestChoice = default;
        bestChoice.score = float.PositiveInfinity;
        var candidates = BuildCandidates(Settings, knownPosition);
        if (candidates.Count == 0) return null;
        MonsterAI best = null;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || roles.ContainsKey(m) || !m.CanTakeOrders || m.IsGivingUp) continue;
            if (benchUntil.TryGetValue(m, out float until) && Time.time < until) continue;
            if (Vector3.Distance(m.transform.position, knownPosition) > RecruitRange) continue;
            if (!TryChooseFlank(m, candidates, 1, out var choice)) continue;
            // 구역을 비우지 않는 몬스터를 먼저: 전역 몬스터(구역 없음), 플레이어가 자기 구역 안에 있는 몬스터.
            // 먼 구역 몬스터를 끌어오면 그 구역이 비어 맵이 한쪽으로 몰린다
            float drain = m.role == MonsterAI.MonsterRole.Global_Stalker || m.useGlobalNavMesh ? -1f
                : m.zoneCenter != null && Vector3.Distance(knownPosition, m.zoneCenter.position) <= m.zoneRadius + 15f ? 0f : 2.5f;
            choice.score += drain + Mathf.Max(0f, choice.eta - 5f) * .6f;
            if (choice.score < bestChoice.score) { best = m; bestChoice = choice; }
        }
        return best;
    }

    /// <summary>인원 초과: 추격자는 남기고, 플레이어에게서 가장 먼 몬스터를 빈 구역으로 보낸다.</summary>
    private void EnforceCap()
    {
        while (roles.Count > TeamSize)
        {
            // 보고 있는 몬스터는 돌려보내지 않는다(09-26 01:37 D가 보면서 복귀 → 2초 뒤 재합류 → A가 밀려나는 반복).
            // 모두 보고 있으면 잠시 넘쳐도 둔다
            var farthest = FarthestNonLead(true);
            if (farthest == null) break;
            // 모두 BrawlDistance(12m) 안에서 붙어 싸우는 중이면 잠시 넘쳐도 둔다 — 코앞 몬스터가 돌아서는 모습이 더 나쁘다.
            // 한 마리라도 떨어지면 그 몬스터부터 돌려보낸다
            if (DistanceToPlayer(farthest) <= BrawlDistance) break;
            Decide(farthest, "cap_release", "사냥 인원 " + TeamSize + "마리 초과, 가장 멂 → 빈 구역으로");
            SendHome(farthest, true);
        }
    }

    /// <summary>
    /// 빈 우회 자리를 채운다. 가장 가까운 몬스터가 아니라 "플레이어 옆·앞으로 가는 길이 있는" 몬스터 중 가장 빨리 닿는 몬스터.
    /// 뒤에서만 올 수 있는 몬스터를 부르면 줄줄이가 되므로 부르지 않는다(최대 거리 flankRecruitRange).
    /// </summary>
    private void FillFlanks()
    {
        if (roles.Count >= TeamSize) return;
        // 3초 넘게 아무도 못 봤으면 새로 부르지 않는다 — 오래된 위치로 몰려가 봐야 헛걸음이다
        if (Time.time - sightTime > 3f) return;
        while (roles.Count < TeamSize)
        {
            var best = BestFreeFlanker(out FlankChoice bestChoice);
            if (best == null) break;
            SetRole(best, Role.Flank);
            Decide(best, "flank_recruit", "우회 자리 비어 있음 → 합류 (약 " + bestChoice.eta.ToString("F1") + "초 거리)");
            AssignFlank(best, true);
        }
    }

    private void UpdateFlankGoals()
    {
        foreach (var m in new List<MonsterAI>(roles.Keys))
        {
            if (RoleOf(m) != Role.Flank || m.IsTraversingLink || !m.CanTakeOrders) continue;
            if (m.CurrentState == MonsterAI.State.Investigate) continue;   // 우회 끝 둘러보는 중 — 끝나면 감독에게 온다
            if (nextFlankPlan.TryGetValue(m, out float at) && Time.time < at) continue;
            AssignFlank(m, false);
        }
    }

    private void ApplySpeeds()
    {
        float near = Settings != null ? Settings.huntNearDistance : 15f;
        float far = Settings != null ? Settings.huntFarDistance : 40f;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null) continue;
            if (!roles.ContainsKey(m)) { m.SetHuntSpeed(-1f); continue; }
            float distance = Vector3.Distance(m.transform.position, knownPosition);
            m.SetHuntSpeed(Mathf.Lerp(TrackingSpeedCap, FarSpeed, Mathf.InverseLerp(near, far, distance)));
        }
    }

    // ────────────────────────────────────────────────
    //  우회 목표
    // ────────────────────────────────────────────────

    /// <summary>플레이어가 곧 있을 곳: 최근에 본 이동 방향으로 flankLeadTime초 앞. 방향을 모르면 마지막으로 안 위치.</summary>
    private Vector3 PredictedPosition()
    {
        Vector3 p = knownPosition;
        if (Time.time - headingTime > 2f || heading.sqrMagnitude < .01f) return p;
        Vector3 ahead = p + heading * PlayerRunSpeed * LeadTime;
        if (NavMesh.Raycast(p, ahead, out var hit, NavMesh.AllAreas)) ahead = hit.position;
        return ahead;
    }

    /// <summary>플레이어 기준 어느 쪽(+1 오른쪽 / -1 왼쪽)인가. 기준 방향은 이동 방향, 모르면 추격자 반대쪽.</summary>
    private int SideOf(Vector3 point)
    {
        Vector3 reference = ReferenceDirection();
        Vector3 to = Vector3.ProjectOnPlane(point - knownPosition, Vector3.up);
        float cross = reference.x * to.z - reference.z * to.x;
        return cross >= 0f ? -1 : 1;
    }

    private Vector3 ReferenceDirection()
    {
        if (Time.time - headingTime <= 2f && heading.sqrMagnitude > .01f) return heading;
        if (lead != null)
        {
            Vector3 away = Vector3.ProjectOnPlane(knownPosition - lead.transform.position, Vector3.up);
            if (away.sqrMagnitude > .25f) return away.normalized;
        }
        return Vector3.forward;
    }

    private struct Candidate
    {
        public Vector3 point;
        public Vector3[] route;      // 플레이어(마지막으로 안 위치)에서 이 지점까지의 길
        public float playerEta;      // 플레이어가 달려서(11) 닿는 시간
    }

    private List<Candidate> candidateCache;
    private float candidateCacheTime = float.NegativeInfinity;

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
    /// 우회 목표 후보(2026-09-26 갈림길 우회): 플레이어가 곧 달려갈 곳.
    /// 마지막으로 안 위치에서 8~32m의 걸을 수 있는 지점 중, 거기까지의 길이 앞쪽(이동 방향)으로 출발하고 너무 돌아가지 않는 곳.
    /// 방향 30° 칸마다 가까운 것(8~18m)·먼 것(18~32m) 하나씩 — 플레이어의 길(route)과 도착 시각을 함께 들고 있다.
    /// 우회 몬스터는 이 길과 겹치지 않는 다른 가지로 들어와야 하므로(TryChooseFlank), 목표는 자연히 갈림길이 된다.
    /// 0.3초 안에는 다시 계산하지 않는다.
    /// </summary>
    private List<Candidate> BuildCandidates(MonsterAI sample, Vector3 predictedUnused)
    {
        if (candidateCache != null && Time.time - candidateCacheTime < .3f) return candidateCache;
        var list = new List<Candidate>();
        candidateCache = list;
        candidateCacheTime = Time.time;
        if (anchors.Count == 0) BuildAnchors();
        if (!NavMesh.SamplePosition(knownPosition, out var origin, 4f, NavMesh.AllAreas)) return list;
        bool headingKnown = Time.time - headingTime <= 2f && heading.sqrMagnitude > .01f;
        Vector3 reference = ReferenceDirection();
        var best = new Vector3?[24];
        var bestError = new float[24];
        foreach (var a in anchors)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a - origin.position, Vector3.up);
            float d = flat.magnitude;
            if (d < 8f || d > 32f || Mathf.Abs(a.y - origin.position.y) > 4f) continue;
            float angle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            int bin = Mathf.Clamp(Mathf.FloorToInt((angle + 180f) / 30f), 0, 11) * 2 + (d < 18f ? 0 : 1);
            float error = Mathf.Abs(d - (d < 18f ? 13f : 24f));
            if (best[bin] == null || error < bestError[bin]) { best[bin] = a; bestError[bin] = error; }
        }
        for (int i = 0; i < best.Length; i++)
        {
            if (best[i] == null) continue;
            Vector3 a = best[i].Value;
            var route = ComputePathFrom(sample, origin.position, a);
            if (route == null) continue;
            route = (Vector3[])route.Clone();
            float length = Length(route);
            if (length > 36f || length > Vector3.Distance(origin.position, a) * 1.6f + 6f) continue;
            // 플레이어가 가는 쪽으로 출발하는 길만(뒤로 돌아가는 곳은 플레이어가 갈 곳이 아니다). 방향을 모르면 추격자 반대쪽 기준
            Vector3 start = RouteStartDirection(route);
            if (Vector3.Dot(start, reference) < (headingKnown ? .2f : -.3f)) continue;
            list.Add(new Candidate { point = a, route = route, playerEta = length / PlayerRunSpeed });
        }
        return list;
    }

    /// <summary>
    /// 옆 우회 후보(갈림길 후보가 없을 때만): 마지막으로 안 위치 둘레 8~30m, 거기서 길로 이어진 곳, 방향 30° 칸마다 하나.
    /// 플레이어 길·도착 시각은 없다(route = null, playerEta = -1) — 추격자와 벌어진 옆에서 다가가는 것만 본다.
    /// </summary>
    private List<Candidate> BuildRadialCandidates(MonsterAI sample)
    {
        var list = new List<Candidate>();
        if (anchors.Count == 0) BuildAnchors();
        if (!NavMesh.SamplePosition(knownPosition, out var origin, 4f, NavMesh.AllAreas)) return list;
        var best = new Vector3?[12];
        var bestError = new float[12];
        foreach (var a in anchors)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a - origin.position, Vector3.up);
            float d = flat.magnitude;
            if (d < 8f || d > 30f || Mathf.Abs(a.y - origin.position.y) > 4f) continue;
            int bin = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + 180f) / 30f), 0, 11);
            float error = Mathf.Abs(d - FlankRadius);
            if (best[bin] == null || error < bestError[bin]) { best[bin] = a; bestError[bin] = error; }
        }
        foreach (var b in best)
        {
            if (b == null) continue;
            var fromPlayer = ComputePathFrom(sample, origin.position, b.Value);
            if (fromPlayer == null || Length(fromPlayer) > Vector3.Distance(origin.position, b.Value) * 1.8f + 6f) continue;
            list.Add(new Candidate { point = b.Value, route = null, playerEta = -1f });
        }
        return list;
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

    /// <summary>우회 길이 플레이어의 길과 겹치는가(목표 앞 5m는 만나는 자리라 뺀다). 겹치면 뒤따라가거나 마주 달려가는 것일 뿐이다.</summary>
    private static bool OverlapsRoute(Vector3[] path, Vector3[] route, float radius)
    {
        if (route == null || route.Length < 2) return false;
        float total = Length(path), walked = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float segment = Vector3.Distance(path[i - 1], path[i]);
            int samples = Mathf.Max(1, Mathf.CeilToInt(segment / 2f));
            for (int k = 0; k < samples; k++)
            {
                float along = walked + segment * (k + .5f) / samples;
                if (total - along < 5f) return false;
                Vector3 point = Vector3.Lerp(path[i - 1], path[i], (k + .5f) / samples);
                for (int j = 1; j < route.Length; j++)
                    if (DistanceToSegment(point, route[j - 1], route[j]) < radius) return true;
            }
            walked += segment;
        }
        return false;
    }

    private struct FlankChoice
    {
        public Vector3[] path;
        public float eta, score;
    }

    /// <summary>
    /// m이 갈 수 있는 가장 좋은 우회 목표(갈림길 우회: 플레이어 길과 겹치지 않는 가지로, 플레이어와 같은 때 도착). 조건(relax가 클수록 느슨):
    /// ① 추격자와 플레이어 기준 70°(relax 2: 45°) 이상 벌어진 방향, ② 다른 우회와 55° 이상(relax 1부터 검사 안 함),
    /// ③ 가는 길이 플레이어 곁 5m(relax 2: 3m)를 지나지 않음 — 지나면 뒤따라가는 것일 뿐이다.
    /// 가장 빨리 닿는 곳. 지금 목표와 크게 다르면 1.5초 벌점(와리가리 방지), 선호 쪽이 아니면 3초 벌점.
    /// </summary>
    private bool TryChooseFlank(MonsterAI m, List<Candidate> candidates, int relax, out FlankChoice choice)
    {
        choice = default;
        choice.score = float.PositiveInfinity;
        Vector3 playerAt = knownPosition;
        Vector3 leadDir = lead != null && lead != m ? Vector3.ProjectOnPlane(lead.transform.position - playerAt, Vector3.up) : Vector3.zero;
        var otherGoals = new List<Vector3>();
        foreach (var pair in roles)
            if (pair.Key != m && pair.Value == Role.Flank && pair.Key != null && pair.Key.CurrentState == MonsterAI.State.Flank)
                otherGoals.Add(pair.Key.FlankGoal);
        int side = preferredSide.TryGetValue(m, out int s) ? s : 0;
        bool hasCurrent = m.CurrentState == MonsterAI.State.Flank;
        Vector3 current = m.FlankGoal;
        float minLeadAngle = relax >= 2 ? 45f : 70f;
        float throughPlayer = relax >= 2 ? 3f : 5f;
        foreach (var c in candidates)
        {
            Vector3 bearing = Vector3.ProjectOnPlane(c.point - playerAt, Vector3.up);
            if (leadDir.sqrMagnitude > 9f && bearing.sqrMagnitude > .01f && Vector3.Angle(bearing, leadDir) < minLeadAngle) continue;
            if (relax == 0)
            {
                bool crowded = false;
                foreach (var g in otherGoals)
                {
                    Vector3 other = Vector3.ProjectOnPlane(g - playerAt, Vector3.up);
                    if (other.sqrMagnitude > .01f && Vector3.Angle(bearing, other) < 55f) { crowded = true; break; }
                }
                if (crowded) continue;
            }
            // 이미 서 있는 곳 근처는 목표가 아니다 — 도착하자마자 '못 찾음'이 되어 와리가리한다
            if (Vector3.Distance(c.point, m.transform.position) < 8f) continue;
            var path = ComputePath(m, c.point);
            if (path == null) continue;
            float length = Length(path);
            if (length > RecruitRange * 1.3f) continue;
            if (PassesNear(path, playerAt, throughPlayer)) continue;
            // 플레이어의 길과 다른 가지로 들어와야 한다(갈림길 우회)
            if (OverlapsRoute(path, c.route, relax >= 2 ? 2.5f : 3.5f)) continue;
            float eta = length / FarSpeed;
            // 플레이어와 같은 때 도착하는 곳: 너무 늦으면(플레이어가 이미 지나감) 빼고, 이르거나 늦은 만큼 벌점
            float score;
            if (c.playerEta >= 0f)
            {
                float late = eta - c.playerEta;
                if (late > (relax >= 2 ? 4f : 2.5f)) continue;
                score = Mathf.Abs(late) * 1.5f + eta * .25f;
            }
            else score = eta;
            if (side != 0 && SideOf(c.point) != side) score += 3f;
            if (hasCurrent && Vector3.Distance(c.point, current) > 6f) score += 1.5f;
            if (score < choice.score) { choice.score = score; choice.path = (Vector3[])path.Clone(); choice.eta = eta; }
        }
        return choice.path != null;
    }

    /// <summary>
    /// 우회 목표를 정해 명령한다. 조건이 맞는 곳이 없으면 조건을 한 단계씩 느슨하게 한다.
    /// 끝내 없으면: 이미 가는 목표가 있으면 계속, 없으면 팀에서 뺀다(뒤따라가면 줄줄이가 되므로).
    /// force면 지금 목표를 버리고 새로 고른다(도착·막힘·줄줄이).
    /// </summary>
    private void AssignFlank(MonsterAI m, bool force)
    {
        nextFlankPlan[m] = Time.time + FlankReplanInterval;
        if (!m.CanTakeOrders) return;
        bool hasCurrent = !force && m.CurrentState == MonsterAI.State.Flank;
        if (hasCurrent)
        {
            // 목표가 한 번 어긋났다고 바로 바꾸지 않는다 — 2초 넘게 계속 어긋나야 바꾼다(좌우 와리가리 방지)
            if (FlankStillValid(m)) { flankInvalidSince.Remove(m); return; }
            if (!flankInvalidSince.TryGetValue(m, out float since)) { flankInvalidSince[m] = Time.time; return; }
            if (Time.time - since < 2f) return;
        }
        flankInvalidSince.Remove(m);
        // 새 목표는 되도록 지금 가던 쪽에서 — 쪽을 바꾸면 몸을 돌려 반대로 뛰게 된다
        if (hasCurrent && !preferredSide.ContainsKey(m)) preferredSide[m] = SideOf(m.FlankGoal);
        var candidates = BuildCandidates(m, PredictedPosition());
        FlankChoice choice = default;
        bool found = false;
        for (int relax = 0; relax < 3 && !found; relax++) found = TryChooseFlank(m, candidates, relax, out choice);
        if (!found)
        {
            // 갈림길(플레이어 길과 겹치지 않는 가지)이 없다 — 뒤에서 줄지은 몬스터에게 흔하다(09-26 판 17번).
            // 갈림길 이전 방식(플레이어 둘레, 추격자와 70°+ 벌어진 옆 지점)으로 한 번 더 찾는다
            var radial = BuildRadialCandidates(m);
            for (int relax = 1; relax < 3 && !found; relax++) found = TryChooseFlank(m, radial, relax, out choice);
        }
        if (!found)
        {
            if (hasCurrent) return;
            if (m.CurrentState == MonsterAI.State.Chase && m.IsSeeingPlayer)
            {
                // 쫓던 몬스터(줄줄이·놓침으로 우회를 받은)가 갈 우회 길이 없다 — 집으로 보내면 '보면서 돌아서기'가 된다.
                // 협공 추격을 그대로 이어간다(줄줄이 검사는 queueSeconds 뒤 다시 본다)
                SetRole(m, Role.Assist);
                Decide(m, "flank_none_keep", "옆·앞으로 가는 우회 길 없음 → 보고 있으니 협공 추격 유지", 2f);
                return;
            }
            Decide(m, "flank_none", "플레이어 옆·앞으로 가는 길 없음(뒤따라가게 됨) → 팀에서 빠짐", 2f);
            SendHome(m, false);
            return;
        }
        Vector3 goal = choice.path[choice.path.Length - 1];
        if (hasCurrent && Vector3.Distance(goal, m.FlankGoal) < 4f) return;   // 같은 목표 — 명령을 다시 보내지 않는다
        m.CommandFlank(choice.path);
        routes[m] = new RouteInfo { corners = choice.path, goal = goal, eta = choice.eta, colorIndex = FlankColor(m) };
        string sideText = SideOf(goal) > 0 ? "오른쪽" : "왼쪽";
        Decide(m, force ? "flank_assign" : "flank_update",
            (force ? "우회 지점 배정" : "플레이어가 움직여 우회 지점 갱신") + " · " + sideText + " · 약 " + choice.eta.ToString("F1") + "초",
            force ? 0f : 1.5f);
    }

    /// <summary>
    /// 지금 우회 목표가 아직 쓸 만한가: 예상 위치에서 4~32m, 추격자와 60° 이상 벌어짐, 남은 길이 플레이어 곁을 지나지 않음.
    /// 쓸 만하면 1초마다 새로 고르지 않는다 — 좌우로 목표가 뒤집히는 와리가리를 막는다.
    /// </summary>
    private bool FlankStillValid(MonsterAI m)
    {
        Vector3 goal = m.FlankGoal;
        Vector3 predicted = PredictedPosition();
        float fromPredicted = Vector3.Distance(goal, predicted);
        if (fromPredicted < 4f || fromPredicted > 40f) return false;
        Vector3 playerAt = knownPosition;
        if (lead != null && lead != m)
        {
            Vector3 leadDir = Vector3.ProjectOnPlane(lead.transform.position - playerAt, Vector3.up);
            Vector3 bearing = Vector3.ProjectOnPlane(goal - playerAt, Vector3.up);
            if (leadDir.sqrMagnitude > 9f && bearing.sqrMagnitude > .01f && Vector3.Angle(bearing, leadDir) < 45f) return false;
        }
        // 플레이어가 이미 지나쳤다(목표가 이동 방향 뒤쪽) — 갈림길을 놓쳤으니 다음 갈림길로
        Vector3 ahead = Vector3.ProjectOnPlane(goal - playerAt, Vector3.up);
        if (Time.time - headingTime <= 2f && heading.sqrMagnitude > .01f && ahead.sqrMagnitude > 1f &&
            Vector3.Dot(ahead.normalized, heading) < -.3f) return false;
        var path = ComputePath(m, goal);
        return path != null && !PassesNear(path, playerAt, 4f);
    }

    private int FlankColor(MonsterAI m)
    {
        int index = 1;
        foreach (var pair in roles)
        {
            if (pair.Key == m) break;
            if (pair.Value == Role.Flank) index++;
        }
        return Mathf.Clamp(index, 1, 2);
    }

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

    // ────────────────────────────────────────────────
    //  구역 · 복귀
    // ────────────────────────────────────────────────

    private void BuildZones()
    {
        zones.Clear();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || m.role != MonsterAI.MonsterRole.Zone_Defender || m.useGlobalNavMesh || m.OriginalZone == null) continue;
            zones.Add(new Zone { center = m.OriginalZone, radius = m.OriginalZoneRadius, owner = m });
            PlaytestRecorder.Record("zone", m.OriginalZone.name.Replace("_Home", ""), m.OriginalZone.position,
                "radius=" + m.OriginalZoneRadius.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ";owner=" + m.name);
        }
    }

    /// <summary>
    /// 복귀: 가장 가까운 빈 구역으로 보낸다. 빈 구역 = 주인이 없거나, 주인이 사냥 중이라 비어 있는 구역.
    /// 전역 몬스터는 구역이 없으니 곧바로 맵 순찰로 돌아간다. bench면 잠시 멀리 보이는 플레이어를 무시한다.
    /// </summary>
    private void SendHome(MonsterAI m, bool bench)
    {
        if (m == null) return;
        RemoveRole(m);
        if (m == lead) lead = null;
        // 팀에서 막 빠진 몬스터가 바로 다시 끼면 인원 초과 → 다른 몬스터가 빠지는 연쇄가 생긴다(09-25 테스트 04:15).
        // 인원 초과로 빠지면 benchSeconds, 그 밖에는 2초 동안 합류하지 않는다(8m 안은 예외)
        benchUntil[m] = Time.time + (bench ? BenchSeconds : 2f);
        m.SetHuntSpeed(-1f);
        if (m.role == MonsterAI.MonsterRole.Global_Stalker || m.useGlobalNavMesh || zones.Count == 0)
        {
            m.CommandPatrol();
            return;
        }
        Zone best = BestEmptyZone(m, out string why);
        if (best == null)
        {
            best = zones.Find(z => z.owner == m);
            if (best == null) { m.CommandPatrol(); return; }
            why = "빈 구역 없음";
        }
        AssignZone(m, best);
        bool inside = Vector3.Distance(m.transform.position, best.center.position) <= best.radius;
        string zoneName = best.center.name.Replace("_Home", "");
        Decide(m, "go_home", (best.center == m.OriginalZone ? "자기 구역" : "빈 구역 " + zoneName) + "으로 복귀 · " + why + (inside ? " · 이미 안" : ""));
    }

    /// <summary>구역 주인을 m으로 바꾸고 그 구역으로 보낸다.</summary>
    private void AssignZone(MonsterAI m, Zone zone)
    {
        foreach (var z in zones) if (z.owner == m && z != zone) z.owner = null;
        zone.owner = m;
        m.CommandReturn(zone.center, zone.radius);
    }

    /// <summary>그 구역을 순찰 중인(사냥에 안 낀) 주인이 있는가.</summary>
    private bool IsCovered(Zone z, MonsterAI except)
    {
        return z.owner != null && z.owner != except && !roles.ContainsKey(z.owner) && !z.owner.IsInStun &&
            (z.owner.CurrentState == MonsterAI.State.Patrol || z.owner.CurrentState == MonsterAI.State.Return || z.owner.CurrentState == MonsterAI.State.Idle);
    }

    /// <summary>
    /// 5마리가 여러 마리처럼 보이게 — 돌아갈 빈 구역은 "가까운 곳"이 아니라 "다른 순찰 몬스터에게서 먼 곳".
    /// 점수 = 가장 가까운 다른 순찰 몬스터(와 그 구역)까지 거리 − 가는 거리 × 0.3. 플레이어가 있는 구역은 사냥 팀이 맡으니 뒤로 미룬다.
    /// </summary>
    private Zone BestEmptyZone(MonsterAI m, out string why)
    {
        why = "";
        Zone best = null;
        float bestScore = float.NegativeInfinity, bestSpread = 0f;
        Vector3 p = player != null ? player.position : knownPosition;
        foreach (var z in zones)
        {
            if (IsCovered(z, m)) continue;
            float spread = SpreadFrom(z.center.position, m);
            float travel = Vector3.Distance(m.transform.position, z.center.position);
            float score = Mathf.Min(spread, 150f) - travel * .3f;
            if (hunting && Vector3.Distance(p, z.center.position) < z.radius) score -= 60f;
            if (score > bestScore) { bestScore = score; best = z; bestSpread = spread; }
        }
        if (best != null) why = "다른 순찰 몬스터와 " + bestSpread.ToString("F0") + "m 떨어진 곳";
        return best;
    }

    /// <summary>point에서 가장 가까운 '다른 순찰 몬스터'(위치와 맡은 구역 중심)까지 거리.</summary>
    private float SpreadFrom(Vector3 point, MonsterAI except)
    {
        float nearest = 999f;
        foreach (var o in MonsterAI.activeMonsters)
        {
            if (o == null || o == except || roles.ContainsKey(o) || o.IsInStun) continue;
            var s = o.CurrentState;
            if (s != MonsterAI.State.Patrol && s != MonsterAI.State.Return && s != MonsterAI.State.Idle) continue;
            nearest = Mathf.Min(nearest, Vector3.Distance(point, o.transform.position));
            if (o.zoneCenter != null && !o.useGlobalNavMesh) nearest = Mathf.Min(nearest, Vector3.Distance(point, o.zoneCenter.position));
        }
        return nearest;
    }

    /// <summary>
    /// 구역 퍼뜨리기(4초마다, 사냥 중에도): 순찰 몬스터 둘이 가까이(40m) 붙어 있고 비어 있는 구역이 있으면,
    /// 그 구역에 더 가까운 쪽을 빠르게(복귀 속도) 옮긴다. 플레이어를 보고 있는 몬스터는 옮기지 않는다.
    /// </summary>
    private void SpreadPatrols()
    {
        if (zones.Count == 0) return;
        var free = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || roles.ContainsKey(m) || m.IsInStun || m.IsSeeingPlayer || m.useGlobalNavMesh ||
                m.role != MonsterAI.MonsterRole.Zone_Defender || !m.CanTakeOrders) continue;
            if (m.CurrentState != MonsterAI.State.Patrol) continue;
            free.Add(m);
        }
        Vector3 p = player != null ? player.position : knownPosition;
        foreach (var z in zones)
        {
            if (IsCovered(z, null)) continue;
            if (hunting && Vector3.Distance(p, z.center.position) < z.radius) continue;
            MonsterAI mover = null;
            float moverDistance = float.PositiveInfinity;
            foreach (var a in free)
            {
                bool crowded = false;
                foreach (var b in MonsterAI.activeMonsters)
                {
                    // 붙어 있는 상대: 다른 순찰 몬스터나 전역 몬스터(사냥에 안 낀). 옮기는 쪽은 구역 몬스터만
                    if (b == null || a == b || roles.ContainsKey(b) || b.IsInStun) continue;
                    if (!free.Contains(b) && !(b.useGlobalNavMesh && b.CurrentState == MonsterAI.State.Patrol)) continue;
                    if (Vector3.Distance(a.transform.position, b.transform.position) < 40f) { crowded = true; break; }
                }
                if (!crowded) continue;
                float d = Vector3.Distance(a.transform.position, z.center.position);
                if (d < moverDistance) { moverDistance = d; mover = a; }
            }
            if (mover == null) continue;
            free.Remove(mover);
            AssignZone(mover, z);
            Decide(mover, "zone_spread", "다른 순찰 몬스터와 붙어 있음, " + z.center.name.Replace("_Home", "") + " 비어 있음 → 빠르게 이동 (" + moverDistance.ToString("F0") + "m)");
            return;   // 한 번에 한 마리 — 다음 검사에서 다시 본다
        }
    }

    // ────────────────────────────────────────────────
    //  도우미
    // ────────────────────────────────────────────────

    private void SetRole(MonsterAI m, Role role)
    {
        if (m == null) return;
        if (role == Role.Lead && RoleOf(m) != Role.Lead) leadChangedAt = Time.time;
        if (role == Role.Flank && RoleOf(m) != Role.Flank) flankSince[m] = Time.time;
        if (role != Role.Flank) flankSince.Remove(m);
        roles[m] = role;
        if (!team.Contains(m)) team.Add(m);
        benchUntil.Remove(m);
        if (role != Role.Flank) routes.Remove(m);
    }

    private void RemoveRole(MonsterAI m)
    {
        roles.Remove(m);
        team.Remove(m);
        routes.Remove(m);
        queueSince.Remove(m);
        nextFlankPlan.Remove(m);
        preferredSide.Remove(m);
        flankInvalidSince.Remove(m);
        flankSince.Remove(m);
    }

    private MonsterAI FarthestNonLead(bool skipSeeing = false)
    {
        MonsterAI farthest = null;
        float d = -1f;
        foreach (var pair in roles)
        {
            if (pair.Key == null || pair.Key == lead) continue;
            if (skipSeeing && pair.Key.IsSeeingPlayer) continue;
            float distance = DistanceToPlayer(pair.Key);
            if (distance > d) { d = distance; farthest = pair.Key; }
        }
        return farthest;
    }

    /// <summary>팀 밖에서 부를 수 있는 몬스터 중 target까지 길이 가장 짧은 몬스터(최대 flankRecruitRange).</summary>
    private MonsterAI NearestFree(Vector3 target, out float bestLength)
    {
        MonsterAI best = null;
        bestLength = float.PositiveInfinity;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || roles.ContainsKey(m) || !m.CanTakeOrders || m.IsGivingUp) continue;
            if (benchUntil.TryGetValue(m, out float until) && Time.time < until) continue;
            if (Vector3.Distance(m.transform.position, target) > RecruitRange) continue;
            float length = PathLength(m, target);
            if (length <= RecruitRange && length < bestLength) { bestLength = length; best = m; }
        }
        return best;
    }

    /// <summary>a가 b(추격자) 뒤 같은 쪽에 있나: 플레이어 기준 방향이 45° 안이고 더 멀다.</summary>
    private bool QueuesBehind(MonsterAI a, MonsterAI b)
    {
        Vector3 p = player != null ? player.position : knownPosition;
        Vector3 toA = Vector3.ProjectOnPlane(a.transform.position - p, Vector3.up);
        Vector3 toB = Vector3.ProjectOnPlane(b.transform.position - p, Vector3.up);
        if (toB.magnitude < 2f || toA.magnitude < .01f) return false;
        return Vector3.Dot(toA.normalized, toB.normalized) > .7f && toA.magnitude >= toB.magnitude - 1f;
    }

    /// <summary>
    /// 협공 추격자의 자리: 플레이어에게서 assistSpacing만큼 떨어진 둘레 한 점.
    /// 추격자 방향과 70° 이상, 다른 협공과 50° 이상 벌어지게 돌린다 — 한 덩어리가 아니라 둘러싼다.
    /// 멀리(assistSpacing의 2배 밖) 있으면 false — 그냥 곧장 다가간다.
    /// </summary>
    public bool TryGetSpacingSlot(MonsterAI m, out Vector3 slot)
    {
        slot = Vector3.zero;
        if (player == null || RoleOf(m) != Role.Assist) return false;
        float spacing = Settings != null ? Mathf.Max(1f, Settings.assistSpacing) : 3.2f;
        Vector3 p = player.position;
        Vector3 dir = Vector3.ProjectOnPlane(m.transform.position - p, Vector3.up);
        if (dir.magnitude > spacing * 2f) return false;
        dir = dir.sqrMagnitude > .01f ? dir.normalized : Vector3.ProjectOnPlane(m.transform.right, Vector3.up).normalized;
        var taken = new List<Vector3>();
        if (lead != null && lead != m) taken.Add(Vector3.ProjectOnPlane(lead.transform.position - p, Vector3.up).normalized);
        foreach (var pair in roles)
            if (pair.Key != null && pair.Key != m && pair.Key != lead && pair.Value == Role.Assist)
                taken.Add(Vector3.ProjectOnPlane(pair.Key.transform.position - p, Vector3.up).normalized);
        for (int step = 0; step < 4; step++)
        {
            bool clash = false;
            foreach (var t in taken)
            {
                if (t.sqrMagnitude < .01f) continue;
                float need = t == taken[0] && lead != null && lead != m ? 70f : 50f;
                float angle = Vector3.SignedAngle(t, dir, Vector3.up);
                if (Mathf.Abs(angle) < need)
                {
                    dir = Quaternion.Euler(0f, (angle >= 0f ? 1f : -1f) * (need - Mathf.Abs(angle) + 5f), 0f) * dir;
                    clash = true;
                }
            }
            if (!clash) break;
        }
        Vector3 wanted = p + dir * spacing;
        if (!NavMesh.SamplePosition(wanted, out var hit, 1.5f, NavMesh.AllAreas)) return false;
        slot = hit.position;
        return true;
    }

    private float DistanceToPlayer(MonsterAI m)
    {
        Vector3 p = player != null ? player.position : knownPosition;
        return Vector3.Distance(m.transform.position, p);
    }

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

    private Vector3[] ComputePathFrom(MonsterAI m, Vector3 from, Vector3 to)
    {
        var filter = m != null ? m.NavigationFilter : new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(from, out var a, 3f, filter) ||
            !NavMesh.SamplePosition(to, out var b, 3f, filter) ||
            !NavMesh.CalculatePath(a.position, b.position, filter, pathBuffer) ||
            pathBuffer.status != NavMeshPathStatus.PathComplete) return null;
        return pathBuffer.corners;
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

    private void RebuildDebug()
    {
        int flank = 0, assist = 0;
        foreach (var pair in roles)
        {
            if (pair.Value == Role.Flank) flank++;
            else if (pair.Value == Role.Assist) assist++;
        }
        summary = "추격 " + (lead != null ? Short(lead) : "없음") + " · 협공 " + assist + " · 우회 " + flank;
    }

    private void OnDrawGizmos()
    {
        if (!hunting) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(knownPosition, 1.5f);
        Gizmos.color = Color.cyan;
        foreach (var r in routes.Values)
            if (r.corners != null)
                for (int i = 1; i < r.corners.Length; i++) Gizmos.DrawLine(r.corners[i - 1], r.corners[i]);
    }
}
