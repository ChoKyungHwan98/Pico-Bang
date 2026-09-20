using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class PreparationRecoveryRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Call(object o, string name) => o.GetType().GetMethod(name, Private).Invoke(o, null);
    static Vector3 V(JToken t) => new Vector3((float)t["x"], (float)t["y"], (float)t["z"]);
    static void Set(object o, string name, object value) => o.GetType().GetField(name).SetValue(o, value);
    static float Number(object o, string name) => (float)o.GetType().GetField(name).GetValue(o);
    public static object Main()
    {
        Assert(EditorApplication.isPlaying,"Needs Play mode");
        GameFlowManager.Instance.DebugBeginTest(); EditorApplication.isPaused = true;
        PlaytestRecorder.Record("automated_validation","qa",Vector3.zero,"preparation-recovery-and-arrival-budget");
        var d = MonsterDirector.Instance; var monsters = MonsterAI.activeMonsters.ToArray();
        var frames = File.ReadLines("PlaytestRecordings/2026-09-20_23-22-30_c7f603/trace.jsonl")
            .Select(JObject.Parse).Where(x => (string)x["kind"] == "frame").ToArray();
        var reports = new List<object>();
        bool invisible = MonsterAI.DebugPlayerInvisible;
        try
        {
            MonsterAI.DebugPlayerInvisible = true;
            int expandedTotal = 0;
            foreach(float time in new[] { 35.3f, 41.7f, 49f, 54f, 90f })
            {
                d.AbortHunt(); var f = frames.OrderBy(x=>Math.Abs((float)x["t"]-time)).First();
                Vector3 target = V(f["player"]["position"]);
                monsters[0].player.GetComponent<PlayerController>().TeleportTo(target,Quaternion.identity);
                foreach(var m in monsters)
                {
                    m.ResetMonster(); var actor = f["monsters"].First(x=>(string)x["name"]==m.name);
                    Assert(NavMesh.SamplePosition(V(actor["position"]),out var h,3,m.NavigationFilter),"Missing recorded nav");
                    Assert(m.GetComponent<NavMeshAgent>().Warp(h.position),"Warp failed");
                    m.transform.position = m.GetComponent<Rigidbody>().position = h.position;
                }
                Physics.SyncTransforms(); d.ReportTargetAlarm(target,Time.time); Call(d,"Coordinate");
                var progress = (IDictionary)typeof(MonsterDirector).GetField("preparationProgress",Private).GetValue(d);
                // Age genuine preparation records without moving simulation time or inventing a route.
                foreach(DictionaryEntry item in progress) Set(item.Value,"progressedAt",Time.time-3f);
                var ages = new Dictionary<object,float>();
                foreach(DictionaryEntry item in progress) ages[item.Key] = Number(item.Value,"progressedAt");
                Call(d,"Coordinate");
                int expanded = 0;
                foreach(DictionaryEntry item in progress)
                {
                    if (Number(item.Value,"retryAt") > Time.time) expanded++;
                    if (ages.ContainsKey(item.Key)) Assert(Number(item.Value,"progressedAt")==ages[item.Key],"Replanning reset stale timer");
                }
                Assert(expanded <= 1,"Expanded more than one actor in a planning tick"); expandedTotal += expanded;
                var occupied = d.DebugRoutes.Select(x=>MonsterRoutePlanner.Make(x.Key,x.Value.corners,x.Value.waypoint,x.Value.detour,false)).ToList();
                var settings = monsters.First(m=>m.role==MonsterAI.MonsterRole.Global_Stalker);
                foreach(var entry in d.DebugRoutes.Where(x=>!x.Value.directChaser))
                    Assert(entry.Value.eta <= Mathf.Clamp(settings.detourTimeLimit,3,12)+.001f,"Support arrives beyond time budget");
                foreach(var entry in d.DebugPreparations)
                {
                    var route = MonsterRoutePlanner.Make(entry.Key,entry.Value.corners,entry.Value.goal,false,false);
                    if(route.length>.2f)
                    {
                        foreach(var other in occupied) Assert(!MonsterRoutePlanner.Conflict(route,other,out _),"Recovery created same-corridor queue");
                        // A NavMesh ray stops at an OffMeshLink; this map has legitimate links.
                        var reachable = new NavMeshPath();
                        Assert(NavMesh.CalculatePath(entry.Key.transform.position,route.goal,entry.Key.NavigationFilter,reachable)
                            && reachable.status==NavMeshPathStatus.PathComplete,"Recovery destination unreachable");
                    }
                    occupied.Add(route);
                }
                Assert(d.DebugTeam.Count+d.DebugPreparations.Count==5,"Dropped healthy monster");
                reports.Add(new {time,expanded,team=d.DebugTeam.Count,walking=d.DebugPreparations.Count(x=>MonsterRoutePlanner.Length(x.Value.corners)>.2f),ms=d.LastPlanMilliseconds,queries=d.DebugPathQueries});
            }
            Assert(expandedTotal>0,"Recovery never exercised");
            var straight=MonsterRoutePlanner.Make(monsters[0],new[]{Vector3.zero,Vector3.right*30},Vector3.right*30,false,false);
            float eta=MonsterRoutePlanner.EstimateTravelTime(straight,10,14,15,40);
            Assert(eta>30f/14f && eta<=3.01f,"Estimate ignores near-goal slowdown");
            d.AbortHunt();
            Assert(((IDictionary)typeof(MonsterDirector).GetField("preparationProgress",Private).GetValue(d)).Count==0,"Old wait state survives round reset");
            return new {passed=true,expandedTotal,estimatedThirtyMetreSeconds=eta,reports};
        }
        finally { d.AbortHunt(); foreach(var m in monsters)m.ResetMonster(); MonsterAI.DebugPlayerInvisible=invisible; }
    }
}
