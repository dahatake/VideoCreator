using System.Text.RegularExpressions;
using VideoCreator.Models;

namespace VideoCreator.Planning;

/// <summary>Evidence for one boundary (before sentence i): how cleanly the previous part ends and the next one starts.</summary>
public sealed record SplitCue(double Closure, double Continuation, bool WrapUp, bool LaughEnded, bool LaughSpans, bool ShotCut, bool Fade)
{
    /// <summary>Added to the topic score of the boundary (same scale as TextTiling depth 0..1). A cut-off previous utterance costs 2.0 —
    /// more than the largest AI chapter bonus (1.6) — so a part never ends mid-thought while a finished utterance is within the length tolerance.</summary>
    public double Score =>
        (Closure > 0 ? 0.3 * Closure : 2.0 * Closure) + 0.8 * Continuation + (WrapUp ? 0.4 : 0)
        + (LaughEnded ? 0.3 : 0) - (LaughSpans ? 0.5 : 0) + (Fade ? 0.8 : ShotCut ? 0.4 : 0);

    /// <summary>The previous part would end in the middle of a thought (on a connective, filler or pointing word). A leaning start of the
    /// next part (「だから」) only lowers the score: in casual speech it is often a discourse filler.</summary>
    public bool MidThought => Closure < 0;
}

/// <summary>
/// Where a long talk can be cut into parts without stopping in the middle of a thought — the cues editors of multi-part
/// YouTube / TikTok videos cut on: the previous utterance is finished (a sentence-final form, not 「〜けど」「〜て」「これ」),
/// the next one stands on its own (no 「それ」「で、」 back-reference), a laugh has just ended, and the picture changes
/// (a shot cut or a fade to black). A question is a complete utterance too (the classic cliffhanger: the question ends one part,
/// the answer opens the next).
/// </summary>
public static partial class SplitCues
{
    const string Enders = "。！？!?";

    // complete: polite/plain sentence-final forms (ASR often drops 「。」 in casual speech)
    [GeneratedRegex(@"(ます|です|ました|でした|ません|ましょう|でしょう|ください|だね|だよ|よね|ですね|ますね|ですよ|ますよ|でしたね|ましたね|ですか|ますか|ある|ない|いる|った|んだ|はい|ね|よ|な)$")]
    private static partial Regex FinalForm();

    // the thought continues: conjunctive particles (〜けど、〜て、〜から …)
    [GeneratedRegex(@"(けど|けども|けれど|けれども|ですが|ますが|ので|から|って|とか|たり|ながら|みたいな|という|っていう|ていうか|っていうか|んで|で|て|し|に|を|は|が|の|と|も)$")]
    private static partial Regex Connective();

    // hesitation or a pointing word as the very last word: the sentence was cut off
    [GeneratedRegex(@"(これ|それ|あれ|この|その|あの|えっと|えー|あー|なんか|ちょっと|まあ|もう)$")]
    private static partial Regex CutOff();

    // the next sentence leans on the previous one
    static readonly string[] Leaning = ["で、", "でも", "だから", "それで", "なので", "ただ、", "しかも", "つまり", "けど", "って", "そしたら", "そうそう", "そうなんです", "そうですね", "それが", "そこで"];

    static readonly string[] WrapUps = ["ということで", "というわけで", "以上", "ありがとうございました", "おしまい", "終わりです", "終わります"];

    // strong sentence-final forms only — used to restore a missing 「。」 inside an ASR sentence
    [GeneratedRegex(@"(ます|です|ました|でした|ません|ましょう|でしょう)(ね|よ|よね)?$")]
    private static partial Regex StrongFinal();

    static string Core(string text) => text.Trim().TrimEnd('　', ' ', '、', ',', '。', '！', '？', '!', '?');

    /// <summary>+1 a finished sentence, +0.6 a question, 0 unknown or softened (「〜けど。」), −1 cut off mid-thought.</summary>
    public static double Closure(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return 0;
        var core = Core(t);
        if (core.Length == 0) return 0;
        if (t.EndsWith('、') || t.EndsWith(',') || CutOff().IsMatch(core)) return -1;
        bool punct = Enders.Contains(t[^1]);
        bool question = t[^1] is '？' or '?' || core.EndsWith('か');
        if (Connective().IsMatch(core) && !FinalForm().IsMatch(core)) return punct ? 0 : -1;
        if (question) return 0.6;
        return punct || FinalForm().IsMatch(core) ? 1 : 0;
    }

    // response words and fillers in front of the real start (「はい、だから…」 still leans on the previous sentence)
    [GeneratedRegex(@"^((はい|ええ|うん|えっと|えー|あー|あの|まあ|なんか|そう)[、,\s]*)+")]
    private static partial Regex LeadingFiller();

