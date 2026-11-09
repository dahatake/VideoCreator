"""Spike 17: does a semantic (embedding) view find where the SUBJECT changes in a real talk?
usage: python 17-topic-embed.py <transcript.json> [out.json]
Embeds every sentence with Azure OpenAI text-embedding-3-large (already deployed on the app's resource), then scores each sentence boundary
by the TextTiling valley depth of the cosine between the mean vectors of the W seconds before / after (W = 30, 60, 90) and prints the strongest
boundaries with the text on both sides, so a person (or a second model) can judge them.
"""
import json, math, subprocess, sys, urllib.request

ENDPOINT = "https://videocreator-ai-40800.cognitiveservices.azure.com/openai/v1/embeddings"
MODEL = "text-embedding-3-large"


def token():
    out = subprocess.run("az account get-access-token --resource https://cognitiveservices.azure.com --query accessToken -o tsv", shell=True, capture_output=True, text=True)
    return out.stdout.strip()


def embed(texts, tok):
    vecs = []
    for i in range(0, len(texts), 96):
        req = urllib.request.Request(ENDPOINT, data=json.dumps({"model": MODEL, "input": texts[i:i + 96]}).encode(), headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=120) as r:
            vecs += [d["embedding"] for d in json.load(r)["data"]]
    return vecs


def cos(a, b):
    na, nb = math.sqrt(sum(x * x for x in a)), math.sqrt(sum(x * x for x in b))
    return sum(x * y for x, y in zip(a, b)) / (na * nb) if na and nb else 0


def mean(vs):
    return [sum(c) / len(vs) for c in zip(*vs)]


def depth(sents, vecs, windows=(30, 60, 90)):
    n = len(sents)
    d = [0.0] * (n + 1)
    for w in windows:
        sim = [0.0] * (n + 1)
        for i in range(1, n):
            l = [vecs[j] for j in range(i - 1, -1, -1) if j == i - 1 or sents[i]["t0"] - sents[j]["t0"] <= w]
            r = [vecs[j] for j in range(i, n) if j == i or sents[j]["t1"] - sents[i]["t0"] <= w]
            sim[i] = cos(mean(l), mean(r))
        for i in range(1, n):
            lp = rp = sim[i]
            j = i - 1
            while j >= 1 and sim[j] >= lp:
                lp = sim[j]; j -= 1
            j = i + 1
            while j < n and sim[j] >= rp:
                rp = sim[j]; j += 1
            d[i] += (lp - sim[i] + rp - sim[i]) / len(windows)
    return d


if __name__ == "__main__":
    tr = json.load(open(sys.argv[1], encoding="utf-8"))
    s = tr["sentences"]
    vecs = embed([x["text"] for x in s], token())
    d = depth(s, vecs)
    order = sorted(range(1, len(s)), key=lambda i: -d[i])
    top = []
    for i in order:
        if all(abs(s[i]["t0"] - s[j]["t0"]) >= 60 for j in top):
            top.append(i)
        if len(top) == 8:
            break
    for i in sorted(top):
        print(f"#{i} {int(s[i]['t0'] // 60)}:{s[i]['t0'] % 60:04.1f} depth {d[i]:.3f}")
        print("   …" + "".join(x["text"] for x in s[max(0, i - 3):i])[-70:])
        print("   ▶" + "".join(x["text"] for x in s[i:i + 3])[:70])
    if len(sys.argv) > 2:
        json.dump({"depth": d, "vectors": vecs}, open(sys.argv[2], "w"))
