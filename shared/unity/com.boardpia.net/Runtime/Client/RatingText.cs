namespace BoardGames.Net
{
    /// <summary>
    /// 対局の始まり・終わりにお知らせの帯へ出す文(ステップ 5-3)。Unity に依存しないので .NET でテストする。
    /// </summary>
    public static class RatingText
    {
        /// <summary>
        /// 対局の始まりの文(対戦相手の名前とレート、レート戦かどうか)。両方ともログインしていなければ null(何も出さない)。
        /// </summary>
        public static string MatchStart(string[] names, int[] ratings, int localSeat, bool roomCodeMatch)
        {
            if (names == null || ratings == null || names.Length != 2 || ratings.Length != 2 || (localSeat != 0 && localSeat != 1))
                return null;
            int other = 1 - localSeat;
            bool meIn = ratings[localSeat] >= 0;
            bool otherIn = ratings[other] >= 0;
            if (!meIn && !otherIn) return null;

            string name = !string.IsNullOrEmpty(names[other]) ? names[other] : otherIn ? "プレイヤー" : "ゲスト";
            string line = "対戦相手: " + name + (otherIn ? "（" + ratings[other] + "）" : "");
            if (!meIn) return line;
            if (roomCodeMatch) return line + "\nルームコードの対戦は、レートが動きません";
            if (!otherIn) return line + "\n相手がログインしていないため、レートは動きません";
            return line + "\nレート戦です（あなた " + ratings[localSeat] + "）";
        }

        /// <summary>終局後の文(新しいレートと増減)。</summary>
        public static string Rated(int rating, int delta)
        {
            string sign = delta > 0 ? "+" + delta : delta < 0 ? "-" + (-delta) : "±0";
            return "レート " + rating + "（" + sign + "）";
        }
    }
}
