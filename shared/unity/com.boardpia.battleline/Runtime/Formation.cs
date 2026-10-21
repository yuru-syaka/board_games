using System;
using System.Collections.Generic;

namespace BoardGames.BattleLine
{
    /// <summary>隊列の役(数値が大きいほど強い)。</summary>
    public enum FormationType
    {
        Host,
        Skirmish,
        Battalion,
        Phalanx,
        Wedge,
    }

    /// <summary>隊列の強さ。霧のフラッグでは役を無視して合計だけで比べる。</summary>
    public readonly struct FormationRank : IComparable<FormationRank>
    {
        public readonly FormationType Type;
        public readonly int Sum;
        public readonly bool Fog;

        public FormationRank(FormationType type, int sum, bool fog)
        {
            Type = type;
            Sum = sum;
            Fog = fog;
        }

        /// <summary>比較用の値(役 × 100 + 合計。霧なら合計だけ)。</summary>
        public int Score => Fog ? Sum : (int)Type * 100 + Sum;

        public int CompareTo(FormationRank other) => Score.CompareTo(other.Score);

        public override string ToString() => Fog ? $"Fog({Sum})" : $"{Type}({Sum})";
    }

    /// <summary>
    /// 役の判定。士気戦術(リーダー・騎兵・盾兵)は最も強くなるように色と数字を選ぶ。
    /// TryBest は「足りない枚数を pool(使ってよい部隊カード)から補ったときに作れる最強の役」を求める
    /// (確保の証明と CPU の見込み計算で使う)。
    /// </summary>
    public static class Formation
    {
        /// <summary>そろった隊列の強さ。cards は部隊カードと士気戦術。</summary>
        public static FormationRank Evaluate(IReadOnlyList<int> cards, bool fog)
        {
            if (!TryBest(cards, cards.Count, fog, null, out var rank))
                throw new ArgumentException("Formation cannot be evaluated");
            return rank;
        }

        /// <summary>
        /// cards に pool から (required − cards.Count) 枚を足して作れる最強の役。pool が足りなければ false。
        /// pool は部隊カード Id ごとの使用可否(null なら空)。
        /// </summary>
        public static bool TryBest(IReadOnlyList<int> cards, int required, bool fog, bool[] pool, out FormationRank best)
        {
            var colors = new List<int>(4);
            var values = new List<int>(4);
            var wilds = new List<TacticKind>(3);
            foreach (int c in cards)
            {
                if (Card.IsTroop(c))
                {
                    colors.Add(Card.ColorOf(c));
                    values.Add(Card.ValueOf(c));
                }
                else
                {
                    wilds.Add(Card.KindOf(c));
                }
            }
            int k = Math.Max(0, required - cards.Count);
            int n = cards.Count + k;
            best = default;

            int fixedSum = 0;
            foreach (int v in values) fixedSum += v;
            int wildSum = 0;
            foreach (var w in wilds) wildSum += MaxValue(w);

            if (k > 0 && CountPool(pool) < k) return false;

            if (fog)
            {
                best = new FormationRank(FormationType.Host, fixedSum + wildSum + TopValues(pool, -1, k), true);
                return true;
            }

            // 強い役から順に、作れるかどうかを調べる(役が上なら合計に関係なく強い)
            int wedge = -1;
            for (int c = 0; c < Card.Colors; c++)
            {
                if (!AllEqual(colors, c)) continue;
                int sum = BestRun(values, wilds, n, k, pool, c);
                if (sum > wedge) wedge = sum;
            }
            if (wedge >= 0)
            {
                best = new FormationRank(FormationType.Wedge, wedge, false);
                return true;
            }

            for (int v = Card.MaxValue; v >= 1; v--)
            {
                if (!AllEqual(values, v)) continue;
                bool wildOk = true;
                foreach (var w in wilds)
                    if (!CanBe(w, v)) wildOk = false;
                if (!wildOk) continue;
                if (k > 0 && CountValue(pool, v) < k) continue;
                best = new FormationRank(FormationType.Phalanx, v * n, false);
                return true;
            }

            int battalion = -1;
            for (int c = 0; c < Card.Colors; c++)
            {
                if (!AllEqual(colors, c)) continue;
                if (k > 0 && CountColor(pool, c) < k) continue;
                int sum = fixedSum + wildSum + TopValues(pool, c, k);
                if (sum > battalion) battalion = sum;
            }
            if (battalion >= 0)
            {
                best = new FormationRank(FormationType.Battalion, battalion, false);
                return true;
            }

            int skirmish = BestRun(values, wilds, n, k, pool, -1);
            if (skirmish >= 0)
            {
                best = new FormationRank(FormationType.Skirmish, skirmish, false);
                return true;
            }

            best = new FormationRank(FormationType.Host, fixedSum + wildSum + TopValues(pool, -1, k), false);
            return true;
        }

