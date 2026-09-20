"""Read a local trace without Unity. Usage: python AgentScripts/analyze_playtest.py [session directory]."""
import collections
import html
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
session = Path(sys.argv[1]) if len(sys.argv) > 1 else Path((ROOT / "PlaytestRecordings/latest-session.txt").read_text(encoding="utf-8-sig").strip())
records = []
for line in (session / "trace.jsonl").read_text(encoding="utf-8-sig").splitlines():
    try:
        records.append(json.loads(line))
    except json.JSONDecodeError:
        pass  # An actively written or interrupted session can end in a partial line.
header = next(r for r in records if r.get("kind") == "header")
frames = [r for r in records if r.get("kind") == "frame"]
events = [r for r in records if r.get("kind") == "event"]
summary_path = session / "summary.json"
summary = json.loads(summary_path.read_text(encoding="utf-8-sig")) if summary_path.exists() else {"outcome": "recording_or_interrupted"}
counts = collections.Counter(e["type"] for e in events)
camera_available = bool(frames) and all(
    sum(v*v for v in f.get("cameraForward", {}).values()) > .5 for f in frames)

def distance(a, b):
    return math.sqrt(sum((a[k]-b[k])**2 for k in ("x", "y", "z")))

no_visible = longest = 0.0
first_visible = None
for previous, frame in zip(frames, frames[1:]):
    dt = max(0, frame["t"] - previous["t"])
    if any(m.get("visibleToCamera") for m in previous["monsters"]):
        if first_visible is None:
            first_visible = previous["t"]
        no_visible = 0
    else:
        no_visible += dt
        longest = max(longest, no_visible)

target_moments = []
for event in events:
    if event["type"] != "target_destroyed" or not frames:
        continue
    frame = min(frames, key=lambda f: abs(f["t"]-event["t"]))
    target_moments.append({"t": event["t"], "position": event["position"],
        "visible_monsters": [m["name"] for m in frame["monsters"] if m.get("visibleToCamera")],
        "nearest_monster_straight_line_m": min((distance(frame["player"]["position"],m["position"]) for m in frame["monsters"]), default=None)})

if not camera_available:
    first_visible = None
    longest = None
    for moment in target_moments:
        moment["visible_monsters"] = None

limits = ["10Hz position samples; summary movement distance accumulates every rendered frame.",
          "Camera visibility uses a body point and wall raycast; it is not eye tracking or video.",
          "Straight-line distance does not account for walls. A route change alone does not prove why the player changed direction.",
          "A zero cameraForward means missing camera data, not zero visible monsters."]
if header.get("schema") == "pico-playtest-v1":
    limits.append("V1 stationarySeconds used render-frame displacement and can overcount pauses between physics updates; validate against sampled velocity.")
else:
    limits.append("V2 stationarySeconds uses sampled planar velocity at 10Hz.")

analysis = {"session": str(session), "summary": summary, "samples": len(frames), "events": dict(counts),
    "camera_data_available": camera_available,
    "automated_validation": any(e["type"] == "automated_validation" for e in events),
    "first_visible_monster_seconds": first_visible, "longest_no_visible_monster_seconds_approx": longest,
    "target_moments": target_moments,
    "limits": limits}
(session / "analysis.json").write_text(json.dumps(analysis,ensure_ascii=False,indent=2),encoding="utf-8")

# A shareable, static map of actual traversed paths over the recorded NavMesh, excluding teleports.
vertices = header.get("navVertices", [])
if vertices and frames:
    x0,x1=min(v["x"] for v in vertices),max(v["x"] for v in vertices)
    z0,z1=min(v["z"] for v in vertices),max(v["z"] for v in vertices)
    scale=min(1250/max(1,x1-x0),850/max(1,z1-z0))
    def point(p):
        return f'{50+(p["x"]-x0)*scale:.1f},{80+(z1-p["z"])*scale:.1f}'
    svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1400" height="1000" viewBox="0 0 1400 1000">',
         '<rect width="1400" height="1000" fill="#101820"/>',
         '<text x="40" y="35" fill="white" font-family="sans-serif" font-size="22">Recorded movement — full session</text>']
    indices=header.get("navTriangles",[])
    for i in range(0,len(indices),3):
        points=" ".join(point(vertices[index]) for index in indices[i:i+3])
        svg.append(f'<polygon points="{points}" fill="#293640"/>')
    for t in header.get("targets",[]):
        x,y=point(t["position"]).split(",")
        svg.append(f'<circle cx="{x}" cy="{y}" r="2" fill="#eab464"/>')
    actor_paths=collections.defaultdict(list)
    for f in frames:
        for actor in [f.get("player")]+f["monsters"]:
            if actor:
                actor_paths[actor["name"]].append(actor["position"])
    colors=["#ffffff","#ff8766","#c19bff","#7de7a2","#ffe16b","#68c6ff"]
    for index,(name,path) in enumerate(actor_paths.items()):
        color=colors[index%len(colors)]
        for a,b in zip(path,path[1:]):
            if distance(a,b)>6:
                continue
            pa,pb=point(a).split(","),point(b).split(",")
            svg.append(f'<line x1="{pa[0]}" y1="{pa[1]}" x2="{pb[0]}" y2="{pb[1]}" stroke="{color}" stroke-width="2" opacity=".8"/>')
        svg.append(f'<text x="{40+index*225}" y="975" fill="{color}" font-family="sans-serif" font-size="16">{html.escape(name)}</text>')
    svg.append('</svg>')
    (session / "paths.svg").write_text("\n".join(svg),encoding="utf-8")
print(json.dumps(analysis,ensure_ascii=False,indent=2))
