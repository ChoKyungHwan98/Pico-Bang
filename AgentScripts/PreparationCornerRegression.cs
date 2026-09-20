using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;

public static class PreparationCornerRegression
{
    public static async Task<object> Main()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Requires Play mode");
        bool background = Application.runInBackground, paused = EditorApplication.isPaused;
        bool invisible = MonsterAI.DebugPlayerInvisible;
        var director = MonsterDirector.Instance;
        bool directorEnabled = director.enabled;
        var all = MonsterAI.activeMonsters.ToArray();
        var enabled = all.ToDictionary(m => m, m => m.enabled);
        try
        {
            Application.runInBackground = true;
            GameFlowManager.Instance.DebugBeginTest();
            PlaytestRecorder.Record("automated_validation", "qa", Vector3.zero, "PreparationCornerRegression");
            MonsterAI.DebugPlayerInvisible = true;
            director.AbortHunt(); director.enabled = false;
            foreach (var other in all) other.enabled = false;
            var m = all.First(x => x.name == "Monster_Global");
            m.enabled = true; m.ResetMonster();
            // Actual 35.277954s preparation order in human session 23-22-30_c7f603.
            var corners = new[] {
                new Vector3(53.930305f,3.0833292f,-30.66113f),
                new Vector3(53.30991f,3.0833292f,-30.489128f),
                new Vector3(49.80991f,3.0833292f,-30.489128f),
                new Vector3(49.476578f,3.0833292f,-30.82246f),
                new Vector3(43.58769f,3.0833292f,-39.71135f) };
            var agent = m.GetComponent<NavMeshAgent>();
            if (!agent.Warp(corners[0])) throw new Exception("Fixture not on NavMesh");
            m.transform.position = corners[0]; m.GetComponent<Rigidbody>().position = corners[0];
            Physics.SyncTransforms(); m.SetHuntSpeed(14); m.CommandPreparation(corners);
            EditorApplication.isPaused = false;
            float began = Time.time, deadline = Time.realtimeSinceStartup + 5;
            while (Time.time - began < .9f && Time.realtimeSinceStartup < deadline) await Task.Delay(30);
            float moved = Vector3.Distance(corners[0], m.transform.position);
            float remaining = Vector3.Distance(corners[corners.Length-1], m.transform.position);
            bool advancesPastCorner = moved > 2f && remaining < Vector3.Distance(corners[0], corners[corners.Length-1]) - 2f;
            // Let it reach its final staging point, where stopping is intentional.
            deadline = Time.realtimeSinceStartup + 6;
            while (Time.time-began < 3f && Time.realtimeSinceStartup < deadline) await Task.Delay(30);
            bool holdsAtFinal = Vector3.Distance(corners[corners.Length-1],m.transform.position) < 2f && m.PlanarVelocity.magnitude < 1;
            m.CommandPreparation(new[] { m.transform.position, m.transform.position });
            Vector3 holdPosition = m.transform.position;
            float holdBegan = Time.time;
            while (Time.time-holdBegan < .3f && Time.realtimeSinceStartup < deadline) await Task.Delay(30);
            bool explicitHoldWorks = Vector3.Distance(holdPosition,m.transform.position) < .2f;
            return new { passed = advancesPastCorner && holdsAtFinal && explicitHoldWorks, advancesPastCorner,
                movedAfterPointNineSeconds = moved, holdsAtFinal, explicitHoldWorks };
        }
        finally
        {
            director.enabled = directorEnabled;
            foreach (var m in all) if (m != null) m.enabled = enabled[m];
            MonsterAI.DebugPlayerInvisible = invisible;
            Application.runInBackground = background;
            EditorApplication.isPaused = paused;
        }
    }
}
