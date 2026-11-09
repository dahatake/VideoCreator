using VideoCreator.Models;

namespace VideoCreator.Qa;

/// <summary>Recommended fix for each QA check. Repair "rule"/"ai" checks can be fixed by QaRepair (verified by re-running QaEngine.PlanChecks); "manual" needs a person.</summary>
public static class QaAdvisor
{
    static readonly Dictionary<string, (string Recommendation, string Repair)> Table = new()
    {
        ["H-01"] = ("「区間の調整」で前後を伸ばす・縮めるか、条件を変えて作り直し、尺をプリセットの範囲に収めてください。", "manual"),
        ["H-02"] = ("書き出しをやり直してください（解像度は編集計画の 9:16 キャンバスで出力されます）。", "manual"),
        ["H-03"] = ("書き出しをやり直してください。BGM の音量を下げると目標ラウドネスに収まりやすくなります。", "manual"),
        ["H-04"] = ("書き出しをやり直してください。効果音や BGM を控えめにするとピークが下がります。", "manual"),
        ["H-05"] = ("はみ出したテロップの文字を短くするか、無効にしてください。", "manual"),
        ["H-06"] = ("「区間の調整」でカットの端を単語の切れ目まで動かしてください。", "manual"),
        ["H-07"] = ("発話に出てこない数字・固有名詞をテロップから外し、発話どおりの表現にします。", "ai"),
        ["H-08"] = ("テロップ・カードの内容を確認して承認してください（自動では承認しません）。", "manual"),
        ["H-09"] = ("素材・挿入ファイルの権利を確認し、「権利確認済み」にしてください。", "manual"),
        ["H-10"] = ("テロップ・カードに残った伏せ語を「●」で伏せます。音声が残っている場合は伏せ字を設定し直して作り直してください。", "rule"),
        ["H-11"] = ("区間内の発話から根拠の単語を結び付けます。結び付けられない場合は、根拠発話どおりの文言に書き換えます。", "ai"),
        ["S-01"] = ("まず前後の空き時間へ表示を延ばし（発話の終わりから 0.5 秒・始まりの 0.25 秒前まで）、足りなければ言いよどみ・重複を単語単位で削ります（言い換えはしません）。", "ai"),
        ["S-02"] = ("字幕・強調テロップの文字を削って 1 行に収まる長さにします。", "ai"),
        ["S-03"] = ("見どころを冒頭に先出しするか、オープニングを無効にしてください。", "manual"),
        ["S-04"] = ("短すぎるショットは「区間の調整」で前後とつなげるか、条件を変えて作り直してください。", "manual"),
        ["S-05"] = ("顔に重なるテロップを短くするか無効にしてください。", "manual"),
        ["S-06"] = ("黒画面・フリーズの箇所を確認し、必要なら区間を調整してください。", "manual"),
        ["S-07"] = ("「区間の調整」で冒頭を指示語（これ・それ など）の前の文脈から始めてください。", "manual"),
        ["S-08"] = ("字幕の先頭にある助詞・助動詞を前の字幕の末尾へ移します。", "rule"),
        ["S-09"] = ("根拠発話の否定（〜ない など）をテロップに戻します。", "ai"),
        ["S-11"] = ("1 本あたりの長さを短くして作り直してください。", "manual"),
        ["S-12"] = ("オープニング・クロージングには出力と同じ解像度（1080×1920）以上の素材を使ってください。", "manual"),
        ["S-13"] = ("話者とカメラの対応（素材の話者番号）を確認して作り直してください。", "manual"),
        ["S-14"] = ("チャプターが 00:00 から 3 つ以上・各 10 秒以上になるよう、本数や長さを変えて作り直してください。", "manual"),
    };

    public static bool IsRepairable(string checkId) => Table.TryGetValue(checkId, out var a) && a.Repair != "manual";

    public static QaResult Attach(QaResult q)
    {
        if (q.Status is not (QaStatus.Warn or QaStatus.Fail) || !Table.TryGetValue(q.CheckId, out var a)) return q;
        var repair = a.Repair;
        // only some H-10 findings (telop text) are rule-fixable; spoken leaks need a re-plan
        if (q.CheckId == "H-10" && q.Items is { Count: > 0 } items && items.All(i => i.Kind == "audio")) repair = "manual";
        // S-08 line-wrap violations inside one caption have no safe automatic fix
        if (q.CheckId == "S-08" && q.Items is { Count: > 0 } k && k.All(i => !i.Detail.StartsWith("先頭が", StringComparison.Ordinal))) repair = "manual";
        // H-08 only fails for a stale approval when nothing is unapproved
        var rec = q.CheckId == "H-08" && q.Items is { Count: 0 } ? "編集後に候補の承認が失効しています。内容を確認して承認し直してください。" : a.Recommendation;
        return q with { Advice = new QaAdvice(rec, repair) };
    }
}
