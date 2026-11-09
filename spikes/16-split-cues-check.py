# Spike 16 (independent check): does SplitCues.Closure agree with a Japanese morphological analyser on where an utterance is finished?
# Input: spikes/out/split-cues/boundaries.json and report.json (written by spikes/16-split-cues.cs split ...).
# janome (pure-Python, IPADIC) tags the last word of every sentence; "dangling" = 接続助詞 / 格助詞 / 係助詞 / 副助詞 / フィラー /
# 連体詞 / 代名詞 / 副詞 / 接続詞, or a verb/adjective/auxiliary in 連用形 (〜て/〜で/〜し); "closed" = an auxiliary or verb in
# 基本形 (終止形), 終助詞, or a question. The C# rule is regex based, so agreement here is evidence from an unrelated method.
# Usage: venv python spikes/16-split-cues-check.py
import json, sys, collections
from pathlib import Path
from janome.tokenizer import Tokenizer

out = Path(__file__).resolve().parent / "out" / "split-cues"
tok = Tokenizer()
PUNCT = set("、。！？!?,. 　")

def judge(text):
    toks = [t for t in tok.tokenize(text.strip()) if t.surface not in PUNCT and not t.part_of_speech.startswith("記号")]
    if not toks:
        return "unknown", ""
    t = toks[-1]
    pos = t.part_of_speech.split(",")
    form = t.infl_form
    tag = f"{t.surface}/{pos[0]}-{pos[1]}/{form}"
    if pos[0] == "助詞":
        if pos[1] == "終助詞" or "終助詞" in pos[1]:  # 「〜ですか」 is tagged 副助詞／並立助詞／終助詞; sentence-final it is the question particle
            return "closed", tag
        if pos[1] in ("接続助詞", "格助詞", "係助詞", "副助詞", "連体化", "並立助詞", "副助詞／並立助詞／終助詞"):
            return "dangling", tag
    if pos[0] in ("フィラー", "連体詞", "接続詞", "副詞") or (pos[0] == "名詞" and pos[1] == "代名詞"):
        return "dangling", tag
    if pos[0] in ("動詞", "形容詞", "助動詞"):
        if form.startswith("基本形") or form == "*" or t.surface == "でしょ":
            return "closed", tag
        if form.startswith("連用") or form.startswith("仮定") or form.startswith("未然"):
            return "dangling", tag
    if pos[0] == "感動詞":
        return "closed", tag
    return "unknown", tag

def sign(c):
    return "closed" if c > 0 else "dangling" if c < 0 else "unknown"

rows = json.loads((out / "boundaries.json").read_text(encoding="utf-8"))
conf = collections.Counter()
disagree = []
for r in rows:
    j, tag = judge(r["prev"])
    s = sign(r["closure"])
    conf[(s, j)] += 1
    if {s, j} == {"closed", "dangling"}:
        disagree.append((r["i"], s, j, tag, r["prev"][-30:]))

both = [(s, j) for (s, j) in conf.elements() if s != "unknown" and j != "unknown"]
agree = sum(1 for s, j in both if s == j)
print(f"boundaries {len(rows)}; rule vs janome (both decided) agreement {agree}/{len(both)} = {agree / max(1, len(both)):.1%}")
for k in sorted(conf):
    print(f"  rule={k[0]:8s} janome={k[1]:8s} {conf[k]}")
print("disagreements (first 15):")
for d in disagree[:15]:
    print("  ", d)

print("\ncut points:")
for r in json.loads((out / "report.json").read_text(encoding="utf-8")):
    j, tag = judge(r["prev_end"])
    print(f"  {r['outline']:6s} {r['condition']:6s} t={r['time']:7.1f}  janome: {j:8s} {tag:28s} …{r['prev_end'][-24:]}")
json.dump({"agreement": agree / max(1, len(both)), "n": len(both), "matrix": {f"{a}|{b}": v for (a, b), v in conf.items()}}, open(out / "janome.json", "w", encoding="utf-8"), ensure_ascii=False)
