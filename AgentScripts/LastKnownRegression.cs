using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class LastKnownRegression
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task<object> Main()
    {
        Assert(EditorApplication.isPlaying, "Requires Play mode");
        bool wasPaused = EditorApplication.isPaused;
        bool wasInvisible = MonsterAI.DebugPlayerInvisible;
        bool wasGod = PlayerHealth.DebugGodMode;
        bool wasBackground = Application.runInBackground;
        var monster = MonsterAI.activeMonsters.First(m => m.name == "Monster_C");
        var director = MonsterDirector.Instance;
        var player = monster.player;
        var body = player.GetComponent<Rigidbody>();
        var agent = monster.GetComponent<NavMeshAgent>();
        try
        {
            Application.runInBackground = true;
            PlayerHealth.DebugGodMode = true;
            EditorApplication.isPaused = true;
            GameFlowManager.Instance.DebugBeginTest();
            PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "LastKnownRegression");
            director.AbortHunt();
            foreach (var m in MonsterAI.activeMonsters) m.ResetMonster();

            Assert(NavMesh.SamplePosition(new Vector3(-3, 0, 1), out var playerHit, 5, NavMesh.AllAreas), "Player point missing");
            Assert(NavMesh.SamplePosition(new Vector3(5, 0, 1), out var monsterHit, 5, monster.NavigationFilter), "Monster point missing");
            player.position = body.position = playerHit.position;
            Assert(agent.Warp(monsterHit.position), "Monster warp failed");
            monster.transform.rotation = Quaternion.LookRotation(player.position - monster.transform.position);
            Physics.SyncTransforms();
            var update = typeof(MonsterAI).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(monster, null);
            Assert(monster.CurrentState == MonsterAI.State.Chase && monster.IsSeeingPlayer, "Visual encounter did not start chase");
            Vector3 lastVisual = player.position;

            MonsterAI.DebugPlayerInvisible = true;
            Assert(NavMesh.SamplePosition(new Vector3(-50, 0, 20), out var shotHit, 8, NavMesh.AllAreas), "Shot point missing");
            player.position = body.position = shotHit.position;
            Physics.SyncTransforms();
            update.Invoke(monster, null);
            Assert(monster.CurrentState == MonsterAI.State.Chase && !monster.IsSeeingPlayer, "Loss of sight did not retain last-position chase");
            float started = Time.time;
            float nextShot = started;
            int shots = 0;
            bool chaseEnded = false;
            EditorApplication.isPaused = false;
            float deadline = Time.realtimeSinceStartup + 24f;
            while (Time.time - started < 13f && Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(75);
                if (Time.time >= nextShot)
                {
                    director.ReportNoise(shotHit.position, 100f, NoiseKind.Shot);
                    director.ReportTargetAlarm(shotHit.position, Time.time);
                    shots++;
                    nextShot = Time.time + 1.5f;
                }
                if (monster.CurrentState == MonsterAI.State.Chase)
                {
                    Assert(Vector3.Distance(agent.destination, lastVisual) < 3.5f,
                        "Blind pursuer followed sound or shared position");
                    Assert(!chaseEnded, "Blind chase restarted without visual contact");
                }
                else chaseEnded = true;
            }
            Assert(Time.time - started >= 12.5f, "Simulation did not finish");
            Assert(shots >= 8, "Repeated-fire condition not exercised");
            Assert(chaseEnded, "Pursuer never checked last visual position and disengaged");
            Assert(MonsterAI.activeMonsters.Count == 5, "Monster count changed");
            Assert(MonsterAI.activeMonsters.All(m => m.CurrentState != MonsterAI.State.Chase),
                "Director started a blind chase after repeated shots");
            return new { passed = true, shots, chaseEnded, finalState = monster.CurrentState.ToString(),
                lastVisual, shotPosition = shotHit.position };
        }
        finally
        {
            EditorApplication.isPaused = true;
            director.AbortHunt();
            MonsterAI.DebugPlayerInvisible = wasInvisible;
            PlayerHealth.DebugGodMode = wasGod;
            Application.runInBackground = wasBackground;
            EditorApplication.isPaused = wasPaused;
        }
    }
}
