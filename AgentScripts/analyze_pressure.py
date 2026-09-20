"""Read-only trace analysis; derived JSON goes to Temp, never changes original recordings."""
import collections, json, math, sys
from pathlib import Path

session = Path(sys.argv[1])
rows = [json.loads(s) for s in (session/'trace.jsonl').read_text(encoding='utf-8-sig').splitlines() if s.strip()]
frames = [r for r in rows if r['kind']=='frame']
events = [r for r in rows if r['kind']=='event']
duration = json.loads((session/'summary.json').read_text(encoding='utf-8-sig'))['duration']
def dist(a,b): return math.hypot(a['x']-b['x'],a['z']-b['z'])
def speed(m): return math.hypot(m['velocity']['x'],m['velocity']['z'])
def active(m): return m['state'] in ('Chase','Intercept')
def nearest_frame(t): return min(frames,key=lambda f:abs(f['t']-t))
def brief(f):
    return {'t':round(f['t'],2),'player':[round(f['player']['position'][k],1) for k in ('x','z')],
        'monsters':[{'name':m['name'],'state':m['state'],'distance':round(dist(m['position'],f['player']['position']),1),
        'speed':round(speed(m),1),'remaining':round(m['remainingDistance'],1),'visible':m['visibleToCamera'],
        'destination':[round(m['destination'][k],1) for k in ('x','z')]} for m in f['monsters']]}
metrics=collections.defaultdict(float); states=collections.defaultdict(lambda:collections.defaultdict(float))
intervals=collections.defaultdict(list); starts={}; bins=collections.defaultdict(lambda:collections.defaultdict(float))
for i,f in enumerate(frames):
    dt=(frames[i+1]['t'] if i+1<len(frames) else duration)-f['t']
    p=f['player']['position']; ms=f['monsters']; hunters=[m for m in ms if active(m)]
    closest=min((dist(p,m['position']) for m in ms),default=999)
    hc=min((dist(p,m['position']) for m in hunters),default=999)
    c=min((dist(p,m['position']) for m in ms if m['state']=='Chase'),default=999)
    flags={'has_chase':c<999,'two_active':len(hunters)>=2,'three_active':len(hunters)>=3,
        'no_active':not hunters,'no_visible':not any(m['visibleToCamera'] for m in ms),
        'all_far15':closest>15,'all_far25':closest>25,'active_far15':hc>15,'active_far25':hc>25,
        'chase_far25':c>25,'two_active_near15':sum(dist(p,m['position'])<=15 for m in hunters)>=2,
        'active_near10':hc<=10,'chase_near10':c<=10,'player_moving':speed(f['player'])>1,
        'near_return15':any(m['state']=='Return' and dist(p,m['position'])<15 for m in ms)}
    bucket=int(f['t']//10)*10; bins[bucket]['duration']+=dt
    for k,v in flags.items():
        if v:
            metrics[k]+=dt; bins[bucket][k]+=dt
            if k not in starts: starts[k]=f['t']
        elif k in starts: intervals[k].append([round(starts.pop(k),2),round(f['t'],2)])
    for m in ms:
        states[m['name']][m['state']]+=dt
        if active(m) and speed(m)<1: states[m['name']]['active_stationary']+=dt
for k,s in starts.items(): intervals[k].append([round(s,2),round(duration,2)])
for k in intervals: intervals[k].sort(key=lambda x:x[0]-x[1])
teleports=[]
for e in events:
    if e['type']!='teleport': continue
    f=nearest_frame(e['t']); p=f['player']['position']; after=[x for x in frames if x['t']>=e['t']+.11]
    next_near=next((x for x in after if any(m['name']==e['actor'] and active(m) and dist(m['position'],x['player']['position'])<=15 for m in x['monsters'])),None)
    teleports.append({'t':round(e['t'],2),'actor':e['actor'],'before_player_distance':round(dist(e['position'],p),1),
        'after_player_distance':round(dist(e['secondary'],p),1),
        'next_active_near15_delay':round(next_near['t']-e['t'],2) if next_near else None})
releases=[]
for e in events:
    if e['type']!='assignment_released': continue
    future=next((x for x in events if x['type']=='route_assigned' and x['actor']==e['actor'] and x['t']>e['t']+.01),None)
    releases.append({'t':round(e['t'],2),'actor':e['actor'],'next_assignment_delay':round(future['t']-e['t'],2) if future else None})
targets=[]
for e in events:
    if e['type']!='target_destroyed':continue
    f=nearest_frame(e['t']); ms=f['monsters'];p=f['player']['position']
    targets.append({'t':round(e['t'],2),'nearest':round(min(dist(p,m['position']) for m in ms),1),
        'nearest_active':round(min((dist(p,m['position']) for m in ms if active(m)),default=999),1),
        'visible':sum(m['visibleToCamera'] for m in ms)})
handoffs=[]
for e in events:
    if e['type']!='pressure_handover':continue
    f=nearest_frame(e['t']); p=f['player']['position']; prior=e['detail'].replace('from=','')
    handoffs.append({'t':round(e['t'],2),'to':e['actor'],'from':prior,
        'distances':{m['name']:round(dist(p,m['position']),1) for m in f['monsters'] if m['name'] in (e['actor'],prior)}})
out={'session':session.name,'duration':duration,'automated':any(e['type']=='automated_validation' for e in events),
    'event_counts':dict(collections.Counter(e['type'] for e in events)),
    'seconds':{k:round(v,2) for k,v in metrics.items()},'percent':{k:round(v/duration*100,1) for k,v in metrics.items()},
    'longest_intervals':{k:v[:5] for k,v in intervals.items()},
    'states_seconds':{n:{k:round(v,1) for k,v in s.items()} for n,s in states.items()},
    'bins':{b:{k:round(v,2) for k,v in s.items()} for b,s in bins.items()},
    'teleports':teleports,'releases':releases,'targets':targets,'handoffs':handoffs,
    'snapshots':[brief(nearest_frame(t)) for t in (0,5,10,20,30,40,50,60,70,80,90,100)]}
Path('Temp').mkdir(exist_ok=True)
Path('Temp',session.name+'-pressure.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in out.items() if k not in ('snapshots','targets','releases','bins')},ensure_ascii=False,indent=2))
