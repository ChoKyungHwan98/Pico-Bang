using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 조정자(Director) — 몬스터가 아니라 뒤에서 몬스터들이 한곳에 몰리지 않게 조정하는 보이지 않는 시스템.
///
/// 설계 목표 (기획 2026-09-12):
///   "플레이어를 최단거리로 잡는 AI"가 아니라 <b>"플레이어가 갈 수 있는 안전한 길을 점점 줄이는 AI"</b>.
///
/// 핵심 규칙
///   1. <b>조정자는 플레이어를 직접 보지 않는다.</b> 몬스터가 보고한 목격 기록만 안다.
///      보고 있는 동안에는 기록이 계속 갱신되고, 놓치는 순간 마지막 값으로 멈춘다.
///      (소리는 목격 기록이 아니다 — "여기서 소리가 났다"일 뿐)
///   2. 전역이든 구역이든 <b>누가 발견해도 포위가 시작된다.</b>
///      구역 몬스터 발견 → 작은 포위 / 전역 몬스터 발견 → 큰 포위 (사냥 중 전역이 보면 격상)
///   3. <b>직접 추격은 허가제, 최대 N마리</b>(기본 2). 가득 차 있으면 추격에 들어가기 전에 곧바로 차단으로 보낸다.
///   4. 차단 = <b>아직 막히지 않은 길</b>에 서 있기. 마지막 목격 위치에서 8방향으로 뻗은 길 중
///      추격자·다른 차단 몬스터가 오는 방향과 먼 길을 고른다. 복도에서 정면으로 달리면 복도 반대쪽 끝(뒤)이 뽑힌다.
///   5. 놓치면 마지막 목격 위치 한 점으로 몰리지 않게 <b>수색 지점을 흩어놓는다</b>(이동 방향 쪽을 더 의심).
///   6. 마지막 목격 후 일정 시간이 지나면 사냥 종료 → 참여자 해제(복귀).
///
/// 별도 역할(Flanker/Blocker) 시스템은 없다. 몬스터 FSM에 명령(수색·차단·해제)과 목적지만 준다.
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

    private enum OrderKind { Cut, Search }

    /// <summary>차단 후보 한 길. AI 테스트 씬에 그대로 그린다.</summary>
    public struct CutCandidate
    {
        public Vector3 point;       // 그 방향으로 바닥을 따라 가다 막히는 곳(최대 거리)
        public Vector3 direction;   // 목격 위치 → point
        public float reach;         // 목격 위치에서 point까지 거리
        public float score;
        public bool rejected;       // 너무 짧게 막혀 버려진 방향(벽·막다른 곳)
        public bool chosen;
    }

    // ── 목격 기록 (공유 정보) ──
    private bool hasSighting;
    private Vector3 sightPosition;
    private float sightTime;
    private Vector3 sightVelocity;        // 목격이 이어지는 동안만 갱신, 끊기면 동결
    private MonsterAI sightSpotter;

    // ── 사냥 ──
    private bool hunting;
    private bool largeHunt;
    private bool searchIssued;            // 이번 시야 상실에 대해 수색 분산을 이미 내렸는가
    private float nextLayoutTime;
    private float nextCoordinateTime;

    private readonly Dictionary<MonsterAI, Vector3> orderPoints = new Dictionary<MonsterAI, Vector3>();
    private readonly Dictionary<MonsterAI, OrderKind> orderKinds = new Dictionary<MonsterAI, OrderKind>();
    private readonly HashSet<MonsterAI> demoted = new HashSet<MonsterAI>();   // 추격 인원이 차서 차단으로 보낸 개체
    private readonly List<MonsterAI> chasers = new List<MonsterAI>();
    private readonly List<CutCandidate> lastCandidates = new List<CutCandidate>();
    private NavMeshPath pathBuffer;

    private const float CoordinateInterval = 0.25f;
    private const float ContinuousSightWindow = 0.3f;   // 이 안에 다시 보고되면 "이어진 목격"으로 보고 방향을 갱신
    private const float MovingSpeedThreshold = 1.5f;    // 이보다 느리면 "멈춰 있음"

    // ── AI 테스트 씬 표시용 ──
    public bool IsHunting => hunting;
    public bool IsLargeHunt => largeHunt;
    public bool HasSighting => hasSighting;
    public Vector3 SightingPosition => sightPosition;
    public float SightingAge => Time.time - sightTime;
    public Vector3 SightingDirection => (sightVelocity.magnitude > MovingSpeedThreshold) ? sightVelocity.normalized : Vector3.zero;
    public string SightingSpotterName => sightSpotter != null ? sightSpotter.name : "-";
    public int DebugChaserCount => chasers.Count;
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugOrderPoints => orderPoints;
    public IReadOnlyList<CutCandidate> DebugCutCandidates => lastCandidates;

    public string DebugOrderLabel(MonsterAI m)
    {
        if (m == null || !orderKinds.TryGetValue(m, out OrderKind k)) { return ""; }
        return k == OrderKind.Cut ? "차단" : "수색";
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnApplicationQuit() { isQuitting = true; }

    /// <summary>게임 재시작 등으로 사냥을 강제 종료하고 목격 기록도 지운다.</summary>
    public void AbortHunt()
    {
        EndHunt("게임 초기화");
        hasSighting = false;
        sightVelocity = Vector3.zero;
        sightSpotter = null;
        lastCandidates.Clear();
    }

    // ────────────────────────────────────────────────
    //  목격 보고 — 조정자가 플레이어에 대해 아는 유일한 경로
    // ────────────────────────────────────────────────

    /// <summary>몬스터가 플레이어를 시야로 보고 있는 동안 매 프레임 호출한다. 추격 허가와 무관하게 정보는 공유된다.</summary>
    public void ReportSighting(MonsterAI spotter, Vector3 seenPosition)
    {
        if (spotter == null) { return; }
        float now = Time.time;

        if (hasSighting && now - sightTime <= ContinuousSightWindow)
        {
            float dt = now - sightTime;
            if (dt > 0.0001f)   // 같은 프레임에 여러 마리가 보고하면 dt가 0 — 방향 갱신 생략
            {
                Vector3 v = seenPosition - sightPosition;
                v.y = 0f;
                sightVelocity = Vector3.Lerp(sightVelocity, v / dt, 0.25f);
            }
        }
        else
        {
            // 목격이 끊겼다가 새로 시작됨 — 이전 방향은 믿지 않는다
            sightVelocity = Vector3.zero;
        }

        hasSighting = true;
        sightPosition = seenPosition;
        sightTime = now;
        sightSpotter = spotter;
        searchIssued = false;

        bool byGlobal = spotter.role == MonsterAI.MonsterRole.Global_Stalker;
        if (!hunting)
        {
            hunting = true;
            largeHunt = byGlobal;
            nextLayoutTime = 0f;
            Log($"<color=orange><b>[사냥 시작]</b></color> {spotter.name} 발견 → {(largeHunt ? "큰" : "작은")} 포위");
        }
        else if (byGlobal && !largeHunt)
        {
            largeHunt = true;
            nextLayoutTime = 0f;
            Log("<color=orange><b>[포위 격상]</b></color> 전역 몬스터 발견 → 큰 포위");
        }
    }

    // ────────────────────────────────────────────────
    //  추격 허가
    // ────────────────────────────────────────────────

    /// <summary>
    /// 몬스터가 플레이어를 보고 추격에 들어가려 할 때 먼저 묻는다. 허가되면 true.
    ///   - 자리가 남아 있으면 허가
    ///   - 전역 몬스터는 항상 허가 — 가득 차 있으면 가장 먼 구역 추격자를 즉시 차단으로 뺀다
    ///   - 차단 대기 중 덮치려는 개체: 가장 먼 구역 추격자보다 swapDistanceMargin 이상 가까울 때만 교대
    ///   - 그 밖(순찰·수색·복귀 중 발견): 거부하고 곧바로 아직 안 막힌 길로 차단 명령 — 추격을 거치지 않으니 한 순간도 3마리가 되지 않는다
    /// </summary>
    public bool RequestChase(MonsterAI m)
    {
        if (m == null) { return false; }
        RefreshChasers();

        int others = 0;
        foreach (MonsterAI c in chasers) { if (c != m) { others++; } }
        if (others < MaxChasers) { return true; }

        MonsterAI farthest = FarthestZoneChaser(m);

        if (m.role == MonsterAI.MonsterRole.Global_Stalker)
        {
            if (farthest != null) { SendToCut(farthest, "전역 몬스터에게 자리 양보"); }
            return true;
        }

        if (m.CurrentState == MonsterAI.State.Intercept)
        {
            if (farthest != null && farthest.CanBeDemoted
                && HorizontalDistance(m.transform.position, sightPosition) + SwapMargin
                   < HorizontalDistance(farthest.transform.position, sightPosition))
            {
                SendToCut(farthest, "더 가까운 차단 몬스터와 교대");
                return true;
            }
            return false;   // 자리를 지킨다
        }

        // 순찰·수색·복귀 중에 발견했는데 자리가 없다 → 곧바로 빈 길로
        SendToCut(m, "추격 인원 가득 — 발견 즉시 차단");
        return false;
    }

    private void Update()
    {
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) { return; }

        float now = Time.time;
        if (now >= nextCoordinateTime)
        {
            nextCoordinateTime = now + CoordinateInterval;
            CoordinateChasers();
        }

        if (!hunting) { return; }

        float age = now - sightTime;
        if (age > HuntMemory)
        {
            EndHunt("놓침");
            return;
        }

        if (age <= LostSightGrace)
        {
            if (now >= nextLayoutTime)
            {
                nextLayoutTime = now + LayoutRefresh;
                LayoutCutters();
            }
        }
        else if (!searchIssued)
        {
            // 놓쳤다: 한 번만 수색 지점을 흩어놓는다. 이후 다시 보이기 전까지 배치는 고정(모르는 걸 추측하지 않음)
            searchIssued = true;
            SpreadSearch();
        }
    }

    // ────────────────────────────────────────────────
    //  추격 인원 안전장치
    // ────────────────────────────────────────────────

    /// <summary>
    /// 허가제를 거치지 않고 추격에 들어간 경우(기절에서 풀린 직후 등)를 위한 안전장치.
    /// 최대치를 넘으면 가장 먼 구역 추격자를 차단으로 뺀다. 우선순위: 전역 → 목격 위치에 가까운 순.
    /// </summary>
    private void CoordinateChasers()
    {
        RefreshChasers();
        demoted.RemoveWhere(m => m == null || m.CurrentState == MonsterAI.State.Chase);

        if (!hasSighting) { return; }
        int guard = 0;
        while (chasers.Count > MaxChasers && guard++ < 5)
        {
            MonsterAI farthest = FarthestZoneChaser(null);
            if (farthest == null || !farthest.CanBeDemoted) { break; }
            SendToCut(farthest, $"추격 {chasers.Count}마리 초과");
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
            float d = HorizontalDistance(c.transform.position, sightPosition);
            if (d > bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    /// <summary>한 마리를 아직 안 막힌 길 중 가장 좋은 곳으로 차단 보낸다. 쓸 만한 길이 없으면 제자리에서 대기.</summary>
    private void SendToCut(MonsterAI m, string reason)
    {
        List<Vector3> pts = SelectCutPoints(1, m);
        Vector3 point = pts.Count > 0 ? pts[0] : m.transform.position;
        m.CommandAmbush(point);
        SetOrder(m, point, OrderKind.Cut);
        demoted.Add(m);
        RefreshChasers();
        Log($"<color=magenta><b>[차단 전환]</b></color> {m.name} — {reason}");
    }

    // ────────────────────────────────────────────────
    //  보이는 동안: 빈 길 차단
    // ────────────────────────────────────────────────

    private void LayoutCutters()
    {
        RefreshChasers();
        int wanted = (largeHunt ? LargeCutters : SmallCutters) + demoted.Count;
        List<Vector3> points = SelectCutPoints(wanted, null);

        List<MonsterAI> candidates = new List<MonsterAI>();
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.IsAvailableForOrders) { candidates.Add(m); }
        }

        HashSet<MonsterAI> assigned = new HashSet<MonsterAI>();
        foreach (KeyValuePair<MonsterAI, Vector3> pair in AssignGreedy(candidates, points, avoidCrossing: true))
        {
            pair.Key.CommandAmbush(pair.Value);
            SetOrder(pair.Key, pair.Value, OrderKind.Cut);
            assigned.Add(pair.Key);
        }

        // 이번 배치에서 빠진 차단 개체는 풀어준다 (직접 쫓는 중이면 그대로)
        List<MonsterAI> stale = new List<MonsterAI>();
        foreach (KeyValuePair<MonsterAI, OrderKind> kv in orderKinds)
        {
            if (kv.Value == OrderKind.Cut && !assigned.Contains(kv.Key)) { stale.Add(kv.Key); }
        }
        foreach (MonsterAI m in stale)
        {
            if (m != null && m.CurrentState != MonsterAI.State.Chase) { m.CommandRelease(); }
            ClearOrder(m);
            demoted.Remove(m);
        }
    }

    /// <summary>
    /// 차단 지점 고르기 — "아직 막히지 않은 길".
    ///
    /// 1) 마지막 목격 위치 P에서 8방향으로 바닥을 따라 최대 거리까지 뻗어 본다. 막히는 곳이 후보.
    /// 2) 최소 거리도 못 가고 막히는 방향은 버린다(벽·막다른 곳). 서로 가까운 후보는 하나로 합친다.
    ///    → 복도 한가운데서는 복도 양 끝 두 방향만 남는다.
    /// 3) 점수: 이미 누가 오고 있는 방향(추격자 위치, 다른 차단 몬스터의 지점)과 각도가 멀수록 +,
    ///    마지막 이동 방향 쪽이면 보조 +, 추격자와 너무 가까우면 −.
    /// 4) 가장 좋은 곳부터 하나씩 고르고, 고른 방향은 "막힘"으로 쳐서 다음 선택에 반영한다.
    /// 전부 목격 기록으로만 계산한다 — 진짜 현재 위치는 쓰지 않는다.
    /// </summary>
    private List<Vector3> SelectCutPoints(int count, MonsterAI exclude)
    {
        List<Vector3> result = new List<Vector3>();
        lastCandidates.Clear();
        if (count <= 0 || !hasSighting) { return result; }

        Vector3 p = sightPosition;
        float maxDist = CandidateMaxDistance;
        float minDist = CandidateMinDistance;
        float merge = CandidateMergeDistance;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 45f * i, 0f) * Vector3.forward;
            Vector3 point = ClampAlongNav(p, p + dir * maxDist);
            Vector3 flat = point - p;
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
                // 가까운 후보가 이미 있으면 더 멀리 뻗은 쪽만 남긴다
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

        // 이미 막힌 방향: 추격자가 오는 방향 + 다른 차단 몬스터가 맡은 지점 방향
        List<Vector3> covered = new List<Vector3>();
        foreach (MonsterAI ch in chasers)
        {
            if (ch == null || ch == exclude) { continue; }
            Vector3 v = ch.transform.position - p;
            v.y = 0f;
            if (v.sqrMagnitude > 0.25f) { covered.Add(v.normalized); }
        }
        foreach (KeyValuePair<MonsterAI, OrderKind> kv in orderKinds)
        {
            if (kv.Value != OrderKind.Cut || kv.Key == exclude || !orderPoints.TryGetValue(kv.Key, out Vector3 op)) { continue; }
            if (exclude == null) { continue; }   // 전체 재배치 때는 기존 차단 지점을 막힘으로 치지 않는다(다시 고르는 중)
            Vector3 v = op - p;
            v.y = 0f;
            if (v.sqrMagnitude > 0.25f) { covered.Add(v.normalized); }
        }

        Vector3 moveDir = SightingDirection;
        float avoid = AvoidChaserRadius;

        for (int pick = 0; pick < count; pick++)
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int k = 0; k < lastCandidates.Count; k++)
            {
                CutCandidate c = lastCandidates[k];
                if (c.rejected || c.chosen) { continue; }

                float minAngle = 180f;
                foreach (Vector3 cov in covered) { minAngle = Mathf.Min(minAngle, Vector3.Angle(c.direction, cov)); }
                float score = minAngle / 180f;
                if (moveDir.sqrMagnitude > 0.01f) { score += (Vector3.Dot(c.direction, moveDir) + 1f) * 0.5f * 0.35f; }
                foreach (MonsterAI ch in chasers)
                {
                    if (ch != null && ch != exclude && HorizontalDistance(ch.transform.position, c.point) < avoid) { score -= 1f; }
                }
                c.score = score;
                lastCandidates[k] = c;
                if (score > bestScore) { bestScore = score; best = k; }
            }
            if (best < 0) { break; }

            CutCandidate chosen = lastCandidates[best];
            chosen.chosen = true;
            lastCandidates[best] = chosen;
            covered.Add(chosen.direction);
            result.Add(chosen.point);
        }
        return result;
    }

    // ────────────────────────────────────────────────
    //  놓친 뒤: 수색 분산
    // ────────────────────────────────────────────────

    /// <summary>
    /// 사냥 참여자(추격하다 놓친 개체 + 차단·수색 명령을 받은 개체)에게 서로 다른 수색 지점을 준다.
    /// 1번은 마지막 목격 위치, 나머지는 이동 방향 쪽으로 치우친 부채꼴. 방향을 모르면 둘레에 고르게.
    /// 플레이어가 반대로 도망쳤다면 AI는 모른다 — 의도된 "따돌림".
    /// </summary>
    private void SpreadSearch()
    {
        List<MonsterAI> participants = new List<MonsterAI>();
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m == null || m.IsInStun) { continue; }
            if (m.CurrentState == MonsterAI.State.Chase || orderKinds.ContainsKey(m)) { participants.Add(m); }
        }
        if (participants.Count == 0) { return; }

        Vector3 p = sightPosition;
        Vector3 d = KnownDirection();
        float near = SearchNear;
        float far = SearchFar;

        List<Vector3> raw = new List<Vector3> { p };
        if (d.sqrMagnitude > 0.01f)
        {
            raw.Add(p + d * near);
            raw.Add(p + Quaternion.Euler(0f, 40f, 0f) * d * near * 1.25f);
            raw.Add(p + Quaternion.Euler(0f, -40f, 0f) * d * near * 1.25f);
            raw.Add(p + d * far);
            raw.Add(p + Quaternion.Euler(0f, 90f, 0f) * d * near);
            raw.Add(p + Quaternion.Euler(0f, -90f, 0f) * d * near);
        }
        else
        {
            for (int i = 0; i < 6; i++) { raw.Add(p + Quaternion.Euler(0f, 60f * i, 0f) * Vector3.forward * near); }
        }

        List<Vector3> points = new List<Vector3>();
        foreach (Vector3 r in raw)
        {
            if (points.Count >= participants.Count) { break; }
            if (!NavMesh.SamplePosition(r, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { continue; }
            bool tooClose = false;
            foreach (Vector3 q in points) { if ((q - hit.position).sqrMagnitude < 9f) { tooClose = true; break; } }
            if (!tooClose) { points.Add(hit.position); }
        }

        HashSet<MonsterAI> assigned = new HashSet<MonsterAI>();
        foreach (KeyValuePair<MonsterAI, Vector3> pair in AssignGreedy(participants, points, avoidCrossing: false))
        {
            pair.Key.CommandSearch(pair.Value);
            SetOrder(pair.Key, pair.Value, OrderKind.Search);
            assigned.Add(pair.Key);
        }
        foreach (MonsterAI m in participants)
        {
            if (!assigned.Contains(m)) { ClearOrder(m); }
        }
        demoted.Clear();

        Log($"<color=yellow><b>[놓침 → 수색 분산]</b></color> {points.Count}곳");
    }

    private void EndHunt(string reason)
    {
        if (hunting) { Log($"<color=grey><b>[사냥 종료]</b></color> {reason}"); }

        foreach (MonsterAI m in new List<MonsterAI>(orderKinds.Keys))
        {
            if (m != null) { m.CommandRelease(); }
        }
        orderPoints.Clear();
        orderKinds.Clear();
        demoted.Clear();
        hunting = false;
        largeHunt = false;
        searchIssued = false;
    }

    // ────────────────────────────────────────────────
    //  보조
    // ────────────────────────────────────────────────

    /// <summary>
    /// 마지막으로 알던 이동 방향. 목격 당시 움직이고 있었으면 그 방향,
    /// 멈춰 있었으면 쫓는 개체들의 반대편(도망칠 쪽으로 추정), 그것도 없으면 방향 없음.
    /// </summary>
    private Vector3 KnownDirection()
    {
        if (sightVelocity.magnitude > MovingSpeedThreshold) { return sightVelocity.normalized; }

        Vector3 away = Vector3.zero;
        foreach (MonsterAI c in chasers)
        {
            if (c == null) { continue; }
            Vector3 v = sightPosition - c.transform.position;
            v.y = 0f;
            if (v.sqrMagnitude > 0.01f) { away += v.normalized; }
        }
        return (away.sqrMagnitude > 0.01f) ? away.normalized : Vector3.zero;
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

    /// <summary>
    /// 몬스터 × 지점 짝을 비용이 싼 순서로 확정한다.
    /// 비용 = <b>실제 경로 길이</b> + (차단일 때) 경로가 목격 위치 근처를 지나면 가산점
    ///        − 이미 같은 명령을 받고 있던 개체 보너스(자리 교체로 우왕좌왕 방지).
    /// 직선거리로 재면 벽 너머 가까운 지점을 골라 실제로는 플레이어 뒤를 따라 도는 같은 길이 된다.
    /// </summary>
    private List<KeyValuePair<MonsterAI, Vector3>> AssignGreedy(List<MonsterAI> monsters, List<Vector3> points, bool avoidCrossing)
    {
        List<KeyValuePair<MonsterAI, Vector3>> result = new List<KeyValuePair<MonsterAI, Vector3>>();
        int n = monsters.Count;
        int s = points.Count;
        if (n == 0 || s == 0) { return result; }

        float[,] cost = new float[n, s];
        for (int mi = 0; mi < n; mi++)
        {
            bool continuing = orderKinds.TryGetValue(monsters[mi], out OrderKind k) && k == (avoidCrossing ? OrderKind.Cut : OrderKind.Search);
            for (int pi = 0; pi < s; pi++)
            {
                cost[mi, pi] = PathCost(monsters[mi].transform.position, points[pi], avoidCrossing) - (continuing ? 5f : 0f);
            }
        }

        bool[] mTaken = new bool[n];
        bool[] pTaken = new bool[s];
        for (int step = 0; step < Mathf.Min(n, s); step++)
        {
            float best = float.MaxValue;
            int bm = -1, bp = -1;
            for (int mi = 0; mi < n; mi++)
            {
                if (mTaken[mi]) { continue; }
                for (int pi = 0; pi < s; pi++)
                {
                    if (pTaken[pi]) { continue; }
                    if (cost[mi, pi] < best) { best = cost[mi, pi]; bm = mi; bp = pi; }
                }
            }
            if (bm < 0) { break; }
            mTaken[bm] = true;
            pTaken[bp] = true;
            result.Add(new KeyValuePair<MonsterAI, Vector3>(monsters[bm], points[bp]));
        }
        return result;
    }

    /// <summary>실제 경로 길이 + 경로의 각 구간이 목격 위치 가까이 지나가면 가산점. 경로를 못 구하면 직선거리 + 큰 벌점.</summary>
    private float PathCost(Vector3 from, Vector3 to, bool avoidCrossing)
    {
        if (pathBuffer == null) { pathBuffer = new NavMeshPath(); }
        Vector3 start = NavMesh.SamplePosition(from, out NavMeshHit sh, 3f, NavMesh.AllAreas) ? sh.position : from;
        if (!NavMesh.CalculatePath(start, to, NavMesh.AllAreas, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete)
        {
            return Vector3.Distance(from, to) + 100f;
        }

        Vector3[] corners = pathBuffer.corners;
        float length = 0f;
        float nearest = float.MaxValue;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
            if (avoidCrossing) { nearest = Mathf.Min(nearest, DistancePointToSegment(sightPosition, corners[i - 1], corners[i])); }
        }
        if (avoidCrossing && corners.Length < 2) { nearest = HorizontalDistance(start, sightPosition); }

        float avoid = CrossingRadius;
        if (avoidCrossing && avoid > 0f && nearest < avoid)
        {
            length += CrossingPenaltyValue * (1f - nearest / avoid);
        }
        return length;
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

    private void SetOrder(MonsterAI m, Vector3 point, OrderKind kind)
    {
        orderPoints[m] = point;
        orderKinds[m] = kind;
    }

    private void ClearOrder(MonsterAI m)
    {
        orderPoints.Remove(m);
        orderKinds.Remove(m);
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
    private int SmallCutters { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0, g.smallHuntCutters) : 1; } }
    private int LargeCutters { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0, g.largeHuntCutters) : 2; } }
    private float CandidateMaxDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMaxDistance : 18f; } }
    private float CandidateMinDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMinDistance : 6f; } }
    private float CandidateMergeDistance { get { MonsterAI g = FindGlobal(); return g != null ? g.cutCandidateMergeDistance : 8f; } }
    private float AvoidChaserRadius { get { MonsterAI g = FindGlobal(); return g != null ? g.cutAvoidChaserRadius : 10f; } }
    private float SwapMargin { get { MonsterAI g = FindGlobal(); return g != null ? g.swapDistanceMargin : 3f; } }
    private float CrossingRadius { get { MonsterAI g = FindGlobal(); return g != null ? g.crossingAvoidRadius : 6f; } }
    private float CrossingPenaltyValue { get { MonsterAI g = FindGlobal(); return g != null ? g.crossingPenalty : 25f; } }
    private float SearchNear { get { MonsterAI g = FindGlobal(); return g != null ? g.searchSpreadNear : 8f; } }
    private float SearchFar { get { MonsterAI g = FindGlobal(); return g != null ? g.searchSpreadFar : 18f; } }
    private float HuntMemory { get { MonsterAI g = FindGlobal(); return g != null ? g.huntMemory : 8f; } }
    private float LayoutRefresh { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.2f, g.layoutRefreshInterval) : 1f; } }
    private float LostSightGrace { get { MonsterAI g = FindGlobal(); return g != null ? Mathf.Max(0.05f, g.lostSightGrace) : 0.5f; } }

    private void OnDrawGizmos()
    {
        if (hasSighting)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sightPosition, 1.5f);
            Gizmos.DrawLine(sightPosition, sightPosition + SightingDirection * 6f);
        }
        foreach (KeyValuePair<MonsterAI, Vector3> pair in orderPoints)
        {
            Gizmos.color = orderKinds.TryGetValue(pair.Key, out OrderKind k) && k == OrderKind.Cut ? Color.magenta : Color.yellow;
            Gizmos.DrawWireSphere(pair.Value, 1f);
        }
    }
}
