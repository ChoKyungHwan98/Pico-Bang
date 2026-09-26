using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감독(뇌 2). "쫓지 않는 몬스터를 플레이어가 다음에 지날 길의 어디에 둘까"만 정한다.
///
/// 끝나는 추격이 끝없이 이어지게(2026-09-26 리서치 반영):
/// - 추격은 몬스터 각자의 것. 몸이 눈으로 보면 쫓고, 시야가 끊기면 마지막으로 본 자리까지만 가 보고 끝낸다.
///   감독은 누구에게도 "플레이어가 저기 있다"고 알려 주지 않는다 — 추격을 시작·연장하는 명령이 없다.
/// - 길목: 감독은 실제 플레이어 위치·이동 방향으로 2~4초 앞 길을 보고, 그 앞·옆의 가려진 자리에 쫓지 않는 몬스터를 보낸다.
///   도착한 몬스터는 거기서 기다린다. 보면 그때 몸이 쫓는다. 급회전하면 예측 신뢰가 떨어져 길목이 헛다리가 된다.
/// - 길은 늘 하나 연다: 길목 몬스터 수 ≤ 길목 방향 수 − 1.
/// - 감전된 방향은 openingTime초 동안 새로 막지 않는다(쏴서 연 길).
/// - 돌려 쓰기: 방금 쫓았거나 감전된 몬스터는 reuseDelay초 동안 길목에 쓰지 않는다. 안 보일 때 재배치는 오래 안 보인 몬스터부터.
/// - 총소리는 쫓지 않는 몬스터 최대 2마리를 "소리 난 곳 확인"으로 부를 뿐, 추격·위치 정보가 아니다.
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

    public enum Role { None, Chase, Stage, Check }

    /// <summary>지도 표시용 경로 정보.</summary>
    public class RouteInfo
    {
        public Vector3[] corners;
        public Vector3 goal;
        public bool directChaser;
        public int colorIndex;
        public float eta;
    }

    /// <summary>길목 하나: 플레이어가 곧 지날 길의 가려진 자리.</summary>
    public struct StageInfo
    {
        public Vector3 point;
        public Vector3 direction;   // 플레이어에게서 그 길이 출발하는 방향
        public int bin;
        public float score;
        public float playerEta;
        public bool assigned;
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

    [Header("추격 (몸이 읽는다)")]
    [Tooltip("시야가 끊기면 마지막으로 본 자리까지 가 보는 최대 시간(초). 소리로는 갱신하지 않는다")]
    public float lostChaseTime = 2f;
    [Tooltip("보고 쫓을 때 가까운 몬스터 속도(플레이어 달리기 11보다 느리게)")]
    public float nearSpeed = 10f;
    [Tooltip("보고 쫓을 때 먼 몬스터 속도")]
    public float farSpeed = 14f;
    public float nearDistance = 15f;
    public float farDistance = 40f;

    [Header("길목 (앞·옆에 미리 두기)")]
    [Tooltip("동시에 길목에서 기다리는 최대 수. 길목 방향 수 − 1을 넘지 않는다(길 하나는 늘 연다)")]
    public int maxStagers = 2;
    [Tooltip("길목 = 플레이어에게서 길로 이 거리(m) 사이, 플레이어 눈에 가려진 곳")]
    public float stageMin = 14f;
    public float stageMax = 40f;
    [Tooltip("예측 시간(초). 예측 신뢰가 높을수록 이만큼 앞 길목을 고른다")]
    public float predictHorizon = 3f;
    [Tooltip("길목으로 가는 속도")]
    public float stageSpeed = 14f;
    [Tooltip("길목에 도착해 이 시간(초) 동안 아무도 안 오면 둘레로")]
    public float stageHoldTime = 6f;
    [Tooltip("감전된 방향을 새로 막지 않는 시간(초)")]
    public float openingTime = 4f;
    [Tooltip("추격이 끝났거나 감전된 몬스터를 길목에 다시 쓰기까지(초)")]
    public float reuseDelay = 4f;

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
    [Tooltip("화면·미러에서 이 시간(초) 넘게 안 보인 몬스터만 재배치")]
    public float unseenBeforeRelocate = 5f;

    [Header("소리")]
    [Tooltip("총소리에 확인하러 오는 최대 수")]
    public int noiseCallers = 2;

    private const float PlanInterval = .5f;
    private const float PlayerRunSpeed = 11f;
    private const float OpeningHalfAngle = 35f;
    private const float ChaserSideAngle = 50f;
    private const float StageDropGrace = 1.5f;

    private readonly Dictionary<MonsterAI, Role> roles = new Dictionary<MonsterAI, Role>();
    private readonly Dictionary<MonsterAI, StageInfo> stageGoals = new Dictionary<MonsterAI, StageInfo>();
    private readonly Dictionary<MonsterAI, float> stageArrivedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> stageMissingSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, RouteInfo> routes = new Dictionary<MonsterAI, RouteInfo>();
    private readonly Dictionary<MonsterAI, Vector3> ringSlots = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, float> relocatedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> chaseEndedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> stunnedAt = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> lastOnScreen = new Dictionary<MonsterAI, float>();
    private readonly List<MonsterAI> noiseCheckers = new List<MonsterAI>();
    private readonly List<MonsterAI> team = new List<MonsterAI>();
    private readonly List<StageInfo> stages = new List<StageInfo>();
    private readonly List<(float angle, float until)> openings = new List<(float, float)>();
    private readonly Dictionary<string, float> decisionThrottle = new Dictionary<string, float>();
    private readonly List<Vector3> anchors = new List<Vector3>();
    private NavMeshPath pathBuffer;

    private MonsterAI spotter;
    private Transform player;
    private Vector3 lastPlayerPos, playerVelocity;
    private bool hasLastPlayerPos;
    private bool hunting, hasSighting;
    private Vector3 sightPosition, heading;
    private float confidence;
    private float sightTime = float.NegativeInfinity;
    private float nextPlan, nextRing, nextRelocate;
    private string summary = "순찰 중";
    private int huntCount;

    // ── 몸이 쓰는 값 ──
    public float LostChaseTime => lostChaseTime;

    // ── 화면·기록용 ──
    /// <summary>누군가 쫓고 있다(기록·지도의 "사냥 중").</summary>
    public bool IsHunting => hunting;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => sightPosition;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => heading;
    public string SightingSpotterName => spotter != null ? spotter.name : "-";
    /// <summary>감독의 예측 위치(플레이어 위치 + 이동 방향 × 예측 시간 × 신뢰).</summary>
    public Vector3 DebugKnownPosition => PlayerPosition + heading * PlayerRunSpeed * predictHorizon * confidence;
    public IReadOnlyList<MonsterAI> DebugTeam => team;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugRoutes => routes;
    public IReadOnlyList<StageInfo> DebugStages => stages;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugRingSlots => ringSlots;
    public string DebugLayoutSummary => summary;
    public int DebugChaserCount
    {
        get
        {
            int count = 0;
            foreach (var m in MonsterAI.activeMonsters) if (m != null && m.CurrentState == MonsterAI.State.Chase) count++;
            return count;
        }
    }
    public Role RoleOf(MonsterAI m)
    {
        if (m == null) return Role.None;
        if (m.CurrentState == MonsterAI.State.Chase) return Role.Chase;
        return roles.TryGetValue(m, out var r) ? r : Role.None;
    }
    /// <summary>추격·길목 몬스터만 이름이 있다(기록 분석이 이 값으로 포위에 낀 몬스터를 센다).</summary>
    public string DebugOrderLabel(MonsterAI m)
    {
        switch (RoleOf(m))
        {
        case Role.Chase: return "추격";
        case Role.Stage: return "길목";
        }
        return "";
    }
    public string DebugRouteLabel(MonsterAI m)
    {
        if (!routes.TryGetValue(m, out var r) || r.directChaser) return "";
        return "길목 ETA~" + r.eta.ToString("F1") + "초";
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
        Vector3 at = m != null ? m.transform.position : PlayerPosition;
        PlaytestRecorder.Record("decision", m != null ? m.name : "director", at, code + "|" + text, DebugKnownPosition);
        DecisionMade?.Invoke(m, text);
    }

    private static string Short(MonsterAI m) => m == null ? "-" : m.name.Replace("Monster_", "");

    // ────────────────────────────────────────────────
    //  몸(MonsterAI)이 알려 오는 것
    // ────────────────────────────────────────────────

    /// <summary>플레이어를 보고 있다(몸은 이미 스스로 추격에 들어갔다). 감독은 길목·소리 확인 역할만 정리한다.</summary>
    public void ReportSighting(MonsterAI observer, Vector3 position)
    {
        if (observer == null || observer.IsInStun) return;
        hasSighting = true;
        sightPosition = position;
        sightTime = Time.time;
        spotter = observer;
        if (!roles.TryGetValue(observer, out var role) || role == Role.None) return;
        Decide(observer, role == Role.Stage ? "stage_engage" : "check_engage",
            (role == Role.Stage ? "길목에서" : "소리 확인 중") + " " + DistanceToPlayer(observer).ToString("F0") + "m 앞에서 봄 → 추격");
        ClearRole(observer);
        nextPlan = 0f;
    }

    /// <summary>시야가 끊겨 마지막으로 본 자리까지 가 봤지만 못 봤다 — 그 추격은 끝. 몬스터는 둘레로 돌아간다.</summary>
    public void ReportChaseEnded(MonsterAI m, float lostFor)
    {
        chaseEndedAt[m] = Time.time;
        SendToRing(m, "chase_end", "시야 끊김 " + lostFor.ToString("F1") + "초, 본 자리에 없음 → 추격 끝");
        nextPlan = 0f;
    }

    /// <summary>소리 확인·감전 뒤 둘러보기가 끝났다.</summary>
    public void ReportSearchDone(MonsterAI m)
    {
        SendToRing(m, "search_done", "확인 끝, 아무것도 없음");
    }

    /// <summary>길목까지 길이 없다.</summary>
    public void ReportBlockFailed(MonsterAI m)
    {
        SendToRing(m, "stage_failed", "길목까지 길 막힘");
        nextPlan = 0f;
    }

    /// <summary>감전: 그 몬스터가 있던 방향은 openingTime초 동안 새로 막지 않는다 — 쏴서 연 길.</summary>
    public void ReportStunned(MonsterAI m)
    {
        stunnedAt[m] = Time.time;
        ClearRole(m);
        Vector3 bearing = Vector3.ProjectOnPlane(m.transform.position - PlayerPosition, Vector3.up);
        if (bearing.magnitude > 1f && bearing.magnitude < 45f)
        {
            openings.Add((Mathf.Atan2(bearing.x, bearing.z) * Mathf.Rad2Deg, Time.time + openingTime));
            Decide(m, "stun_opening", "감전 → " + DirectionName(bearing.normalized) + " 방향 " + openingTime.ToString("F0") + "초 동안 새로 막지 않음");
        }
        nextPlan = 0f;
    }

    /// <summary>감전이 풀렸는데 플레이어가 안 보인다 — 쫓지 않는다(총 맞은 쪽으로 가는 것도 흔적 추적이다). 둘레로.</summary>
    public void ReportRecovered(MonsterAI m)
    {
        SendToRing(m, "recovered", "감전 풀림, 안 보임");
    }

    public void ReportIdle(MonsterAI m)
    {
        if (RoleOf(m) != Role.None) Decide(m, "idle", "갈 길이 없음 → 멈춤");
        ClearRole(m);
        ringSlots.Remove(m);
    }

    /// <summary>
    /// 소리. 몬스터 귀(자기 청각과 소리 범위 중 작은 쪽)에 닿으면 들은 것이다.
    /// 추격·길목에 안 낀 몬스터 중 들은 몬스터 최대 noiseCallers마리가 서로 다른 쪽에서 확인하러 온다(걸음).
    /// 소리는 추격을 시작하거나 늘리지 않고, 쫓는 몬스터에게 위치를 알려 주지도 않는다.
    /// 과녁 소리는 누군가 쫓고 있으면 무시한다(플레이어 위치가 아니므로).
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

        // 이미 확인하러 오는 몬스터는 목적지만 옮긴다 — 쏠 때마다 새 몬스터를 부르지 않는다
        noiseCheckers.RemoveAll(c => c == null || c.CurrentState != MonsterAI.State.Investigate || RoleOf(c) != Role.Check);
        foreach (var c in noiseCheckers) if (listeners.Contains(c)) c.CommandSearch(position);
        int need = Mathf.Max(0, noiseCallers) - noiseCheckers.Count;
        if (need <= 0) return;
        var free = new List<MonsterAI>();
        foreach (var m in listeners) if (IsFree(m) && Reusable(m)) free.Add(m);   // 방금 쫓았거나 감전된 몬스터는 부르지 않는다
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
            roles[m] = Role.Check;
            ringSlots.Remove(m);
            need--;
            Decide(m, "noise_call", (kind == NoiseKind.Shot ? "총소리" : "과녁 소리") + " 들음 (" +
                Vector3.Distance(m.transform.position, position).ToString("F0") + "m) → 확인하러 감 (" + noiseCheckers.Count + "번째, 발견은 눈으로)");
        }
    }

    public void AbortHunt()
    {
        hunting = false;
        hasSighting = false;
        sightTime = float.NegativeInfinity;
        heading = Vector3.zero;
        confidence = 0f;
        roles.Clear(); stageGoals.Clear(); stageArrivedAt.Clear(); stageMissingSince.Clear(); routes.Clear(); ringSlots.Clear(); relocatedAt.Clear();
        hasLastPlayerPos = false; playerVelocity = Vector3.zero;
        chaseEndedAt.Clear(); stunnedAt.Clear(); lastOnScreen.Clear(); noiseCheckers.Clear(); team.Clear(); stages.Clear(); openings.Clear();
        decisionThrottle.Clear();
        summary = "순찰 중";
    }

    // ────────────────────────────────────────────────
    //  매 프레임 · 계획
    // ────────────────────────────────────────────────

    private void Update()
    {
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) return;
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player")?.transform;
            hasLastPlayerPos = false;
        }
        if (player == null) return;
        UpdatePrediction(Time.deltaTime);
        if (Time.time >= nextPlan) Plan();
        if (Time.time >= nextRing) { nextRing = Time.time + ringInterval; PlaceRing(); }
        if (useRelocation && Time.time >= nextRelocate) { nextRelocate = Time.time + 1f; RelocateHidden(); }
    }

    /// <summary>
    /// 예측: 플레이어 이동 방향(부드럽게)과 신뢰(0~1). 똑바로 달리면 신뢰가 오르고,
    /// 80° 넘게 꺾으면 0.3 아래로, 150° 넘게 돌아서면 0 — 급회전이 길목을 헛다리로 만든다.
    /// </summary>
    private void UpdatePrediction(float dt)
    {
        // 실제로 움직인 거리로 속도를 잰다(물리 속도는 벽에 비비거나 순간이동하면 틀린다)
        Vector3 at = player.position;
        if (hasLastPlayerPos && dt > 0f)
        {
            Vector3 moved = Vector3.ProjectOnPlane(at - lastPlayerPos, Vector3.up) / dt;
            if (moved.magnitude < 30f) playerVelocity = Vector3.Lerp(playerVelocity, moved, Mathf.Clamp01(dt * 8f));
        }
        lastPlayerPos = at;
        hasLastPlayerPos = true;
        Vector3 v = playerVelocity;
        if (v.magnitude < 2f)
        {
            confidence = Mathf.MoveTowards(confidence, 0f, dt * .5f);
            return;
        }
        Vector3 dir = v.normalized;
        if (heading.sqrMagnitude < .01f) { heading = dir; confidence = .5f; return; }
        float turn = Vector3.Angle(heading, dir);
        if (turn > 150f)
        {
            if (confidence > .2f) Decide(null, "prediction_reset", "플레이어가 돌아섬 → 예측 버림", 1f);
            confidence = 0f;
            heading = dir;
            return;
        }
        if (turn > 80f)
        {
            if (confidence > .5f) Decide(null, "prediction_drop", "플레이어가 크게 꺾음 → 예측 신뢰 낮춤", 1f);
            confidence = Mathf.Min(confidence, .3f);
        }
        heading = Vector3.Slerp(heading, dir, Mathf.Clamp01(dt * 4f)).normalized;
        confidence = Mathf.MoveTowards(confidence, 1f, dt * .4f);
    }

    private void Plan()
    {
        nextPlan = Time.time + PlanInterval;
        openings.RemoveAll(o => Time.time >= o.until);
        bool anyChase = DebugChaserCount > 0;
        if (anyChase && !hunting) { hunting = true; huntCount++; Decide(null, "hunt_start", "추격 시작 #" + huntCount); }
        else if (!anyChase && hunting) { hunting = false; Decide(null, "hunt_end", "아무도 쫓지 않음 — 이번 추격에서 빠져나감"); }
        BuildStages();
        AssignStages();
        ApplySpeeds();
        RebuildSummary();
    }

    private void ApplySpeeds()
    {
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null) continue;
            switch (RoleOf(m))
            {
            case Role.Chase:
                m.SetHuntSpeed(Mathf.Lerp(nearSpeed, farSpeed, Mathf.InverseLerp(nearDistance, farDistance, DistanceToPlayer(m))));
                break;
            case Role.Stage:
                m.SetHuntSpeed(stageSpeed);
                break;
            default:
                m.SetHuntSpeed(-1f);
                break;
            }
        }
    }

    // ────────────────────────────────────────────────
    //  길목
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
    /// 길목 후보: 플레이어에게서 길로 stageMin~stageMax m, 플레이어 눈에 가려진 걸을 수 있는 자리.
    /// 그 길이 출발하는 방향(45° 칸)마다 가장 좋은 자리 하나. 뒤쪽(진행 방향과 반대)은 빼고,
    /// 추격자가 있는 쪽(50°)과 감전으로 연 쪽(±35°)도 뺀다.
    /// 점수 = 앞쪽일수록(× 예측 신뢰) + 예측 거리(신뢰 높으면 3초 앞, 낮으면 가까운 옆길)에 가까울수록.
    /// </summary>
    private void BuildStages()
    {
        stages.Clear();
        if (anchors.Count == 0) BuildAnchors();
        Vector3 p = PlayerPosition;
        if (!NavMesh.SamplePosition(p, out var origin, 4f, NavMesh.AllAreas)) return;
        Vector3 eye = p + Vector3.up * 1.5f;
        float targetLength = Mathf.Lerp(stageMin + 4f, PlayerRunSpeed * predictHorizon, confidence);
        var chaserSides = new List<Vector3>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || m.CurrentState != MonsterAI.State.Chase) continue;
            Vector3 side = Vector3.ProjectOnPlane(m.transform.position - p, Vector3.up);
            if (side.sqrMagnitude > 4f) chaserSides.Add(side);
        }
        var best = new StageInfo?[8];
        var mask = ObstacleMask;
        foreach (var a in anchors)
        {
            Vector3 flat = Vector3.ProjectOnPlane(a - origin.position, Vector3.up);
            float d = flat.magnitude;
            if (d < stageMin * .6f || d > stageMax + 5f || Mathf.Abs(a.y - origin.position.y) > 4f) continue;
            if (!Physics.Linecast(eye, a + Vector3.up * 1.3f, mask, QueryTriggerInteraction.Ignore)) continue;   // 플레이어에게 보이는 자리는 길목이 아니다
            float bearingAngle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            bool opened = false;
            foreach (var o in openings) if (Mathf.Abs(Mathf.DeltaAngle(o.angle, bearingAngle)) < OpeningHalfAngle) { opened = true; break; }
            if (opened) continue;
            bool chaserSide = false;
            foreach (var side in chaserSides) if (Vector3.Angle(side, flat) < ChaserSideAngle) { chaserSide = true; break; }
            if (chaserSide) continue;
            if (!NavMesh.CalculatePath(origin.position, a, NavMesh.AllAreas, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
            var route = pathBuffer.corners;
            float length = Length(route);
            if (length < stageMin || length > stageMax || length > d * 1.8f + 6f) continue;
            Vector3 dir = RouteStartDirection(route);
            if (dir.sqrMagnitude < .01f) continue;
            float ahead = heading.sqrMagnitude > .01f ? Vector3.Dot(dir, heading) : 0f;
            if (ahead < -.2f) continue;   // 뒤쪽 길목은 쓰지 않는다 — 뒤에서 오는 건 추격자 몫
            float score = ahead * confidence * 2f - Mathf.Abs(length - targetLength) / 10f;
            int bin = Mathf.Clamp(Mathf.FloorToInt((Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f) / 45f), 0, 7);
            if (best[bin] != null && best[bin].Value.score >= score) continue;
            best[bin] = new StageInfo { point = a, direction = dir, bin = bin, score = score, playerEta = length / PlayerRunSpeed };
        }
        foreach (var s in best) if (s != null) stages.Add(s.Value);
    }

    /// <summary>
    /// 길목 배정(0.5초마다): 쓸 수 있는 몬스터(추격·감전·소리 확인 중이 아니고, 방금 쫓거나 감전되지 않은 몬스터)를
    /// 점수 좋은 길목부터 하나씩 보낸다. 플레이어보다 1초 넘게 늦게 닿는 몬스터, 가는 길이 플레이어 곁 5m를 지나는 몬스터는 제외.
    /// 수 ≤ min(maxStagers, 길목 방향 수 − 1) — 길 하나는 늘 연다.
    /// 길목에 도착해 stageHoldTime초 동안 아무도 안 오면, 또는 맡은 길목이 사라지면 둘레로.
    /// </summary>
    private void AssignStages()
    {
        Vector3 p = PlayerPosition;
        foreach (var m in new List<MonsterAI>(stageGoals.Keys))
        {
            if (m == null || RoleOf(m) != Role.Stage) { stageGoals.Remove(m); stageArrivedAt.Remove(m); routes.Remove(m); continue; }
            if (m.IsHolding && !stageArrivedAt.ContainsKey(m)) stageArrivedAt[m] = Time.time;
            if (stageArrivedAt.TryGetValue(m, out float arrived) && Time.time - arrived > stageHoldTime)
                SendToRing(m, "stage_timeout", "길목에서 " + stageHoldTime.ToString("F0") + "초 기다렸지만 안 옴");
        }

        int allowed = Mathf.Min(maxStagers, stages.Count - 1);
        var candidates = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || !m.CanTakeOrders) continue;
            var role = RoleOf(m);
            if (role == Role.Stage) { candidates.Add(m); continue; }
            if (IsFree(m) && Reusable(m)) candidates.Add(m);
        }

        var cost = new Dictionary<(MonsterAI, int), float>();
        var paths = new Dictionary<(MonsterAI, int), Vector3[]>();
        for (int i = 0; i < stages.Count; i++)
            foreach (var m in candidates)
            {
                bool holds = stageGoals.TryGetValue(m, out var goal) && Vector3.Distance(goal.point, stages[i].point) < 8f;
                var path = ComputePath(m, stages[i].point);
                if (path == null) continue;
                float eta = Length(path) / Mathf.Max(1f, stageSpeed);
                if (!holds && (PassesNear(path, p, 5f) || eta > stages[i].playerEta + 1f)) continue;
                cost[(m, i)] = eta * .3f - stages[i].score - (holds ? 1.5f : 0f);
                paths[(m, i)] = (Vector3[])path.Clone();
            }

        var assigned = new HashSet<MonsterAI>();
        var taken = new HashSet<int>();
        while (assigned.Count < allowed)
        {
            MonsterAI bestM = null;
            int bestI = -1;
            float bestC = float.PositiveInfinity;
            foreach (var pair in cost)
            {
                if (assigned.Contains(pair.Key.Item1) || taken.Contains(pair.Key.Item2)) continue;
                if (pair.Value < bestC) { bestC = pair.Value; bestM = pair.Key.Item1; bestI = pair.Key.Item2; }
            }
            if (bestM == null) break;
            assigned.Add(bestM);
            taken.Add(bestI);
            SendToStage(bestM, bestI, paths[(bestM, bestI)]);
        }
        // 맡은 길목이 1.5초 넘게 후보에 없을 때만 둘레로(한 번 빠졌다고 바로 불러들이면 배정·해제가 반복된다)
        foreach (var m in new List<MonsterAI>(stageGoals.Keys))
        {
            if (assigned.Contains(m) || RoleOf(m) != Role.Stage) { stageMissingSince.Remove(m); continue; }
            if (!stageMissingSince.TryGetValue(m, out float since)) { stageMissingSince[m] = Time.time; continue; }
            if (Time.time - since < StageDropGrace) continue;
            stageMissingSince.Remove(m);
            SendToRing(m, "stage_drop", stages.Count <= 1 ? "갈 수 있는 길이 하나뿐 → 길목 비움(길 하나는 늘 연다)" : "맡은 길목이 사라짐(플레이어가 다른 길로)");
        }
    }

    private bool Reusable(MonsterAI m)
    {
        if (chaseEndedAt.TryGetValue(m, out float ended) && Time.time - ended < reuseDelay) return false;
        if (stunnedAt.TryGetValue(m, out float stunned) && Time.time - stunned < reuseDelay) return false;
        return true;
    }

    private void SendToStage(MonsterAI m, int index, Vector3[] path)
    {
        var s = stages[index];
        s.assigned = true;
        stages[index] = s;
        bool fresh = !stageGoals.TryGetValue(m, out var old) || Vector3.Distance(old.point, s.point) >= 8f || RoleOf(m) != Role.Stage;
        roles[m] = Role.Stage;
        ringSlots.Remove(m);
        if (!fresh && m.IsHolding) { stageGoals[m] = s; return; }   // 같은 길목에서 기다리는 중 — 자리를 옮기지 않는다
        stageGoals[m] = s;
        if (fresh) stageArrivedAt.Remove(m);
        float eta = Length(path) / Mathf.Max(1f, stageSpeed);
        routes[m] = new RouteInfo { corners = path, goal = s.point, eta = eta, colorIndex = s.bin % 2 + 1 };
        Vector3 face = Vector3.ProjectOnPlane(PlayerPosition - s.point, Vector3.up);
        m.CommandBlock(s.point, face);
        if (fresh) Decide(m, "stage_assign", DirectionName(s.direction) + " 길목에서 대기 · 약 " + eta.ToString("F1") + "초 (플레이어 약 " + s.playerEta.ToString("F1") + "초, 예측 " + Mathf.RoundToInt(confidence * 100f) + "%)");
    }

    private void ClearRole(MonsterAI m)
    {
        roles.Remove(m);
        stageGoals.Remove(m);
        stageArrivedAt.Remove(m);
        stageMissingSince.Remove(m);
        routes.Remove(m);
        noiseCheckers.Remove(m);
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
        if (heading.sqrMagnitude < .01f) return "옆길";
        float angle = Vector3.SignedAngle(heading, dir, Vector3.up);
        if (Mathf.Abs(angle) <= 45f) return "앞";
        if (Mathf.Abs(angle) >= 135f) return "뒤";
        return angle > 0f ? "오른쪽" : "왼쪽";
    }

    // ────────────────────────────────────────────────
    //  둘레 순찰 · 재배치
    // ────────────────────────────────────────────────

    private Vector3 PlayerPosition => player != null ? player.position : sightPosition;

    private Vector3 PlayerForward
    {
        get
        {
            if (heading.sqrMagnitude > .01f) return heading;
            if (player == null) return Vector3.forward;
            Vector3 f = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            return f.sqrMagnitude > .01f ? f.normalized : Vector3.forward;
        }
    }

    /// <summary>추격·길목·소리 확인·감전 중이 아닌, 순찰하는 몬스터.</summary>
    private bool IsFree(MonsterAI m)
    {
        if (m == null || !m.CanTakeOrders || RoleOf(m) != Role.None) return false;
        var st = m.CurrentState;
        return st == MonsterAI.State.Patrol || st == MonsterAI.State.Return || st == MonsterAI.State.Idle;
    }

    /// <summary>
    /// 둘레 자리(ringInterval초마다): 순찰하는 몬스터에게 플레이어 둘레 ringMin~ringMax m,
    /// 방향을 고르게 나눈 자리를 준다(첫 자리는 플레이어가 가는 쪽, 쫓는 몬스터가 있으면 그 반대쪽부터).
    /// 자리는 플레이어 눈에 안 보이고 길로 이어진 곳. 자리가 12m 넘게 바뀔 때만 명령한다.
    /// </summary>
    private void PlaceRing()
    {
        if (player == null) return;
        if (anchors.Count == 0) BuildAnchors();
        var free = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters) if (IsFree(m)) free.Add(m);
        if (free.Count == 0) return;

        Vector3 p = PlayerPosition;
        Vector3 fwd = PlayerForward;
        float baseAngle = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Vector3 chaseSide = Vector3.zero;
        foreach (var m in MonsterAI.activeMonsters)
            if (m != null && m.CurrentState == MonsterAI.State.Chase) chaseSide += Vector3.ProjectOnPlane(m.transform.position - p, Vector3.up).normalized;
        if (chaseSide.sqrMagnitude > .01f) baseAngle = Mathf.Atan2(-chaseSide.x, -chaseSide.z) * Mathf.Rad2Deg;
        int n = free.Count;
        float step = 360f / n;
        var slots = new List<Vector3>();
        for (int i = 0; i < n; i++)
            if (FindRingSlot(p, baseAngle + i * step, Mathf.Min(40f, step * .5f), out Vector3 slot)) slots.Add(slot);
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
            Decide(bestM, "ring_place", "둘레 자리 (플레이어에서 " + Vector3.Distance(p, chosen).ToString("F0") + "m)", 3f);
        }
    }

    /// <summary>플레이어 둘레 한 방향의 자리: 거리 ringMin~ringMax, 방향 ±spread°, 플레이어에게 안 보이는 곳 우선, 가운데 거리에 가까운 곳.</summary>
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
    /// 재배치(1초마다, 한 번에 한 마리): 순찰하는 몬스터 중 플레이어에게서 relocateDistance m 넘게 떨어지고,
    /// 화면·미러에서 unseenBeforeRelocate초 넘게 안 보였고, 방금(reuseDelay × 1.5초) 쫓거나 감전되지 않은 몬스터를
    /// 오래 안 보인 순서로 골라, 플레이어 앞쪽(±60°) 둘레의 카메라에 안 보이는 자리(relocateMin m 밖)로 옮긴다.
    /// 떨쳐 낸 몬스터가 곧바로 앞에 다시 나오지 않게 — 탈출을 무효로 만들지 않는다.
    /// </summary>
    private void RelocateHidden()
    {
        if (player == null) return;
        Vector3 p = PlayerPosition;
        foreach (var m in MonsterAI.activeMonsters)
            if (m != null && VisibleToAnyCamera(m.transform.position)) lastOnScreen[m] = Time.time;

        MonsterAI pick = null;
        float oldest = float.PositiveInfinity;
        float cooldown = reuseDelay * 1.5f;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (!IsFree(m)) continue;
            if (relocatedAt.TryGetValue(m, out float at) && Time.time - at < relocateCooldown) continue;
            if (chaseEndedAt.TryGetValue(m, out float ended) && Time.time - ended < cooldown) continue;
            if (stunnedAt.TryGetValue(m, out float stunned) && Time.time - stunned < cooldown) continue;
            float seen = lastOnScreen.TryGetValue(m, out float s) ? s : float.NegativeInfinity;
            if (Time.time - seen < unseenBeforeRelocate) continue;
            if (Vector3.Distance(m.transform.position, p) < relocateDistance) continue;
            if (seen < oldest) { oldest = seen; pick = m; }
        }
        if (pick == null) return;
        Vector3 fwd = PlayerForward;
        if (!FindRingSlot(p, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + Random.Range(-60f, 60f), 45f, out Vector3 slot)) return;
        if (Vector3.Distance(slot, p) < relocateMin || VisibleToAnyCamera(slot)) return;
        if (!NavMesh.SamplePosition(slot, out var hit, 2f, NavMesh.AllAreas)) return;
        Vector3 from = pick.transform.position;
        relocatedAt[pick] = Time.time;
        ringSlots[pick] = hit.position;
        pick.Relocate(hit.position);
        Relocated?.Invoke(pick, from, hit.position);
        Decide(pick, "relocate", "화면에서 " + (float.IsNegativeInfinity(oldest) ? "한 번도" : (Time.time - oldest).ToString("F0") + "초") + " 안 보임, " +
            Vector3.Distance(from, p).ToString("F0") + "m → 앞쪽 둘레로 재배치 (플레이어에서 " + Vector3.Distance(hit.position, p).ToString("F0") + "m)");
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

    private void SendToRing(MonsterAI m, string code, string why)
    {
        if (m == null) return;
        ClearRole(m);
        m.SetHuntSpeed(-1f);
        Vector3 p = PlayerPosition;
        Vector3 away = Vector3.ProjectOnPlane(m.transform.position - p, Vector3.up);
        float angle = away.sqrMagnitude > .01f ? Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg : Random.Range(0f, 360f);
        if (!FindRingSlot(p, angle, 60f, out Vector3 slot)) { m.CommandPatrolAt(m.transform.position); Decide(m, code, why + " → 제자리 순찰"); return; }
        ringSlots[m] = slot;
        m.CommandPatrolAt(slot);
        Decide(m, code, why + " → 둘레 자리로 (" + Vector3.Distance(p, slot).ToString("F0") + "m)");
    }

    // ────────────────────────────────────────────────
    //  도우미
    // ────────────────────────────────────────────────

    private static bool PassesNear(Vector3[] path, Vector3 playerAt, float radius)
    {
        float total = Length(path), walked = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float segment = Vector3.Distance(path[i - 1], path[i]);
            // 마지막 8m는 도착 구간이라 검사하지 않는다
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

    /// <summary>기록·화면용 한 줄. "길목 k/n"은 QA 도구가 길목을 센다.</summary>
    private void RebuildSummary()
    {
        team.Clear();
        int chase = 0, stage = 0;
        foreach (var m in MonsterAI.activeMonsters)
        {
            switch (RoleOf(m))
            {
            case Role.Chase: chase++; team.Add(m); break;
            case Role.Stage: stage++; team.Add(m); break;
            }
        }
        summary = "추격 " + chase + " · 길목 " + stage + "/" + stages.Count + " · 예측 " + Mathf.RoundToInt(confidence * 100f) + "%" +
            (openings.Count > 0 ? " · 연 길 " + openings.Count : "");
    }

    private void OnDrawGizmos()
    {
        foreach (var s in stages)
        {
            Gizmos.color = s.assigned ? Color.red : Color.green;
            Gizmos.DrawWireSphere(s.point, 1.2f);
        }
        Gizmos.color = Color.cyan;
        foreach (var r in routes.Values)
            if (r.corners != null)
                for (int i = 1; i < r.corners.Length; i++) Gizmos.DrawLine(r.corners[i - 1], r.corners[i]);
    }
}
