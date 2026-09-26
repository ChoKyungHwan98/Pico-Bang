# -*- coding: utf-8 -*-
"""
Pico-Bang! 플레이 기록 QA 도구 (표준 라이브러리만 사용).

플레이 기록: PlaytestRecordings/<날짜_시각_id>/trace.jsonl  (PlaytestRecorder.cs가 씀)
모든 기록에는 화면 타이머와 같은 시각(clock, 예 "04:22" = 남은 시간)이 붙어 있다.
플레이 중 F8을 누르면 그 순간이 qa_mark로 남는다.

사용법 (저장소 루트에서):
  python Tools/QA/pico_qa.py list                    최근 기록 목록
  python Tools/QA/pico_qa.py summary [기록]          한 판 요약 + 의심 장면 목록
  python Tools/QA/pico_qa.py marks [기록]            F8 표시 목록과 그 순간 상황
  python Tools/QA/pico_qa.py at 04:22 [기록] [초]    화면 타이머 04:22 전후(기본 ±5초) 상세
  python Tools/QA/pico_qa.py decisions [기록]        감독 판단 전체(시각순)

[기록]: 생략하면 가장 최근. 폴더 이름 일부(예 "18-46") 또는 -2(두 번째로 최근)도 된다.
"""
import io
import json
import math
import os
import sys
from collections import Counter, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'PlaytestRecordings'))

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


# ───────────────────────── 읽기 ─────────────────────────

def sessions():
    if not os.path.isdir(ROOT):
        return []
    names = [n for n in os.listdir(ROOT) if os.path.isfile(os.path.join(ROOT, n, 'trace.jsonl'))]
    return sorted(names)


def pick(arg):
    names = sessions()
    if not names:
        sys.exit('기록이 없습니다: ' + ROOT)
    if arg is None:
        return names[-1]
    if arg.startswith('-') and arg[1:].isdigit():
        return names[-int(arg[1:])]
    hits = [n for n in names if arg in n]
    if not hits:
        sys.exit('기록을 찾지 못했습니다: ' + arg)
    return hits[-1]


