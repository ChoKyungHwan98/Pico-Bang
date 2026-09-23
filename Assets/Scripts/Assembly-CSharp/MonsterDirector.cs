using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Joint monster/route selection: one pressure pursuer, up to two independent approaches.
/// Sightings and shot events supply knowledge. Unselected monsters walk to separate approaches; no teleports.</summary>
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
    private static void ResetStatics() { instance = null; isQuitting = false; }
    public class RouteInfo
    {
        public Vector3[] corners;
        public bool detour, directChaser;
        public Vector3 waypoint;
        public Vector3 goal;
        public float overlap, eta;
        public int colorIndex;
    }
    public struct CutCandidate
    {
        public Vector3 point, direction;
        public float reach, score;
        public bool rejected, chosen;
        public string note;
    }
    private MonsterRoutePlanner planner;
    private readonly List<MonsterAI> team = new List<MonsterAI>();
    private readonly Dictionary<MonsterAI, RouteInfo> routes = new Dictionary<MonsterAI, RouteInfo>();
    private readonly Dictionary<MonsterAI, Vector3> orders = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, Vector3> waypoints = new Dictionary<MonsterAI, Vector3>();
    private readonly List<CutCandidate> candidates = new List<CutCandidate>();
    private readonly Dictionary<MonsterAI, RouteInfo> preparations = new Dictionary<MonsterAI, RouteInfo>();
    private sealed class PreparationProgress
    {
        public Vector3 position;
        public float progressedAt, retryAt;
        public string status;
        public bool noRoute;
    }
    private readonly Dictionary<MonsterAI, PreparationProgress> preparationProgress = new Dictionary<MonsterAI, PreparationProgress>();
    private readonly Dictionary<MonsterAI, float> followSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> blockedUntil = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> zoneSwapUntil = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, float> routeInvalidSince = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, Vector3> routeInvalidGoal = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, Vector3> invalidatedRouteGoal = new Dictionary<MonsterAI, Vector3>();
    private const float CutoffReachedDistance = 6f;
    private const float CutoffPassedDot = -.55f;
    private const float RouteInvalidHold = .4f;
    private const float ForcedReplanCooldown = .8f;
    private float nextEncounterPlan, nextZoneSwap, directionEvidenceTime = float.NegativeInfinity;
    private MonsterAI pressure, spotter, global;
    private Transform player;
    private Vector3 knownPosition, previousSighting, sightDirection, evidenceDirection;
    private float knowledgeTime = float.NegativeInfinity, sightTime = float.NegativeInfinity;
    private float nextPlan, nextSafety, pressureSince, lastForcedReplan = float.NegativeInfinity;
    private bool hunting, hasSighting;
    private int followBreaks, handovers, planCount;
    private string summary = "대기";
    public float LastPlanMilliseconds { get; private set; }
    public int DebugPlanCount => planCount;
    public int DebugPathQueries => planner.PathQueries;
    public bool IsHunting => hunting;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => previousSighting;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => sightDirection;
    public string SightingSpotterName => spotter != null ? spotter.name : "-";
    public int DebugChaserCount => pressure != null && pressure.CurrentState == MonsterAI.State.Chase ? 1 : 0;
    public IReadOnlyList<MonsterAI> DebugTeam => team;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugRoutes => routes;
    public IReadOnlyDictionary<MonsterAI, RouteInfo> DebugPreparations => preparations;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugOrderPoints => orders;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugWaypoints => waypoints;
    public IReadOnlyList<CutCandidate> DebugCutCandidates => candidates;
    public Vector3 DebugCandidateOrigin => knownPosition;
    public Vector3 DebugKnownPosition => knownPosition;
    public float DebugConvergenceRadius => MonsterRoutePlanner.Convergence;
    public int DebugFollowBreaks => followBreaks;
    public int DebugHandovers => handovers;
    public string DebugLayoutSummary => summary;
    public string DebugOrderLabel(MonsterAI m) => preparations.ContainsKey(m) ? "통로 준비" : !team.Contains(m) ? "" : m == pressure ? "압박" : "협공";
    public string DebugPreparationStatus(MonsterAI m) => preparationProgress.TryGetValue(m, out var p) ? p.status ?? "moving" : "";
    public string DebugRouteLabel(MonsterAI m)
    {
        if (preparations.ContainsKey(m)) return "통로 준비";
        if (!routes.TryGetValue(m, out var r)) return "";
        return (r.directChaser ? "추격" : r.detour ? "우회 협공" : "별도 진입") +
                (r.directChaser ? "" : " ETA~" + r.eta.ToString("F1")) + " ov" + r.overlap.ToString("F2");
    }
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
    private float FarSpeed => Settings != null ? Mathf.Max(TrackingSpeedCap, Settings.huntFarSpeed) : 14f;
    private float Memory => Settings != null ? Mathf.Max(1, Settings.huntMemory) : 8f;
    private float Refresh => Settings != null ? Mathf.Clamp(Settings.layoutRefreshInterval, .3f, 1f) : .75f;
    private float EstimateArrival(MonsterRoutePlanner.Option route) => MonsterRoutePlanner.EstimateTravelTime(
        route, TrackingSpeedCap, FarSpeed, Settings != null ? Settings.huntNearDistance : 15f,
        Settings != null ? Settings.huntFarDistance : 40f);
    private Vector3 StrategicDirection => Time.time - directionEvidenceTime <= 3f ? evidenceDirection :
        Time.time - sightTime <= 3f ? Vector3.ProjectOnPlane(sightDirection, Vector3.up).normalized : Vector3.zero;

    private Vector3? RetainedMissionGoal(MonsterAI m)
    {
        if (m == null || m == pressure || !routes.TryGetValue(m, out var old)) return null;
        float distance = Vector3.Distance(old.goal, knownPosition);
        if (distance < CutoffReachedDistance || distance > 45f) return null;
        Vector3 direction = StrategicDirection;
        if (direction.sqrMagnitude > .1f && Vector3.Dot((old.goal - knownPosition).normalized, direction) < CutoffPassedDot) return null;
        return old.goal;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        planner = new MonsterRoutePlanner();
        planner.Rebuild();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void OnApplicationQuit() { isQuitting = true; }
    public void AbortHunt()
    {
        EndHunt();
        hasSighting = false;
        knowledgeTime = sightTime = float.NegativeInfinity;
        blockedUntil.Clear(); followSince.Clear();
        zoneSwapUntil.Clear(); nextEncounterPlan = nextZoneSwap = 0;
        routeInvalidSince.Clear(); routeInvalidGoal.Clear(); invalidatedRouteGoal.Clear();
        lastForcedReplan = float.NegativeInfinity;
        evidenceDirection = Vector3.zero; directionEvidenceTime = float.NegativeInfinity;
    }
    private void EndHunt()
    {
        foreach (var m in team)
            if (m != null) { m.SetHuntSpeed(-1); m.CommandGoHome(3f); }
        foreach (var m in preparations.Keys)
            if (m != null && !m.IsInStun) { m.SetHuntSpeed(-1); m.CommandGoHome(0f); }
        preparations.Clear();
        preparationProgress.Clear();
        team.Clear(); routes.Clear(); orders.Clear(); waypoints.Clear(); candidates.Clear();
        routeInvalidSince.Clear(); routeInvalidGoal.Clear(); invalidatedRouteGoal.Clear();
        pressure = null; hunting = false; summary = "수색 종료 · 구역 복귀";
    }
    private void Remember(Vector3 point, float timestamp)
    {
        if (timestamp < knowledgeTime) return;
        Vector3 movement = Vector3.ProjectOnPlane(point - knownPosition, Vector3.up);
        if (knowledgeTime > float.NegativeInfinity && timestamp - knowledgeTime <= 3f && movement.magnitude >= .75f)
        {
            Vector3 sample = movement.normalized;
            evidenceDirection = evidenceDirection.sqrMagnitude < .1f ? sample : Vector3.Slerp(evidenceDirection, sample, .65f).normalized;
            directionEvidenceTime = timestamp;
        }
        knownPosition = point; knowledgeTime = Mathf.Min(Time.time, timestamp);
        if (pressure != null) pressure.RefreshPursuitEvidence(point);
        if (!hunting) { hunting = true; nextPlan = 0; }
        else EvaluateRouteInvalidation(point);
    }

    /// <summary>
    /// A sight report arrives from every monster that can see the player, often several times in one frame.
    /// Route changes therefore require a sustained invalid condition and are emitted once per assigned goal.
    /// Player knowledge remains live; only the tactical mission is stabilised.
    /// </summary>
    private void EvaluateRouteInvalidation(Vector3 point)
    {
        Vector3 direction = StrategicDirection;
        foreach (var pair in routes)
        {
            if (pair.Key == null || pair.Value.directChaser) continue;
            Vector3 goal = pair.Value.goal;
            Vector3 gap = Vector3.ProjectOnPlane(goal - point, Vector3.up);
            bool reached = gap.magnitude < CutoffReachedDistance;
            bool passed = direction.sqrMagnitude > .1f && gap.sqrMagnitude > 1f &&
                Vector3.Dot(gap.normalized, direction) < CutoffPassedDot;
            if (!reached && !passed)
            {
                routeInvalidSince.Remove(pair.Key);
                routeInvalidGoal.Remove(pair.Key);
                invalidatedRouteGoal.Remove(pair.Key);
                continue;
            }

            if (!routeInvalidGoal.TryGetValue(pair.Key, out Vector3 watchedGoal) ||
                Vector3.Distance(watchedGoal, goal) >= 1f)
            {
                routeInvalidGoal[pair.Key] = goal;
                routeInvalidSince[pair.Key] = Time.time;
                invalidatedRouteGoal.Remove(pair.Key);
                continue;
            }
            if (invalidatedRouteGoal.TryGetValue(pair.Key, out Vector3 alreadyInvalidated) &&
                Vector3.Distance(alreadyInvalidated, goal) < 1f) continue;
            if (Time.time - routeInvalidSince[pair.Key] < RouteInvalidHold ||
                Time.time - lastForcedReplan < ForcedReplanCooldown) continue;

            lastForcedReplan = Time.time;
            invalidatedRouteGoal[pair.Key] = goal;
            nextPlan = Mathf.Min(nextPlan, Time.time);
            PlaytestRecorder.Record("corridor_invalidated", pair.Key.name, pair.Key.transform.position,
                reached ? "player_reached_cutoff" : "player_passed_cutoff", goal);
            break;
        }
    }
    public void ReportSighting(MonsterAI observer, Vector3 position)
    {
        if (observer == null || observer.IsInStun || observer.IsHomeLocked) return;
        if (hasSighting && Time.time - sightTime > .05f) sightDirection = (position - previousSighting).normalized;
        hasSighting = true; previousSighting = position; sightTime = Time.time; spotter = observer;
        Remember(position, Time.time);
    }
    public void ReportTargetAlarm(Vector3 shotPosition, float shotTime)
    {
        // An old projectile impact cannot replace a more recent sighting.
        if (Time.time - shotTime <= Memory) Remember(shotPosition, shotTime);
    }
    public void ReportNoise(Vector3 position, float radius, NoiseKind kind)
    {
        if (hunting && kind == NoiseKind.TargetDestroyed) return;
        int listeners = 0;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null || m.IsInStun || m.IsHomeLocked) continue;
            if (Vector3.Distance(m.transform.position, position) <= Mathf.Min(radius, m.EffectiveHearingRange))
                listeners++;
        }
        if (listeners == 0) return;
        Remember(position, Time.time);
        PlaytestRecorder.Record("noise_evidence", "player", position,
            kind + ":listeners=" + listeners.ToString(System.Globalization.CultureInfo.InvariantCulture));
        // Repeated firing refreshes evidence. It does not revoke valid corridor missions or force an immediate regroup.
    }
    public void ReportReturnEncounter(MonsterAI observer)
    {
        if (observer == null || !observer.ReleaseReturnLockForEncounter()) return;
        blockedUntil.Remove(observer);
        ReportSighting(observer, observer.player.position);
        if (Time.time < nextEncounterPlan) return;
        nextEncounterPlan = Time.time + .2f;
        Coordinate();
    }
    public bool TryGetPursuitHint(MonsterAI m, out Vector3 hint)
    {
        hint = knownPosition;
        return hunting && team.Contains(m) && Time.time - knowledgeTime <= Memory;
    }
    public bool RequestChase(MonsterAI m)
    {
        if (m == null || !m.CanReceiveTactics || !hunting) return false;
        if (pressure == null && Time.time >= nextPlan) Coordinate();
        return pressure == m;
    }
    public void InvalidateRoute(MonsterAI m)
    {
        nextPlan = 0;
        if (m != null && m.IsInStun) blockedUntil[m] = Time.time + .5f;
    }

    public void RefreshCloseApproach(MonsterAI m)
    {
        if (!hunting || m == pressure || !team.Contains(m) || !Eligible(m) || m.IsTraversingLink ||
            Vector3.Distance(m.transform.position, knownPosition) > 18f) return;
        var corners = planner.Path(m, m.transform.position, knownPosition);
        if (corners == null) return;
        var approach = MonsterRoutePlanner.Make(m, corners, knownPosition, false, false);
        foreach (var pair in routes)
        {
            if (pair.Key == m || pair.Key == null || pair.Key.IsInStun) continue;
            var otherCorners = pair.Key == pressure || pair.Key.IsFinalApproach
                ? planner.Path(pair.Key, pair.Key.transform.position, knownPosition) : pair.Value.corners;
            if (otherCorners == null) continue;
            var other = MonsterRoutePlanner.Make(pair.Key, otherCorners, pair.Value.waypoint, pair.Value.detour, false);
            if (MonsterRoutePlanner.Conflict(approach, other, out _)) return;
        }
        // Each independently arriving monster continues closing; acquiring this path does not replace the pursuer.
        m.CommandTacticalRoute(corners, knownPosition, false);
        routes[m] = new RouteInfo { corners = corners, goal = knownPosition, waypoint = knownPosition,
            eta = EstimateArrival(approach), colorIndex = routes[m].colorIndex };
        orders[m] = knownPosition; waypoints.Remove(m);
    }
    private bool Eligible(MonsterAI m) => m != null && m.CanReceiveTactics && !m.IsGivingUp &&
        (!blockedUntil.TryGetValue(m, out float until) || Time.time >= until);

    private void Update()
    {
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) return;
        if (player == null) player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (Time.time >= nextSafety)
        {
            nextSafety = Time.time + .2f;
            CheckFollowing();
            CheckPreparationProgress();
        }
        if (Time.time >= nextZoneSwap) { nextZoneSwap = Time.time + 2f; TrySwapReturnZones(); }
        if (!hunting) return;
        if (Time.time - knowledgeTime > Memory) { EndHunt(); return; }
        if (Time.time >= nextPlan) Coordinate();
        foreach (var m in team)
        {
            if (m == null) continue;
            float distance = Vector3.Distance(m.transform.position, knownPosition);
            float near = Settings != null ? Settings.huntNearDistance : 15f;
            float far = Settings != null ? Settings.huntFarDistance : 40f;
            m.SetHuntSpeed(Mathf.Lerp(TrackingSpeedCap, FarSpeed, Mathf.InverseLerp(near, far, distance)));
        }
    }

    private void Coordinate()
    {
        // Do not revoke assignments or change their destinations in the middle of an OffMeshLink jump.
        foreach (var member in team)
            if (member != null && member.IsTraversingLink) { nextPlan = Time.time + .15f; return; }
        foreach (var member in preparations.Keys)
            if (member != null && member.IsTraversingLink) { nextPlan = Time.time + .15f; return; }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        nextPlan = Time.time + Refresh;
        planner.BeginPlan();
        if (planner.AnchorCount == 0) planner.Rebuild();
        candidates.Clear();
        var options = new List<MonsterRoutePlanner.Option>();
        var direct = new List<MonsterRoutePlanner.Option>();
        float maxSeconds = Settings != null ? Mathf.Clamp(Settings.detourTimeLimit, 3f, 12f) : 8f;
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (!Eligible(m)) continue;
            var pressurePath = planner.Path(m, m.transform.position, knownPosition);
            if (pressurePath != null)
            {
                var pressureOption = MonsterRoutePlanner.Make(m, pressurePath, knownPosition, false, false);
                pressureOption.eta = EstimateArrival(pressureOption);
                direct.Add(pressureOption);
            }
            // Supports reserve connected exits/cutoffs. They never use another shortest path to the same evidence point.
            var available = planner.BuildCutoffs(m, knownPosition, StrategicDirection, FarSpeed * maxSeconds, RetainedMissionGoal(m));
            options.AddRange(available);
            foreach (var o in available)
            {
                o.eta = EstimateArrival(o);
                candidates.Add(new CutCandidate { point = o.waypoint, reach = o.length, note = m.name });
            }
        }
        MonsterRoutePlanner.Option bestPressure = null, bestA = null, bestB = null;
        float bestScore = float.NegativeInfinity;
        var conflicts = new Dictionary<(MonsterRoutePlanner.Option, MonsterRoutePlanner.Option), bool>();
        bool Conflicts(MonsterRoutePlanner.Option a, MonsterRoutePlanner.Option b)
        {
            if (a.monster == b.monster) return true;
            if (conflicts.TryGetValue((a, b), out bool value)) return value;
            value = MonsterRoutePlanner.Conflict(a, b, out _);
            conflicts[(a, b)] = value; conflicts[(b, a)] = value;
            return value;
        }
        bool SupportConflict(MonsterRoutePlanner.Option a, MonsterRoutePlanner.Option b) => Conflicts(a, b) ||
            Vector3.Distance(a.goal, b.goal) < 8f || (a.approachDirection.sqrMagnitude > .1f &&
            b.approachDirection.sqrMagnitude > .1f && Vector3.Dot(a.approachDirection, b.approachDirection) > .65f);
        bool ArrivesInTime(MonsterRoutePlanner.Option o) => o.eta <= maxSeconds &&
            o.eta <= Mathf.Max(4.5f, o.targetEta + 3.5f);
        float SupportScore(MonsterRoutePlanner.Option o) =>
            10f - o.eta + Mathf.Clamp(o.targetEta - o.eta, -2f, 2f) +
            (o.retained ? 5f : team.Contains(o.monster) ? 1.5f : 0f);
        int supportLimit = Settings != null ? Mathf.Clamp(Settings.huntTeamSize - 1, 0, 2) : 2;
        // Pressure continuity is a constraint, not a small bonus that extra support slots can outweigh.
        MonsterRoutePlanner.Option incumbent = direct.Find(p => p.monster == pressure);
        bool keepPressure = incumbent != null && pressure.CurrentState == MonsterAI.State.Chase &&
            (incumbent.length <= 18f || Time.time - pressureSince < 3f);
        MonsterRoutePlanner.Option encounter = null;
        if (!keepPressure || (incumbent != null && incumbent.length > 18f))
            foreach (var p in direct)
                if (p.monster.CurrentState == MonsterAI.State.Return && p.monster.HasCloseVisibleEncounter &&
                    p.length + 2f < (incumbent != null ? incumbent.length : float.PositiveInfinity) &&
                    (encounter == null || p.length < encounter.length)) encounter = p;
        float nearest = float.PositiveInfinity;
        foreach (var p in direct) { nearest = Mathf.Min(nearest, p.length); }
        foreach (var p in direct)
        {
            if (encounter != null ? p != encounter : keepPressure && p.monster != pressure) continue;
            // Never sacrifice immediate pressure just to increase the support count.
            if (encounter == null && !keepPressure && p.length > nearest + 2f) continue;
            float score = -2f * p.length / TrackingSpeedCap + (p.monster == pressure ? 2.5f : 0);
            if (score > bestScore) { bestScore = score; bestPressure = p; bestA = bestB = null; }
            if (supportLimit == 0) continue;
            for (int i = 0; i < options.Count; i++)
            {
                var a = options[i];
                if (!ArrivesInTime(a) || Conflicts(p, a)) continue;
                float oneScore = score + SupportScore(a);
                if (oneScore > bestScore) { bestScore = oneScore; bestPressure = p; bestA = a; bestB = null; }
                if (supportLimit < 2) continue;
                for (int j = i + 1; j < options.Count; j++)
                {
                    var b = options[j];
                    if (!ArrivesInTime(b) || Conflicts(p, b) || SupportConflict(a, b)) continue;
                    float twoScore = oneScore + SupportScore(b);
                    if (twoScore > bestScore) { bestScore = twoScore; bestPressure = p; bestA = a; bestB = b; }
                }
            }
        }
        if (bestPressure == null)
        {
            foreach (var m in team) if (m != null && !m.IsInStun) m.CommandGoHome(2f);
            foreach (var m in preparations.Keys) if (m != null && !m.IsInStun) m.CommandGoHome(0f);
            preparations.Clear();
            preparationProgress.Clear();
            team.Clear(); routes.Clear(); orders.Clear(); waypoints.Clear(); pressure = null;
            summary = "도달 가능한 참여자 없음 · 재평가";
        }
        else ApplyAssignment(bestPressure, bestA, bestB);
        planCount++;
        LastPlanMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
    }

    private void ApplyAssignment(MonsterRoutePlanner.Option p, MonsterRoutePlanner.Option a, MonsterRoutePlanner.Option b)
    {
        var selected = new List<MonsterRoutePlanner.Option> { p };
        if (a != null) selected.Add(a);
        if (b != null) selected.Add(b);
        var previousRoutes = new Dictionary<MonsterAI, RouteInfo>(routes);
        MonsterAI previousPressure = pressure;
        foreach (var old in team)
        {
            if (old == null || selected.Exists(o => o.monster == old)) continue;
            PlaytestRecorder.Record("assignment_released", old.name, old.transform.position, "not_selected_by_joint_plan");
            // Non-participation means preparing a different approach, not abandoning the hunt for a distant home.
        }
        if (pressure != p.monster)
        {
            if (pressure != null)
            {
                handovers++;
                PlaytestRecorder.Record("pressure_handover", p.monster.name, p.monster.transform.position, "from=" + pressure.name);
            }
            pressureSince = Time.time;
        }
        pressure = p.monster;
        team.Clear(); routes.Clear(); orders.Clear(); waypoints.Clear();
        for (int i = 0; i < selected.Count; i++)
        {
            var o = selected[i]; var m = o.monster;
            team.Add(m);
            float overlap = 0;
            foreach (var other in selected)
            {
                if (other == o) continue;
                MonsterRoutePlanner.Conflict(o, other, out float value); overlap = Mathf.Max(overlap, value);
            }
            routes[m] = new RouteInfo { corners = o.corners, waypoint = o.waypoint, goal = o.goal, eta = o.eta,
                detour = o.detour, directChaser = i == 0, colorIndex = i, overlap = overlap };
            orders[m] = i == 0 ? knownPosition : o.goal;
            bool sameMission = i == 0 ? previousPressure == m : previousRoutes.TryGetValue(m, out var oldRoute) &&
                !oldRoute.directChaser && Vector3.Distance(oldRoute.goal, o.goal) < 1f;
            if (!sameMission)
            {
                PlaytestRecorder.Record("route_assigned", m.name, m.transform.position, i == 0 ? "pressure" : "cutoff", orders[m], o.corners);
                if (i > 0) PlaytestRecorder.Record("support_route_estimate", m.name, m.transform.position,
                    "estimated_seconds=" + o.eta.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                    ";player_seconds=" + o.targetEta.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), o.goal);
            }
            if (i == 0) m.CommandPressure(knownPosition);
            else
            {
                if (o.detour) waypoints[m] = o.waypoint;
                if (!sameMission || m.CurrentState != MonsterAI.State.Intercept)
                    m.CommandTacticalRoute(o.corners, o.waypoint, o.detour);
            }
        }
        PlanPreparations(selected);
        summary = $"압박 1 + 동시 접근 {selected.Count - 1} · 통로 준비 {preparations.Count}";
    }

    private void PlanPreparations(List<MonsterRoutePlanner.Option> selected)
    {
        var previous = new Dictionary<MonsterAI, RouteInfo>(preparations);
        preparations.Clear();
        var occupied = new List<MonsterRoutePlanner.Option>(selected);
        var remaining = new List<MonsterAI>();
        foreach (var m in MonsterAI.activeMonsters)
            if (m != null && !team.Contains(m) && m.CanReceiveTactics && !m.IsGivingUp) remaining.Add(m);
        remaining.Sort((a,b) => Vector3.SqrMagnitude(a.transform.position-knownPosition).CompareTo(
            Vector3.SqrMagnitude(b.transform.position-knownPosition)));
        Vector3 preparationFocus = knownPosition;
        Vector3 strategicDirection = StrategicDirection;
        if (strategicDirection.sqrMagnitude > .1f && NavMesh.SamplePosition(knownPosition + strategicDirection * 12f,
            out var focusHit, 8f, NavMesh.AllAreas)) preparationFocus = focusHit.position;
        // Expand only one stale preparation per plan, oldest first. Movement, not replanning, resets the clock.
        MonsterAI retry = null;
        float oldest = float.PositiveInfinity;
        foreach (var m in remaining)
            if (preparationProgress.TryGetValue(m, out var p) && Time.time - p.progressedAt >= 2.5f &&
                Time.time >= p.retryAt && !m.HasCloseVisibleEncounter && Eligible(m) && p.progressedAt < oldest)
            { retry = m; oldest = p.progressedAt; }
        foreach (var m in remaining)
        {
            previous.TryGetValue(m, out var old);
            if (!preparationProgress.TryGetValue(m, out var progress))
            {
                progress = new PreparationProgress { position = m.transform.position, progressedAt = Time.time };
                preparationProgress[m] = progress;
            }
            bool expanded = m == retry;
            bool queueCooldown = blockedUntil.TryGetValue(m, out float until) && Time.time < until;
            MonsterRoutePlanner.Option option = null;
            // Preserve a valid future corridor across repeated shots. Reassign only after the player passes it,
            // it disconnects, or it conflicts with a currently active corridor.
            if (old != null && !m.HasCloseVisibleEncounter && !queueCooldown)
            {
                Vector3 towardGoal = Vector3.ProjectOnPlane(old.goal - knownPosition, Vector3.up);
                bool stillAhead = strategicDirection.sqrMagnitude < .1f || towardGoal.sqrMagnitude < 1f ||
                    Vector3.Dot(towardGoal.normalized, strategicDirection) >= CutoffPassedDot;
                var retainedPath = stillAhead && Vector3.Distance(old.goal, knownPosition) >= CutoffReachedDistance &&
                    Vector3.Distance(old.goal, knownPosition) <= 55f ? planner.Path(m, m.transform.position, old.goal) : null;
                if (retainedPath != null)
                {
                    var retained = MonsterRoutePlanner.Make(m, retainedPath, old.goal, false, true);
                    bool conflict = false;
                    foreach (var other in occupied)
                        if (Vector3.Distance(retained.goal, other.goal) < 7f || MonsterRoutePlanner.Conflict(retained, other, out _))
                        { conflict = true; break; }
                    if (!conflict) option = retained;
                }
            }
            // A visible close encounter must face the player, never turn away to a staging point.
            if (option == null && !m.HasCloseVisibleEncounter && !queueCooldown)
                option = planner.Prepare(m, preparationFocus, occupied, old != null ? old.goal : (Vector3?)null, expanded);
            progress.noRoute = option == null;
            if (expanded)
            {
                progress.retryAt = Time.time + 2.5f;
                PlaytestRecorder.Record("preparation_retry", m.name, m.transform.position,
                    option != null ? "independent_route_found" : "no_safe_alternative",
                    option != null ? option.goal : m.transform.position);
                // If the wider search fails, keep a still-valid normal route rather than interrupting it.
                if (option == null) option = planner.Prepare(m, preparationFocus, occupied, old != null ? old.goal : (Vector3?)null);
                progress.noRoute = option == null;
            }
            if (option == null)
            {
                // No independent route exists. Wait off the chase rather than queue behind it.
                var here = m.transform.position;
                option = MonsterRoutePlanner.Make(m, new[] { here, here }, here, false, false);
            }
            var route = new RouteInfo { corners = option.corners, goal = option.goal, waypoint = option.goal, colorIndex = 3 };
            preparations[m] = route; orders[m] = option.goal;
            occupied.Add(option);
            m.SetHuntSpeed(FarSpeed);
            m.CommandPreparation(option.corners);
            if (old == null || Vector3.Distance(old.goal, option.goal) > 1f)
            {
                PlaytestRecorder.Record("approach_preparation", m.name, m.transform.position,
                    option.length < .2f ? "no_separate_route_hold" : "walk_to_connected_approach", option.goal, option.corners);
            }
        }
        foreach (var m in previous.Keys)
            if (!preparations.ContainsKey(m)) { preparationProgress.Remove(m); }
    }

    private void CheckPreparationProgress()
    {
        if (!hunting) return;
        foreach (var pair in preparationProgress)
        {
            var m = pair.Key; var p = pair.Value;
            if (m == null || m.CurrentState != MonsterAI.State.Prepare || m.IsInStun || m.IsTraversingLink)
            { p.progressedAt = Time.time; continue; }
            if (Vector3.Distance(m.transform.position, p.position) >= .75f)
            { p.position = m.transform.position; p.progressedAt = Time.time; }
            string status = p.noRoute ? "no_independent_route" : m.PreparationAtGoal ? "at_staging_point" :
                Time.time - p.progressedAt >= 1.5f ? "movement_stalled" : "moving";
            if (status != p.status)
            {
                p.status = status;
                PlaytestRecorder.Record("preparation_status", m.name, m.transform.position, status);
            }
            // The regular bounded planning tick handles retries; waiting agents must not create a replan storm.
        }
    }

    private void CheckFollowing()
    {
        if (!hunting) return;
        var movers = new List<MonsterAI>(team);
        foreach (var m in preparations.Keys) if (!movers.Contains(m)) movers.Add(m);
        foreach (var rear in movers)
        {
            if (rear == null || rear == pressure || rear.IsInStun || rear.IsTraversingLink) continue;
            bool following = false;
            foreach (var front in movers)
            {
                if (front == rear || front == null || front.IsInStun || front.IsTraversingLink) continue;
                Vector3 gap = front.transform.position - rear.transform.position; gap.y = 0;
                Vector3 v = rear.PlanarVelocity;
                float followDistance = Settings != null ? Mathf.Max(1f, Settings.followDistance) : 7f;
                Vector3 frontVelocity = front.PlanarVelocity;
                bool sameFlow = frontVelocity.magnitude < 1.5f ||
                    Vector3.Dot(v.normalized, frontVelocity.normalized) > .45f;
                if (gap.magnitude < followDistance * 1.35f && gap.magnitude > .5f && v.magnitude > 1.5f &&
                    Vector3.Dot(v.normalized, gap.normalized) > .6f && sameFlow &&
                    !NavMesh.Raycast(rear.transform.position, front.transform.position, out _, rear.NavigationFilter))
                { following = true; break; }
            }
            if (!following) { followSince.Remove(rear); continue; }
            if (!followSince.TryGetValue(rear, out float since)) { followSince[rear] = Time.time; continue; }
            float hold = Settings != null ? Mathf.Max(.35f, Settings.followHoldTime * .6f) : .6f;
            if (Time.time - since < hold) continue;
            PlaytestRecorder.Record("follow_break", rear.name, rear.transform.position, "same_corridor_queue");
            var here = rear.transform.position;
            rear.CommandPreparation(new[] { here, here });
            if (preparations.ContainsKey(rear))
                preparations[rear] = new RouteInfo { corners = new[] { here, here }, goal = here, waypoint = here, colorIndex = 3 };
            blockedUntil[rear] = Time.time + 1.2f; followSince.Remove(rear);
            followBreaks++; nextPlan = 0;
        }
    }


    private float HomePathLength(MonsterAI m, Transform home)
    {
        if (home == null) return float.PositiveInfinity;
        var agent = m.GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return float.PositiveInfinity;
        var path = new NavMeshPath();
        if (!NavMesh.SamplePosition(home.position, out var hit, 3f, m.NavigationFilter) ||
            !NavMesh.CalculatePath(m.transform.position, hit.position, m.NavigationFilter, path) ||
            path.status != NavMeshPathStatus.PathComplete) return float.PositiveInfinity;
        float length = 0;
        var points = path.corners;
        for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }
    private bool CanExchangeHome(MonsterAI m) => m != null && m.isActiveAndEnabled &&
        m.role == MonsterAI.MonsterRole.Zone_Defender && !m.useGlobalNavMesh && m.zoneCenter != null &&
        !m.IsInStun && !m.IsTraversingLink && !m.HasCloseVisibleEncounter &&
        (!zoneSwapUntil.TryGetValue(m, out float until) || Time.time >= until);

    private void TrySwapReturnZones()
    {
        foreach (var a in MonsterAI.activeMonsters)
        {
            if (!CanExchangeHome(a) || a.CurrentState != MonsterAI.State.Return || team.Contains(a)) continue;
            foreach (var b in MonsterAI.activeMonsters)
            {
                if (a == b || !CanExchangeHome(b) || a.zoneCenter == b.zoneCenter ||
                    Vector3.Distance(b.transform.position, b.zoneCenter.position) <= b.zoneRadius ||
                    Vector3.Distance(a.transform.position, b.zoneCenter.position) > 12f) continue;
                bool covered = false;
                foreach (var other in MonsterAI.activeMonsters)
                    if (other != null && other != a && other != b && other.role == MonsterAI.MonsterRole.Zone_Defender &&
                        (other.zoneCenter == a.zoneCenter || other.zoneCenter == b.zoneCenter ||
                        (other.CurrentState == MonsterAI.State.Patrol && Vector3.Distance(other.transform.position, b.zoneCenter.position) < b.zoneRadius)))
                    { covered = true; break; }
                if (covered) continue;
                float aa = HomePathLength(a, a.zoneCenter), bb = HomePathLength(b, b.zoneCenter);
                float ab = HomePathLength(a, b.zoneCenter), ba = HomePathLength(b, a.zoneCenter);
                if (float.IsInfinity(aa) || float.IsInfinity(bb) || float.IsInfinity(ab) || float.IsInfinity(ba) ||
                    aa - ab < 10f || aa + bb - ab - ba < 15f) continue;
                var home = a.zoneCenter; float radius = a.zoneRadius;
                a.zoneCenter = b.zoneCenter; a.zoneRadius = b.zoneRadius;
                b.zoneCenter = home; b.zoneRadius = radius;
                zoneSwapUntil[a] = zoneSwapUntil[b] = Time.time + 20f;
                a.RefreshAssignedHome(); b.RefreshAssignedHome();
                PlaytestRecorder.Record("zone_swap", a.name, a.zoneCenter.position, "with=" + b.name, b.zoneCenter.position);
                return;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!hunting) return;
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(knownPosition, MonsterRoutePlanner.Convergence);
        foreach (var pair in routes)
        {
            Gizmos.color = pair.Value.directChaser ? Color.red : pair.Value.colorIndex == 1 ? Color.cyan : Color.magenta;
            var points = pair.Value.corners;
            for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1] + Vector3.up, points[i] + Vector3.up);
        }
    }
}
