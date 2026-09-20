using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;

public static class PlaytestPolishRegression
{
    private static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    // Stop Play mode after this returns, then verify summary.json was saved by lifecycle callbacks.
    public static async Task<object> BeginNormalRoundForStopValidation()
    {
        Assert(EditorApplication.isPlaying, "Requires Play mode in ShooterInGame");
        bool background = Application.runInBackground;
        try
        {
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            GameFlowManager.Instance.OnStartButtonClicked();
            float deadline = Time.realtimeSinceStartup + 12;
            while (!PlaytestRecorder.Instance.IsRecording && Time.realtimeSinceStartup < deadline)
                await Task.Delay(100);
            Assert(PlaytestRecorder.Instance.IsRecording, "Normal Start did not begin recording");
            PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "normal_start_and_editor_stop");
            await Task.Delay(1200);
            return new { normalStartRecords = true, session = PlaytestRecorder.LastSessionDirectory };
        }
        finally { Application.runInBackground = background; }
    }

    public static object RecorderCameraValidation()
    {
        Assert(EditorApplication.isPlaying, "Requires Play mode in ShooterInGame");
        GameFlowManager.Instance.DebugBeginTest();
        PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "RecorderCameraValidation");
        var camera=(Camera)typeof(PlaytestRecorder).GetField("gameplayCamera",Private).GetValue(PlaytestRecorder.Instance);
        Assert(camera!=null && camera.name!="~OverviewCamera", "Gameplay camera was not captured");
        return new {camera=camera.name,position=camera.transform.position,forward=camera.transform.forward};
    }
    public static async Task<object> Validate()
    {
        Assert(EditorApplication.isPlaying,"Requires Play mode in ShooterInGame");
        EditorApplication.isPaused=true;
        var manager=TargetManager.Instance; var flow=GameFlowManager.Instance;
        var player=GameObject.FindGameObjectWithTag("Player").transform;
        var body=player.GetComponent<Rigidbody>();
        var portal=UnityEngine.Object.FindFirstObjectByType<ProceduralExitPortal>(FindObjectsInactive.Include);
        Vector3 start=flow.PlayerStartPoint.position;
        var seeded=new List<object>();
        var originalRandom=UnityEngine.Random.state;
        for(int seed=1;seed<=3;seed++)
        {
            UnityEngine.Random.InitState(seed); manager.ResetGame(); Physics.SyncTransforms();
            var targets=UnityEngine.Object.FindObjectsByType<Target>(FindObjectsSortMode.None);
            Assert(targets.Length>=manager.TargetGoal,"Too few targets to finish");
            Assert(targets.Length==150,"Fixture should place all 150 targets");
            Assert(targets.Count(t=>t.PlacementHeight>=3.3f)>=20,"High targets missing");
            Assert(targets.Count(t=>t.PlacementHeight<2)>=20,"Low targets missing");
            var path=new NavMeshPath(); NavMesh.SamplePosition(start,out var from,3,NavMesh.AllAreas);
            foreach(var target in targets)
            {
                Assert(NavMesh.CalculatePath(from.position,target.ShootingPosition,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete,"Unreachable shooting position");
                Vector3 eye=target.ShootingPosition+Vector3.up*1.5f, aim=target.transform.position-eye;
                bool hit=Physics.Raycast(eye,aim.normalized,out var ray,aim.magnitude+1,~0,QueryTriggerInteraction.Ignore);
                Assert(hit && ray.collider.GetComponentInParent<Target>()==target,"Target is occluded from its recorded shooting position");
                Assert(Physics.Raycast(target.transform.position+target.transform.forward*.3f,-target.transform.forward,out var wall,1,~(1<<target.gameObject.layer),QueryTriggerInteraction.Ignore),"Missing supporting wall");
            }
            Assert(new Vector2(manager.PortalPosition.x-start.x,manager.PortalPosition.z-start.z).magnitude<.01f,"Portal drifted from start");
            Assert(!portal.IsUnlocked && !portal.GetComponent<BoxCollider>().enabled,"Locked portal can be entered");
            seeded.Add(new{seed,count=targets.Length,low=targets.Count(t=>t.PlacementHeight<2),high=targets.Count(t=>t.PlacementHeight>=3.3f)});
        }
        UnityEngine.Random.state=originalRandom;
        bool background=Application.runInBackground;
        try
        {
            Application.runInBackground=true; PlayerHealth.DebugGodMode=true;
            foreach(var monster in MonsterAI.activeMonsters) { monster.CommandGoHome(30); }
            player.position=body.position=start;
            flow.DebugBeginTest();
            PlaytestRecorder.Record("automated_validation","qa",start);
            float began=Time.time;
            EditorApplication.isPaused=false;
            // Move the player 10m in small steps. This is a trace fixture, not a claim of human play.
            for(int i=1;i<=20;i++)
            {
                body.position=player.position=start+Vector3.right*(i*.5f);
                Physics.SyncTransforms(); await Task.Delay(100);
            }
            Assert(Time.time-began>1,"Simulation did not advance");
            var target=UnityEngine.Object.FindObjectsByType<Target>(FindObjectsSortMode.None)[0];
            target.OnHit(player.position,Time.time);
            var monsterToStun=MonsterAI.activeMonsters[0]; monsterToStun.OnHitByLaser(player.position);
            while(manager.DestroyedCount<manager.TargetGoal) manager.OnTargetDestroyed();
            Assert(portal.IsUnlocked && portal.GetComponent<BoxCollider>().enabled,"Goal did not unlock portal");
            Assert(flow.IsGameRunning,"Unlocking away from the portal must not clear the game");
            await Task.Delay(300);
            player.GetComponent<PlayerController>().TeleportTo(manager.PortalPosition,flow.PlayerStartPoint.rotation);
            Physics.SyncTransforms();
            float deadline=Time.realtimeSinceStartup+3;
            while(flow.IsGameRunning && Time.realtimeSinceStartup<deadline) await Task.Delay(100);
            Assert(!flow.IsGameRunning,"Actual trigger entry did not finish the game");
            Assert(!PlaytestRecorder.Instance.IsRecording,"Finished session is still recording");
            string directory=PlaytestRecorder.LastSessionDirectory;
            Assert(System.IO.File.Exists(System.IO.Path.Combine(directory,"summary.json")),"No summary saved");
            return new{seeded,portalFixed=true,lockedTriggerDisabled=true,goalUnlocks=true,triggerCompletes=true,session=directory};
        }
        finally
        {
            EditorApplication.isPaused=true; Application.runInBackground=background; PlayerHealth.DebugGodMode=false;
            PlaytestRecorder.EndRound("automated_validation");
        }
    }
}
