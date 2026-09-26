using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Recording scene only: real gameplay plus a live tactical map of the same round.
/// 지도는 세 가지를 보여 준다: 누가 무엇을 하고 있나(색·이름표), 왜 그렇게 했나(판단 기록),
/// 내 사격을 누가 들었나(소리 파동과 들은 몬스터로 이어지는 선).
/// </summary>
[DefaultExecutionOrder(300)]
public sealed class PortfolioCaptureView : MonoBehaviour
{
    [SerializeField] private bool mapFirst = false;
    [SerializeField] private bool showLabels = true;

    [Tooltip("판단 기록 한 줄을 지도 위에 보여 주는 시간(초)")]
    [SerializeField] private float decisionLifetime = 7f;

    [Tooltip("판단 기록을 한 번에 보여 주는 최대 줄 수")]
    [SerializeField] private int maxDecisions = 5;

    [Tooltip("소리 파동과 '들음' 표시를 보여 주는 시간(초)")]
    [SerializeField] private float noiseLifetime = 2.5f;

    // 지도 색 — 범례와 같다. 플레이어만 노랑이고 몬스터는 하는 일에 따라 색이 바뀐다
    private static readonly Color PlayerColor = new Color(1f, .9f, .15f);
    private static readonly Color ChaseColor = new Color(1f, .3f, .28f);
    private static readonly Color[] DetourColors = { new Color(.25f, .85f, 1f), new Color(.9f, .45f, 1f) };
    private static readonly Color ReturnColor = new Color(.45f, .58f, 1f);
    private static readonly Color PatrolColor = new Color(.4f, .82f, .45f);
    private static readonly Color SearchColor = new Color(1f, .58f, .15f);
    private static readonly Color IdleColor = new Color(.5f, .5f, .5f);
    private static readonly Color MissColor = new Color(.62f, .62f, .62f);

    // Layer 31 belongs to RearViewMirror's player visuals. Keep map overlays
    // separate so excluding them from the gameplay camera never hides the player.
    private const int MapLayer = 30;
    private Camera gameplayCamera;
    private Camera mapCamera;
    private RectTransform reticle;
    private Vector2 originalReticlePosition;
    private Material lineMaterial;
    private readonly Dictionary<MonsterAI, LineRenderer> routes = new Dictionary<MonsterAI, LineRenderer>();
    private readonly Dictionary<MonsterAI, LineRenderer> markers = new Dictionary<MonsterAI, LineRenderer>();
    private readonly Dictionary<MonsterAI, NavMeshAgent> agents = new Dictionary<MonsterAI, NavMeshAgent>();
    private readonly List<LineRenderer> noiseRings = new List<LineRenderer>();
    private readonly List<LineRenderer> noiseLinks = new List<LineRenderer>();
    private readonly List<LineRenderer> exitMarks = new List<LineRenderer>();
    private readonly List<LineRenderer> ringMarks = new List<LineRenderer>();
    private readonly List<LineRenderer> relocateDashes = new List<LineRenderer>();

    private struct Relocation
    {
        public Vector3 from, to;
        public float time;
    }
    private readonly List<Relocation> relocations = new List<Relocation>();
    private LineRenderer playerMarker, playerOutline, playerHeading;
    private Transform player;
    private float overlayY;
    private GUIStyle labelStyle, boxStyle, feedStyle;
    private Texture2D labelBackground;
    private bool wasShowingMap;

    private struct NoisePulse
    {
        public MonsterDirector.NoiseReport report;
        public float time;
    }
    private readonly List<NoisePulse> pulses = new List<NoisePulse>();

    private struct Decision
    {
        public float time;
        public string text;
        public Color color;
    }
    private readonly List<Decision> decisions = new List<Decision>();
    private readonly Dictionary<MonsterAI, float> heardAt = new Dictionary<MonsterAI, float>();

    private void OnEnable()
    {
        MonsterDirector.NoiseReported += OnNoise;
        MonsterDirector.DecisionMade += OnDecision;
        MonsterDirector.Relocated += OnRelocated;
    }

    private void OnDisable()
    {
        MonsterDirector.NoiseReported -= OnNoise;
        MonsterDirector.DecisionMade -= OnDecision;
        MonsterDirector.Relocated -= OnRelocated;
    }