        /// <summary>
        /// 連番(color ≥ 0 なら同色連番)で作れる最大の合計。作れなければ −1。
        /// 足りない数字は士気戦術と pool のカードで埋める。
        /// </summary>
        static int BestRun(List<int> values, List<TacticKind> wilds, int n, int k, bool[] pool, int color)
        {
            for (int start = Card.MaxValue - n + 1; start >= 1; start--)
            {
                int end = start + n - 1;
                var missing = new List<int>(n);
                bool ok = true;
                for (int v = start; v <= end && ok; v++)
                {
                    int count = 0;
                    foreach (int fv in values)
                        if (fv == v) count++;
                    if (count > 1) ok = false;
                    else if (count == 0) missing.Add(v);
                }
                if (!ok) continue;
                foreach (int fv in values)
                    if (fv < start || fv > end) ok = false;
                if (!ok) continue;
                if (missing.Count != wilds.Count + k) continue;

                if (AssignWilds(wilds, 0, missing, new bool[missing.Count], pool, color))
                    return (start + end) * n / 2;
            }
            return -1;
        }

        /// <summary>士気戦術を missing の数字に割り当て、残りの数字を pool で埋められるか。</summary>
        static bool AssignWilds(List<TacticKind> wilds, int index, List<int> missing, bool[] used, bool[] pool, int color)
        {
            if (index == wilds.Count)
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    if (used[i]) continue;
                    if (!PoolHas(pool, missing[i], color)) return false;
                }
                return true;
            }
            for (int i = 0; i < missing.Count; i++)
            {
                if (used[i] || !CanBe(wilds[index], missing[i])) continue;
                used[i] = true;
                bool ok = AssignWilds(wilds, index + 1, missing, used, pool, color);
                used[i] = false;
                if (ok) return true;
            }
            return false;
        }

        static bool PoolHas(bool[] pool, int value, int color)
        {
            if (pool == null) return false;
            if (color >= 0) return pool[Card.Troop(color, value)];
            for (int c = 0; c < Card.Colors; c++)
                if (pool[Card.Troop(c, value)]) return true;
            return false;
        }

        /// <summary>士気戦術がその数字になれるか。</summary>
        static bool CanBe(TacticKind kind, int value)
        {
            switch (kind)
            {
                case TacticKind.Leader: return true;
                case TacticKind.Cavalry: return value == 8;
                case TacticKind.Shield: return value >= 1 && value <= 3;
                default: return false;
            }
        }

        static int MaxValue(TacticKind kind)
        {
            switch (kind)
            {
                case TacticKind.Leader: return 10;
                case TacticKind.Cavalry: return 8;
                case TacticKind.Shield: return 3;
                default: return 0;
            }
        }

        static bool AllEqual(List<int> list, int value)
        {
            foreach (int x in list)
                if (x != value) return false;
            return true;
        }

        static int CountPool(bool[] pool)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int i = 0; i < Card.TroopCount; i++)
                if (pool[i]) n++;
            return n;
        }

        static int CountValue(bool[] pool, int value)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int c = 0; c < Card.Colors; c++)
                if (pool[Card.Troop(c, value)]) n++;
            return n;
        }

        static int CountColor(bool[] pool, int color)
        {
            if (pool == null) return 0;
            int n = 0;
            for (int v = 1; v <= Card.MaxValue; v++)
                if (pool[Card.Troop(color, v)]) n++;
            return n;
        }

        /// <summary>pool から数字の大きい順に count 枚取った合計(color ≥ 0 ならその色だけ)。</summary>
        static int TopValues(bool[] pool, int color, int count)
        {
            if (count <= 0 || pool == null) return 0;
            int sum = 0;
            for (int v = Card.MaxValue; v >= 1 && count > 0; v--)
            {
                for (int c = 0; c < Card.Colors && count > 0; c++)
                {
                    if (color >= 0 && c != color) continue;
                    if (!pool[Card.Troop(c, v)]) continue;
                    sum += v;
                    count--;
                }
            }
            return sum;
        }

        /// <summary>画面表示用の役名。</summary>
        public static string NameOf(FormationRank rank)
        {
            if (rank.Fog) return "合計";
            switch (rank.Type)
            {
                case FormationType.Wedge: return "同色連番";
                case FormationType.Phalanx: return "同数字";
                case FormationType.Battalion: return "同色";
                case FormationType.Skirmish: return "連番";
                default: return "バラ";
            }
        }
    }
}
