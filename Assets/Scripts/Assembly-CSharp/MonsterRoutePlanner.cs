using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Reachable route alternatives and symmetric corridor conflicts. No player-facing direction slots.</summary>
public sealed class MonsterRoutePlanner
{
    public sealed class Option
    {
        public MonsterAI monster;
        public Vector3[] corners;
        public Vector3 waypoint, goal;
        public float length, eta, targetEta;
        public Vector3 approachDirection;
        public bool detour, retained;
        internal List<Sample> samples;
        internal NavMeshQueryFilter filter;
    }

    internal struct Sample
    {
        public Vector3 point, direction;
        public float fromGoal, weight;
    }

    public const float Convergence = 2.5f;
    private readonly List<Vector3> anchors = new List<Vector3>();
    private readonly NavMeshPath buffer = new NavMeshPath();
    private readonly Dictionary<(Vector3, Vector3, int, int, int), Vector3[]> pathCache = new Dictionary<(Vector3, Vector3, int, int, int), Vector3[]>();
    public int PathQueries { get; private set; }
    public int AnchorCount => anchors.Count;

    public void Rebuild()
    {
        anchors.Clear();
        var mesh = NavMesh.CalculateTriangulation();
        var cells = new HashSet<Vector3Int>();
        for (int i = 0; i < mesh.indices.Length; i += 3)
        {
            Vector3 a = mesh.vertices[mesh.indices[i]], b = mesh.vertices[mesh.indices[i + 1]], c = mesh.vertices[mesh.indices[i + 2]];
            AddAnchor((a + b + c) / 3f, cells);
            AddAnchor((a + b) * .5f, cells);
            AddAnchor((b + c) * .5f, cells);
            AddAnchor((c + a) * .5f, cells);
        }
    }

    private void AddAnchor(Vector3 p, HashSet<Vector3Int> cells)
    {
        var cell = new Vector3Int(Mathf.FloorToInt(p.x / 5f), Mathf.FloorToInt(p.y / 2f), Mathf.FloorToInt(p.z / 5f));
        if (cells.Add(cell)) { anchors.Add(p); }
    }

    public void BeginPlan() { PathQueries = 0; pathCache.Clear(); }

    public Vector3[] Path(MonsterAI m, Vector3 from, Vector3 to)
    {
        var filter = m.NavigationFilter;
        var key = (from, to, filter.agentTypeID, filter.areaMask, m.NavigationCostKey);
        if (pathCache.TryGetValue(key, out var cached)) { return cached; }
        PathQueries++;
        if (!NavMesh.SamplePosition(from, out var a, 2f, filter) ||
            !NavMesh.SamplePosition(to, out var b, 3f, filter) ||
            !NavMesh.CalculatePath(a.position, b.position, filter, buffer) ||
            buffer.status != NavMeshPathStatus.PathComplete) { pathCache[key] = null; return null; }
        var corners = buffer.corners;
        var result = corners.Length >= 2 ? corners : new[] { a.position, b.position };
        pathCache[key] = result;
        return result;
    }