    /// <summary>−1 the sentence continues the previous one (「で、」「だから」「そうですね」), −0.6 starts with a pointing word, else 0.</summary>
    public static double Continuation(string text)
    {
        var t = text.TrimStart('　', ' ');
        if (Leaning.Any(x => t.StartsWith(x, StringComparison.Ordinal))) return -1;
        var body = LeadingFiller().Replace(t, "");
        if (body.Length < t.Length && Leaning.Any(x => body.StartsWith(x, StringComparison.Ordinal))) return -1;
        if (JapaneseText.StartsWithDemonstrative(t) && !t.StartsWith("あの", StringComparison.Ordinal)) return -0.6;
        return 0;
    }

    public static bool IsWrapUp(string text) { var t = Core(text); return WrapUps.Any(w => t.Contains(w, StringComparison.Ordinal) && t.Length - t.LastIndexOf(w, StringComparison.Ordinal) <= w.Length + 8); }

    /// <summary>Cue for the boundary before each sentence (index 0 and n are neutral).</summary>
    public static SplitCue[] Compute(Transcript tr, SignalSet sig, IReadOnlyList<Interval>? laughs = null, IReadOnlyList<Interval>? fades = null)
    {
        var s = tr.Sentences; int n = s.Count;
        var res = new SplitCue[n + 1];
        var none = new SplitCue(0, 0, false, false, false, false, false);
        res[0] = none; if (n > 0) res[n] = none;
        for (int i = 1; i < n; i++)
        {
            double end = s[i - 1].T1, start = Math.Max(end, s[i].T0), mid = (end + start) / 2;
            bool laughEnded = false, laughSpans = false;
            foreach (var l in laughs ?? [])
            {
                if (l.T0 < mid - 0.3 && l.T1 > mid + 0.3) laughSpans = true;
                else if (l.T1 >= end - 1.0 && l.T1 <= start + 0.3) laughEnded = true;
            }
            bool shot = sig.Shots.Any(x => x >= end - 0.3 && x <= start + 0.3);
            bool fade = (fades ?? []).Any(f => f.T1 >= end - 0.5 && f.T0 <= start + 0.5);
            res[i] = new SplitCue(Closure(s[i - 1].Text), Continuation(s[i].Text), IsWrapUp(s[i - 1].Text), laughEnded, laughSpans, shot, fade);
        }
        return res;
    }

    /// <summary>Short Japanese reasons for the boundary report (empty when nothing notable).</summary>
    public static List<string> Describe(SplitCue c)
    {
        var why = new List<string>();
        if (c.Fade) why.Add("映像の暗転");
        else if (c.ShotCut) why.Add("映像の場面転換");
        if (c.WrapUp) why.Add("話のまとめの直後");
        if (c.LaughEnded) why.Add("笑いの直後");
        if (c.Closure >= 1) why.Add("文が言い切りで終わる");
        else if (c.Closure > 0) why.Add("問いかけで終わる");
        return why;
    }

    /// <summary>
    /// Restores sentence ends the recogniser missed: an ASR sentence is split between two words when the left side ends with a
    /// polite/plain sentence-final form (「〜と思います」) and a pause of at least <paramref name="minPause"/> s follows — e.g.
    /// 「…ぜひどっかだと思います｜あともう一個なんですけどね…」. Words are not changed, only the sentence partition, so every
    /// word stays in exactly one sentence. Sentences whose display text is not the concatenation of their words are kept as they are.
    /// </summary>
    public static Transcript Resegment(Transcript tr, double minPause = 0.3, double minPieceSec = 0.6)
    {
        var w = tr.Words; var res = new List<Sentence>(); bool changed = false;
        string Concat(int a, int b) => string.Concat(Enumerable.Range(a, b - a + 1).Select(i => w[i].Text));
        static string Squash(string x) => new(x.Where(c => !char.IsWhiteSpace(c)).ToArray());
        foreach (var s in tr.Sentences)
        {
            if (s.LastWord <= s.FirstWord || s.LastWord >= w.Count || Squash(Concat(s.FirstWord, s.LastWord)) != Squash(s.Text))
            { res.Add(s with { Index = res.Count }); continue; }
            int a = s.FirstWord;
            for (int k = s.FirstWord + 1; k <= s.LastWord; k++)
            {
                if (w[k].T0 - w[k - 1].T1 < minPause || !w[k].Text.Any(char.IsLetterOrDigit)) continue;
                if (w[k - 1].T1 - w[a].T0 < minPieceSec || w[s.LastWord].T1 - w[k].T0 < minPieceSec) continue;
                var left = Concat(a, k - 1);
                if (!StrongFinal().IsMatch(Core(left)) || left.TrimEnd().EndsWith('、')) continue;
                res.Add(new Sentence(res.Count, a == s.FirstWord ? s.T0 : w[a].T0, w[k - 1].T1, left.Trim(), s.Speaker, a, k - 1));
                a = k; changed = true;
            }
            res.Add(a == s.FirstWord ? s with { Index = res.Count } : new Sentence(res.Count, w[a].T0, s.T1, Concat(a, s.LastWord).Trim(), s.Speaker, a, s.LastWord));
        }
        return changed ? tr with { Sentences = res } : tr;
    }
}
