using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Full-duration behavior fixture: the player fires every cooldown while following valid NavMesh routes.
public static class RepeatedFireCorridorRegression
{
    static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    static Vector3 OnMesh(MonsterAI m,Vector3 p)
    {
        Assert(NavMesh.SamplePosition(p,out var hit,8,m.NavigationFilter),"No NavMesh near "+p);
        return hit.position;
    }
    public static async Task<object> Main()
    {
        Assert(EditorApplication.isPlaying,"Requires Play mode");
        bool background=Application.runInBackground, paused=EditorApplication.isPaused;
        bool invisible=MonsterAI.DebugPlayerInvisible, god=PlayerHealth.DebugGodMode;
        var d=MonsterDirector.Instance; var monsters=MonsterAI.activeMonsters.ToArray();
        try
        {
            Application.runInBackground=true; PlayerHealth.DebugGodMode=true; MonsterAI.DebugPlayerInvisible=true;
            GameFlowManager.Instance.DebugBeginTest(); EditorApplication.isPaused=true;
            PlaytestRecorder.Record("automated_validation","qa",Vector3.zero,"RepeatedFireCorridorRegression");
            d.AbortHunt();
            var origins=new Dictionary<string,Vector3> {
                {"Monster_C",new Vector3(10,0,1)}, {"Monster_Global",new Vector3(41,1,1)},
                {"Monster_A",new Vector3(43,1,13)}, {"Monster_B",new Vector3(-63,0,30)}, {"Monster_D",new Vector3(15,2,-72)} };
            foreach(var m in monsters)
            {
                m.ResetMonster(); var p=OnMesh(m,origins[m.name]);
                Assert(m.GetComponent<NavMeshAgent>().Warp(p),"Monster warp failed");
                m.transform.position=m.GetComponent<Rigidbody>().position=p;
            }
            var reference=monsters[0]; var player=reference.player; var body=player.GetComponent<Rigidbody>();
            Vector3 position=OnMesh(reference,new Vector3(-3,0,1));
            var planner=new MonsterRoutePlanner();
            var movement=new List<Vector3>{position};
            foreach(var destination in new[]{new Vector3(-50,0,20),new Vector3(20,0,50),new Vector3(65,0,0),new Vector3(10,0,-45)})
            {
                var segment=planner.Path(reference,movement[movement.Count-1],destination);
                Assert(segment!=null,"Player route unavailable"); movement.AddRange(segment.Skip(1));
            }
            player.position=body.position=position; Physics.SyncTransforms();
            float started=Time.time, previous=started, nextShot=started, nextSample=started+.5f;
            int corner=1, shots=0, samples=0, supportSamples=0, splitSupportSamples=0, regroupViolations=0, goalChanges=0;
            float currentAllBehind=0,maxAllBehind=0;
            var oldGoals=new Dictionary<MonsterAI,Vector3>();
            EditorApplication.isPaused=false;
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.time-started<18f && Time.realtimeSinceStartup<deadline)
            {
                await Task.Delay(50);
                float now=Time.time,dt=Mathf.Min(.15f,now-previous); previous=now;
                if(corner<movement.Count)
                {
                    position=Vector3.MoveTowards(position,movement[corner],11f*dt);
                    if(Vector3.Distance(position,movement[corner])<.1f) corner++;
                    player.position=body.position=position; Physics.SyncTransforms();
                }
                if(now>=nextShot)
                {
                    nextShot=now+1.5f; shots++;
                    d.ReportNoise(position,50f,NoiseKind.Shot);
                    d.ReportTargetAlarm(position,now);
                    PlaytestRecorder.Record("repeated_fire_fixture","player",position,"shot="+shots);
                }
                if(now<nextSample) continue;
                nextSample=now+.25f; samples++;
                Assert(d.DebugRoutes.Count(x=>x.Value.directChaser)==1,"Lost sole pressure pursuer");
                var supports=d.DebugRoutes.Where(x=>!x.Value.directChaser).ToArray();
                if(supports.Length>0) supportSamples++;
                foreach(var s in supports)
                {
                    if(Vector3.Distance(s.Value.goal,d.DebugKnownPosition)<7f) regroupViolations++;
                    if(oldGoals.TryGetValue(s.Key,out var old)&&Vector3.Distance(old,s.Value.goal)>3f) goalChanges++;
                    oldGoals[s.Key]=s.Value.goal;
                }
                if(supports.Length>=2)
                {
                    Vector3 a=Vector3.ProjectOnPlane(supports[0].Value.goal-position,Vector3.up).normalized;
                    Vector3 b=Vector3.ProjectOnPlane(supports[1].Value.goal-position,Vector3.up).normalized;
                    if(a.sqrMagnitude>.1f&&b.sqrMagnitude>.1f&&Vector3.Dot(a,b)<=.65f) splitSupportSamples++;
                }
                Vector3 direction=corner<movement.Count?Vector3.ProjectOnPlane(movement[corner]-position,Vector3.up).normalized:Vector3.zero;
                bool allBehind=now-started>3f&&direction.sqrMagnitude>.1f&&monsters.Where(m=>!m.IsInStun)
                    .All(m=>Vector3.Dot(Vector3.ProjectOnPlane(m.transform.position-position,Vector3.up),direction)<-1f);
                currentAllBehind=allBehind?currentAllBehind+.25f:0; maxAllBehind=Mathf.Max(maxAllBehind,currentAllBehind);
            }
            Assert(Time.time-started>=17.5f,"Simulation did not complete");
            Assert(shots>=11,"Did not exercise repeated fire");
            Assert(regroupViolations<=samples*.08f,$"A passed cutoff stayed on the evidence too long: {regroupViolations}/{samples}");
            Assert(supportSamples>=samples*.35f,"Connected cutoff support was rarely available");
            Assert(splitSupportSamples>0,"Never occupied two distinct connected exits");
            Assert(maxAllBehind<8f,"All monsters formed one trailing pack for too long");
            Assert(goalChanges<=shots*2,"Corridor missions churned on repeated shots");
            return new {passed=true,shots,samples,supportSamples,splitSupportSamples,goalChanges,maxAllBehindSeconds=maxAllBehind,
                finalTeam=d.DebugTeam.Select(m=>m.name).ToArray(),finalPreparations=d.DebugPreparations.Count};
        }
        finally
        {
            EditorApplication.isPaused=true; d.AbortHunt(); foreach(var m in monsters) if(m!=null)m.ResetMonster();
            MonsterAI.DebugPlayerInvisible=invisible; PlayerHealth.DebugGodMode=god;
            Application.runInBackground=background; EditorApplication.isPaused=paused;
        }
    }
}
