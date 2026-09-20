using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;

// Read-only: queries baked navigation using recorded coordinates. Does not move objects or enter Play mode.
public static class PressureTraceNavAudit
{
    static Vector3 V(JToken t) => new Vector3((float)t["x"],(float)t["y"],(float)t["z"]);
    public static object Main()
    {
        var rows=File.ReadLines("PlaytestRecordings/2026-09-20_22-33-56_d9316b/trace.jsonl").Select(JObject.Parse).ToArray();
        var frames=rows.Where(r=>(string)r["kind"]=="frame").ToArray();
        var agents=UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var result=new List<object>();
        foreach(float time in new[]{17.991f,45.411f,70.853f})
        {
            var f=frames.OrderBy(r=>Math.Abs((float)r["t"]-time)).First();
            var ev=rows.First(r=>(string)r["kind"]=="event" && (string)r["type"]=="route_assigned" && Math.Abs((float)r["t"]-time)<.005f);
            foreach(var m in f["monsters"])
            {
                var agent=agents.FirstOrDefault(a=>a.name==(string)m["name"]);
                if(agent==null) continue;
                var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
                // Edit-mode agents are not registered: per-agent GetAreaCost logs errors there.
                // Global costs are an approximation; the Play-mode regression uses actual agent filters.
                for(int i=0;i<32;i++) filter.SetAreaCost(i,agent.isOnNavMesh?agent.GetAreaCost(i):NavMesh.GetAreaCost(i));
                var path=new NavMeshPath();
                bool ok=NavMesh.SamplePosition(V(m["position"]),out var from,2f,filter) &&
                    NavMesh.SamplePosition(V(ev["secondary"]),out var to,3f,filter) &&
                    NavMesh.CalculatePath(from.position,to.position,filter,path) && path.status==NavMeshPathStatus.PathComplete;
                float length=0;var corners=path.corners;
                for(int i=1;i<corners.Length;i++)length+=Vector3.Distance(corners[i-1],corners[i]);
                result.Add(new{time,sampleTime=(float)f["t"],name=(string)m["name"],state=(string)m["state"],complete=ok,length=ok?length:-1});
            }
        }
        return new{navVertices=NavMesh.CalculateTriangulation().vertices.Length,result};
    }
}