    private void OnRelocated(MonsterAI monster, Vector3 from, Vector3 to)
    {
        relocations.Add(new Relocation { from = from, to = to, time = Time.time });
        if (relocations.Count > 4) relocations.RemoveAt(0);
    }

    private void OnNoise(MonsterDirector.NoiseReport report)
    {
        pulses.Add(new NoisePulse { report = report, time = Time.time });
        if (pulses.Count > 4) pulses.RemoveAt(0);
        if (report.listeners != null)
            foreach (var m in report.listeners) if (m != null) heardAt[m] = Time.time;
        string what = report.kind == NoiseKind.Shot ? "총소리" : "과녁 소리";
        int count = report.listeners != null ? report.listeners.Length : 0;
        if (report.ignored) AddDecision(what + " — 사냥 중이라 무시", MissColor);
        else if (count == 0) AddDecision(what + " — 아무도 못 들음", MissColor);
        else AddDecision(what + " — " + count + "마리 들음", PlayerColor);
    }

    private void OnDecision(MonsterAI monster, string text)
    {
        if (monster == null) { AddDecision("감독: " + text, Color.white); return; }
        AddDecision(ShortName(monster) + ": " + text, MonsterColor(monster, MonsterDirector.Instance));
    }

    private void AddDecision(string text, Color color)
    {
        // 같은 문장이 연달아 오면 한 줄로 합친다
        if (decisions.Count > 0 && decisions[decisions.Count - 1].text == text)
        {
            var last = decisions[decisions.Count - 1];
            last.time = Time.time;
            decisions[decisions.Count - 1] = last;
            return;
        }
        decisions.Add(new Decision { time = Time.time, text = text, color = color });
        while (decisions.Count > Mathf.Max(1, maxDecisions)) decisions.RemoveAt(0);
    }

