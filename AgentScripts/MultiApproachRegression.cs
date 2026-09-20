using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class MultiApproachRegression
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Call(object o,string method) => o.GetType().GetMethod(method,Private).Invoke(o,null);
    static void Set(object o,string field,object value) => o.GetType().GetField(field,Private).SetValue(o,value);
    static Vector3 V(JToken t)=>new Vector3((float)t["x"],(float)t["y"],(float)t["z"]);
    public static async Task<object> SimultaneousClosing()
    {
        Assert(EditorApplication.isPlaying,"Needs play mode");
        var d=MonsterDirector.Instance; var all=MonsterAI.activeMonsters.ToArray();
        bool background=Application.runInBackground;
        var c=all.First(m=>m.name=="Monster_C"); var side=all.First(m=>m.name=="Monster_Global");
        try
        {
            GameFlowManager.Instance.DebugBeginTest(); EditorApplication.isPaused=true;
            PlaytestRecorder.Record("automated_validation","qa",Vector3.zero,"simultaneous-closing");
            d.AbortHunt(); MonsterAI.DebugPlayerInvisible=true;
            foreach(var m in all){m.ResetMonster();m.CommandGoHome(30);}
            var target=new Vector3(20,0,1);
            c.player.GetComponent<PlayerController>().TeleportTo(target,Quaternion.identity);
            foreach(var m in new[]{c,side})
            {
                m.ResetMonster(); var point=m==c?new Vector3(12,0,1):new Vector3(28,0,1);
                Assert(NavMesh.SamplePosition(point,out var hit,3,m.NavigationFilter),"Missing convergence nav");
                m.GetComponent<NavMeshAgent>().Warp(hit.position); m.GetComponent<Rigidbody>().position=hit.position;
            }
            c.CommandPressure(target); Set(d,"pressure",c); Set(d,"pressureSince",Time.time-20);
            ((List<MonsterAI>)typeof(MonsterDirector).GetField("team",Private).GetValue(d)).Add(c);
            d.ReportTargetAlarm(target,Time.time); Call(d,"Coordinate");
            Assert(d.DebugTeam.Contains(side),"Independent side approach was rejected");
            Assert(side.CurrentState==MonsterAI.State.Intercept,"Side arrival should keep its independent approach");
            Vector3 supportGoal=d.DebugRoutes[side].goal;
            Assert(Vector3.Distance(supportGoal,target)>=7f,"Support was sent to the same evidence point");
            Assert(c.CurrentState==MonsterAI.State.Chase,"Side assignment displaced close pursuer");
            float cBefore=Vector3.Distance(c.transform.position,target),sBefore=Vector3.Distance(side.transform.position,supportGoal);
            float start=Time.time; Application.runInBackground=true; EditorApplication.isPaused=false;
            float deadline=Time.realtimeSinceStartup+5;
            while(Time.time-start<.55f && Time.realtimeSinceStartup<deadline){d.ReportTargetAlarm(target,Time.time);await Task.Delay(30);}
            EditorApplication.isPaused=true;
            float cAfter=Vector3.Distance(c.transform.position,target),sAfter=Vector3.Distance(side.transform.position,supportGoal);
            Assert(cBefore-cAfter>2f && sBefore-sAfter>2f,"Pursuit and cutoff must advance simultaneously");
            Assert(c.CurrentState==MonsterAI.State.Chase && side.CurrentState==MonsterAI.State.Intercept,"Arrivals became sequential");
            return new{pressure=c.name,support=side.name,supportGoal,cBefore,cAfter,sBefore,sAfter,simultaneous=true};
        }
        finally{EditorApplication.isPaused=true; Application.runInBackground=background;MonsterAI.DebugPlayerInvisible=false;d.AbortHunt();}
    }
    public static object ReplayPressureRegressions()
    {
        Assert(EditorApplication.isPlaying,"Needs play mode");
        GameFlowManager.Instance.DebugBeginTest(); EditorApplication.isPaused=true;
        PlaytestRecorder.Record("automated_validation","qa",Vector3.zero,"multi-approach-pressure-replay");
        var d=MonsterDirector.Instance; var monsters=MonsterAI.activeMonsters.ToArray();
        var frames=File.ReadLines("PlaytestRecordings/2026-09-20_22-33-56_d9316b/trace.jsonl")
            .Select(JObject.Parse).Where(x=>(string)x["kind"]=="frame").ToArray();
        var reports=new List<object>();
        try
        {
            MonsterAI.DebugPlayerInvisible=true;
            foreach(float time in new[]{17.99f,45.41f,70.85f})
            {
                d.AbortHunt();
                var f=frames.OrderBy(x=>Math.Abs((float)x["t"]-time)).First();
                var p=V(f["player"]["position"]);
                monsters[0].player.GetComponent<PlayerController>().TeleportTo(p,Quaternion.identity);
                MonsterAI incumbent=null;
                foreach(var m in monsters)
                {
                    m.ResetMonster(); var actor=f["monsters"].First(x=>(string)x["name"]==m.name);
                    Assert(NavMesh.SamplePosition(V(actor["position"]),out var h,3f,m.NavigationFilter),"Missing snapshot nav");
                    m.GetComponent<NavMeshAgent>().Warp(h.position); m.GetComponent<Rigidbody>().position=h.position;
                    if((string)actor["state"]=="Chase") incumbent=m;
                }
                Assert(incumbent!=null,"Snapshot has no incumbent");
                incumbent.CommandPressure(p); Set(d,"pressure",incumbent); Set(d,"pressureSince",Time.time-20f);
                ((List<MonsterAI>)typeof(MonsterDirector).GetField("team",Private).GetValue(d)).Add(incumbent);
                d.ReportTargetAlarm(p,Time.time); Physics.SyncTransforms(); Call(d,"Coordinate");
                Assert(d.DebugRoutes.Single(x=>x.Value.directChaser).Key==incumbent,"Pressure regressed at "+time);
                Assert(incumbent.CurrentState==MonsterAI.State.Chase,"Existing pursuer withdrawn");
                var options=d.DebugRoutes.Select(x=>MonsterRoutePlanner.Make(x.Key,x.Value.corners,x.Value.waypoint,x.Value.detour,false)).ToList();
                for(int i=0;i<options.Count;i++) for(int j=i+1;j<options.Count;j++)
                    Assert(!MonsterRoutePlanner.Conflict(options[i],options[j],out _),"Combat corridors overlap");
                int walkingPreparations=0;
                foreach(var entry in d.DebugPreparations)
                {
                    Assert(entry.Key.CurrentState==MonsterAI.State.Prepare,"Preparation FSM mismatch");
                    var option=MonsterRoutePlanner.Make(entry.Key,entry.Value.corners,entry.Value.goal,false,false);
                    if(option.length>.2f)
                    {
                        walkingPreparations++;
                        foreach(var occupied in options) Assert(!MonsterRoutePlanner.Conflict(option,occupied,out _),"Preparation queues behind an approach");
                    }
                    options.Add(option);
                }
                Assert(d.DebugTeam.Count+d.DebugPreparations.Count==monsters.Length,"Healthy actor abandoned to distant patrol");
                var priorPressure=incumbent;
                foreach(var support in d.DebugTeam.Where(m=>m!=incumbent).ToArray()) d.RefreshCloseApproach(support);
                Assert(priorPressure.CurrentState==MonsterAI.State.Chase,"Support arrival demoted pursuer");
                Assert(typeof(MonsterAI).GetMethod("TeleportHome")==null,"Teleport API remains");
                reports.Add(new{time,pressure=incumbent.name,simultaneousApproaches=d.DebugTeam.Count-1,
                    walkingPreparations,preparations=d.DebugPreparations.Count,ms=d.LastPlanMilliseconds,queries=d.DebugPathQueries,
                    members=d.DebugTeam.Select(m=>m.name).ToArray()});
            }
            return reports;
        }
        finally { d.AbortHunt(); foreach(var m in monsters)m.ResetMonster(); MonsterAI.DebugPlayerInvisible=false; }
    }
}
