# Spike 28 (analysis): telop events and timing statistics from spikes/28-variety-telops.cs OCR output (docs/IMPLEMENTATION.md §25).
#  Tracks OCR lines across frames into telop events, classifies them (red-box narration / speech caption / other) and measures timing against
#  word timestamps from Azure fast transcription, size, position and colour. Prints numbers only (and short OCR strings with --examples).
# Usage: python spikes/28-variety-telops.py <name>=<ocr.jsonl>[,<asr.json>[,<cuts.txt>[,<info.json>]]] ... [--examples] [--json out.json]
import json, sys, colorsys, statistics as st, re, unicodedata
from collections import Counter

def norm(s):
    return ''.join(c for c in unicodedata.normalize('NFKC', s) if c.isalnum())

def bigrams(s):
    return {s[i:i + 2] for i in range(len(s) - 1)}

def pct(xs, q):
    xs = sorted(xs)
    if not xs: return float('nan')
    p = q * (len(xs) - 1); lo = int(p)
    return xs[lo] + (xs[min(lo + 1, len(xs) - 1)] - xs[lo]) * (p - lo)

def iou(a, b):
    x0, y0, x1, y1 = max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3])
    if x1 <= x0 or y1 <= y0: return 0.0
    inter = (x1 - x0) * (y1 - y0)
    return inter / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter)

def hsv(c):
    return colorsys.rgb_to_hsv(c[0] / 255, c[1] / 255, c[2] / 255)

def is_red(c):
    h, s, v = hsv(c)
    return (h < 0.04 or h > 0.95) and s > 0.6 and v > 0.55

def hue_name(c):
    h, s, v = hsv(c)
    if s < 0.3 or v < 0.3: return 'white' if v > 0.75 and s < 0.3 else 'dark' if v < 0.3 else 'gray'
    deg = h * 360
    for lim, name in [(15, 'red'), (40, 'orange'), (70, 'yellow'), (160, 'green'), (200, 'cyan'), (255, 'blue'), (290, 'purple'), (345, 'pink'), (361, 'red')]:
        if deg < lim: return name

def dist(a, b):
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5

def fill_of(colors, ring):
    # the telop's own fill: a saturated colour clearly different from the background just outside; else white (white fill + dark outline)
    cand = [c for c in colors if dist(c[:3], ring) > 70 and c[3] >= 0.06]
    sat = [c for c in cand if hsv(c[:3])[1] > 0.4 and hsv(c[:3])[2] > 0.35]
    if sat: return hue_name(max(sat, key=lambda c: c[3])[:3])
    if any(min(c[:3]) > 200 for c in cand): return 'white'
    return 'other'

def load(path):
    lines = open(path, encoding='utf-8').read().splitlines()
    head = json.loads(lines[0]); frames = [json.loads(l) for l in lines[1:]]
    return head, frames

def overlap(a, b):
    # same row and overlapping horizontally: OCR boxes of one telop jitter in width when part of the text is missed
    yo = min(a[3], b[3]) - max(a[1], b[1]); xo = min(a[2], b[2]) - max(a[0], b[0])
    return yo > 0.5 * min(a[3] - a[1], b[3] - b[1]) and xo > 0.3 * min(a[2] - a[0], b[2] - b[0])

def tsim(a, b):
    A, B = bigrams(norm(a)), bigrams(norm(b))
    return len(A & B) / max(1, min(len(A), len(B)))

