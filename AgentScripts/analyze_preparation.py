"""Read-only, time-weighted preparation and handoff diagnostics for a recorded session."""
import collections
import json
import math
import sys
from pathlib import Path

session = Path(sys.argv[1])
rows = [json.loads(s) for s in (session / 'trace.jsonl').read_text(encoding='utf-8-sig').splitlines()]
frames = [r for r in rows if r['kind'] == 'frame']
events = [r for r in rows if r['kind'] == 'event']
duration = json.loads((session / 'summary.json').read_text(encoding='utf-8-sig'))['duration']
def distance(a, b): return math.hypot(a['x'] - b['x'], a['z'] - b['z'])
def speed(m): return math.hypot(m['velocity']['x'], m['velocity']['z'])
def near(t): return min(frames, key=lambda f: abs(f['t'] - t))
def brief(f):
    return {'t': round(f['t'], 2), 'player': f['player']['position'], 'monsters': [
        {'name': m['name'], 'state': m['state'], 'distance': round(distance(m['position'], f['player']['position']), 1),
         'speed': round(speed(m), 1), 'remaining': round(m['remainingDistance'], 1),
         'position': m['position'], 'destination': m['destination']} for m in f['monsters']]}
stats = collections.defaultdict(lambda: collections.defaultdict(float))
starts = {}; bouts = []
for i, f in enumerate(frames):
    end = frames[i + 1]['t'] if i + 1 < len(frames) else duration
    for m in f['monsters']:
        name = m['name']; stopped = m['state'] == 'Prepare' and speed(m) < 1
        if m['state'] == 'Prepare':
            stats[name]['seconds'] += end - f['t']
            stats[name]['stopped' if stopped else 'moving'] += end - f['t']
        if stopped and name not in starts: starts[name] = f['t']
        if not stopped and name in starts:
            start = starts.pop(name)
            if f['t'] - start >= 2: bouts.append({'name': name, 'start': round(start, 2), 'end': round(f['t'], 2)})
for name, start in starts.items():
    if duration - start >= 2: bouts.append({'name': name, 'start': round(start, 2), 'end': round(duration, 2)})
handoffs = []
event_states = {}
for e in events:
    if e['type'] == 'monster_state': event_states[e['actor']] = e['detail'].split(' -> ')[-1]
    if e['type'] != 'pressure_handover': continue
    old = e['detail'].replace('from=', '')
    # A nearest 10 Hz frame can precede the stun; events retain the actual transition order.
    handoffs.append({'t': round(e['t'], 2), 'from': old, 'to': e['actor'], 'old_state': event_states.get(old),
        'nearby_events': [{'t': round(x['t'], 2), 'type': x['type'], 'actor': x['actor'], 'detail': x['detail']}
          for x in events if abs(x['t'] - e['t']) < .15 and x['type'] in ('monster_stunned', 'player_hit', 'monster_state', 'assignment_released')]})
out = {'session': session.name, 'preparation': {n: {k: round(v, 2) for k, v in s.items()} for n, s in stats.items()},
       'stopped_bouts': sorted(bouts, key=lambda b: b['start'] - b['end']), 'handoffs': handoffs,
       'gap_events': [{k: e[k] for k in ('t', 'type', 'actor', 'detail')} for e in events
           if 47 <= e['t'] <= 60.5 and e['type'] in ('monster_stunned', 'approach_preparation', 'player_hit', 'shot')],
       'snapshots': [brief(near(t)) for t in (48, 49, 51, 54, 58, 60, 90, 110, 125)]}
Path('Temp', session.name + '-preparation.json').write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in out.items() if k != 'snapshots'}, ensure_ascii=False, indent=2))