class Trace:
    def __init__(self, name):
        self.name = name
        self.header = {}
        self.frames = []
        self.events = []
        with io.open(os.path.join(ROOT, name, 'trace.jsonl'), encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                o = json.loads(line)
                k = o.get('kind')
                if k == 'frame':
                    if not o.get('clock'):
                        o['clock'] = clock_of(o.get('timeRemaining', 0))
                    self.frames.append(o)
                elif k == 'event':
                    self.events.append(o)
                elif k == 'header':
                    self.header = o
        # 옛 기록(clock 없음)은 가장 가까운 프레임의 시각을 쓴다
        for e in self.events:
            if not e.get('clock'):
                f = self.frame_at(e['t'])
                e['clock'] = f['clock'] if f else ''

    def frame_at(self, t):
        if not self.frames:
            return None
        lo, hi = 0, len(self.frames) - 1
        while lo < hi:
            mid = (lo + hi) // 2
            if self.frames[mid]['t'] < t:
                lo = mid + 1
            else:
                hi = mid
        best = self.frames[lo]
        if lo > 0 and abs(self.frames[lo - 1]['t'] - t) < abs(best['t'] - t):
            best = self.frames[lo - 1]
        return best

    def t_of_clock(self, clock):
        """화면 타이머 "mm:ss" → 기록 시각 t. 같은 초의 한가운데."""
        target = parse_clock(clock)
        hits = [f['t'] for f in self.frames if parse_clock(f['clock']) == target]
        if hits:
            return (hits[0] + hits[-1]) / 2
        # 없으면 가장 가까운 초
        best = min(self.frames, key=lambda f: abs(parse_clock(f['clock']) - target))
        return best['t']


def clock_of(seconds):
    s = max(0, int(math.floor(seconds)))
    return '%02d:%02d' % (s // 60, s % 60)


def parse_clock(text):
    if not text:
        return -1
    if ':' in text:
        m, s = text.split(':')
        return int(m) * 60 + int(s)
    return int(float(text))


# ───────────────────────── 도우미 ─────────────────────────

def short(name):
    return (name or '').replace('Monster_', '')


def planar(a, b):
    return math.hypot(a['x'] - b['x'], a['z'] - b['z'])


def relative_side(player, monster):
    """플레이어가 보는 방향 기준으로 몬스터가 어느 쪽인가."""
    p, f = player['position'], player.get('forward') or {'x': 0, 'z': 1}
    dx, dz = monster['position']['x'] - p['x'], monster['position']['z'] - p['z']
    if math.hypot(dx, dz) < .5:
        return '붙음'
    ang = math.degrees(math.atan2(dx, dz) - math.atan2(f['x'], f['z']))
    ang = (ang + 180) % 360 - 180
    if abs(ang) <= 35:
        return '앞'
    if abs(ang) >= 145:
        return '뒤'
    return ('오른쪽' if ang > 0 else '왼쪽') + ('앞' if abs(ang) < 90 else '뒤')


def speed(v):
    return math.hypot(v['x'], v['z'])


def decision_text(e):
    d = e.get('detail', '')
    return d.split('|', 1)[1] if '|' in d else d


def decision_code(e):
    d = e.get('detail', '')
    return d.split('|', 1)[0] if '|' in d else d


EVENT_NAMES = {
    'decision': '판단', 'monster_state': '상태', 'monster_stunned': '감전', 'player_hit': '피격',
    'qa_mark': '★QA 표시', 'noise_evidence': '소리', 'target_destroyed': '과녁', 'monster_idle': '멈춤',
    'session_start': '시작', 'session_end': '끝', 'role': '역할',
}


def event_line(e):
    kind = e['type']
    who = short(e.get('actor'))
    if kind == 'decision':
        return '%s  %-6s %s' % (e['clock'], who, decision_text(e))
    if kind == 'monster_state':
        return '%s  %-6s (상태 %s)' % (e['clock'], who, e.get('detail'))
    if kind == 'role':
        return '%s  %-6s (역할 → %s)' % (e['clock'], who, e.get('detail') or '없음')
    return '%s  %-6s [%s] %s' % (e['clock'], who, EVENT_NAMES.get(kind, kind), e.get('detail', ''))


def frame_table(f):
    p = f['player']
    rows = []
    for m in sorted(f['monsters'], key=lambda m: planar(m['position'], p['position'])):
        d = planar(m['position'], p['position'])
        rows.append('    %-6s %-6s %-12s %5.1fm  %-6s %s  속도 %4.1f%s' % (
            short(m['name']), m.get('role') or '-', m.get('label') or m.get('state'), d,
            relative_side(p, m), '보는중' if m.get('seesPlayer') else '      ',
            speed(m.get('velocity', {'x': 0, 'z': 0})),
            '  화면에 보임' if m.get('visibleToCamera') else ''))
    return rows


# ───────────────────────── 의심 장면 찾기 ─────────────────────────

def suspicious(tr):
    """사용자가 싫어했던 모습들을 자동으로 찾는다."""
    found = []
    by_monster = defaultdict(list)
    for f in tr.frames:
        for m in f['monsters']:
            by_monster[m['name']].append((f, m))

    for name, seq in by_monster.items():
        prev = None
        still_since = None
        for f, m in seq:
            # ① 보면서 집으로(복귀 시작 순간 플레이어를 보고 있음)
            if prev and prev[1]['state'] != 'Return' and m['state'] == 'Return' and m.get('seesPlayer'):
                found.append((f['t'], f['clock'], short(name), '플레이어를 보면서 복귀 시작'))
            # ② 사냥 역할인데 1.5초 넘게 멈춤(감전·접촉 제외)
            moving = speed(m.get('velocity', {'x': 0, 'z': 0})) > .5
            busy = m.get('role') and not m.get('stunned') and planar(m['position'], f['player']['position']) > 2.5
            if busy and not moving:
                if still_since is None:
                    still_since = (f, m.get('role') or '', m.get('label') or m['state'])
            else:
                if still_since is not None and f['t'] - still_since[0]['t'] >= 1.5:
                    found.append((still_since[0]['t'], still_since[0]['clock'], short(name),
                                  '사냥 중(%s · %s) %.1f초 멈춤' % (still_since[1], still_since[2], f['t'] - still_since[0]['t'])))
                still_since = None
            prev = (f, m)

    # ③ 줄줄이: 플레이어 기준 같은 쪽(45° 안) 10m 안에 사냥 몬스터 2마리 이상이 2초 넘게
    streak = None
    for f in tr.frames:
        p = f['player']['position']
        hunters = [m for m in f['monsters'] if m.get('role') and not m.get('stunned') and planar(m['position'], p) < 25]
        queued = False
        for i in range(len(hunters)):
            for j in range(i + 1, len(hunters)):
                a, b = hunters[i]['position'], hunters[j]['position']
                va = (a['x'] - p['x'], a['z'] - p['z'])
                vb = (b['x'] - p['x'], b['z'] - p['z'])
                na, nb = math.hypot(*va), math.hypot(*vb)
                if na < .5 or nb < .5:
                    continue
                cos = (va[0] * vb[0] + va[1] * vb[1]) / (na * nb)
                if cos > math.cos(math.radians(45)) and planar(a, b) < 10:
                    queued = True
        if queued:
            streak = streak or f
        else:
            if streak and f['t'] - streak['t'] >= 2:
                found.append((streak['t'], streak['clock'], '-', '줄줄이 %.1f초 (같은 쪽 10m 안 2마리+)' % (f['t'] - streak['t'])))
            streak = None

    # ④ 와리가리: 한 몬스터가 3초 안에 상태를 4번 이상 바꿈
    changes = defaultdict(list)
    for e in tr.events:
        if e['type'] == 'monster_state':
            changes[e['actor']].append(e)
    for name, seq in changes.items():
        i = 0
        while i < len(seq):
            j = i
            while j < len(seq) and seq[j]['t'] - seq[i]['t'] <= 3:
                j += 1
            if j - i >= 4:
                found.append((seq[i]['t'], seq[i]['clock'], short(name),
                              '3초 안에 상태 %d번 바뀜: %s' % (j - i, ' / '.join(x['detail'] for x in seq[i:j]))))
                i = j
            else:
                i += 1

    # ⑤ 멈춤(Idle)
    for e in tr.events:
        if e['type'] == 'monster_idle':
            found.append((e['t'], e['clock'], short(e['actor']), '멈춤(Idle): ' + e.get('detail', '')))
    found.sort()
    return found


# ───────────────────────── 명령 ─────────────────────────

def cmd_list():
    for n in sessions()[-15:]:
        path = os.path.join(ROOT, n, 'summary.json')
        extra = ''
        if os.path.isfile(path):
            try:
                s = json.load(io.open(path, encoding='utf-8'))
                extra = '  %5.0f초  과녁 %s  %s' % (s.get('duration', 0), s.get('targetsDestroyed', '?'), s.get('outcome', ''))
            except Exception:
                pass
        print(n + extra)


def cmd_summary(tr):
    print('기록:', tr.name, '·', tr.header.get('scene', ''), '· AI:', tr.header.get('aiPolicy', ''))
    if tr.frames:
        print('시각: %s → %s (%.0f초)' % (tr.frames[0]['clock'], tr.frames[-1]['clock'], tr.frames[-1]['t']))
    kinds = Counter(e['type'] for e in tr.events)
    print('피격 %d · 감전 %d · 과녁 %d · QA 표시 %d' % (kinds['player_hit'], kinds['monster_stunned'], kinds['target_destroyed'], kinds['qa_mark']))
    codes = Counter(decision_code(e) for e in tr.events if e['type'] == 'decision')
    if codes:
        print('감독 판단:', ', '.join('%s %d' % kv for kv in codes.most_common()))
    hunting = [f for f in tr.frames if f.get('hunting')]
    if tr.frames:
        print('사냥 중 %.0f%%' % (100.0 * len(hunting) / len(tr.frames)))
    found = suspicious(tr)
    print('\n의심 장면 %d개 (화면 타이머 기준):' % len(found))
    for t, clock, who, text in found:
        print('  %s  %-6s %s' % (clock, who, text))
    encircle_report(tr)
    zone_report(tr)
    marks = [e for e in tr.events if e['type'] == 'qa_mark']
    if marks:
        print('\nF8 표시:', ', '.join(e['clock'] for e in marks))


def encircle_report(tr):
    """
    포위 지표(사냥 중일 때만) — 핵심 경험 "5마리가 여러 마리처럼 포위하듯 쫓아옴"을 숫자로.
    - 20m 안 사냥 몬스터: 팀 3마리 중 실제로 가까이 있는 수
    - 2마리+ 붙음 / 그중 양쪽: 20m 안 두 마리 이상일 때, 플레이어 기준 반대쪽(내적 < 0)에서 오는 쌍이 있는 비율
    - 우회 성공: 우회 배정 중 옆에서 플레이어를 보고 협공으로 바뀐 비율(engage / (flank_assign + flank_recruit))
    """
    near, two, both = [], 0, 0
    for f in tr.frames:
        if not f.get('hunting'):
            continue
        p = f['player']['position']
        vs = []
        for m in f['monsters']:
            if not m.get('role'):
                continue
            dx, dz = m['position']['x'] - p['x'], m['position']['z'] - p['z']
            d = math.hypot(dx, dz)
            if d < 20:
                vs.append((dx / d, dz / d) if d > .1 else (0.0, 0.0))
        near.append(len(vs))
        if len(vs) >= 2:
            two += 1
            if any(vs[i][0] * vs[j][0] + vs[i][1] * vs[j][1] < 0 for i in range(len(vs)) for j in range(i + 1, len(vs))):
                both += 1
    codes = Counter(decision_code(e) for e in tr.events if e['type'] == 'decision')
    assigned = codes['flank_assign'] + codes['flank_recruit']
    n = max(1, len(near))
    print('\n포위 지표(사냥 중): 20m 안 사냥 몬스터 평균 %.1f · 2마리+ 붙음 %d%% · 그중 양쪽에서 %d%% · 우회 성공 %d/%d(%d%%) · 사냥 시작 %d / 끝 %d · 우회 교대 %d' % (
        sum(near) / n, 100 * two // n, 100 * both // max(1, two), codes['engage'], assigned, 100 * codes['engage'] // max(1, assigned),
        codes['hunt_start'], codes['hunt_end'], codes['flank_swap'] + codes['flank_rotate']))


# 구역 기록(zone 이벤트)이 없는 옛 기록용: 포트폴리오 씬 구역 (2026-09-25 씬 값)
FALLBACK_ZONES = {
    'Assets/Scenes/ShooterInGame_Portfolio.unity': [
        ('Zone_A', 44.6, 74.7, 50), ('Zone_B', -85.2, 22.9, 50), ('Zone_C', 82.1, -10.0, 50), ('Zone_D', 5.5, -76.2, 50)],
}


def zones_of(tr):
    zs = []
    for e in tr.events:
        if e['type'] == 'zone':
            r = 50.0
            for part in e.get('detail', '').split(';'):
                if part.startswith('radius='):
                    r = float(part[7:])
            zs.append((e['actor'], e['position']['x'], e['position']['z'], r))
    return zs or FALLBACK_ZONES.get(tr.header.get('scene', ''), [])


def zone_report(tr):
    """구역마다: 사냥에 안 낀 몬스터가 안에 있는 시간(지킴), 플레이어만 있는 시간, 둘 다 없는 시간(빔)."""
    zs = zones_of(tr)
    if not zs or not tr.frames:
        return
    guarded, empty, with_player = Counter(), Counter(), Counter()
    crowd = 0
    for f in tr.frames:
        p = f['player']['position']
        free = [m for m in f['monsters'] if not m.get('role') and m['state'] in ('Patrol', 'Return', 'Idle', 'Investigate')]
        for name, x, z, r in zs:
            c = {'x': x, 'z': z}
            if any(planar(m['position'], c) <= r for m in free):
                guarded[name] += 1
            elif planar(p, c) <= r:
                with_player[name] += 1
            else:
                empty[name] += 1
        if any(planar(free[i]['position'], free[j]['position']) < 40
               for i in range(len(free)) for j in range(i + 1, len(free))):
            crowd += 1
    n = float(len(tr.frames))
    print('\n구역 (지킴 = 사냥에 안 낀 몬스터가 안에 있음 / 빔 = 몬스터도 플레이어도 없음):')
    for name, x, z, r in zs:
        print('  %-7s 지킴 %3.0f%%  플레이어만 %3.0f%%  빔 %3.0f%%' % (name, 100 * guarded[name] / n, 100 * with_player[name] / n, 100 * empty[name] / n))
    print('  순찰 몬스터끼리 40m 안에 붙어 있던 시간 %.0f%%' % (100 * crowd / n))


def cmd_at(tr, clock, window):
    center = tr.t_of_clock(clock)
    lo, hi = center - window, center + window
    f = tr.frame_at(center)
    print('기록 %s · 화면 %s (t=%.1f초) 전후 %d초' % (tr.name, f['clock'], f['t'], window))
    print('사냥 %s · 감독: %s · 체력 %s · 과녁 %s' % ('중' if f.get('hunting') else '아님', f.get('director', ''), f.get('health'), f.get('targets')))
    for label, t in (('시작', lo), ('그 순간', center), ('끝', hi)):
        fr = tr.frame_at(t)
        print('\n  [%s %s] 플레이어 (%.0f, %.0f) 속도 %.1f' % (label, fr['clock'], fr['player']['position']['x'], fr['player']['position']['z'], speed(fr['player'].get('velocity', {'x': 0, 'z': 0}))))
        print('    이름   역할   상태          거리    방향   시야    이동')
        for row in frame_table(fr):
            print(row)
    print('\n  일어난 일:')
    for e in tr.events:
        if lo <= e['t'] <= hi and e['type'] not in ('shot', 'role'):
            print('   ', event_line(e))
    found = [x for x in suspicious(tr) if lo <= x[0] <= hi]
    if found:
        print('\n  이 구간의 의심 장면:')
        for t, c, who, text in found:
            print('    %s  %-6s %s' % (c, who, text))


def cmd_marks(tr, window):
    marks = [e for e in tr.events if e['type'] == 'qa_mark']
    if not marks:
        print('F8 표시가 없습니다.')
        return
    for e in marks:
        print('=' * 70)
        print('★ QA 표시 %s' % e.get('detail', ''))
        cmd_at(tr, e['clock'], window)


def cmd_decisions(tr):
    for e in tr.events:
        if e['type'] in ('decision', 'monster_stunned', 'player_hit', 'qa_mark'):
            print(event_line(e))


def main(argv):
    if not argv or argv[0] in ('-h', '--help', 'help'):
        print(__doc__)
        return
    cmd = argv[0]
    rest = argv[1:]
    if cmd == 'list':
        cmd_list()
        return
    if cmd == 'at':
        if not rest:
            sys.exit('예: pico_qa.py at 04:22')
        clock = rest[0]
        session = None
        window = 5
        for x in rest[1:]:
            if x.isdigit():
                window = int(x)
            else:
                session = x
        cmd_at(Trace(pick(session)), clock, window)
        return
    session = rest[0] if rest else None
    tr = Trace(pick(session))
    if cmd == 'summary':
        cmd_summary(tr)
    elif cmd == 'marks':
        cmd_marks(tr, 5)
    elif cmd == 'decisions':
        cmd_decisions(tr)
    else:
        print(__doc__)


if __name__ == '__main__':
    main(sys.argv[1:])
