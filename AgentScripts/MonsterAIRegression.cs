using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;

// Run through Unity CLI run_script in the paused ShooterInGame_AITest scene. No scene assets are saved.
public static class MonsterAIRegression
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Call(object o, string name) { o.GetType().GetMethod(name, Private).Invoke(o, null); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static Vector3 OnMesh(MonsterAI m, Vector3 point)
    {
        Assert(NavMesh.SamplePosition(point, out var hit, 8f, m.NavigationFilter), "No NavMesh at " + point);
        return hit.position;
    }
    public static async Task<object> MovingScenario()
    {
        Assert(EditorApplication.isPlaying, "Requires Play mode");
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        GameFlowManager.Instance.DebugBeginTest();
        PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "MovingScenario");
        EditorApplication.isPaused = true;
        var d = MonsterDirector.Instance;
        d.AbortHunt();
        MonsterAI.DebugPlayerInvisible = true;
        var origins = new Dictionary<string, Vector3> {
            { "Monster_C", new Vector3(10,0,1) }, { "Monster_Global", new Vector3(41,1,1) },
            { "Monster_A", new Vector3(43,1,13) }, { "Monster_B", new Vector3(-63,0,30) }, { "Monster_D", new Vector3(15,2,-72) }
        };
        foreach (var m in MonsterAI.activeMonsters)
        {
            m.ResetMonster(); m.showDebugLog = false;
            m.GetComponent<NavMeshAgent>().Warp(OnMesh(m, origins[m.name]));
        }
        var reference = MonsterAI.activeMonsters[0];
        var player = reference.player;
        var body = player.GetComponent<Rigidbody>();
        Vector3 pos = OnMesh(reference, new Vector3(-3,0,1));
        var planner = new MonsterRoutePlanner();
        var movementPoints = new List<Vector3> { pos };
        foreach (var destination in new[] { new Vector3(-12,0,40), new Vector3(55,0,5), new Vector3(20,0,-50) })
        {
            var segment=planner.Path(reference,movementPoints[movementPoints.Count-1],destination);
            Assert(segment!=null,"Movement path unavailable to "+destination);
            movementPoints.AddRange(segment.Skip(1));
        }
        var movement=movementPoints.ToArray();
        player.position = body.position = pos;
        Physics.SyncTransforms();
        d.ReportTargetAlarm(pos, Time.time);
        Call(d, "Coordinate");
        var report = new List<object>();
        float start = Time.time, previous = start, nextLog = start, nextShot = start;
        int corner = 1;
        try
        {
            EditorApplication.isPaused = false;
            float deadline = Time.realtimeSinceStartup + 20;
            while (Time.time - start < 12 && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(50);
                float dt = Mathf.Min(.2f, Time.time - previous); previous = Time.time;
                // First 4 seconds: shooting at a target. Then run at actual player speed on a valid route.
                if (Time.time - start > 4 && corner < movement.Length)
                {
                    pos = Vector3.MoveTowards(pos, movement[corner], 11f * dt);
                    if (Vector3.Distance(pos,movement[corner]) < .1f) corner++;
                    body.position = player.position = pos;
                    Physics.SyncTransforms();
                }
                if (Time.time >= nextShot)
                {
                    nextShot = Time.time + 1.5f;
                    if (Time.time - start > 4)
                    {
                        var observer=d.DebugTeam.FirstOrDefault(x=>x.CanReceiveTactics && !x.IsHomeLocked)
                            ?? MonsterAI.activeMonsters.First(x=>x.CanReceiveTactics && !x.IsHomeLocked);
                        d.ReportSighting(observer,pos);
                    }
                    else d.ReportTargetAlarm(pos, Time.time);
                }
                if (Time.time >= nextLog)
                {
                    Assert(d.DebugRoutes.Count(x=>x.Value.directChaser)==1, "Fresh alarm lost the pressure assignment");
                    Assert(d.DebugRoutes.Count<=3, "Team size exceeded");
                    nextLog = Time.time + 1f;
                    report.Add(new { t=Time.time-start, player=pos.ToString(), summary=d.DebugLayoutSummary,
                        ms=d.LastPlanMilliseconds, queries=d.DebugPathQueries,
                        routes=d.DebugRoutes.Select(x=>new {monster=x.Key.name,x.Value.directChaser,x.Value.eta,goal=x.Value.goal.ToString()}).ToArray(),
                        monsters=MonsterAI.activeMonsters.Select(m=>new {m.name,state=m.StateLabel,pos=m.transform.position.ToString(),speed=m.PlanarVelocity.magnitude,dest=m.DebugDestination.ToString()}).ToArray() });
                }
            }
        }
        finally { EditorApplication.isPaused = true; MonsterAI.DebugPlayerInvisible = false; Application.runInBackground = background; }
        Assert(Time.time - start >= 11f, "Simulation did not advance; no runtime validation occurred");
        return report;
    }
    public static object StaticScenarios()
    {
        Assert(EditorApplication.isPlaying && EditorApplication.isPaused, "Requires paused Play mode");
        var d = MonsterDirector.Instance;
        var monsters = MonsterAI.activeMonsters.OrderBy(m => m.name).ToArray();
        var player = monsters[0].player;
        var report = new List<object>();
        Vector3[] targets = { new Vector3(-3,0,1), new Vector3(-50,0,20), new Vector3(10,0,-45), new Vector3(20,0,50), new Vector3(65,0,0), new Vector3(28,0,92) };
        var origins = new Dictionary<string, Vector3> {
            { "Monster_C", new Vector3(10,0,1) }, { "Monster_Global", new Vector3(41,1,1) },
            { "Monster_A", new Vector3(43,1,13) }, { "Monster_B", new Vector3(-63,0,30) }, { "Monster_D", new Vector3(15,2,-72) }
        };
        foreach (var target in targets)
        {
            d.AbortHunt();
            foreach (var m in monsters)
            {
                m.ResetMonster();
                Assert(m.zoneCenter != null && !m.zoneCenter.IsChildOf(m.transform), "Home must not move with the monster");
                Assert(m.GetComponent<NavMeshAgent>().Warp(OnMesh(m, origins[m.name])), "Warp failed");
            }
            var location = OnMesh(monsters[0], target);
            player.position = location;
            player.GetComponent<Rigidbody>().position = location;
            Physics.SyncTransforms();
            d.ReportTargetAlarm(location, Time.time);
            Call(d, "Coordinate");
            Assert(d.DebugTeam.Count >= 1 && d.DebugTeam.Count <= 3, "Team bounds");
            Assert(d.DebugRoutes.Count(x => x.Value.directChaser) == 1, "Must have one pressure route");
            var settings=monsters.First(m=>m.role==MonsterAI.MonsterRole.Global_Stalker);
            float routeLimit=Mathf.Max(settings.huntNearSpeed,settings.huntFarSpeed)*Mathf.Clamp(settings.detourTimeLimit,3f,12f);
            Assert(d.DebugRoutes.Where(x=>!x.Value.directChaser).All(x=>MonsterRoutePlanner.Length(x.Value.corners)<=routeLimit+.01f), "Support exceeds configured route budget");
            var assigned = d.DebugRoutes.Select(k => MonsterRoutePlanner.Make(k.Key, k.Value.corners, k.Value.waypoint, k.Value.detour, false)).ToArray();
            for (int i = 0; i < assigned.Length; i++)
                for (int j = i + 1; j < assigned.Length; j++)
                    Assert(!MonsterRoutePlanner.Conflict(assigned[i], assigned[j], out _), "Conflicting assignment");
            report.Add(new { target = location.ToString(), team = d.DebugTeam.Select(m=>m.name).ToArray(),
                routes = d.DebugRoutes.Select(k=> new { monster=k.Key.name,k.Value.detour,k.Value.overlap,k.Value.eta,length=MonsterRoutePlanner.Length(k.Value.corners),corners=k.Value.corners.Select(v=>v.ToString()).ToArray() }).ToArray(),
                ms=d.LastPlanMilliseconds, queries=d.DebugPathQueries });
        }
        return report;
    }

    public static object CorridorRegression()
    {
        var m = MonsterAI.activeMonsters[0];
        var planner = new MonsterRoutePlanner();
        var shortPath = planner.Path(m, new Vector3(10,0,1), new Vector3(-3,0,1));
        var longPath = planner.Path(m, new Vector3(43,1,13), new Vector3(-3,0,1));
        Assert(shortPath != null && longPath != null, "Fixture paths unavailable");
        var a = MonsterRoutePlanner.Make(m, shortPath, Vector3.zero, false, false);
        var b = MonsterRoutePlanner.Make(m, longPath, Vector3.zero, false, false);
        bool ab = MonsterRoutePlanner.Conflict(a,b,out float rateAB), ba = MonsterRoutePlanner.Conflict(b,a,out float rateBA);
        Assert(ab && ba && Mathf.Abs(rateAB-rateBA)<.0001f, "Shared terminal corridor must conflict symmetrically");
        Assert(planner.Path(m,new Vector3(10000,0,10000),Vector3.zero)==null,"Unreachable origin accepted");
        return new { sharedCorridorRejected=ab, symmetricOverlap=rateAB, shortLength=a.length,longLength=b.length };
    }

    public static async Task<object> StunRegression()
    {
        Assert(EditorApplication.isPlaying && EditorApplication.isPaused,"Requires paused Play mode");
        bool background=Application.runInBackground;
        var d=MonsterDirector.Instance;
        var m=d.DebugTeam.FirstOrDefault();
        Assert(m!=null,"Run the moving scenario first");
        float start=Time.time;
        try
        {
            Application.runInBackground=true;
            m.OnHitByLaser(m.player.position);
            Assert(m.IsInStun && !m.CanReceiveTactics,"Stun did not exclude orders");
            Assert(typeof(MonsterAI).GetMethod("TeleportHome")==null,"Gameplay teleport API was restored");
            Call(d,"Coordinate");
            Assert(!d.DebugTeam.Contains(m),"Stunned monster retained a slot");
            EditorApplication.isPaused=false;
            float deadline=Time.realtimeSinceStartup+8;
            while(Time.time-start < m.stunFreezeTime+m.stunRecoverTime+.5f && Time.realtimeSinceStartup<deadline)
            {
                d.ReportTargetAlarm(m.player.position,Time.time);
                await Task.Delay(100);
            }
            Assert(!m.IsInStun,"Stun did not recover");
            return new { stunExcluded=true,stunCannotTeleport=true,recovered=true,elapsed=Time.time-start,state=m.StateLabel };
        }
        finally { EditorApplication.isPaused=true; Application.runInBackground=background; }
    }

    public static async Task<object> FollowingRegression()
    {
        Assert(EditorApplication.isPlaying && EditorApplication.isPaused,"Requires paused Play mode");
        // Independent fixture: a previous collision scenario may have ended the round.
        GameFlowManager.Instance.DebugBeginTest();
        PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "FollowingRegression");
        bool godMode=PlayerHealth.DebugGodMode;
        PlayerHealth.DebugGodMode=true;
        var d=MonsterDirector.Instance;
        bool background=Application.runInBackground;
        d.AbortHunt();
        foreach(var m in MonsterAI.activeMonsters) m.ResetMonster();
        var front=MonsterAI.activeMonsters.First(m=>m.name=="Monster_C");
        var rear=MonsterAI.activeMonsters.First(m=>m.name=="Monster_Global");
        var position=OnMesh(front,new Vector3(-3,0,1));
        front.player.position=front.player.GetComponent<Rigidbody>().position=position;
        front.GetComponent<NavMeshAgent>().Warp(OnMesh(front,new Vector3(20,0,1)));
        rear.GetComponent<NavMeshAgent>().Warp(OnMesh(rear,new Vector3(25,0,1)));
        front.GetComponent<Rigidbody>().position=front.transform.position;
        rear.GetComponent<Rigidbody>().position=rear.transform.position;
        Physics.SyncTransforms();
        d.ReportTargetAlarm(position,Time.time);
        typeof(MonsterDirector).GetField("pressure",Private).SetValue(d,front);
        var team=(List<MonsterAI>)typeof(MonsterDirector).GetField("team",Private).GetValue(d);
        team.Add(front); team.Add(rear);
        front.CommandPressure(position);
        var path=new MonsterRoutePlanner().Path(rear,rear.transform.position,position);
        rear.CommandTacticalRoute(path,position,false);
        front.SetHuntSpeed(10); rear.SetHuntSpeed(10);
        int before=d.DebugFollowBreaks;
        float start=Time.time;
        try
        {
            // Deliberately inject an invalid following route to test the runtime safety net independently of the planner.
            d.enabled=false; Application.runInBackground=true; MonsterAI.DebugPlayerInvisible=true;
            EditorApplication.isPaused=false;
            float deadline=Time.realtimeSinceStartup+6;
            while(Time.time-start<3 && Time.realtimeSinceStartup<deadline && d.DebugFollowBreaks==before)
            {
                d.ReportTargetAlarm(position,Time.time);
                Call(d,"CheckFollowing");
                await Task.Delay(100);
            }
            Assert(d.DebugFollowBreaks==before+1,"Sustained same-lane following not broken");
            Assert(rear.CurrentState==MonsterAI.State.Prepare,"Rear support must leave the queue to prepare a different approach");
            Assert(front.CurrentState==MonsterAI.State.Chase,"Pressure pursuer must continue");
            return new {followingBroken=true,pressurePreserved=true,elapsed=Time.time-start};
        }
        finally
        {
            EditorApplication.isPaused=true; Application.runInBackground=background;
            d.enabled=true; MonsterAI.DebugPlayerInvisible=false;
            PlayerHealth.DebugGodMode=godMode;
        }
    }

    public static object ReturnAndKnowledgeRegression()
    {
        Assert(EditorApplication.isPlaying && EditorApplication.isPaused, "Requires paused Play mode");
        var d = MonsterDirector.Instance;
        d.AbortHunt();
        foreach (var other in MonsterAI.activeMonsters) other.ResetMonster();
        Assert(typeof(MonsterAI).GetMethod("TeleportHome") == null, "Gameplay teleport API still exists");
        Assert(typeof(MonsterDirector).GetMethod("UpdateReturns", Private) == null, "Teleport scheduler still exists");
        var m = MonsterAI.activeMonsters.First(x => x.name == "Monster_C");
        var source = OnMesh(m, new Vector3(10,0,1));
        m.GetComponent<NavMeshAgent>().Warp(source);
        m.CommandGoHome(0);
        Assert(Vector3.Distance(m.transform.position,source)<.2f, "Return changed position");
        Assert(Vector3.Distance(m.GetComponent<NavMeshAgent>().destination,m.zoneCenter.position)<3f, "Return must walk home");
        d.ReportTargetAlarm(source,Time.time); Call(d,"Coordinate");
        var hunter=d.DebugTeam[0];
        Assert(d.TryGetPursuitHint(hunter,out var hint) && Vector3.Distance(hint,source)<.1f,"Wrong evidence");
        d.ReportTargetAlarm(m.zoneCenter.position,Time.time-1);
        Assert(d.TryGetPursuitHint(hunter,out hint) && Vector3.Distance(hint,source)<.1f,"Old evidence replaced new");
        typeof(MonsterDirector).GetField("knowledgeTime",Private).SetValue(d,Time.time-20);
        Call(d,"Update");
        Assert(!d.IsHunting && d.DebugTeam.Count==0 && d.DebugPreparations.Count==0,"Hunt/preparation must expire");
        return new {teleportRemoved=true,returnWalksHome=true,oldShotIgnored=true,knowledgeExpired=true};
    }
}
