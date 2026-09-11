using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 감독 — 두 개의 뇌 중 위쪽. 전역 몬스터의 "전두지휘"를 맡는다.
///
/// 참고작: 에일리언: 아이솔레이션의 감독 AI / 에일리언 본체 분리.
/// 감독은 플레이어 위치를 안다. 그러나 구역 몬스터에게 정답을 주지 않는다.
/// 플레이어 근처의 <b>흐린 지점</b>으로 보낼 뿐이고, 마지막 발견은 각 몬스터가 제 눈과 귀로 한다.
///
/// 기획 (2026-09-11):
///   - 구역 몬스터 4마리는 전역 몬스터의 <b>눈먼 수족</b>이다. 스스로 머리를 쓰지 않고 시킨 대로 움직여서 읽힌다.
///     (소울라이크에서 가드만 하면 무조건 잡기를 쓰는 적이 재미없는 것처럼, 완벽히 대응하는 적은 재미가 없다)
///   - 전역 몬스터가 제보하면 <b>제보 지점에서 가장 가까운 구역 몬스터 2마리</b>만 간다. 나머지 2마리는 순찰 유지.
///   - <b>동시에 쫓는 건 최대 2마리.</b> 셋째부터 줄줄이 따라가게 되면 전역 1 + 가장 가까운 구역 1만 쫓고,
///     나머지는 플레이어가 가려는 쪽으로 앞질러 가서 매복한다 — 비엔나 소시지 행렬 방지.
///
/// 씬에 배치할 필요 없음 — 첫 호출 시 자동 생성된다.
/// 수치는 전역 몬스터의 인스펙터 값을 따른다(없으면 기본값).
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

    /// <summary>현재 무리 호출(사냥)이 진행 중인가.</summary>
    public bool IsHunting => hunting;

    // ── 사냥(제보 → 파견) ──
    private bool hunting;
    private MonsterAI caller;             // 제보한 전역 몬스터
    private Transform prey;
    private float lastSeenTime;
    private float nextDispatchTime;
    private readonly List<MonsterAI> squad = new List<MonsterAI>();   // 이번 사냥에 파견된 구역 몬스터
    private readonly Dictionary<MonsterAI, float> bearings = new Dictionary<MonsterAI, float>();
    private readonly Dictionary<MonsterAI, Vector3> lastSent = new Dictionary<MonsterAI, Vector3>();

    // ── 추격 인원 조율 ──
    private Transform player;
    private Vector3 playerPrevPos;
    private Vector3 playerVelocity;       // 부드럽게 만든 속도
    private float nextCoordinateTime;
    private float nextInterceptRefresh;
    private float noChaserSince = -1f;
    private readonly List<MonsterAI> chasers = new List<MonsterAI>();

    private const float CoordinateInterval = 0.25f;

    // AI 테스트 씬 표시용
    public IReadOnlyDictionary<MonsterAI, Vector3> DebugDispatchPoints => lastSent;
    public IReadOnlyList<MonsterAI> DebugSquad => squad;
    public Vector3 DebugPlayerVelocity => playerVelocity;
    public int DebugChaserCount => chasers.Count;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnApplicationQuit() { isQuitting = true; }

    /// <summary>게임 재시작 등으로 호출을 강제 종료한다.</summary>
    public void AbortHunt()
    {
        if (hunting) { EndHunt("게임 초기화"); }
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null) { m.EndIntercept(); }
        }
    }

    /// <summary>
    /// 전역 몬스터가 플레이어를 보고 있는 동안 매 프레임 호출한다.
    /// 최초 호출에서 사냥이 시작되고(파견 2마리 선정), 이후 호출은 "아직 보고 있다"는 갱신 신호로 쓰인다.
    /// </summary>
    public void ReportSpotted(MonsterAI spotter, Transform target)
    {
        if (spotter == null || target == null) { return; }

        lastSeenTime = Time.time;
        prey = target;

        if (!hunting)
        {
            hunting = true;
            caller = spotter;
            nextDispatchTime = 0f;
            SelectSquad(target.position);
            AssignBearings();

            if (spotter.showDebugLog)
            {
                Debug.Log($"<color=orange><b>[제보]</b></color> {spotter.name} → 파견 {squad.Count}마리");
            }
        }
    }

    /// <summary>AI 테스트 씬: 전역 몬스터가 지금 플레이어를 본 것처럼 제보를 강제한다.</summary>
    public void DebugForceReport(Transform target)
    {
        MonsterAI global = FindGlobal();
        if (global == null || target == null) { return; }
        if (hunting) { EndHunt("강제 제보로 재시작"); }
        ReportSpotted(global, target);
    }

    private void Update()
    {
        if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) { return; }

        TrackPlayer();

        if (Time.time >= nextCoordinateTime)
        {
            nextCoordinateTime = Time.time + CoordinateInterval;
            CoordinateChasers();
        }

        UpdateHunt();
    }

    // ────────────────────────────────────────────────
    //  사냥: 제보 → 가장 가까운 2마리 파견
    // ────────────────────────────────────────────────

    private void UpdateHunt()
    {
        if (!hunting) { return; }
        if (prey == null || caller == null) { EndHunt("대상 소실"); return; }

        if (Time.time - lastSeenTime > caller.callMemory)
        {
            EndHunt("놓침");
            return;
        }

        if (Time.time < nextDispatchTime) { return; }
        nextDispatchTime = Time.time + Mathf.Max(0.5f, caller.dispatchInterval);
        Dispatch();
    }

    /// <summary>제보 지점에서 가장 가까운 구역 몬스터 N마리를 고른다. 사냥이 끝날 때까지 바꾸지 않는다.</summary>
    private void SelectSquad(Vector3 reportPoint)
    {
        squad.Clear();
        List<MonsterAI> candidates = new List<MonsterAI>();
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.role == MonsterAI.MonsterRole.Zone_Defender && !m.IsInStun) { candidates.Add(m); }
        }
        candidates.Sort((a, b) =>
            (a.transform.position - reportPoint).sqrMagnitude.CompareTo((b.transform.position - reportPoint).sqrMagnitude));

        int size = Mathf.Max(0, caller != null ? caller.dispatchSquadSize : 2);
        for (int i = 0; i < candidates.Count && i < size; i++) { squad.Add(candidates[i]); }
    }

    /// <summary>
    /// 파견조에게 방향을 나눠 준다. 각자 플레이어의 어느 쪽에 있는지 각도순으로 정렬해 고르게 벌린다
    /// — 목표가 한 점으로 모이지 않아 같은 길로 줄지어 오지 않는다.
    /// </summary>
    private void AssignBearings()
    {
        bearings.Clear();
        lastSent.Clear();
        if (squad.Count == 0) { return; }

        Vector3 center = prey.position;
        List<MonsterAI> members = new List<MonsterAI>(squad);
        members.Sort((a, b) => AngleAround(center, a.transform.position).CompareTo(AngleAround(center, b.transform.position)));

        float start = AngleAround(center, members[0].transform.position);
        float step = Mathf.PI * 2f / members.Count;
        for (int i = 0; i < members.Count; i++) { bearings[members[i]] = start + step * i; }
    }

    /// <summary>파견조에게 흐린 지점을 보낸다. 몬스터는 그곳으로 가서 수색할 뿐 — 직접 보면 그때부터 쫓는다.</summary>
    private void Dispatch()
    {
        Vector3 center = prey.position;
        foreach (MonsterAI m in squad)
        {
            if (m == null || !m.CanReceiveDispatch) { continue; }
            if (caller.dispatchRange > 0f && Vector3.Distance(m.transform.position, center) > caller.dispatchRange) { continue; }

            if (!bearings.TryGetValue(m, out float bearing))
            {
                bearing = AngleAround(center, m.transform.position);
                bearings[m] = bearing;
            }

            float angle = bearing + Random.Range(-caller.dispatchAngleJitter, caller.dispatchAngleJitter) * Mathf.Deg2Rad;
            float distance = Random.Range(caller.dispatchBlurMin, Mathf.Max(caller.dispatchBlurMin, caller.dispatchBlurMax));
            Vector3 raw = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, distance, NavMesh.AllAreas)) { continue; }

            m.ReceiveSignal(hit.position);
            lastSent[m] = hit.position;
        }
    }

    private void EndHunt(string reason)
    {
        if (hunting && caller != null && caller.showDebugLog)
        {
            Debug.Log($"<color=grey><b>[사냥 종료]</b></color> {reason}");
        }
        // 파견된 개체는 불러들이지 않는다 — 각자 수색을 마치고 제 FSM대로 복귀한다
        squad.Clear();
        bearings.Clear();
        lastSent.Clear();
        hunting = false;
        caller = null;
        prey = null;
    }

    // ────────────────────────────────────────────────
    //  추격 인원 조율: 최대 2마리, 넘치면 매복
    // ────────────────────────────────────────────────

    private void TrackPlayer()
    {
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go == null) { return; }
            player = go.transform;
            playerPrevPos = player.position;
        }
        if (Time.deltaTime <= 0f) { return; }
        Vector3 v = (player.position - playerPrevPos) / Time.deltaTime;
        v.y = 0f;
        // 순간 속도는 튀므로 부드럽게
        playerVelocity = Vector3.Lerp(playerVelocity, v, Mathf.Clamp01(Time.deltaTime * 4f));
        playerPrevPos = player.position;
    }

    /// <summary>
    /// 쫓는 개체가 최대치를 넘으면 뒤처진 구역 몬스터를 매복으로 돌린다.
    /// 우선순위: 전역 몬스터 → 플레이어에게 가장 가까운 구역 몬스터.
    /// 매복하던 개체가 덮쳐서 다시 셋이 되면, 이 규칙에 따라 가장 뒤처진 개체가 대신 매복으로 빠진다
    /// — 결과적으로 "뒤따라오던 놈"과 "앞에서 기다리던 놈"이 역할을 교대한다.
    /// </summary>
    private void CoordinateChasers()
    {
        chasers.Clear();
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.CurrentState == MonsterAI.State.Chase) { chasers.Add(m); }
        }

        MonsterAI global = FindGlobal();
        int max = Mathf.Max(1, global != null ? global.maxSimultaneousChasers : 2);

        if (player != null && chasers.Count > max)
        {
            Vector3 p = player.position;
            chasers.Sort((a, b) =>
            {
                bool ag = a.role == MonsterAI.MonsterRole.Global_Stalker;
                bool bg = b.role == MonsterAI.MonsterRole.Global_Stalker;
                if (ag != bg) { return ag ? -1 : 1; }
                return (a.transform.position - p).sqrMagnitude.CompareTo((b.transform.position - p).sqrMagnitude);
            });

            for (int i = max; i < chasers.Count; i++)
            {
                MonsterAI extra = chasers[i];
                // 방금 역할이 바뀐 개체는 잠시 두고 본다 (추격↔매복을 오가며 떠는 것 방지)
                if (!extra.CanBeDemoted) { continue; }
                extra.BeginIntercept(ComputeInterceptPoint(extra));
                if (extra.showDebugLog)
                {
                    Debug.Log($"<color=magenta><b>[매복]</b></color> {extra.name} — 추격 {chasers.Count}마리 초과, 앞질러 대기");
                }
            }
        }

        // 매복 지점 갱신 — 플레이어가 방향을 바꾸면 따라 옮긴다
        if (Time.time >= nextInterceptRefresh)
        {
            nextInterceptRefresh = Time.time + Mathf.Max(0.3f, global != null ? global.interceptRefreshInterval : 1.5f);
            foreach (MonsterAI m in MonsterAI.activeMonsters)
            {
                if (m != null && m.IsIntercepting) { m.UpdateInterceptPoint(ComputeInterceptPoint(m)); }
            }
        }

        // 아무도 쫓지 않은 채 잠시 지나면 매복도 푼다
        bool anyChasing = false;
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.CurrentState == MonsterAI.State.Chase) { anyChasing = true; break; }
        }
        if (anyChasing) { noChaserSince = -1f; }
        else if (noChaserSince < 0f) { noChaserSince = Time.time; }
        else if (Time.time - noChaserSince > 2f)
        {
            foreach (MonsterAI m in MonsterAI.activeMonsters)
            {
                if (m != null && m.IsIntercepting) { m.EndIntercept(); }
            }
        }
    }

    /// <summary>
    /// 매복 지점: 플레이어가 달려가는 방향으로 interceptLeadDistance 앞.
    /// 벽 너머로 잡히지 않게 걸을 수 있는 바닥 위에서 막히는 지점까지만 간다(복도가 꺾이면 모퉁이 앞).
    /// 플레이어가 멈춰 있으면 쫓는 무리의 반대편 — 도망칠 방향을 막는다.
    /// </summary>
    private Vector3 ComputeInterceptPoint(MonsterAI interceptor)
    {
        if (player == null) { return interceptor.transform.position; }
        Vector3 p = player.position;

        MonsterAI global = FindGlobal();
        float lead = global != null ? global.interceptLeadDistance : 15f;

        Vector3 dir;
        if (playerVelocity.magnitude > 1.5f)
        {
            dir = playerVelocity.normalized;
        }
        else
        {
            Vector3 away = Vector3.zero;
            foreach (MonsterAI c in chasers)
            {
                if (c == null || c == interceptor) { continue; }
                Vector3 d = p - c.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) { away += d.normalized; }
            }
            dir = (away.sqrMagnitude > 0.01f) ? away.normalized : (p - interceptor.transform.position).normalized;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { dir = Vector3.forward; }
            dir.Normalize();
        }

        if (!NavMesh.SamplePosition(p, out NavMeshHit onNav, 3f, NavMesh.AllAreas)) { return p; }
        Vector3 wanted = onNav.position + dir * lead;
        if (NavMesh.Raycast(onNav.position, wanted, out NavMeshHit edge, NavMesh.AllAreas))
        {
            // 막힌 곳 바로 앞 (벽에 딱 붙지 않게 1m 물러남)
            wanted = edge.position - dir * 1f;
        }
        return NavMesh.SamplePosition(wanted, out NavMeshHit final, 4f, NavMesh.AllAreas) ? final.position : onNav.position;
    }

    private static MonsterAI FindGlobal()
    {
        foreach (MonsterAI m in MonsterAI.activeMonsters)
        {
            if (m != null && m.role == MonsterAI.MonsterRole.Global_Stalker) { return m; }
        }
        return null;
    }

    private static float AngleAround(Vector3 center, Vector3 point)
    {
        Vector3 d = point - center;
        return Mathf.Atan2(d.z, d.x);
    }

    private void OnDrawGizmos()
    {
        if (!hunting) { return; }
        Gizmos.color = new Color(1f, 0.5f, 0f);
        foreach (KeyValuePair<MonsterAI, Vector3> pair in lastSent)
        {
            Gizmos.DrawWireSphere(pair.Value, 1f);
            if (pair.Key != null) { Gizmos.DrawLine(pair.Key.transform.position, pair.Value); }
        }
    }
}
