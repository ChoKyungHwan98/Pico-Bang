using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using Newtonsoft.Json;

// Paused snapshot comparison: same recorded positions/evidence for both policies.
// This measures assignment availability, not a replay of the user's gameplay.
public static class CooperationRollbackRegression
{
    [Serializable] public class Actor { public string name, state; public Vector3 position; }
    [Serializable] public class Frame { public string kind; public float t; public Actor player; public Actor[] monsters; }
    [Serializable] public class Row { public float t; public int team; public int supports; public string[] members; public float[] lengths; }
    [Serializable] public class Report { public string policy; public List<Row> rows = new List<Row>(); public float meanSupports; public int cooperativeSnapshots; }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static object Before() => Run("restricted");
    public static object After() => Run("restored");
    private static object Run(string policy)
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused) throw new Exception("Requires paused Play mode");
        var director = MonsterDirector.Instance;
        var monsters = MonsterAI.activeMonsters.ToArray();
        string project = Path.GetDirectoryName(Application.dataPath);
        var lines = File.ReadAllLines(Path.Combine(project,"PlaytestRecordings/2026-09-20_21-07-12_9d34c2/trace.jsonl"));
        var frames = lines.Select(JsonConvert.DeserializeObject<Frame>).Where(f=>f.kind=="frame").ToList();
        bool suppress = PlaytestRecorder.SuppressAutomaticRecording;
        PlaytestRecorder.SuppressAutomaticRecording = true;
        var report = new Report { policy = policy };
        try
        {
            for (float t=3; t<50; t+=3)
            {
                var frame=frames.OrderBy(f=>Mathf.Abs(f.t-t)).First();
                director.AbortHunt();
                foreach (var m in monsters)
                {
                    m.ResetMonster();
                    var actor=frame.monsters.First(x=>x.name==m.name);
                    if (!NavMesh.SamplePosition(actor.position,out var hit,8f,m.NavigationFilter) ||
                        !m.GetComponent<NavMeshAgent>().Warp(hit.position)) throw new Exception("Snapshot placement failed: "+m.name);
                }
                var player=monsters[0].player;
                player.GetComponent<PlayerController>().TeleportTo(frame.player.position,Quaternion.identity);
                Physics.SyncTransforms();
                director.ReportTargetAlarm(frame.player.position,Time.time);
                typeof(MonsterDirector).GetMethod("Coordinate",Private).Invoke(director,null);
                var routes=director.DebugRoutes.ToArray();
                if(routes.Count(r=>r.Value.directChaser)!=1) throw new Exception("Missing pressure route");
                var options=routes.Select(r=>MonsterRoutePlanner.Make(r.Key,r.Value.corners,r.Value.waypoint,r.Value.detour,false)).ToArray();
                for(int i=0;i<options.Length;i++) for(int j=i+1;j<options.Length;j++)
                    if(MonsterRoutePlanner.Conflict(options[i],options[j],out _)) throw new Exception("Shared corridor accepted");
                var row=new Row {t=frame.t,team=routes.Length,supports=routes.Length-1,
                    members=routes.Select(r=>r.Key.name).ToArray(),lengths=options.Select(o=>o.length).ToArray()};
                report.rows.Add(row);
            }
        }
        finally { PlaytestRecorder.SuppressAutomaticRecording=suppress; }
        report.meanSupports=(float)report.rows.Average(r=>r.supports);
        report.cooperativeSnapshots=report.rows.Count(r=>r.supports>0);
        string output=Path.Combine(project,"Temp/cooperation-"+policy+".json");
        File.WriteAllText(output,JsonConvert.SerializeObject(report,Formatting.Indented));
        return new {report.policy,snapshots=report.rows.Count,report.cooperativeSnapshots,report.meanSupports,output};
    }
}
