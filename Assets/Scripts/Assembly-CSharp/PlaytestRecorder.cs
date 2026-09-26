using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Local, append-only playtest trace. Samples at 10 Hz; distance accumulates each rendered frame.
/// QA: 모든 기록에 화면 타이머 시각(clock, 예: "04:22")이 붙는다. 플레이 중 F8을 누르면 그 순간을 표시(qa_mark)한다.
/// 분석: Tools/QA/pico_qa.py (사용법은 CLAUDE.md "플레이 QA").
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class PlaytestRecorder : MonoBehaviour
{
    public static PlaytestRecorder Instance { get; private set; }
    public static string LastSessionDirectory { get; private set; }
    public bool IsRecording => writer != null;
    public static bool SuppressAutomaticRecording; // Automated tests must not masquerade as user sessions.
    private StreamWriter writer;
    private Transform player;
    private PlayerHealth health;
    private Camera gameplayCamera;
    private float startedAt, nextSample, nextFlush, lastCapturedAt;
    private int frameCount, eventCount;
    private readonly Dictionary<int, DistanceTrack> tracks = new Dictionary<int, DistanceTrack>();
    private readonly Dictionary<int, NavMeshAgent> agents = new Dictionary<int, NavMeshAgent>();
    private readonly Dictionary<int, string> roles = new Dictionary<int, string>();
    private int qaMarks;
    private float qaToastUntil;
    private string qaToast;
    private GUIStyle qaStyle;

    [Serializable] public class DistanceTrack
    {
        public int id;
        public string name;
        public float distance, planarDistance, stationarySeconds;
        public int teleports;
        [NonSerialized] public Vector3 previous;
    }
    [Serializable] private class Actor
    {
        public int id;
        public string name, state, label, role, pathStatus;
        public Vector3 position, forward, velocity, destination;
        public bool hasPath, pathPending, stunned, visibleToCamera, seesPlayer;
        public float remainingDistance;
    }
    [Serializable] private class Frame
    {
        public string kind = "frame";
        public float t;
        public string clock;
        public bool hunting;
        public Vector3 known;
        public string director;
        public Actor player;
        public Actor[] monsters;
        public Vector3 cameraPosition, cameraForward;
        public int health, targets;
        public float timeRemaining;
    }
    [Serializable] private class Event
    {
        public string kind = "event", type, actor, detail, clock;
        public float t;
        public Vector3 position, secondary;
        public Vector3[] route;
    }
    [Serializable] private class LayoutItem
    {
        public int id;
        public Vector3 position, normal, shootingPosition;
        public float height;
    }
    [Serializable] private class Header
    {
        public string kind = "header", schema = "pico-playtest-v2", utc, scene, unity;
        public string aiPolicy = "last-sight-ambient-stage-2026-09-26";
        public float sampleInterval = .1f;
        public Vector3 start, portal;
        public int targetGoal;
        public LayoutItem[] targets;
        public Vector3[] navVertices;
        public int[] navTriangles;
    }
    [Serializable] private class Summary
    {
        public string outcome, scene, endedUtc;
        public float duration;
        public int frames, events, targetsDestroyed;
        public DistanceTrack[] actors;
    }

    private void Awake() { Instance = this; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instance = null; SuppressAutomaticRecording = false; LastSessionDirectory = null; }

    public static void BeginRound()
    {
        if (!SuppressAutomaticRecording && Instance != null && !Instance.IsRecording) { Instance.Begin(); }
    }
    public static void EndRound(string reason) { if (Instance != null) { Instance.End(reason); } }

    private void Begin()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (player == null) { return; }
        health = player.GetComponent<PlayerHealth>();
        gameplayCamera = Camera.main;
        if (gameplayCamera == null || gameplayCamera.name == "~OverviewCamera")
        {
            gameplayCamera = null;
            foreach (var candidate in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.name == "~OverviewCamera") continue;
                if (gameplayCamera == null || candidate.isActiveAndEnabled) gameplayCamera = candidate;
                if (candidate.isActiveAndEnabled) break;
            }
        }
        startedAt = Time.time; nextSample = startedAt; nextFlush = startedAt + 1f; lastCapturedAt = startedAt;
        frameCount = eventCount = qaMarks = 0; qaToastUntil = 0f; tracks.Clear(); agents.Clear(); roles.Clear();
        try
        {
            string root = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "../PlaytestRecordings")) : Path.Combine(Application.persistentDataPath, "PlaytestRecordings");
            string id = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            LastSessionDirectory = Path.Combine(root, id);
            Directory.CreateDirectory(LastSessionDirectory);
            writer = new StreamWriter(Path.Combine(LastSessionDirectory, "trace.jsonl"), false, new UTF8Encoding(false), 65536);
            File.WriteAllText(Path.Combine(root, "latest-session.txt"), LastSessionDirectory);
            var layout = new List<LayoutItem>();
            foreach (var t in FindObjectsByType<Target>(FindObjectsSortMode.None))
            {
                layout.Add(new LayoutItem { id = t.GetInstanceID(), position = t.transform.position, normal = t.transform.forward, height = t.PlacementHeight, shootingPosition = t.ShootingPosition });
            }
            var nav = NavMesh.CalculateTriangulation();
            Write(new Header { utc = DateTime.UtcNow.ToString("O"), scene = SceneManager.GetActiveScene().path, unity = Application.unityVersion,
                start = player.position, portal = TargetManager.Instance != null ? TargetManager.Instance.PortalPosition : player.position,
                targetGoal = TargetManager.Instance != null ? TargetManager.Instance.TargetGoal : 0,
                targets = layout.ToArray(), navVertices = nav.vertices, navTriangles = nav.indices });
            Track(player, 0); foreach (var m in MonsterAI.activeMonsters) { if (m != null) Track(m.transform, 0); }
            Record("session_start", "player", player.position);
            Capture(); writer.Flush();
            Debug.Log("[Playtest] 기록 시작: " + LastSessionDirectory);
        }
        catch (Exception ex) { Fail(ex); }
    }

    public static void Record(string type, string actor, Vector3 position, string detail = "", Vector3 secondary = default, Vector3[] route = null)
    {
        var r = Instance;
        if (r == null || !r.IsRecording) { return; }
        r.eventCount++;
        r.Write(new Event { type = type, actor = actor, position = position, detail = detail, secondary = secondary, route = route, t = Time.time - r.startedAt, clock = Clock() });
    }

    /// <summary>화면 타이머와 같은 형식("mm:ss", 남은 시간). 사용자가 "4분 22초쯤"이라고 말하면 이 값으로 찾는다.</summary>
    public static string Clock()
    {
        var tm = TargetManager.Instance;
        if (tm == null) return "";
        float t = Mathf.Max(0f, tm.CurrentTime);
        return Mathf.FloorToInt(t / 60f).ToString("00") + ":" + Mathf.FloorToInt(t % 60f).ToString("00");
    }

    private void Update()
    {
        if (!IsRecording || Keyboard.current == null || !Keyboard.current.f8Key.wasPressedThisFrame) return;
        qaMarks++;
        string clock = Clock();
        Record("qa_mark", "player", player != null ? player.position : Vector3.zero, "mark=" + qaMarks);
        try { writer.Flush(); } catch (Exception ex) { Fail(ex); }
        qaToast = "QA 표시 #" + qaMarks + " · " + clock + " — 기록됨";
        qaToastUntil = Time.unscaledTime + 2.5f;
        Debug.Log("[Playtest] " + qaToast);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime > qaToastUntil || string.IsNullOrEmpty(qaToast)) return;
        if (qaStyle == null)
        {
            qaStyle = new GUIStyle(GUI.skin.box) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            qaStyle.normal.textColor = Color.yellow;
        }
        GUI.Box(new Rect(Screen.width * .5f - 220f, Screen.height * .18f, 440f, 44f), qaToast, qaStyle);
    }
    private void LateUpdate()
    {
        if (!IsRecording) { return; }
        if (GameFlowManager.Instance == null || !GameFlowManager.Instance.IsGameRunning) { End("stopped"); return; }
        Track(player, Time.deltaTime);
        foreach (var m in MonsterAI.activeMonsters) { if (m != null) { Track(m.transform, Time.deltaTime); } }
        if (Time.time >= nextSample) { nextSample = Time.time + .1f; Capture(); }
        if (IsRecording && Time.time >= nextFlush)
        {
            nextFlush = Time.time + 1f;
            try { writer.Flush(); } catch (Exception ex) { Fail(ex); }
        }
    }

    private void Track(Transform actor, float dt)
    {
        if (actor == null) { return; }
        int id = actor.GetInstanceID();
        if (!tracks.TryGetValue(id, out var track))
        {
            tracks[id] = new DistanceTrack { id = id, name = actor.name, previous = actor.position }; return;
        }
        Vector3 delta = actor.position - track.previous;
        if (delta.magnitude > Mathf.Max(5f, 60f * dt))
        {
            track.teleports++; Record("position_discontinuity", actor.name, track.previous, "excluded_from_distance", actor.position);
        }
        else
        {
            track.distance += delta.magnitude; delta.y = 0;
            track.planarDistance += delta.magnitude;
        }
        track.previous = actor.position;
    }

    private Actor Snapshot(Transform transform, MonsterAI monster = null)
    {
        if (transform == null) { return null; }
        var result = new Actor { id = transform.GetInstanceID(), name = transform.name, position = transform.position, forward = transform.forward };
        if (monster == null)
        {
            var body = transform.GetComponent<Rigidbody>(); result.velocity = body != null ? body.linearVelocity : Vector3.zero;
            result.state = "player"; return result;
        }
        int id = monster.GetInstanceID();
        if (!agents.TryGetValue(id, out var agent)) { agent = monster.GetComponent<NavMeshAgent>(); agents[id] = agent; }
        result.state = monster.CurrentState.ToString(); result.label = monster.StateLabel; result.stunned = monster.IsInStun;
        result.seesPlayer = monster.IsSeeingPlayer;
        result.velocity = monster.PlanarVelocity; result.destination = monster.DebugDestination;
        if (gameplayCamera != null)
        {
            Vector3 point = transform.position + Vector3.up * 1.3f;
            Vector3 view = gameplayCamera.WorldToViewportPoint(point);
            result.visibleToCamera = view.z > 0 && view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1 &&
                (!Physics.Linecast(gameplayCamera.transform.position, point, out var occluder, monster.obstacleMask, QueryTriggerInteraction.Ignore)
                 || occluder.transform.IsChildOf(transform));
        }
        var d = MonsterDirector.Instance; result.role = d != null ? d.DebugOrderLabel(monster) : "";
        if (!roles.TryGetValue(id, out string old) || old != result.role)
        {
            roles[id] = result.role; Record("role", monster.name, transform.position, result.role);
        }
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            result.hasPath = agent.hasPath; result.pathPending = agent.pathPending;
            result.pathStatus = agent.pathStatus.ToString();
            result.remainingDistance = float.IsInfinity(agent.remainingDistance) ? -1 : agent.remainingDistance;
        }
        return result;
    }

    private void Capture()
    {
        float capturedAt = Time.time;
        float sampleDelta = frameCount == 0 ? 0f : Mathf.Max(0f, capturedAt - lastCapturedAt);
        lastCapturedAt = capturedAt;
        UpdateStationary(player, sampleDelta);
        var monsters = new List<Actor>();
        foreach (var m in MonsterAI.activeMonsters)
        {
            if (m == null) continue;
            UpdateStationary(m.transform, sampleDelta, m.PlanarVelocity);
            monsters.Add(Snapshot(m.transform, m));
        }
        var director = MonsterDirector.Instance;
        Write(new Frame { t = Time.time - startedAt, clock = Clock(), player = Snapshot(player), monsters = monsters.ToArray(),
            hunting = director != null && director.IsHunting,
            known = director != null ? director.DebugKnownPosition : Vector3.zero,
            director = director != null ? director.DebugLayoutSummary : "",
            cameraPosition = gameplayCamera != null ? gameplayCamera.transform.position : Vector3.zero,
            cameraForward = gameplayCamera != null ? gameplayCamera.transform.forward : Vector3.zero,
            health = health != null ? health.GetCurrentHealth() : 0,
            targets = TargetManager.Instance != null ? TargetManager.Instance.DestroyedCount : 0,
            timeRemaining = TargetManager.Instance != null ? TargetManager.Instance.CurrentTime : 0 });
        frameCount++;
    }

    private void UpdateStationary(Transform actor, float dt, Vector3? knownVelocity = null)
    {
        if (actor == null || dt <= 0f || !tracks.TryGetValue(actor.GetInstanceID(), out var track)) return;
        Vector3 velocity = knownVelocity ?? (actor.GetComponent<Rigidbody>() != null ? actor.GetComponent<Rigidbody>().linearVelocity : Vector3.zero);
        velocity.y = 0f;
        if (velocity.magnitude <= .15f) track.stationarySeconds += dt;
    }

    private void Write(object record)
    {
        if (!IsRecording) { return; }
        try { writer.WriteLine(JsonUtility.ToJson(record)); } catch (Exception ex) { Fail(ex); }
    }
    private void End(string reason)
    {
        if (!IsRecording) { return; }
        Track(player, 0);
        foreach (var m in MonsterAI.activeMonsters) { if (m != null) { Track(m.transform, 0); } }
        Capture();
        Record("session_end", "session", player != null ? player.position : Vector3.zero, reason);
        try
        {
            writer.Flush(); writer.Dispose(); writer = null;
            var summary = new Summary { outcome = reason, scene = SceneManager.GetActiveScene().path, endedUtc = DateTime.UtcNow.ToString("O"),
                duration = Time.time - startedAt, frames = frameCount, events = eventCount,
                targetsDestroyed = TargetManager.Instance != null ? TargetManager.Instance.DestroyedCount : 0,
                actors = new List<DistanceTrack>(tracks.Values).ToArray() };
            File.WriteAllText(Path.Combine(LastSessionDirectory, "summary.json"), JsonUtility.ToJson(summary, true));
            Debug.Log("[Playtest] 기록 저장: " + LastSessionDirectory);
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Fail(Exception ex)
    {
        try { writer?.Dispose(); } catch { }
        writer = null;
        Debug.LogWarning("[Playtest] 기록 중단 (게임은 계속됩니다): " + ex.Message);
    }
    private void OnApplicationQuit() { End("application_quit"); }
    private void OnDisable() { End("play_stopped"); }
    private void OnDestroy() { End("scene_closed"); if (Instance == this) { Instance = null; } }
}
