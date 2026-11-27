namespace VideoCreator.Media;

/// <summary>
/// FFmpeg expressions (libavutil/eval.c) are rejected when their tree is deeper than <see cref="MaxDepth"/>: the filter then fails to configure
/// with a bare EINVAL ("Failed to configure input pad") and nothing names the expression. A chain a+b+c+… parses into a left-deep tree as deep
/// as it is long, so every expression built from a list of variable length must be balanced.
/// </summary>
public static class FfExpr
{
    public const int MaxDepth = 100;

    /// <summary>Sum of the terms as a balanced tree: depth grows with log2 of the count instead of the count.</summary>
    public static string Sum(IReadOnlyList<string> terms) => terms.Count == 0 ? "0" : Sum(terms, 0, terms.Count);

    static string Sum(IReadOnlyList<string> t, int lo, int hi)
    {
        if (hi - lo == 1) return t[lo];
        int mid = (lo + hi) / 2;
        return $"({Sum(t, lo, mid)}+{Sum(t, mid, hi)})";
    }

    /// <summary>
    /// Piecewise function of t: <paramref name="pieces"/>[0] before <paramref name="starts"/>[0], pieces[i] from starts[i-1] (inclusive) to starts[i],
    /// the last piece from the last start on. A binary search of if(lt(t,…)) — depth and per-frame cost grow with log2 of the count, and if() evaluates only the branch it takes.
    /// </summary>
    public static string Piecewise(IReadOnlyList<string> pieces, IReadOnlyList<string> starts)
    {
        if (pieces.Count != starts.Count + 1) throw new ArgumentException("one more piece than starts expected");
        return Piecewise(pieces, starts, 0, pieces.Count);
    }

    static string Piecewise(IReadOnlyList<string> p, IReadOnlyList<string> s, int lo, int hi)
    {
        if (hi - lo == 1) return p[lo];
        int mid = (lo + hi) / 2;
        return $"if(lt(t,{s[mid - 1]}),{Piecewise(p, s, lo, mid)},{Piecewise(p, s, mid, hi)})";
    }
}