def track(head, frames):
    dt = 1 / head['fps']; active = []; done = []
    for f in frames:
        t = f['t']; nxt = []
        used = set()
        for l in f['lines']:
            best = None
            for i, e in enumerate(active):
                if i in used or not overlap(e['box'], l['box']): continue
                o = iou(e['box'], l['box']); sim = tsim(e['texts'][-1], l['text'])
                if sim > 0.25 or o > 0.6:
                    if best is None or sim + o > best[1]: best = (i, sim + o)
            if best:
                e = active[best[0]]; used.add(best[0])
                e['box'] = l['box']; e['texts'].append(l['text']); e['glyphs'].append(l['glyph']); e['end'] = t + dt
                e['colors'].append(l['colors']); e['rings'].append(l['ring']); e['last'] = t
            else:
                nxt.append({'start': t, 'end': t + dt, 'last': t, 'box': l['box'], 'texts': [l['text']], 'glyphs': [l['glyph']], 'colors': [l['colors']], 'rings': [l['ring']]})
        # an event survives up to 0.75 s of missed frames (OCR flicker on stylised text)
        still = []
        for e in active:
            if t - e['last'] <= 3 * dt + 1e-6: still.append(e)
            else: done.append(e)
        active = still + nxt
    done.extend(active)
    done.sort(key=lambda e: e['start'])
    # merge fragments of one telop: same place, similar text, ≤ 1 s apart
    merged = []
    for e in done:
        for m in reversed(merged[-12:]):
            if 0 <= e['start'] - m['end'] <= 1.0 and overlap(m['box'], e['box']) and tsim(' '.join(m['texts']), ' '.join(e['texts'])) > 0.3:
                for k in ('texts', 'glyphs', 'colors', 'rings'): m[k] += e[k]
                m['end'] = max(m['end'], e['end']); m['last'] = max(m['last'], e['last']); m['box'] = e['box']
                break
        else: merged.append(e)
    return merged

def summarise(e, H):
    texts = Counter(e['texts']); text = max(texts, key=lambda s: (texts[s], len(s)))
    ring = [int(st.median(r[i] for r in e['rings'])) for i in range(3)]
    red_box = sum(1 for r in e['rings'] if is_red(r)) >= 0.6 * len(e['rings'])
    fills = Counter(fill_of(c, r) for c, r in zip(e['colors'], e['rings']))
    b = e['box']
    return {'start': round(e['start'], 2), 'end': round(e['end'], 2), 'dur': round(e['end'] - e['start'], 2), 'text': text,
            'n': len(norm(text)), 'glyph': st.median(e['glyphs']) / H, 'cx': (b[0] + b[2]) / 2, 'cy': (b[1] + b[3]) / 2 / H, 'x1': b[2],
            'red_box': red_box, 'fill': fills.most_common(1)[0][0], 'ring': ring}

def words_of(asr):
    ws = []
    for p in asr['phrases']:
        for w in p.get('words', []):
            ws.append((w['offsetMilliseconds'] / 1000, (w['offsetMilliseconds'] + w['durationMilliseconds']) / 1000, w['text']))
    ws.sort()
    return ws

def utterance_ends(ws):
    ends = []
    for i, (t0, t1, s) in enumerate(ws):
        gap = ws[i + 1][0] - t1 if i + 1 < len(ws) else 9
        if gap >= 0.3 or re.search(r'[。？！?!]$', s): ends.append(t1)
    return ends

def match_speech(ev, ws):
    # chars of the transcript near the telop; the matched span gives the speech start/end the telop shows
    win = [w for w in ws if w[1] > ev['start'] - 4 and w[0] < ev['end'] + 1.5]
    chars = []
    for w in win:
        for ch in norm(w[2]): chars.append((ch, w[0], w[1]))
    s = ''.join(c[0] for c in chars); target = norm(ev['text'])
    tb = bigrams(target)
    if len(tb) < 2: return None
    hits = [i for i in range(len(s) - 1) if s[i:i + 2] in tb]
    if not hits: return 0.0, None, None
    # densest cluster of hits ≈ the spoken phrase
    best = (0, 0, 0)
    for a in range(len(hits)):
        for b2 in range(a, len(hits)):
            if hits[b2] - hits[a] > len(target) * 1.6 + 2: break
            got = len({s[h:h + 2] for h in hits[a:b2 + 1]} & tb)
            if got > best[0]: best = (got, hits[a], hits[b2] + 1)
    rec = best[0] / len(tb)
    return rec, chars[best[1]][1], chars[best[2]][2]