    public List<Option> Build(MonsterAI m, Vector3 target, float maxLength)
    {
        var result = new List<Option>();
        Vector3[] direct = Path(m, m.transform.position, target);
        if (direct == null) { return result; }
        result.Add(Make(m, direct, target, false, false));

        // A retained route competes in the same assignment as new routes; it has no exemption from overlap checks.
        if (m.TryGetTacticalWaypoint(out var retained))
        {
            AddVia(result, m, target, retained, maxLength, true);
        }
        var nearby = new List<Vector3>();
        foreach (var p in anchors)
        {
            float d = Vector3.Distance(p, target);
            if (d >= 7f && d <= 42f) { nearby.Add(p); }
        }
        // Spatially distributed anchors are taken from actual walkable triangles, including around bends.
        nearby.Sort((a, b) => Vector3.SqrMagnitude(a - target).CompareTo(Vector3.SqrMagnitude(b - target)));
        var usedCells = new HashSet<Vector3Int>();
        var spatial = new List<Vector3>();
        foreach (var p in nearby)
        {
            var cell = new Vector3Int(Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.y / 3f), Mathf.FloorToInt(p.z / 10f));
            if (!usedCells.Add(cell)) { continue; }
            spatial.Add(p);
        }
        // Bound synchronous work. Keep nearby junctions, then distribute the rest across the full radius;
        // simply taking the nearest 20 would miss alternative entrances around large obstacles.
        const int budget = 20, nearCount = 6;
        int count = Mathf.Min(budget, spatial.Count);
        for (int i = 0; i < count; i++)
        {
            int index = spatial.Count <= budget || i < nearCount ? i :
                nearCount + Mathf.RoundToInt((i - nearCount) * (spatial.Count - nearCount - 1f) / (budget - nearCount - 1));
            AddVia(result, m, target, spatial[index], maxLength, false);
        }
        result.Sort((a, b) => (a.length - (a.retained ? 10f : 0f)).CompareTo(b.length - (b.retained ? 10f : 0f)));
        // Keep alternatives from different approach corridors, not eight waypoints on the same corridor.
        var diverse = new List<Option> { result.Find(o => !o.detour) };
        foreach (var option in result)
        {
            if (diverse.Contains(option)) { continue; }
            bool duplicate = false;
            foreach (var kept in diverse)
            {
                if (SameApproach(option, kept)) { duplicate = true; break; }
            }
            if (!duplicate || option.retained) { diverse.Add(option); }
            if (diverse.Count >= 7) { break; }
        }
        return diverse;
    }

    /// <summary>
    /// Connected exits around the latest evidence. These routes end at an exit/cutoff instead of making every
    /// supporter converge on the evidence point. Direction is evidence-derived and never a fixed world-space slot.
    /// </summary>
    public List<Option> BuildCutoffs(MonsterAI m, Vector3 evidence, Vector3 travelDirection,
        float maxLength, Vector3? retainedGoal)
    {
        var result = new List<Option>();
        var goals = new List<Vector3>();
        if (retainedGoal.HasValue) goals.Add(retainedGoal.Value);
        foreach (var p in anchors)
        {
            float distance = Vector3.Distance(p, evidence);
            if (distance >= 9f && distance <= 36f) goals.Add(p);
        }
        Vector3 flatDirection = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
        goals.Sort((a, b) =>
        {
            float da = Vector3.Distance(a, evidence), db = Vector3.Distance(b, evidence);
            float forwardA = flatDirection.sqrMagnitude > .1f ? Vector3.Dot((a - evidence).normalized, flatDirection) : 0f;
            float forwardB = flatDirection.sqrMagnitude > .1f ? Vector3.Dot((b - evidence).normalized, flatDirection) : 0f;
            float retainedA = retainedGoal.HasValue && Vector3.Distance(a, retainedGoal.Value) < 1f ? -20f : 0f;
            float retainedB = retainedGoal.HasValue && Vector3.Distance(b, retainedGoal.Value) < 1f ? -20f : 0f;
            return (da - forwardA * 10f + retainedA).CompareTo(db - forwardB * 10f + retainedB);
        });

        var cells = new HashSet<Vector3Int>();
        int tested = 0;
        foreach (var goal in goals)
        {
            if (++tested > 28) break;
            float radial = Vector3.Distance(goal, evidence);
            if (radial < 8f || radial > 42f) continue;
            var cell = new Vector3Int(Mathf.FloorToInt(goal.x / 6f), Mathf.FloorToInt(goal.y / 3f), Mathf.FloorToInt(goal.z / 6f));
            if (!cells.Add(cell)) continue;
            var evidenceRoute = Path(m, evidence, goal);
            if (evidenceRoute == null || Length(evidenceRoute) > 48f) continue;
            Vector3 exitDirection = ExitDirection(evidenceRoute);
            if (flatDirection.sqrMagnitude > .1f && Vector3.Dot(exitDirection, flatDirection) < -.35f &&
                !(retainedGoal.HasValue && Vector3.Distance(goal, retainedGoal.Value) < 1f)) continue;
            var monsterRoute = Path(m, m.transform.position, goal);
            if (monsterRoute == null || Length(monsterRoute) > maxLength) continue;
            // A cutoff route that passes through the evidence point is merely another follower.
            bool followsThroughEvidence = false;
            for (int i = 1; i < monsterRoute.Length; i++)
                if (DistanceToSegment(evidence, monsterRoute[i - 1], monsterRoute[i]) < 4.5f)
                { followsThroughEvidence = true; break; }
            if (followsThroughEvidence) continue;
            var option = Make(m, monsterRoute, goal, true,
                retainedGoal.HasValue && Vector3.Distance(goal, retainedGoal.Value) < 1f);
            option.targetEta = Length(evidenceRoute) / 11f;
            option.approachDirection = exitDirection;
            result.Add(option);
        }
        result.Sort((a, b) =>
            (a.length - (a.retained ? 35f : 0f) - Vector3.Dot(a.approachDirection, flatDirection) * 8f)
            .CompareTo(b.length - (b.retained ? 35f : 0f) - Vector3.Dot(b.approachDirection, flatDirection) * 8f));
        if (result.Count > 8) result.RemoveRange(8, result.Count - 8);
        return result;
    }

    private static Vector3 ExitDirection(Vector3[] evidenceRoute)
    {
        Vector3 start = evidenceRoute[0];
        for (int i = 1; i < evidenceRoute.Length; i++)
        {
            Vector3 delta = Vector3.ProjectOnPlane(evidenceRoute[i] - start, Vector3.up);
            if (delta.magnitude >= 3f) return delta.normalized;
        }
        return Vector3.ProjectOnPlane(evidenceRoute[evidenceRoute.Length - 1] - start, Vector3.up).normalized;
    }

    private void AddVia(List<Option> list, MonsterAI m, Vector3 target, Vector3 via, float maxLength, bool retained)
    {
        var first = Path(m, m.transform.position, via);
        if (first == null) { return; }
        var second = Path(m, via, target);
        if (second == null || Length(first) + Length(second) > maxLength) { return; }
        // Do not cross the target to get behind it, or walk down a dead end and double back.
        for (int i = 1; i < first.Length; i++)
        {
            if (DistanceToSegment(target, first[i - 1], first[i]) < 4f) { return; }
        }
        var joined = new List<Vector3>(first);
        for (int i = 1; i < second.Length; i++) { joined.Add(second[i]); }
        for (int i = 1; i < joined.Count - 1; i++)
        {
            if (Vector3.Dot((joined[i] - joined[i - 1]).normalized, (joined[i + 1] - joined[i]).normalized) < -.65f) { return; }
        }
        list.Add(Make(m, joined.ToArray(), via, true, retained));
    }

    /// <summary>Walkable staging points on connected routes near the last evidence, not compass slots.</summary>
    public Option Prepare(MonsterAI m, Vector3 target, List<Option> occupied, Vector3? retainedGoal, bool expandSearch = false)
    {
        var nearby = new List<Vector3>();
        if (retainedGoal.HasValue) nearby.Add(retainedGoal.Value);
        foreach (var anchor in anchors)
        {
            float distance = Vector3.Distance(anchor, target);
            if (distance >= 10f && distance <= (expandSearch ? 50f : 35f)) nearby.Add(anchor);
        }
        nearby.Sort((a, b) => (Vector3.Distance(a, m.transform.position) + Mathf.Abs(Vector3.Distance(a, target) - 18f))
            .CompareTo(Vector3.Distance(b, m.transform.position) + Mathf.Abs(Vector3.Distance(b, target) - 18f)));
        var cells = new HashSet<Vector3Int>();
        Option best = null; float bestScore = float.NegativeInfinity; int tested = 0;
        // Include an existing destination first so nearby equivalent samples do not churn the route.
        if (retainedGoal.HasValue && !expandSearch) nearby.Insert(0, retainedGoal.Value);
        if (expandSearch)
        {
            // A long wait must inspect different corridors, not repeat the same nearest samples.
            var distributed = new List<Vector3>();
            var spreadCells = new HashSet<Vector3Int>();
            foreach (var p in nearby)
            {
                if (Vector3.Distance(p, m.transform.position) < 3f) continue;
                var cell = new Vector3Int(Mathf.FloorToInt(p.x / 6), Mathf.FloorToInt(p.y / 3), Mathf.FloorToInt(p.z / 6));
                if (spreadCells.Add(cell)) distributed.Add(p);
            }
            nearby.Clear();
            int count = Mathf.Min(24, distributed.Count);
            for (int i = 0; i < count; i++)
            {
                int index = distributed.Count <= 24 || i < 8 ? i :
                    8 + Mathf.RoundToInt((i - 8) * (distributed.Count - 9f) / 15f);
                nearby.Add(distributed[index]);
            }
        }
        foreach (var point in nearby)
        {
            if (Vector3.Distance(point, target) < 10f || Vector3.Distance(point, target) > (expandSearch ? 50f : 35f)) continue;
            var cell = new Vector3Int(Mathf.FloorToInt(point.x / 6), Mathf.FloorToInt(point.y / 3), Mathf.FloorToInt(point.z / 6));
            if (!cells.Add(cell)) continue;
            if (++tested > (expandSearch ? 24 : 16)) break;
            var fromTarget = Path(m, target, point);
            if (fromTarget == null || Length(fromTarget) > (expandSearch ? 65f : 45f)) continue;
            var path = Path(m, m.transform.position, point);
            if (path == null) continue;
            bool unsafeRoute = false;
            for (int i = 1; i < path.Length; i++)
                if (DistanceToSegment(target, path[i - 1], path[i]) < 6f) { unsafeRoute = true; break; }
            if (unsafeRoute) continue;
            var option = Make(m, path, point, false, false);
            foreach (var other in occupied)
            {
                if (Vector3.Distance(point, other.goal) < 7f || Conflict(option, other, out _)) { unsafeRoute = true; break; }
                // A preparation point must not be on an active approach even if its owner arrives later.
                for (int i = 1; i < other.corners.Length; i++)
                    if (DistanceToSegment(point, other.corners[i - 1], other.corners[i]) < 3f &&
                        !NavMesh.Raycast(point, other.corners[i], out _, m.NavigationFilter)) { unsafeRoute = true; break; }
                if (unsafeRoute) break;
            }
            if (unsafeRoute) continue;
            float score = -option.length / 14f - Mathf.Abs(Length(fromTarget) - 18f) / 10f;
            if (!expandSearch && retainedGoal.HasValue && Vector3.Distance(point, retainedGoal.Value) < 1f) score += 2f;
            if (score > bestScore) { bestScore = score; best = option; }
        }
        return best;
    }

    /// <summary>Travel estimate includes the actual speed curve and bends; not a guaranteed arrival time.</summary>
    public static float EstimateTravelTime(Option option, float nearSpeed, float farSpeed, float nearDistance, float farDistance)
    {
        float time = 0;
        nearSpeed = Mathf.Max(1f, nearSpeed); farSpeed = Mathf.Max(nearSpeed, farSpeed);
        farDistance = Mathf.Max(nearDistance + .1f, farDistance);
        for (int i = 1; i < option.corners.Length; i++)
        {
            Vector3 from = option.corners[i - 1], to = option.corners[i];
            float length = Vector3.Distance(from, to);
            int samples = Mathf.Max(1, Mathf.CeilToInt(length / 5f));
            for (int j = 0; j < samples; j++)
            {
                Vector3 point = Vector3.Lerp(from, to, (j + .5f) / samples);
                float speed = Mathf.Lerp(nearSpeed, farSpeed, Mathf.InverseLerp(nearDistance, farDistance, Vector3.Distance(point, option.goal)));
                time += length / samples / speed;
            }
            if (i < option.corners.Length - 1)
                time += Vector3.Angle(to - from, option.corners[i + 1] - to) / 90f * .12f;
        }
        return time;
    }

    public static Option Make(MonsterAI m, Vector3[] corners, Vector3 waypoint, bool detour, bool retained)
    {
        var o = new Option { monster = m, corners = corners, waypoint = waypoint, goal = corners[corners.Length - 1], length = Length(corners), detour = detour, retained = retained, filter = m.NavigationFilter };
        if (corners.Length >= 2) o.approachDirection = Vector3.ProjectOnPlane(corners[corners.Length - 1] - corners[corners.Length - 2], Vector3.up).normalized;
        o.samples = Samples(corners);
        return o;
    }

    public static float Length(Vector3[] points)
    {
        float length = 0;
        if (points != null) { for (int i = 1; i < points.Length; i++) { length += Vector3.Distance(points[i - 1], points[i]); } }
        return length;
    }

    private static List<Sample> Samples(Vector3[] path)
    {
        var samples = new List<Sample>();
        float remaining = Length(path);
        for (int i = 1; i < path.Length; i++)
        {
            var delta = path[i] - path[i - 1];
            float length = delta.magnitude;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / 1.5f));
            for (int j = 0; j < count; j++)
            {
                float t = (j + .5f) / count;
                samples.Add(new Sample { point = Vector3.Lerp(path[i - 1], path[i], t), direction = delta.normalized, fromGoal = remaining - length * t, weight = length / count });
            }
            remaining -= length;
        }
        return samples;
    }

    private static bool SameApproach(Option a, Option b)
    {
        Vector3 pa = AtDistanceFromGoal(a.corners, 7f), pb = AtDistanceFromGoal(b.corners, 7f);
        return Vector3.Distance(pa, pb) < 3f && !NavMesh.Raycast(pa, pb, out _, a.filter);
    }

    public static Vector3 AtDistanceFromGoal(Vector3[] path, float distance)
    {
        for (int i = path.Length - 1; i > 0; i--)
        {
            float length = Vector3.Distance(path[i], path[i - 1]);
            if (length >= distance) { return Vector3.Lerp(path[i], path[i - 1], distance / Mathf.Max(.001f, length)); }
            distance -= length;
        }
        return path[0];
    }

    /// <summary>Symmetric. Long shared stretches and shared terminal corridors cannot be diluted by a long detour.</summary>
    public static bool Conflict(Option a, Option b, out float overlap)
    {
        float ab = Shared(a, b, out float runAB, out float tailAB);
        float ba = Shared(b, a, out float runBA, out float tailBA);
        overlap = Mathf.Max(ab, ba);
        return overlap > .38f || Mathf.Max(runAB, runBA) >= 5f || Mathf.Max(tailAB, tailBA) >= 3f;
    }

    private static float Shared(Option a, Option b, out float longestRun, out float tail)
    {
        float considered = 0, matched = 0, run = 0;
        longestRun = 0; tail = 0;
        foreach (var sa in a.samples)
        {
            if (sa.fromGoal <= Convergence) { continue; }
            considered += sa.weight;
            bool shares = false;
            foreach (var sb in b.samples)
            {
                if (sb.fromGoal <= Convergence || Vector3.Dot(sa.direction, sb.direction) < .65f ||
                    Mathf.Abs(sa.point.y - sb.point.y) > 1.5f || Vector3.SqrMagnitude(sa.point - sb.point) > 9f) { continue; }
                // Nearby lanes across a wall are different corridors. Orthogonal crossings are not following.
                if (!NavMesh.Raycast(sa.point, sb.point, out _, a.filter)) { shares = true; break; }
            }
            if (shares)
            {
                matched += sa.weight; run += sa.weight;
                longestRun = Mathf.Max(longestRun, run);
                if (sa.fromGoal <= 9f) { tail += sa.weight; }
            }
            else { run = 0; }
        }
        return considered > 0 ? matched / considered : 0;
    }

    public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var d = b - a;
        return Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(.0001f, d.sqrMagnitude)));
    }
}
