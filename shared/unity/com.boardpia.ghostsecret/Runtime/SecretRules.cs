using System;
using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostSecret
{
    /// <summary>
    /// ゴースト・シークレットのルール。
    /// 手番の人は山札から 1 枚引いて手札を 2 枚にし、1 枚を出して効果を行う(ウラナイとアカリは、見たあとにもう 1 つ決める)。
    /// 残りが 1 人になるか、山札が尽きたらラウンドの終わり。勝者は秘密のかけらを 1 個もらい、先に決まった数を集めた人の勝ち。
    /// 次のラウンドの準備は、ラウンドが終わった手の中でそのまま行う(種 + ラウンドの番号でシャッフルするので、作り直しても同じ)。
    /// </summary>
    public sealed class SecretRules : IGameRules<SecretState, SecretMove>
    {
        public IReadOnlyList<SecretMove> GetLegalMoves(SecretState s)
        {
            var moves = new List<SecretMove>();
            if (s.Result.IsOver || s.Phase == SecretPhase.Over) return moves;
            int me = s.CurrentPlayer;
            switch (s.Phase)
            {
                case SecretPhase.Fortune:
                    moves.Add(SecretMove.FortuneOrder(false));
                    if (s.Peek.Count >= 2) moves.Add(SecretMove.FortuneOrder(true));
                    return moves;
                case SecretPhase.Lamp:
                    moves.Add(SecretMove.LampSwap(false));
                    moves.Add(SecretMove.LampSwap(true));
                    return moves;
            }

            var others = Targets(s, me, includeSelf: false);
            foreach (int card in s.Players[me].Hand)
            {
                int v = SecretCards.ValueOf(card);
                switch (v)
                {
                    case Ghost.Curse:
                        break; // 自分からは出せない
                    case Ghost.Whisper:
                        if (others.Count == 0) moves.Add(SecretMove.Play(card));
                        foreach (int t in others)
                            for (int g = 0; g < Ghost.Kinds; g++)
                                if (SecretCards.IsGuessable(g)) moves.Add(SecretMove.Play(card, t, g));
                        break;
                    case Ghost.Compare:
                    case Ghost.Lamp:
                        if (others.Count == 0) moves.Add(SecretMove.Play(card));
                        foreach (int t in others) moves.Add(SecretMove.Play(card, t));
                        break;
                    case Ghost.Blow:
                        foreach (int t in Targets(s, me, includeSelf: true)) moves.Add(SecretMove.Play(card, t));
                        break;
                    default:
                        moves.Add(SecretMove.Play(card));
                        break;
                }
            }
            return moves;
        }

        /// <summary>効果で選べる人(脱落しておらず、カクレで守られていない人。自分は includeSelf のときだけ、守られていても選べる)。</summary>
        public static List<int> Targets(SecretState s, int me, bool includeSelf)
        {
            var list = new List<int>();
            for (int q = 0; q < s.PlayerCount; q++)
            {
                if (q == me)
                {
                    if (includeSelf && !s.Players[q].Out) list.Add(q);
                    continue;
                }
                if (!s.Players[q].Out && !s.Players[q].Protected) list.Add(q);
            }
            return list;
        }

        public bool IsLegal(SecretState s, SecretMove move)
        {
            foreach (var m in GetLegalMoves(s))
                if (m.Equals(move)) return true;
            return false;
        }

        public SecretState ApplyMove(SecretState state, SecretMove move)
        {
            if (!IsLegal(state, move)) throw new ArgumentException("illegal move: " + move);
            var s = state.Clone();
            s.Ply++;
            switch (s.Phase)
            {
                case SecretPhase.Fortune:
                    ApplyFortune(s, move);
                    break;
                case SecretPhase.Lamp:
                    ApplyLamp(s, move);
                    break;
                default:
                    ApplyPlay(s, move);
                    break;
            }
            RefreshKnowledge(s);
            return s;
        }

        public GameResult GetResult(SecretState state) => state.Result;

        // ---------- 手番 ----------

        static void ApplyPlay(SecretState s, SecretMove m)
        {
            int p = s.CurrentPlayer;
            var me = s.Players[p];
            me.Hand.Remove(m.Card);
            me.Played.Add(m.Card);
            int v = SecretCards.ValueOf(m.Card);
            s.AddEvent(EventKind.Played, p, m.Target, m.Card, guess: m.Guess);

            switch (v)
            {
                case Ghost.Whisper:
                    if (m.Target < 0)
                    {
                        s.AddEvent(EventKind.NoEffect, p);
                        break;
                    }
                    {
                        var target = s.Players[m.Target];
                        if (SecretCards.ValueOf(target.Hand[0]) == m.Guess)
                        {
                            s.AddEvent(EventKind.GuessHit, p, m.Target, guess: m.Guess);
                            Eliminate(s, m.Target, EliminationReason.Guessed);
                        }
                        else
                        {
                            s.AddEvent(EventKind.GuessMiss, p, m.Target, me.Hand[0], guess: m.Guess, viewers: SecretEvent.Only(p, m.Target));
                            target.Known[p] = me.Hand[0];
                        }
                    }
                    break;

                case Ghost.Fortune:
                    {
                        int n = Math.Min(2, s.Deck.Count);
                        if (n == 0)
                        {
                            s.AddEvent(EventKind.NoEffect, p);
                            break;
                        }
                        for (int i = 0; i < n; i++)
                        {
                            s.Peek.Add(s.Deck[s.Deck.Count - 1]);
                            s.Deck.RemoveAt(s.Deck.Count - 1);
                        }
                        s.AddEvent(EventKind.FortuneLook, p, -1, s.Peek[0], n > 1 ? s.Peek[1] : -1, amount: n, viewers: SecretEvent.Only(p));
                        s.Phase = SecretPhase.Fortune;
                        return; // 戻す順番を決めるまで手番は終わらない
                    }

                case Ghost.Rumor:
                    PassHands(s, p);
                    break;

                case Ghost.Compare:
                    if (m.Target < 0)
                    {
                        s.AddEvent(EventKind.NoEffect, p);
                        break;
                    }
                    {
                        var target = s.Players[m.Target];
                        int a = me.Hand[0], b = target.Hand[0];
                        s.AddEvent(EventKind.Compared, p, m.Target, a, b, viewers: SecretEvent.Only(p, m.Target));
                        me.Known[m.Target] = b;
                        target.Known[p] = a;
                        int va = SecretCards.ValueOf(a), vb = SecretCards.ValueOf(b);
                        if (va < vb) Eliminate(s, p, EliminationReason.Compared);
                        else if (vb < va) Eliminate(s, m.Target, EliminationReason.Compared);
                    }
                    break;

                case Ghost.Hide:
                    me.Protected = true;
                    s.AddEvent(EventKind.Hidden, p);
                    break;

                case Ghost.Blow:
                    {
                        var target = s.Players[m.Target];
                        int discarded = target.Hand[0];
                        target.Hand.Clear();
                        target.Played.Add(discarded);
                        s.AddEvent(EventKind.Blown, p, m.Target, discarded);
                        int dv = SecretCards.ValueOf(discarded);
                        if (dv == Ghost.Secret)
                        {
                            Eliminate(s, m.Target, EliminationReason.SecretLost, discarded);
                        }
                        else
                        {
                            if (dv == Ghost.Curse) Eliminate(s, p, EliminationReason.Cursed, m.Target == p ? discarded : -1);
                            if (!target.Out) Draw(s, m.Target);
                        }
                    }
                    break;

                case Ghost.Lamp:
                    if (m.Target < 0)
                    {
                        s.AddEvent(EventKind.NoEffect, p);
                        break;
                    }
                    {
                        int seen = s.Players[m.Target].Hand[0];
                        s.AddEvent(EventKind.LampLook, p, m.Target, seen, viewers: SecretEvent.Only(p));
                        me.Known[m.Target] = seen;
                        s.PendingTarget = m.Target;
                        s.Phase = SecretPhase.Lamp;
                        return; // 交換するかを決めるまで手番は終わらない
                    }

                case Ghost.Secret:
                    Eliminate(s, p, EliminationReason.SecretLost, m.Card);
                    break;
            }
            EndTurn(s);
        }

        static void ApplyFortune(SecretState s, SecretMove m)
        {
            int p = s.CurrentPlayer;
            var order = new List<int>(s.Peek);
            bool swap = m.Choice == 1 && order.Count == 2;
            if (swap) order.Reverse();
            // order[0] がいちばん上(山札は末尾が上)
            for (int i = order.Count - 1; i >= 0; i--) s.Deck.Add(order[i]);
            s.Peek.Clear();
            s.AddEvent(EventKind.FortuneSet, p, amount: swap ? 1 : 0);
            s.Phase = SecretPhase.Play;
            EndTurn(s);
        }

        static void ApplyLamp(SecretState s, SecretMove m)
        {
            int p = s.CurrentPlayer;
            int t = s.PendingTarget;
            var me = s.Players[p];
            var target = s.Players[t];
            bool swap = m.Choice == 1;
            if (swap)
            {
                int mine = me.Hand[0], theirs = target.Hand[0];
                me.Hand[0] = theirs;
                target.Hand[0] = mine;
                me.Known[t] = mine;
                target.Known[p] = theirs;
            }
            s.AddEvent(EventKind.LampSwap, p, t, amount: swap ? 1 : 0);
            s.PendingTarget = -1;
            s.Phase = SecretPhase.Play;
            EndTurn(s);
        }

        /// <summary>ウワサ: 残っていてカクレでない人が、手札を次の人(その中で番号 +1 の方向)に同時に渡す。</summary>
        static void PassHands(SecretState s, int p)
        {
            var seats = new List<int>();
            for (int i = 0; i < s.PlayerCount; i++)
            {
                int q = (p + i) % s.PlayerCount;
                if (!s.Players[q].Out && !s.Players[q].Protected && s.Players[q].Hand.Count > 0) seats.Add(q);
            }
            if (seats.Count < 2)
            {
                s.AddEvent(EventKind.NoEffect, p);
                return;
            }
            var cards = new int[seats.Count];
            for (int i = 0; i < seats.Count; i++) cards[i] = s.Players[seats[i]].Hand[0];
            for (int i = 0; i < seats.Count; i++)
            {
                int giver = seats[i], receiver = seats[(i + 1) % seats.Count];
                s.Players[receiver].Hand[0] = cards[i];
                s.Players[giver].Known[receiver] = cards[i];
                s.AddEvent(EventKind.Passed, giver, receiver, cards[i], viewers: SecretEvent.Only(giver, receiver));
            }
        }

        static void Draw(SecretState s, int q)
        {
            int card;
            if (s.Deck.Count > 0)
            {
                card = s.Deck[s.Deck.Count - 1];
                s.Deck.RemoveAt(s.Deck.Count - 1);
            }
            else if (s.SetAside >= 0)
            {
                card = s.SetAside;
                s.SetAside = SecretState.SetAsideTaken;
            }
            else
            {
                return; // 引けるカードが無い(起きないはずの場合)
            }
            s.Players[q].Hand.Add(card);
            s.AddEvent(EventKind.Drew, q, card: card, viewers: SecretEvent.Only(q));
        }

        /// <summary>q を脱落させる。手札は公開して自分の前に置く。eventCard を渡すと、出来事にはそのカードを出す(手札がもう無いとき)。</summary>
        static void Eliminate(SecretState s, int q, int reason, int eventCard = -1)
        {
            var pl = s.Players[q];
            if (pl.Out) return;
            int shown = pl.Hand.Count > 0 ? pl.Hand[0] : eventCard;
            foreach (int c in pl.Hand) pl.Played.Add(c);
            pl.Hand.Clear();
            pl.Out = true;
            pl.Protected = false;
            s.AddEvent(EventKind.Eliminated, q, card: shown, amount: reason);
        }

        // ---------- 手番・ラウンドの終わり ----------

        static void EndTurn(SecretState s)
        {
            if (s.ActiveCount <= 1)
            {
                var winners = new List<int>();
                for (int q = 0; q < s.PlayerCount; q++)
                    if (!s.Players[q].Out) winners.Add(q);
                EndRound(s, winners);
                return;
            }
            if (s.Deck.Count == 0)
            {
                Showdown(s);
                return;
            }
            StartTurn(s, NextActive(s, s.CurrentPlayer));
        }

        static int NextActive(SecretState s, int from)
        {
            for (int i = 1; i <= s.PlayerCount; i++)
            {
                int q = (from + i) % s.PlayerCount;
                if (!s.Players[q].Out) return q;
            }
            return from;
        }

        static void StartTurn(SecretState s, int q)
        {
            s.CurrentPlayer = q;
            s.Players[q].Protected = false;
            s.Phase = SecretPhase.Play;
            Draw(s, q);
        }

        static void Showdown(SecretState s)
        {
            int bestValue = -1, bestSum = -1;
            var winners = new List<int>();
            for (int q = 0; q < s.PlayerCount; q++)
            {
                var pl = s.Players[q];
                if (pl.Out) continue;
                int card = pl.Hand[0];
                int value = SecretCards.ValueOf(card), sum = pl.PlayedSum;
                s.AddEvent(EventKind.Showdown, q, card: card, amount: sum);
                if (value > bestValue || (value == bestValue && sum > bestSum))
                {
                    bestValue = value;
                    bestSum = sum;
                    winners.Clear();
                    winners.Add(q);
                }
                else if (value == bestValue && sum == bestSum)
                {
                    winners.Add(q);
                }
            }
            EndRound(s, winners);
        }

        static void EndRound(SecretState s, List<int> winners)
        {
            foreach (int w in winners)
            {
                s.Players[w].Tokens++;
                s.AddEvent(EventKind.RoundWin, w, amount: s.Players[w].Tokens);
            }
            int need = SecretLogic.TokensToWin(s.PlayerCount);
            var reached = new List<int>();
            for (int q = 0; q < s.PlayerCount; q++)
                if (s.Players[q].Tokens >= need) reached.Add(q);
            if (reached.Count > 0)
            {
                Finish(s, reached);
                return;
            }
            if (s.Round >= SecretLogic.MaxRounds)
            {
                int most = 0;
                foreach (var pl in s.Players) most = Math.Max(most, pl.Tokens);
                var top = new List<int>();
                for (int q = 0; q < s.PlayerCount; q++)
                    if (s.Players[q].Tokens == most) top.Add(q);
                Finish(s, top);
                return;
            }
            // 次のラウンドは、前のラウンドの最初の手番から近い勝者が始める
            int starter = winners[0], best = int.MaxValue;
            foreach (int w in winners)
            {
                int d = (w - s.RoundStarter + s.PlayerCount) % s.PlayerCount;
                if (d < best)
                {
                    best = d;
                    starter = w;
                }
            }
            StartRound(s, starter);
        }

        static void Finish(SecretState s, List<int> top)
        {
            s.Result = top.Count == 1 ? GameResult.WinFor(top[0]) : GameResult.Draw;
            s.Phase = SecretPhase.Over;
            s.AddEvent(EventKind.GameEnd, top.Count == 1 ? top[0] : -1);
        }

        /// <summary>新しいラウンドを準備して、starter の手番を始める(山札は種とラウンドの番号から決まる)。</summary>
        internal static void StartRound(SecretState s, int starter)
        {
            s.Round++;
            var ids = new List<int>();
            for (int c = 0; c < SecretCards.Count; c++) ids.Add(c);
            Shuffle(ids, new Random(unchecked(s.Seed * 1000003 + s.Round * 7919 + 13)));

            foreach (var pl in s.Players)
            {
                pl.Hand.Clear();
                pl.Played.Clear();
                pl.Out = false;
                pl.Protected = false;
                for (int i = 0; i < pl.Known.Length; i++) pl.Known[i] = -1;
            }
            s.Deck.Clear();
            s.Deck.AddRange(ids);
            s.FaceUpAside.Clear();
            s.Peek.Clear();
            s.PendingTarget = -1;

            s.SetAside = s.Deck[s.Deck.Count - 1];
            s.Deck.RemoveAt(s.Deck.Count - 1);
            if (s.PlayerCount == 2)
                for (int i = 0; i < SecretLogic.FaceUpAsideForTwo; i++)
                {
                    s.FaceUpAside.Add(s.Deck[s.Deck.Count - 1]);
                    s.Deck.RemoveAt(s.Deck.Count - 1);
                }

            s.RoundStarter = starter;
            s.AddEvent(EventKind.RoundStart, starter, amount: s.Round);
            for (int i = 0; i < s.PlayerCount; i++) Draw(s, (starter + i) % s.PlayerCount);
            StartTurn(s, starter);
        }

        /// <summary>知っている手札のうち、もう持っていないものを忘れる。</summary>
        static void RefreshKnowledge(SecretState s)
        {
            for (int p = 0; p < s.PlayerCount; p++)
            {
                var known = s.Players[p].Known;
                for (int q = 0; q < s.PlayerCount; q++)
                {
                    if (known[q] < 0) continue;
                    if (q == p || s.Players[q].Out || !s.Players[q].Hand.Contains(known[q])) known[q] = -1;
                }
            }
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>かけらの多い順の席(同数は番号順)。</summary>
        public static List<int> Ranking(SecretState s)
        {
            var order = new List<int>();
            for (int q = 0; q < s.PlayerCount; q++) order.Add(q);
            order.Sort((a, b) => s.Players[a].Tokens != s.Players[b].Tokens ? s.Players[b].Tokens.CompareTo(s.Players[a].Tokens) : a.CompareTo(b));
            return order;
        }
    }

    /// <summary>対局の始まりの局面を作る。</summary>
    public static class SecretSetup
    {
        public static SecretState CreateRandom(Random rng, int playerCount)
        {
            if (!SecretLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            var s = new SecretState(playerCount) { Seed = rng.Next() };
            SecretRules.StartRound(s, rng.Next(playerCount));
            return s;
        }
    }
}
