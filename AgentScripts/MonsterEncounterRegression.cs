using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Ephemeral CLI test. Paused Play mode; never saves scene assets.
public static class MonsterEncounterRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private).Invoke(o, args);
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static Vector3 Mesh(MonsterAI m, Vector3 p)
    {
        Assert(NavMesh.SamplePosition(p, out var h, 8f, m.NavigationFilter), "No nav at " + p);
        return h.position;
    }
    static void Warp(MonsterAI m, Vector3 p)
    {
        var v = Mesh(m, p); Assert(m.GetComponent<NavMeshAgent>().Warp(v), "Warp failed");
        m.transform.position = v; m.GetComponent<Rigidbody>().position = v;
        var dir = m.player.position - v; dir.y = 0;
        if (dir.sqrMagnitude > .01f) m.transform.rotation = Quaternion.LookRotation(dir);
    }
    static void TeamValid(MonsterDirector d)
    {
        Assert(d.DebugTeam.Count >= 1 && d.DebugTeam.Count <= 3, "Invalid team size");
        Assert(MonsterAI.activeMonsters.Count(m => m.CurrentState == MonsterAI.State.Chase) == 1, "Must have one actual pursuer");
        var routes = d.DebugRoutes.Select(p => MonsterRoutePlanner.Make(p.Key,p.Value.corners,p.Value.waypoint,p.Value.detour,false)).ToArray();
        for (int i=0;i<routes.Length;i++) for (int j=i+1;j<routes.Length;j++)
            Assert(!MonsterRoutePlanner.Conflict(routes[i],routes[j],out _),"Conflicting corridors");
    }
    public static object Main()
    {
        Assert(EditorApplication.isPlaying, "Needs play mode");
        GameFlowManager.Instance.DebugBeginTest();
        EditorApplication.isPaused = true;
        PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "EncounterHandoffZones");
        var d = MonsterDirector.Instance;
        var all = MonsterAI.activeMonsters.ToArray();
        var masks = all.ToDictionary(m=>m,m=>m.obstacleMask);
        var c=all.First(m=>m.name=="Monster_C"); var a=all.First(m=>m.name=="Monster_A");
        var b=all.First(m=>m.name=="Monster_B"); var near=all.First(m=>m.name=="Monster_D");
        var checks=new List<string>();
        var settings=all.First(m=>m.role==MonsterAI.MonsterRole.Global_Stalker);
        int teamSize=settings.huntTeamSize;
        void Reset()
        {
            d.AbortHunt(); MonsterAI.DebugPlayerInvisible=false;
            foreach(var m in all) { m.ResetMonster(); m.obstacleMask=0; m.showDebugLog=false; m.CommandGoHome(30f); }
            var p=Mesh(c,new Vector3(-3,0,1)); c.player.position=p; c.player.GetComponent<Rigidbody>().position=p;
            Set(d,"player",c.player); Physics.SyncTransforms();
        }
        void PressureAt(Vector3 position)
        {
            c.ResetMonster(); Warp(c,position);
            d.ReportSighting(c,c.player.position); Call(d,"Coordinate");
            Assert(c.CurrentState==MonsterAI.State.Chase,"Fixture pressure not C");
        }
        try
        {
            Reset(); PressureAt(new Vector3(20,0,1)); Warp(near,new Vector3(3,0,1)); near.CommandGoHome(30f);
            var blocked=(Dictionary<MonsterAI,float>)typeof(MonsterDirector).GetField("blockedUntil",Private).GetValue(d);
            blocked[near]=Time.time+30;
            Assert(near.HasCloseVisibleEncounter,"Fixture encounter not visible");
            Call(near,"Update");
            Assert(!near.IsHomeLocked && near.CurrentState==MonsterAI.State.Chase,"Locked return failed to take pressure");
            TeamValid(d); checks.Add("close visible return bypasses both locks and takes pressure immediately");

            Reset(); settings.huntTeamSize=1;
            PressureAt(new Vector3(-1,0,1)); Warp(near,new Vector3(3,0,1)); near.CommandGoHome(30f);
            Call(near,"Update");
            Assert(c.CurrentState==MonsterAI.State.Chase,"Closer pressure should stay");
            Assert(near.CurrentState==MonsterAI.State.Prepare && near.GetComponent<NavMeshAgent>().isStopped,"Encounter must guard without forming a queue: " + near.StateLabel);
            TeamValid(d); checks.Add("same corridor encounter guards instead of walking past or following");
            MonsterAI.DebugPlayerInvisible=true; d.AbortHunt(); Call(near,"Update");
            Assert(near.CurrentState==MonsterAI.State.Return && !near.GetComponent<NavMeshAgent>().isStopped,"Expired hunt must resume walking home");
            checks.Add("preparation resumes walking home when hunt ends");
            settings.huntTeamSize=teamSize;

            Reset(); PressureAt(new Vector3(20,0,1)); Warp(near,new Vector3(3,0,1));
            near.CommandGoHome(30f); MonsterAI.DebugPlayerInvisible=true;
            d.ReportReturnEncounter(near);
            Assert(near.IsHomeLocked,"Invisible encounter unlocked return");
            MonsterAI.DebugPlayerInvisible=false; Set(near,"isJumping",true); d.ReportReturnEncounter(near);
            Assert(near.IsHomeLocked,"Jump interrupted"); Set(near,"isJumping",false);
            Set(near,"isStunned",true); d.ReportReturnEncounter(near);
            Assert(near.IsHomeLocked,"Stun interrupted"); Set(near,"isStunned",false);
            checks.Add("invisible, jumping and stunned actors cannot force a handoff");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer=31; wall.transform.position=(near.transform.position+c.player.position)*.5f+Vector3.up;
                wall.transform.localScale=new Vector3(.5f,5f,10f); near.obstacleMask=1<<31;
                Physics.SyncTransforms();
                Assert(!near.HasCloseVisibleEncounter,"Wall did not occlude fixture");
                d.ReportReturnEncounter(near); Assert(near.IsHomeLocked,"Wall occlusion bypassed return lock");
                checks.Add("near player behind a wall cannot trigger visual handoff");
            }
            finally { UnityEngine.Object.DestroyImmediate(wall); near.obstacleMask=0; }

            Reset(); MonsterAI.DebugPlayerInvisible=true;
            var origins=new Dictionary<string,Vector3> {
                {"Monster_C",new Vector3(10,0,1)}, {"Monster_Global",new Vector3(41,1,1)},
                {"Monster_A",new Vector3(43,1,13)}, {"Monster_B",new Vector3(-63,0,30)}, {"Monster_D",new Vector3(15,2,-72)} };
            foreach(var m in all) { m.ResetMonster(); Warp(m,origins[m.name]); }
            d.ReportTargetAlarm(c.player.position,Time.time); Call(d,"Coordinate"); TeamValid(d);
            Assert(!d.DebugTeam.Contains(near),"D should start outside the team");
            var before=d.DebugTeam.Select(m=>m.name).ToArray();
            float beforeNext=(float)typeof(MonsterDirector).GetField("nextPlan",Private).GetValue(d);
            Warp(a,new Vector3(65,1,13)); Warp(near,new Vector3(40,1,13));
            d.ReportNoise(c.player.position,100f,NoiseKind.Shot);
            float next=(float)typeof(MonsterDirector).GetField("nextPlan",Private).GetValue(d);
            Assert(Mathf.Approximately(next,beforeNext),"Repeated shot revoked corridor missions immediately");
            Call(d,"Coordinate"); TeamValid(d);
            Assert(d.DebugRoutes.Where(x=>!x.Value.directChaser).All(x=>Vector3.Distance(x.Value.goal,c.player.position)>=7f),
                "Shot regrouped support on the evidence point");
            Assert(d.DebugTeam.Contains(c),"Fresh shot displaced the valid pressure pursuer");
            checks.Add("repeated shot refreshes evidence without immediate regroup: "+string.Join(",",before)+" -> "+string.Join(",",d.DebugTeam.Select(m=>m.name)));

            Reset(); MonsterAI.DebugPlayerInvisible=true;
            var homeA=a.zoneCenter; var homeB=b.zoneCenter; float radiusA=a.zoneRadius, radiusB=b.zoneRadius;
            Warp(a,homeB.position); Warp(b,homeA.position); a.CommandGoHome(30f); b.CommandGoHome(30f);
            foreach(var other in all.Where(m=>m!=a && m!=b)) Warp(other,new Vector3(15,0,-72));
            Call(d,"TrySwapReturnZones");
            Assert(a.zoneCenter==homeB && b.zoneCenter==homeA,"Beneficial homes not exchanged");
            Assert(a.zoneRadius==radiusB && b.zoneRadius==radiusA,"Radii not exchanged");
            Assert(all.Where(m=>m.role==MonsterAI.MonsterRole.Zone_Defender).Select(m=>m.zoneCenter).Distinct().Count()==4,"Duplicate zone ownership");
            Call(d,"TrySwapReturnZones"); Assert(a.zoneCenter==homeB,"Swap oscillated");
            a.ResetMonster(); b.ResetMonster(); Assert(a.zoneCenter==homeA && b.zoneCenter==homeB,"Reset did not restore original homes");
            checks.Add("beneficial paired home exchange, unique ownership, cooldown and round reset");

            Reset(); MonsterAI.DebugPlayerInvisible=true;
            Warp(a,homeB.position); Warp(b,homeA.position); a.CommandGoHome(30f);
            b.ResetMonster(); Warp(b,homeA.position); b.CommandPressure(c.player.position);
            foreach(var other in all.Where(m=>m!=a && m!=b)) Warp(other,new Vector3(15,0,-72));
            Vector3 pursuitDestination=b.GetComponent<NavMeshAgent>().destination;
            Call(d,"TrySwapReturnZones");
            Assert(a.zoneCenter==homeB && b.zoneCenter==homeA,"Active partner home not exchanged");
            Assert(b.CurrentState==MonsterAI.State.Chase && Vector3.Distance(b.GetComponent<NavMeshAgent>().destination,pursuitDestination)<.01f,"Home swap interrupted pursuit");
            checks.Add("active partner receives future home without interrupting its pursuit");
            return new { passed=checks.Count, checks };
        }
        finally
        {
            d.AbortHunt(); foreach(var m in all) { m.ResetMonster(); m.obstacleMask=masks[m]; }
            settings.huntTeamSize=teamSize;
            MonsterAI.DebugPlayerInvisible=false;
        }
    }
}