    private void Start()
    {
        gameplayCamera = Camera.main;
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (gameplayCamera == null) { enabled = false; return; }
        var crosshair = CrosshairFx.Instance;
        if (crosshair != null)
        {
            reticle = crosshair.transform.Find("Reticle") as RectTransform;
            if (reticle != null) originalReticlePosition = reticle.anchoredPosition;
        }

        var tri = NavMesh.CalculateTriangulation();
        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
        if (tri.vertices != null && tri.vertices.Length > 0)
        {
            bounds = new Bounds(tri.vertices[0], Vector3.zero);
            foreach (var vertex in tri.vertices) bounds.Encapsulate(vertex);
        }
        overlayY = bounds.max.y + 10f;
        var objectCamera = new GameObject("~PortfolioMapCamera");
        objectCamera.transform.SetParent(transform, false);
        mapCamera = objectCamera.AddComponent<Camera>();
        var data = mapCamera.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        mapCamera.orthographic = true;
        mapCamera.transform.SetPositionAndRotation(
            new Vector3(bounds.center.x, overlayY + 30f, bounds.center.z), Quaternion.Euler(90f, 0f, 0f));
        mapCamera.orthographicSize = Mathf.Max(bounds.size.z * .5f,
            bounds.size.x * .5f / ((float)Screen.width / Mathf.Max(1, Screen.height))) * 1.07f;
        mapCamera.nearClipPlane = .3f;
        mapCamera.farClipPlane = overlayY + 200f;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(.07f, .09f, .12f);
        mapCamera.cullingMask = gameplayCamera.cullingMask | (1 << MapLayer);
        mapCamera.depth = gameplayCamera.depth - 20f;
        mapCamera.enabled = false;
        lineMaterial = new Material(Shader.Find("Sprites/Default"));
        // 그리는 순서: 뒤에 만든 선이 위에 그려지도록 sortingOrder를 준다
        playerOutline = CreateLine("PlayerOutline", Color.black, 1f, true, 10);
        playerMarker = CreateLine("PlayerMarker", PlayerColor, 1f, false, 11);
        playerHeading = CreateLine("PlayerHeading", PlayerColor, 1f, false, 11);
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current.f6Key.wasPressedThisFrame) mapFirst = !mapFirst;
        if (Keyboard.current.f7Key.wasPressedThisFrame) showLabels = !showLabels;
    }

    private void LateUpdate()
    {
        if (mapCamera == null || gameplayCamera == null) return;
        bool showing = GameFlowManager.Instance != null && GameFlowManager.Instance.IsGameRunning;
        if (!showing)
        {
            if (wasShowingMap) gameplayCamera.rect = new Rect(0, 0, 1, 1);
            if (reticle != null) reticle.anchoredPosition = originalReticlePosition;
            wasShowingMap = false;
            mapCamera.enabled = false;
            SetLinesActive(false);
            // 다음 판은 새 기록으로 시작한다
            pulses.Clear(); decisions.Clear(); heardAt.Clear();
            return;
        }
        wasShowingMap = true;
        mapCamera.enabled = true;
        if (mapFirst)
        {
            mapCamera.rect = new Rect(0, 0, 1, 1);
            gameplayCamera.rect = new Rect(.62f, .055f, .36f, .37f);
            mapCamera.depth = gameplayCamera.depth - 20f;
        }
        else
        {
            gameplayCamera.rect = new Rect(0, 0, 1, 1);
            mapCamera.rect = new Rect(.635f, .05f, .34f, .34f);
            mapCamera.depth = gameplayCamera.depth + 20f;
        }
        gameplayCamera.cullingMask &= ~(1 << MapLayer);
        if (reticle != null)
        {
            var canvas = reticle.GetComponentInParent<Canvas>();
            float scale = canvas != null ? Mathf.Max(.01f, canvas.scaleFactor) : 1f;
            reticle.anchoredPosition = originalReticlePosition + new Vector2(
                (gameplayCamera.rect.center.x - .5f) * Screen.width / scale,
                (gameplayCamera.rect.center.y - .5f) * Screen.height / scale);
        }
        DrawMap();
    }

    // 지도 한 픽셀이 월드 몇 m인가 — 표시 크기를 화면 기준으로 맞춘다
    private float MetersPerPixel => mapCamera.orthographicSize * 2f / Mathf.Max(1f, mapCamera.pixelHeight);

    private void DrawMap()
    {
        foreach (var line in routes.Values) if (line != null) line.enabled = false;
        foreach (var line in markers.Values) if (line != null) line.enabled = false;
        var director = MonsterDirector.Instance;
        float px = MetersPerPixel;

        foreach (var monster in MonsterAI.activeMonsters)
        {
            if (monster == null) continue;
            if (!markers.TryGetValue(monster, out var marker) || marker == null)
                markers[monster] = marker = CreateLine(monster.name + "_Marker", Color.white, 1f, false, 6);
            Color color = MonsterColor(monster, director);

            Vector3[] corners = RouteOf(monster, director);
            if (corners != null && corners.Length >= 2)
            {
                if (!routes.TryGetValue(monster, out var line) || line == null)
                    routes[monster] = line = CreateLine(monster.name + "_Route", color, 1f, false, 3);
                bool main = monster.CurrentState != MonsterAI.State.Return;
                Color routeColor = color;
                routeColor.a = main ? .95f : .45f;
                line.enabled = true;
                line.widthMultiplier = (main ? 3.5f : 2f) * px;
                line.startColor = line.endColor = routeColor;
                line.positionCount = corners.Length + 1;
                line.SetPosition(0, Lift(monster.transform.position));
                for (int i = 0; i < corners.Length; i++) line.SetPosition(i + 1, Lift(corners[i]));
            }

            marker.startColor = marker.endColor = color;
            Disc(marker, monster.transform.position, 13f * px);
            marker.enabled = true;
        }

        bool hasPlayer = player != null;
        playerMarker.enabled = playerOutline.enabled = playerHeading.enabled = hasPlayer;
        if (hasPlayer)
        {
            Disc(playerMarker, player.position, 15f * px);
            playerOutline.widthMultiplier = 3f * px;
            Circle(playerOutline, player.position, 9f * px);
            Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            playerHeading.widthMultiplier = 4f * px;
            playerHeading.positionCount = 2;
            playerHeading.SetPosition(0, Lift(player.position));
            playerHeading.SetPosition(1, Lift(player.position + forward.normalized * 22f * px));
        }
        DrawNoise(px);
        DrawNet(director, px);
    }

    /// <summary>
    /// 포위망: 출구(막힘 = 빨강 채운 점, 빔 = 초록 테두리), 둘레 순찰 자리(흐린 초록 원),
    /// 재배치(옮기기 전 → 뒤, 흐려지는 점선).
    /// </summary>
    private void DrawNet(MonsterDirector director, float px)
    {
        int e = 0, r = 0, d = 0;
        if (director != null)
        {
            if (director.IsHunting)
                foreach (var exit in director.DebugExits)
                {
                    var mark = Pooled(exitMarks, e++, "Exit", true, 2);
                    mark.startColor = mark.endColor = exit.blocked ? ChaseColor : PatrolColor;
                    mark.widthMultiplier = (exit.blocked ? 4f : 2.5f) * px;
                    Circle(mark, exit.point, 7f * px);
                    mark.enabled = true;
                }
            foreach (var pair in director.DebugRingSlots)
            {
                if (pair.Key == null || pair.Key.CurrentState != MonsterAI.State.Patrol && pair.Key.CurrentState != MonsterAI.State.Return) continue;
                var mark = Pooled(ringMarks, r++, "RingSlot", true, 1);
                Color c = PatrolColor;
                c.a = .35f;
                mark.startColor = mark.endColor = c;
                mark.widthMultiplier = 2f * px;
                Circle(mark, pair.Value, 10f);
                mark.enabled = true;
            }
        }
        relocations.RemoveAll(x => Time.time - x.time > 4f);
        foreach (var x in relocations)
        {
            Color c = Color.white;
            c.a = Mathf.Clamp01(1f - (Time.time - x.time) / 4f);
            Vector3 step = x.to - x.from;
            int dashes = Mathf.Clamp(Mathf.RoundToInt(step.magnitude / 6f), 2, 30);
            for (int i = 0; i < dashes; i += 2)
            {
                var dash = Pooled(relocateDashes, d++, "Relocate", false, 3);
                dash.startColor = dash.endColor = c;
                dash.widthMultiplier = 2.5f * px;
                dash.positionCount = 2;
                dash.SetPosition(0, Lift(x.from + step * i / dashes));
                dash.SetPosition(1, Lift(x.from + step * (i + 1) / dashes));
                dash.enabled = true;
            }
        }
        for (int i = e; i < exitMarks.Count; i++) exitMarks[i].enabled = false;
        for (int i = r; i < ringMarks.Count; i++) ringMarks[i].enabled = false;
        for (int i = d; i < relocateDashes.Count; i++) relocateDashes[i].enabled = false;
    }

    /// <summary>총소리: 쏜 자리에서 소리 반경까지 퍼지는 원 + 들은 몬스터로 이어지는 선. 아무도 못 들으면 회색.</summary>
    private void DrawNoise(float px)
    {
        pulses.RemoveAll(p => Time.time - p.time > noiseLifetime);
        int ring = 0, link = 0;
        foreach (var pulse in pulses)
        {
            var r = pulse.report;
            float age = Time.time - pulse.time;
            float fade = 1f - age / Mathf.Max(.1f, noiseLifetime);
            bool heard = !r.ignored && r.listeners != null && r.listeners.Length > 0;
            Color color = heard ? PlayerColor : MissColor;
            color.a = Mathf.Clamp01(fade);

            var circle = Pooled(noiseRings, ring++, "NoiseRing", true, 4);
            circle.startColor = circle.endColor = color;
            circle.widthMultiplier = 2.5f * px;
            Circle(circle, r.position, r.radius * Mathf.Clamp01(age / .35f));
            circle.enabled = true;

            if (!heard) continue;
            foreach (var m in r.listeners)
            {
                if (m == null) continue;
                var line = Pooled(noiseLinks, link++, "NoiseLink", false, 5);
                line.startColor = line.endColor = color;
                line.widthMultiplier = 2.5f * px;
                line.positionCount = 2;
                line.SetPosition(0, Lift(r.position));
                line.SetPosition(1, Lift(m.transform.position));
                line.enabled = true;
            }
        }
        for (int i = ring; i < noiseRings.Count; i++) noiseRings[i].enabled = false;
        for (int i = link; i < noiseLinks.Count; i++) noiseLinks[i].enabled = false;
    }

    /// <summary>표시할 경로: 추격은 실제 이동 경로, 출구 막기는 감독이 준 경로, 자리 이동은 흐리게.</summary>
    private Vector3[] RouteOf(MonsterAI monster, MonsterDirector director)
    {
        if (monster.IsInStun) return null;
        if (monster.IsBlocking && director != null && director.DebugRoutes.TryGetValue(monster, out var block) && !monster.IsClosingIn)
            return block.corners;
        if (monster.CurrentState != MonsterAI.State.Chase && monster.CurrentState != MonsterAI.State.Return) return null;
        if (!agents.TryGetValue(monster, out var agent) || agent == null)
            agents[monster] = agent = monster.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isOnNavMesh && agent.hasPath) return agent.path.corners;
        if (director != null && director.DebugRoutes.TryGetValue(monster, out var route)) return route.corners;
        return null;
    }

    private static Color MonsterColor(MonsterAI m, MonsterDirector director)
    {
        if (m.IsInStun) return Mathf.Repeat(Time.time * 4f, 1f) < .5f ? Color.white : new Color(.7f, .7f, .7f);
        switch (m.CurrentState)
        {
        case MonsterAI.State.Chase: return ChaseColor;
        case MonsterAI.State.Block:
            if (director != null && director.DebugRoutes.TryGetValue(m, out var route) && route.colorIndex >= 2)
                return DetourColors[1];
            return DetourColors[0];
        case MonsterAI.State.Return: return ReturnColor;
        case MonsterAI.State.Investigate: return SearchColor;
        case MonsterAI.State.Idle: return IdleColor;
        default: return PatrolColor;
        }
    }

    /// <summary>이름표: 감독이 준 역할 + 몸의 상태. 예) "추격", "출구 · 출구로", "순찰".</summary>
    private static string Judgment(MonsterAI m, MonsterDirector director)
    {
        if (m.IsInStun) return "감전";
        string role = director != null ? director.DebugOrderLabel(m) : "";
        string state = m.StateLabel;
        if (role.Length == 0) return state;
        if (state == role || (role == "추격" && state == "추격")) return role;
        return role + " · " + state;
    }

    private static string ShortName(MonsterAI m) => m.name.Replace("Monster_", "");

    private LineRenderer Pooled(List<LineRenderer> pool, int index, string name, bool loop, int order)
    {
        while (pool.Count <= index) pool.Add(CreateLine(name + pool.Count, Color.white, 1f, loop, order));
        return pool[index];
    }

    private LineRenderer CreateLine(string name, Color color, float width, bool loop, int order)
    {
        var go = new GameObject(name);
        go.layer = MapLayer;
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.material = lineMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = width;
        line.numCapVertices = 6;
        line.numCornerVertices = 2;
        line.sortingOrder = order;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private Vector3 Lift(Vector3 p) => new Vector3(p.x, overlayY, p.z);

    /// <summary>채운 원: 아주 짧은 선에 둥근 끝을 붙이면 선 굵기만 한 원이 된다.</summary>
    private void Disc(LineRenderer line, Vector3 center, float diameter)
    {
        line.loop = false;
        line.widthMultiplier = diameter;
        line.positionCount = 2;
        line.SetPosition(0, Lift(center) - Vector3.right * .01f);
        line.SetPosition(1, Lift(center) + Vector3.right * .01f);
    }

    private void Circle(LineRenderer line, Vector3 center, float radius)
    {
        const int segments = 32;
        line.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, Lift(center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius));
        }
    }

    private void SetLinesActive(bool value)
    {
        if (playerMarker != null) playerMarker.enabled = value;
        if (playerOutline != null) playerOutline.enabled = value;
        if (playerHeading != null) playerHeading.enabled = value;
        foreach (var line in routes.Values) if (line != null) line.enabled = value;
        foreach (var line in markers.Values) if (line != null) line.enabled = value;
        foreach (var line in noiseRings) if (line != null) line.enabled = value;
        foreach (var line in noiseLinks) if (line != null) line.enabled = value;
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private void OnGUI()
    {
        if (!wasShowingMap || mapCamera == null) return;
        if (labelStyle == null)
        {
            labelBackground = new Texture2D(1, 1);
            labelBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, .62f));
            labelBackground.Apply();
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1), richText = true, wordWrap = false };
            labelStyle.normal.background = labelBackground;
            boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false };
            boxStyle.normal.textColor = Color.white;
            feedStyle = new GUIStyle(boxStyle) { fontSize = 13, alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 8, 5, 5) };
        }
        var director = MonsterDirector.Instance;
        string caption = director != null && director.IsHunting
            ? "전술 지도  |  사냥 중 · " + director.DebugLayoutSummary
            : "전술 지도  |  순찰 중";
        string legend =
            "<color=#" + Hex(PlayerColor) + ">● 나</color>  " +
            "<color=#" + Hex(ChaseColor) + ">● 추격</color>  " +
            "<color=#" + Hex(DetourColors[0]) + ">● 출구 막기</color>  " +
            "<color=#" + Hex(ReturnColor) + ">● 자리 이동</color>  " +
            "<color=#" + Hex(PatrolColor) + ">● 순찰</color>  " +
            "<color=#" + Hex(SearchColor) + ">● 수색</color>  " +
            "<color=#" + Hex(IdleColor) + ">● 멈춤</color>";
        const float captionHeight = 64f;
        Rect captionRect = mapFirst
            ? new Rect(14, Screen.height - captionHeight - 14, 470, captionHeight)
            : new Rect(Screen.width * .635f, Screen.height * .61f - captionHeight,
                Screen.width * .34f, captionHeight);
        GUI.Box(captionRect, caption + "\n" + legend + "\n<size=11>F6 화면 전환 · F7 이름 표시 · F8 QA 표시</size>", boxStyle);
        if (mapFirst)
        {
            GUI.Box(new Rect(Screen.width * .62f, Screen.height * .575f,
                Screen.width * .36f, 32), "실제 플레이 화면", boxStyle);
        }
        DrawDecisionFeed(captionRect);

        if (!showLabels) return;
        foreach (var monster in MonsterAI.activeMonsters)
        {
            if (monster == null) continue;
            string text = "<color=#" + Hex(MonsterColor(monster, director)) + ">" + ShortName(monster) +
                " · " + Judgment(monster, director) + "</color>";
            if (heardAt.TryGetValue(monster, out float heard) && Time.time - heard <= noiseLifetime)
                text += " <color=#" + Hex(PlayerColor) + ">들음!</color>";
            MapLabel(monster.transform.position, text);
        }
        foreach (var pulse in pulses)
        {
            var r = pulse.report;
            int count = r.listeners != null ? r.listeners.Length : 0;
            string what = r.kind == NoiseKind.Shot ? "총소리" : "과녁 소리";
            string result = r.ignored ? "무시됨" : count == 0 ? "아무도 못 들음" : count + "마리 들음";
            Color color = !r.ignored && count > 0 ? PlayerColor : MissColor;
            MapLabel(r.position, "<color=#" + Hex(color) + ">" + what + " · " + result + "</color>", -26f);
        }
        if (player != null)
            MapLabel(player.position, "<color=#" + Hex(PlayerColor) + ">나</color>");
    }

    private void MapLabel(Vector3 world, string richText, float yOffset = 0f)
    {
        Vector3 point = mapCamera.WorldToScreenPoint(Lift(world));
        if (point.z <= 0 || !mapCamera.pixelRect.Contains(point)) return;
        Vector2 size = labelStyle.CalcSize(new GUIContent(richText));
        GUI.Label(new Rect(point.x + 10, Screen.height - point.y - size.y * .5f + yOffset, size.x, size.y),
            richText, labelStyle);
    }

    /// <summary>최근 판단 기록 — 캡션 바로 위에 쌓는다. 오래된 줄은 흐려진다.</summary>
    private void DrawDecisionFeed(Rect captionRect)
    {
        decisions.RemoveAll(d => Time.time - d.time > decisionLifetime);
        if (decisions.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        for (int i = decisions.Count - 1; i >= 0; i--)
        {
            var d = decisions[i];
            Color c = d.color;
            c = Color.Lerp(c, new Color(.45f, .45f, .45f), Mathf.Clamp01((Time.time - d.time) / decisionLifetime));
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<color=#").Append(Hex(c)).Append('>').Append(d.text).Append("</color>");
        }
        float height = 12f + decisions.Count * 18f;
        GUI.Box(new Rect(captionRect.x, captionRect.y - height - 4f, captionRect.width, height), sb.ToString(), feedStyle);
    }

    private void OnDestroy()
    {
        if (gameplayCamera != null) gameplayCamera.rect = new Rect(0, 0, 1, 1);
        if (reticle != null) reticle.anchoredPosition = originalReticlePosition;
        if (lineMaterial != null) Destroy(lineMaterial);
        if (labelBackground != null) Destroy(labelBackground);
    }
}