def analyse(name, ocr, asr=None, cuts=None, info=None, examples=False):
    head, frames = load(ocr); H = head['height']; W = head['width']
    dur = len(frames) / head['fps']
    evs = [summarise(e, H) for e in track(head, frames)]
    evs = [e for e in evs if e['dur'] >= 0.5 and e['n'] >= 2]
    persistent = [e for e in evs if e['dur'] > 20]
    evs = [e for e in evs if e['dur'] <= 20]
    ws = words_of(asr) if asr else []
    ends = utterance_ends(ws) if ws else []
    for e in evs:
        m = match_speech(e, ws) if ws else None
        e['recall'] = m[0] if m else 0
        e['sp0'], e['sp1'] = (m[1], m[2]) if m and m[1] is not None else (None, None)
        e['kind'] = 'narration' if e['red_box'] else 'speech' if e['recall'] >= 0.5 else 'other'
    # one red box read as two OCR lines (「今後の」「きそう…。」): same row, overlapping in time → one narration
    na0 = sorted((e for e in evs if e['kind'] == 'narration'), key=lambda e: e['start']); keep = []
    for e in na0:
        k = keep[-1] if keep else None
        if k and e['start'] < k['end'] and abs(e['cy'] - k['cy']) < 0.04:
            k['end'] = max(k['end'], e['end']); k['dur'] = round(k['end'] - k['start'], 2)
            k['text'] = k['text'] + e['text'] if e['cx'] > k['cx'] else e['text'] + k['text']; k['n'] = len(norm(k['text']))
            k['cx'] = max(k['cx'], e['cx'])
        else: keep.append(e)
    evs = [e for e in evs if e['kind'] != 'narration'] + keep
    out = {'name': name, 'duration_min': dur / 60}
    by = {k: [e for e in evs if e['kind'] == k] for k in ['narration', 'speech', 'other']}
    out['events_per_min'] = {k: len(v) / (dur / 60) for k, v in by.items()}
    sp, na = by['speech'], by['narration']
    capg = pct([e['glyph'] for e in sp], 0.5)
    out['speech'] = {
        'count': len(sp), 'glyph_p10_p50_p90': [pct([e['glyph'] for e in sp], q) for q in (0.1, 0.5, 0.9)],
        'enlarged_share(>=1.6x median)': sum(e['glyph'] >= 1.6 * capg for e in sp) / max(1, len(sp)),
        'dur_p25_p50_p75': [pct([e['dur'] for e in sp], q) for q in (0.25, 0.5, 0.75)],
        'chars_p50': pct([e['n'] for e in sp], 0.5),
        'onset_lag_p25_p50_p75': [pct([e['start'] - e['sp0'] for e in sp if e['sp0'] is not None], q) for q in (0.25, 0.5, 0.75)],
        'offset_after_speech_p25_p50_p75': [pct([e['end'] - e['sp1'] for e in sp if e['sp1'] is not None], q) for q in (0.25, 0.5, 0.75)],
        'cps_p50_p90': [pct([e['n'] / e['dur'] for e in sp], q) for q in (0.5, 0.9)],
        'fill': Counter(e['fill'] for e in sp).most_common(),
        'cx_left_mid_right': [sum(e['cx'] < W / 3 for e in sp) / max(1, len(sp)), sum(W / 3 <= e['cx'] <= 2 * W / 3 for e in sp) / max(1, len(sp)), sum(e['cx'] > 2 * W / 3 for e in sp) / max(1, len(sp))],
        'cy_p50': pct([e['cy'] for e in sp], 0.5),
    }
    def prev_end(t):
        xs = [x for x in ends if x <= t + 0.05]
        return xs[-1] if xs else None
    def speaking(t):
        return any(w[0] <= t <= w[1] for w in ws)
    gaps = [e['start'] - prev_end(e['start']) for e in na if prev_end(e['start']) is not None]
    bottom = [e['glyph'] for e in sp if e['cy'] > 0.8 and e['glyph'] < 1.6 * capg]
    def ending(s):
        s = re.sub(r'[。．.…・!！ー〜~1-9]+$', '', s)
        if re.search(r'[?？]', s[-2:] if s else ''): return 'question'
        if not s: return 'other'
        c = s[-1]
        if re.match(r'[\u30a0-\u30ff\u4e00-\u9fffA-Za-z0-9]', c): return 'noun (体言止め)'
        return 'predicate (用言・終助詞)'
    out['narration'] = {
        'count': len(na), 'per_min': len(na) / (dur / 60),
        'glyph_p50': pct([e['glyph'] for e in na], 0.5), 'glyph_ratio_to_caption': pct([e['glyph'] for e in na], 0.5) / capg if sp else None,
        'bottom_caption_glyph_p50': pct(bottom, 0.5), 'glyph_ratio_to_bottom_caption': pct([e['glyph'] for e in na], 0.5) / pct(bottom, 0.5) if bottom else None,
        'ending': Counter(ending(e['text']) for e in na).most_common(),
        'exclamation': sum(bool(re.search(r'[!！]', e['text'])) for e in na) / max(1, len(na)),
        'chars_p25_p50_p75': [pct([e['n'] for e in na], q) for q in (0.25, 0.5, 0.75)],
        'dur_p25_p50_p75': [pct([e['dur'] for e in na], q) for q in (0.25, 0.5, 0.75)],
        'after_utterance_end_p25_p50_p75': [pct(gaps, q) for q in (0.25, 0.5, 0.75)],
        'starts_while_someone_speaks': sum(speaking(e['start']) for e in na) / max(1, len(na)),
        'ends_with_maru': sum(e['text'].endswith('。') for e in na) / max(1, len(na)),
        'right_third': sum(e['cx'] > 2 * W / 3 for e in na) / max(1, len(na)),
        'cy_p50': pct([e['cy'] for e in na], 0.5),
        'gap_to_next_narration_p50': pct([b['start'] - a['start'] for a, b in zip(na, na[1:])], 0.5),
    }
    # concurrency: how many telops share the screen
    if cuts:
        cs = cuts
        def since_cut(t):
            xs = [c for c in cs if c <= t + 0.05]
            return t - xs[-1] if xs else None
        def to_next_cut(t):
            xs = [c for c in cs if c > t + 0.05]
            return xs[0] - t if xs else None
        out['cuts'] = {'asl': dur / (len(cs) + 1),
                       'narration_since_cut_p50': pct([x for x in map(since_cut, [e['start'] for e in na]) if x is not None], 0.5),
                       'narration_within_0.3s_of_cut': sum(1 for e in na if (since_cut(e['start']) or 9) < 0.3) / max(1, len(na)),
                       'narration_end_within_0.3s_of_cut': sum(1 for e in na if (to_next_cut(e['end']) or 9) < 0.3) / max(1, len(na))}
    if info and info.get('heatmap'):
        hm = info['heatmap']; vals = sorted(h['value'] for h in hm); hi = vals[int(0.8 * len(vals))]
        def rate(kind, top):
            bins = [h for h in hm if (h['value'] >= hi) == top]
            span = sum(h['end_time'] - h['start_time'] for h in bins) / 60
            n = sum(1 for e in by[kind] for h in bins if h['start_time'] <= e['start'] < h['end_time'])
            return n / span if span else float('nan')
        out['heatmap_top20%_vs_rest_per_min'] = {k: [rate(k, True), rate(k, False)] for k in ['narration', 'speech']}
    if examples:
        out['narration_examples'] = [e['text'] for e in na]
        out['enlarged_examples'] = [e['text'] for e in sp if e['glyph'] >= 1.6 * capg][:40]
    out['persistent'] = [(e['text'], round(e['dur'])) for e in persistent][:6]
    return out

if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if '=' in a and not a.startswith('--')]
    ex = '--examples' in sys.argv
    res = []
    for a in args:
        name, files = a.split('=', 1); f = files.split(',')
        asr = json.load(open(f[1], encoding='utf-8')) if len(f) > 1 and f[1] else None
        cuts = [float(x) for x in open(f[2]).read().split()] if len(f) > 2 and f[2] else None
        info = json.load(open(f[3], encoding='utf-8')) if len(f) > 3 and f[3] else None
        res.append(analyse(name, f[0], asr, cuts, info, ex))
    s = json.dumps(res, ensure_ascii=False, indent=1, default=lambda x: round(x, 3))
    if '--json' in sys.argv: open(sys.argv[sys.argv.index('--json') + 1], 'w', encoding='utf-8').write(s)
    print(re.sub(r'(\d+\.\d{3})\d+', r'\1', s))
