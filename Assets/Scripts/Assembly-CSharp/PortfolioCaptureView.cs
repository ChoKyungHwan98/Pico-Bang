using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>Recording scene only: real gameplay plus a live tactical map of the same round.</summary>
[DefaultExecutionOrder(300)]
public sealed class PortfolioCaptureView : MonoBehaviour
{
    [SerializeField] private bool mapFirst = false;
    [SerializeField] private bool showLabels = true;

    private static readonly Color[] RouteColors =
    {
        new Color(1f, .68f, .24f), new Color(.24f, .86f, 1f), new Color(.91f, .42f, 1f)
    };
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
    private LineRenderer playerMarker;
    private Transform player;
    private float overlayY;
    private GUIStyle labelStyle, boxStyle;
    private bool wasShowingMap;

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
        playerMarker = CreateLine("PlayerMarker", Color.white, 1f, true);
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

    private void DrawMap()
    {
        foreach (var line in routes.Values) if (line != null) line.enabled = false;
        foreach (var line in markers.Values) if (line != null) line.enabled = false;
        var director = MonsterDirector.Instance;
        foreach (var monster in MonsterAI.activeMonsters)
        {
            if (monster == null) continue;
            if (!markers.TryGetValue(monster, out var marker) || marker == null)
                markers[monster] = marker = CreateLine(monster.name + "_Marker", Color.white, .9f, true);
            Color color = new Color(.64f, .71f, .79f);
            if (director != null && director.DebugRoutes.TryGetValue(monster, out var route))
            {
                color = RouteColors[Mathf.Clamp(route.colorIndex, 0, RouteColors.Length - 1)];
                if (!routes.TryGetValue(monster, out var line) || line == null)
                    routes[monster] = line = CreateLine(monster.name + "_Route", color, .7f, false);
                line.enabled = true;
                line.startColor = line.endColor = color;
                line.positionCount = route.corners.Length;
                for (int i = 0; i < route.corners.Length; i++) line.SetPosition(i, Lift(route.corners[i]));
            }
            if (monster.IsInStun) color = Color.gray;
            marker.startColor = marker.endColor = color;
            Circle(marker, monster.transform.position, 2f);
            marker.enabled = true;
        }
        playerMarker.enabled = player != null;
        if (player != null) Circle(playerMarker, player.position, 2.4f);
    }

    private LineRenderer CreateLine(string name, Color color, float width, bool loop)
    {
        var go = new GameObject(name);
        go.layer = MapLayer;
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.material = lineMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = width;
        line.numCapVertices = 3;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private Vector3 Lift(Vector3 p) => new Vector3(p.x, overlayY, p.z);

    private void Circle(LineRenderer line, Vector3 center, float radius)
    {
        const int segments = 24;
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
        foreach (var line in routes.Values) if (line != null) line.enabled = value;
        foreach (var line in markers.Values) if (line != null) line.enabled = value;
    }

    private void OnGUI()
    {
        if (!wasShowingMap || mapCamera == null) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = Color.white;
            boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 15, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft };
            boxStyle.normal.textColor = Color.white;
        }
        var director = MonsterDirector.Instance;
        string caption = director != null && director.IsHunting
            ? "전술 지도  |  직접 추적 1 · 우회 " + Mathf.Max(0, director.DebugTeam.Count - 1)
            : "전술 지도  |  순찰 중";
        Rect captionRect = mapFirst
            ? new Rect(14, Screen.height - 66, 420, 48)
            : new Rect(Screen.width * .635f, Screen.height * .61f - 48,
                Screen.width * .34f, 48);
        GUI.Box(captionRect, caption + "\nF6 화면 전환  ·  F7 이름 표시", boxStyle);
        if (mapFirst)
        {
            GUI.Box(new Rect(Screen.width * .62f, Screen.height * .575f,
                Screen.width * .36f, 32), "실제 플레이 화면", boxStyle);
        }
        if (!showLabels) return;
        foreach (var monster in MonsterAI.activeMonsters)
        {
            if (monster == null) continue;
            Vector3 point = mapCamera.WorldToScreenPoint(Lift(monster.transform.position));
            if (point.z <= 0 || !mapCamera.pixelRect.Contains(point)) continue;
            string role = director != null ? director.DebugOrderLabel(monster) : "";
            labelStyle.normal.textColor = role == "추적" ? RouteColors[0] :
                role == "우회" ? RouteColors[1] : Color.white;
            GUI.Label(new Rect(point.x + 12, Screen.height - point.y - 13, 145, 45),
                monster.name.Replace("Monster_", "") + (role.Length > 0 ? " [" + role + "]" : ""), labelStyle);
        }
        if (player != null)
        {
            var point = mapCamera.WorldToScreenPoint(Lift(player.position));
            labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(point.x + 12, Screen.height - point.y - 13, 100, 30), "플레이어", labelStyle);
        }
    }

    private void OnDestroy()
    {
        if (gameplayCamera != null) gameplayCamera.rect = new Rect(0, 0, 1, 1);
        if (reticle != null) reticle.anchoredPosition = originalReticlePosition;
        if (lineMaterial != null) Destroy(lineMaterial);
    }
}
