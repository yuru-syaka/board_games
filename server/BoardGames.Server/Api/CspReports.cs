using System.Text.Json;

namespace BoardGames.Server.Api;

/// <summary>
/// ブラウザが送ってくる CSP の違反の報告を読み、1 件 1 行のログにする（ステップ 6-3）。
/// 2 つの形式に対応する:
///   - report-uri（古い形式）: Content-Type application/csp-report、本文 {"csp-report": {"document-uri", "blocked-uri", "effective-directive", …}}
///   - report-to（Reporting API）: Content-Type application/reports+json、本文 [{"type":"csp-violation","url","body":{"documentURL","blockedURL","effectiveDirective",…}}]
/// URL は「? より前」だけを残す（ログインの戻り先の ?code= などをログに残さないため）。
/// おかしな本文は黙って捨てる（ブラウザにエラーを返しても意味がないため）。
/// </summary>
public static class CspReports
{
    /// <summary>受け付ける本文の大きさの上限。</summary>
    public const int MaxBodyLength = 16 * 1024;

    /// <summary>1 回の報告から書くログの行数の上限（同じページの違反がまとめて届くことがあるため）。</summary>
    public const int MaxEntries = 5;

    public static IReadOnlyList<string> Parse(string? body)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength) return lines;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var classic))
            {
                lines.Add(Line(Str(classic, "effective-directive") ?? Str(classic, "violated-directive"),
                    Str(classic, "blocked-uri"), Str(classic, "document-uri"), Str(classic, "source-file"),
                    Num(classic, "line-number"), Str(classic, "disposition")));
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var report in root.EnumerateArray())
                {
                    if (lines.Count >= MaxEntries) break;
                    if (report.ValueKind != JsonValueKind.Object || Str(report, "type") != "csp-violation" ||
                        !report.TryGetProperty("body", out var b) || b.ValueKind != JsonValueKind.Object) continue;
                    lines.Add(Line(Str(b, "effectiveDirective"), Str(b, "blockedURL"), Str(b, "documentURL") ?? Str(report, "url"),
                        Str(b, "sourceFile"), Num(b, "lineNumber"), Str(b, "disposition")));
                }
            }
        }
        catch (JsonException)
        {
            // 読めない本文は捨てる
        }
        return lines;
    }

    static string Line(string? directive, string? blocked, string? page, string? source, long? line, string? disposition) =>
        "csp-violation" +
        $" directive={Clean(directive)}" +
        $" blocked={Clean(StripQuery(blocked))}" +
        $" page={Clean(StripQuery(page))}" +
        $" source={Clean(StripQuery(source))}{(line is { } l ? ":" + l : "")}" +
        $" disposition={Clean(disposition)}";

    /// <summary>URL の ? と # より後ろを取り除く（"inline" "eval" などの URL でない値はそのまま）。</summary>
    public static string? StripQuery(string? url)
    {
        if (url == null) return null;
        int cut = url.IndexOfAny(new[] { '?', '#' });
        return cut >= 0 ? url.Substring(0, cut) : url;
    }

    /// <summary>ログを 1 行に保ち、長すぎる値を切る。</summary>
    static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "-";
        var oneLine = new string(s.Select(c => char.IsControl(c) || c == ' ' ? '_' : c).ToArray());
        return oneLine.Length > 200 ? oneLine.Substring(0, 200) + "…" : oneLine;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}
