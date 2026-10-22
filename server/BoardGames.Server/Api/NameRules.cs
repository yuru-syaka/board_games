using System.Globalization;
using System.Text;

namespace BoardGames.Server.Api;

/// <summary>
/// 表示名の決まり（ステップ 5-4）。ランキングとプロフィールに出る名前なので、ここで必ず確かめる。
///   - 前後の空白は取り除き、続いた空白は 1 つにする
///   - 2〜12 文字（絵文字や結合文字は見た目の 1 文字で数える）
///   - 改行・制御文字・見えない文字ははねる
///   - NG ワード（運営を名乗るもの・ひどい言葉）を含む名前ははねる。全角・半角、大文字・小文字、ひらがな・カタカナの違いは同じとみなす
///   - 同じ名前は使える（名前でユーザーを見分けない）
/// </summary>
public static class NameRules
{
    public const int MinLength = 2;
    public const int MaxLength = 12;

    // 比べる前に NormalizeForCheck をかけるので、ここは半角・小文字・カタカナで書く
    static readonly string[] NgWords =
    {
        // 運営・公式を名乗る
        "運営", "公式", "boardpia", "ボードピア", "admin", "official", "スタッフ", "staff", "moderator",
        // ひどい言葉・性的な言葉（必要に応じて足す）
        // （「シネマ」「Essex」のような普通の言葉をはねないよう、短すぎる語は入れない）
        "コロス", "殺す", "死ね", "キチガイ", "ガイジ", "fuck", "shit", "bitch", "nigger", "nigga",
        "チンコ", "マンコ", "セックス", "porn",
    };

    /// <summary>名前を整えて確かめる。だめなら false と、利用者に見せる理由（日本語）。</summary>
    public static bool TryNormalize(string? input, out string name, out string error)
    {
        name = "";
        error = "";
        if (input == null)
        {
            error = "名前を入力してください。";
            return false;
        }

        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in input.Trim())
        {
            if (c is not ('\n' or '\r') && char.IsWhiteSpace(c))
            {
                space = true; // タブなどの空白は、まとめて 1 つの空白にする
                continue;
            }
            var cat = char.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                || (cat == UnicodeCategory.Format && c != '‍')) // 絵文字をつなぐ ZWJ だけは許す
            {
                error = "名前に使えない文字が含まれています。";
                return false;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        name = sb.ToString();

        int length = new StringInfo(name).LengthInTextElements;
        if (length < MinLength || length > MaxLength)
        {
            error = $"名前は {MinLength}〜{MaxLength} 文字にしてください。";
            return false;
        }

        var check = NormalizeForCheck(name);
        foreach (var ng in NgWords)
        {
            if (check.Contains(NormalizeForCheck(ng), StringComparison.Ordinal))
            {
                error = "その名前は使えません。別の名前にしてください。";
                return false;
            }
        }
        return true;
    }

    /// <summary>NG ワードと比べるための形: 全角英数を半角に、ひらがなをカタカナに、小文字に、空白と記号を取り除く。</summary>
    static string NormalizeForCheck(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            char c = c0;
            if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);      // 全角英数・記号 → 半角
            if (c >= 'ぁ' && c <= 'ゖ') c = (char)(c + 0x60);        // ひらがな → カタカナ
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || c == 'ー' || c == '・') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
