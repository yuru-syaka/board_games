using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostDerby
{
    /// <summary>
    /// ゴースト・ダービーのルール(docs/games/ghostderby.md)。
    /// 賭け・いたずら・追い賭けは、席の順に 1 人ずつ伏せて決め、最後の人で一斉に公開する。
    /// レースはカードをめくるだけで進むので、最後の人の手の中で、次に決めることができるまでまとめて進める。
    /// </summary>
    public sealed class DerbyRules : IGameRules<DerbyState, DerbyMove>
    {
        public IReadOnlyList<DerbyMove> GetLegalMoves(DerbyState s)
        {
            var moves = new List<DerbyMove>();
            if (s.Result.IsOver || s.CurrentPlayer < 0) return moves;
            var p = s.Players[s.CurrentPlayer];
            switch (s.Phase)
            {
                case DerbyPhase.Bet:
                    for (int r = 0; r < DerbyLogic.Runners; r++)
                        for (int a = 1; a <= Math.Min(DerbyLogic.MaxBet, p.Coins); a++) moves.Add(DerbyMove.Bet(r, a));
                    break;
                case DerbyPhase.Trick:
                    foreach (int c in p.Hand)
                    {
                        if (!DerbyCards.IsTrick(c)) continue;
                        if (!DerbyCards.NeedsTarget(DerbyCards.TrickOf(c))) moves.Add(DerbyMove.Trick(c, -1));
                        else for (int r = 0; r < DerbyLogic.Runners; r++) moves.Add(DerbyMove.Trick(c, r));
                    }
                    break;
                case DerbyPhase.Chase:
                    moves.Add(DerbyMove.Pass);
                    for (int r = 0; r < DerbyLogic.Runners; r++)
                    {
                        if (s.IsFinished(r)) continue;
                        for (int a = 1; a <= Math.Min(DerbyLogic.MaxChase, p.Coins); a++) moves.Add(DerbyMove.Chase(r, a));
                    }
                    break;
            }
            return moves;
        }

        public bool IsLegal(DerbyState s, DerbyMove m)
        {
            if (s.Result.IsOver || s.CurrentPlayer < 0) return false;
            var p = s.Players[s.CurrentPlayer];
            switch (m.Kind)
            {
                case MoveKind.Bet:
                    return s.Phase == DerbyPhase.Bet && DerbyLogic.IsRunner(m.A) && m.B >= 1 && m.B <= Math.Min(DerbyLogic.MaxBet, p.Coins);
                case MoveKind.Trick:
                    if (s.Phase != DerbyPhase.Trick || !DerbyCards.IsTrick(m.A) || !p.Hand.Contains(m.A)) return false;
                    return DerbyCards.NeedsTarget(DerbyCards.TrickOf(m.A)) ? DerbyLogic.IsRunner(m.B) : m.B == -1;
                case MoveKind.Chase:
                    return s.Phase == DerbyPhase.Chase && DerbyLogic.IsRunner(m.A) && !s.IsFinished(m.A) &&
                           m.B >= 1 && m.B <= Math.Min(DerbyLogic.MaxChase, p.Coins);
                case MoveKind.Pass:
                    return s.Phase == DerbyPhase.Chase && m.A == -1 && m.B == 0;
                default:
                    return false;
            }
        }

        public GameResult GetResult(DerbyState s) => s.Result;

        public DerbyState ApplyMove(DerbyState state, DerbyMove m)
        {
            if (!IsLegal(state, m)) throw new ArgumentException($"Illegal move: {m}");
            var s = state.Clone();
            int seat = s.CurrentPlayer;
            var p = s.Players[seat];
            switch (m.Kind)
            {
                case MoveKind.Bet:
                    p.Coins -= m.B;
                    p.BetRunner = m.A;
                    p.BetAmount = m.B;
                    break;
                case MoveKind.Trick:
                    p.Hand.Remove(m.A);
                    s.Deck.Add(m.A);
                    s.TrickTarget[m.A - DerbyLogic.FirstTrick] = m.B;
                    s.TrickOwner[m.A - DerbyLogic.FirstTrick] = seat;
                    break;
                case MoveKind.Chase:
                    p.Coins -= m.B;
                    p.ChaseRunner = m.A;
                    p.ChaseAmount = m.B;
                    p.ChaseOdds = DerbyCards.ChaseOdds(s.RankOf(m.A));
                    break;
            }
            p.Decided = true;
            s.AddEvent(EventKind.Decided, seat);
            s.Ply++;

            s.CurrentPlayer = NextToDecide(s);
            if (s.CurrentPlayer < 0) Resolve(s);
            return s;
        }

        // ---------- 対局の始まり ----------

        public static void Begin(DerbyState s)
        {
            for (int i = 0; i < DerbyLogic.TrickCount; i++) s.DrawPile.Add(DerbyLogic.FirstTrick + i);
            Shuffle(s.DrawPile, new Random(unchecked(s.Seed * 31 + 7)));
            foreach (var p in s.Players)
                for (int i = 0; i < DerbyLogic.HandSize; i++) Draw(s, p);
            StartRace(s, 1);
        }

        static void Draw(DerbyState s, DerbyPlayer p)
        {
            int top = s.DrawPile.Count - 1;
            p.Hand.Add(s.DrawPile[top]);
            s.DrawPile.RemoveAt(top);
        }

        static void StartRace(DerbyState s, int race)
        {
            s.Race = race;
            s.Phase = DerbyPhase.Bet;
            s.ChaseDone = false;
            s.Rebuilds = 0;
            s.Finish.Clear();
            s.Deck.Clear();
            s.Seen.Clear();
            Array.Clear(s.Position, 0, s.Position.Length);
            Array.Clear(s.Frozen, 0, s.Frozen.Length);
            for (int i = 0; i < DerbyLogic.TrickCount; i++) s.TrickTarget[i] = s.TrickOwner[i] = -1;

            var odds = DerbyCards.OddsTable.ToList();
            Shuffle(odds, new Random(unchecked(s.Seed * 1000003 + race * 6151 + 29)));
            odds.CopyTo(s.Odds);

            s.AddEvent(EventKind.RaceStart, amount: race);
            for (int i = 0; i < s.PlayerCount; i++)
            {
                var p = s.Players[i];
                p.BetRunner = p.ChaseRunner = -1;
                p.BetAmount = p.ChaseAmount = p.ChaseOdds = 0;
                p.Decided = false;
                if (p.Hand.Count < DerbyLogic.HandSize) Draw(s, p);
                if (p.Coins < DerbyLogic.RescueCoins)
                {
                    s.AddEvent(EventKind.Rescued, i, amount: DerbyLogic.RescueCoins - p.Coins);
                    p.Coins = DerbyLogic.RescueCoins;
                }
            }
            s.CurrentPlayer = NextToDecide(s);
        }

        // ---------- 決定の公開 ----------

        static int NextToDecide(DerbyState s)
        {
            for (int i = 0; i < s.PlayerCount; i++)
                if (!s.Players[i].Decided) return i;
            return -1;
        }

        static void ClearDecided(DerbyState s)
        {
            foreach (var p in s.Players) p.Decided = false;
        }

        static void Resolve(DerbyState s)
        {
            switch (s.Phase)
            {
                case DerbyPhase.Bet:
                    for (int i = 0; i < s.PlayerCount; i++)
                        s.AddEvent(EventKind.BetShown, i, s.Players[i].BetRunner, s.Players[i].BetAmount);
                    ClearDecided(s);
                    s.Phase = DerbyPhase.Trick;
                    s.CurrentPlayer = NextToDecide(s);
                    break;
                case DerbyPhase.Trick:
                    ClearDecided(s);
                    for (int r = 0; r < DerbyLogic.Runners; r++)
                        for (int k = 0; k < DerbyLogic.RunCardsPerRunner; k++) s.Deck.Add(DerbyCards.RunCard(r, k));
                    Shuffle(s.Deck, DeckRng(s));
                    s.AddEvent(EventKind.RunStart, amount: s.Deck.Count);
                    Run(s);
                    break;
                case DerbyPhase.Chase:
                    for (int i = 0; i < s.PlayerCount; i++)
                    {
                        var p = s.Players[i];
                        s.AddEvent(EventKind.ChaseShown, i, p.ChaseRunner, p.ChaseAmount, p.ChaseOdds);
                    }
                    ClearDecided(s);
                    s.ChaseDone = true;
                    Run(s);
                    break;
            }
        }

        // ---------- レース ----------

        static Random DeckRng(DerbyState s) => new Random(unchecked(s.Seed * 1000003 + s.Race * 7919 + s.Rebuilds * 104729 + 13));

        static void Run(DerbyState s)
        {
            while (s.Finish.Count < DerbyLogic.Finishers)
            {
                if (s.Deck.Count == 0) Rebuild(s);
                int top = s.Deck.Count - 1;
                int card = s.Deck[top];
                s.Deck.RemoveAt(top);
                if (DerbyCards.IsRun(card)) FlipRun(s, card);
                else FlipTrick(s, card);

                if (s.Finish.Count < DerbyLogic.Finishers && !s.ChaseDone && s.Position.Max() > DerbyLogic.ChaseLine && TryStartChase(s))
                    return;
            }
            EndRace(s);
        }

        static bool TryStartChase(DerbyState s)
        {
            if (s.Players.All(p => p.Coins <= 0))
            {
                s.ChaseDone = true;
                return false;
            }
            s.Phase = DerbyPhase.Chase;
            foreach (var p in s.Players) p.Decided = p.Coins <= 0; // コインがない人はパス扱い
            s.CurrentPlayer = NextToDecide(s);
            s.AddEvent(EventKind.ChaseStart);
            return true;
        }

        static void Rebuild(DerbyState s)
        {
            s.Rebuilds++;
            s.Seen.Clear();
            for (int r = 0; r < DerbyLogic.Runners; r++)
            {
                if (s.IsFinished(r)) continue;
                for (int k = 0; k < DerbyLogic.RunCardsPerRunner; k++) s.Deck.Add(DerbyCards.RunCard(r, k));
            }
            Shuffle(s.Deck, DeckRng(s));
            s.AddEvent(EventKind.Reshuffled, amount: s.Deck.Count);
        }

        static void FlipRun(DerbyState s, int card)
        {
            s.Seen.Add(card);
            int r = DerbyCards.RunnerOf(card);
            if (s.IsFinished(r)) return;
            int steps = DerbyCards.Steps(s.Odds[r], DerbyCards.RunIndex(card));
            s.AddEvent(EventKind.RunFlip, card: card, amount: steps);
            if (s.Frozen[r] > 0)
            {
                s.Frozen[r]--;
                s.AddEvent(EventKind.Skipped, card: r);
                return;
            }
            MoveTo(s, r, s.Position[r] + steps);
        }

        static void FlipTrick(DerbyState s, int card)
        {
            int i = card - DerbyLogic.FirstTrick;
            int target = s.TrickTarget[i];
            s.AddEvent(EventKind.TrickFlip, s.TrickOwner[i], card, target);
            bool active = target >= 0 && !s.IsFinished(target);
            switch (DerbyCards.TrickOf(card))
            {
                case TrickKind.Dash:
                    if (active) MoveTo(s, target, s.Position[target] + 3);
                    break;
                case TrickKind.Stumble:
                    if (active) MoveTo(s, target, Math.Max(0, s.Position[target] - 2));
                    break;
                case TrickKind.Swap:
                    if (active) SwapAhead(s, target);
                    break;
                case TrickKind.Freeze:
                    if (active)
                    {
                        s.Frozen[target]++;
                        s.AddEvent(EventKind.Frozen, card: target);
                    }
                    break;
                case TrickKind.Tailwind:
                    Tailwind(s);
                    break;
            }
        }

        static void SwapAhead(DerbyState s, int runner)
        {
            int ahead = -1;
            for (int r = 0; r < DerbyLogic.Runners; r++)
            {
                if (s.IsFinished(r) || s.Position[r] <= s.Position[runner]) continue;
                if (ahead < 0 || s.Position[r] < s.Position[ahead]) ahead = r;
            }
            if (ahead < 0) return;
            int mine = s.Position[runner], theirs = s.Position[ahead];
            MoveTo(s, runner, theirs);
            MoveTo(s, ahead, mine);
        }

        static void Tailwind(DerbyState s)
        {
            int last = int.MaxValue;
            for (int r = 0; r < DerbyLogic.Runners; r++)
                if (!s.IsFinished(r)) last = Math.Min(last, s.Position[r]);
            for (int r = 0; r < DerbyLogic.Runners; r++)
                if (!s.IsFinished(r) && s.Position[r] == last) MoveTo(s, r, last + 2);
        }

        static void MoveTo(DerbyState s, int r, int to)
        {
            if (s.Finish.Count >= DerbyLogic.Finishers) return;
            to = Math.Min(to, DerbyLogic.Goal);
            int from = s.Position[r];
            s.Position[r] = to;
            if (from != to) s.AddEvent(EventKind.Moved, card: r, amount: from, amount2: to);
            if (to >= DerbyLogic.Goal)
            {
                s.Finish.Add(r);
                s.AddEvent(EventKind.Finished, card: r, amount: s.Finish.Count);
            }
        }

        static void EndRace(DerbyState s)
        {
            int first = s.Finish[0], second = s.Finish[1];
            for (int i = 0; i < s.PlayerCount; i++)
            {
                var p = s.Players[i];
                int pay = Payout(p.BetRunner, p.BetAmount, s.Odds[p.BetRunner], first, second) +
                          Payout(p.ChaseRunner, p.ChaseAmount, p.ChaseOdds, first, second);
                if (p.BetRunner == first) p.Hits++;
                if (p.ChaseRunner == first) p.Hits++;
                p.Coins += pay;
                s.AddEvent(EventKind.Payout, i, amount: pay, amount2: pay - p.BetAmount - p.ChaseAmount);
            }
            s.AddEvent(EventKind.RaceEnd, amount: s.Race);
            if (s.Race >= DerbyLogic.Races) Finish(s);
            else StartRace(s, s.Race + 1);
        }

        /// <summary>1 着は賭け金 × 配当、2 着は賭け金を返す、3 着以下は没収。</summary>
        public static int Payout(int runner, int amount, int odds, int first, int second)
        {
            if (runner < 0 || amount <= 0) return 0;
            if (runner == first) return amount * odds;
            return runner == second ? amount : 0;
        }

        static void Finish(DerbyState s)
        {
            s.Phase = DerbyPhase.Over;
            s.CurrentPlayer = -1;
            var ranks = Ranking(s);
            var top = Enumerable.Range(0, s.PlayerCount).Where(i => ranks[i] == 0).ToList();
            s.Result = top.Count == 1 ? GameResult.WinFor(top[0]) : GameResult.Draw;
            s.AddEvent(EventKind.GameEnd, s.Result.Winner);
        }

        /// <summary>席ごとの順位(0 = 1 位)。コインの多い順、同じなら 1 着を当てた回数の多い順。それも同じなら同じ順位。</summary>
        public static int[] Ranking(DerbyState s)
        {
            var ranks = new int[s.PlayerCount];
            for (int i = 0; i < s.PlayerCount; i++)
            {
                var me = s.Players[i];
                ranks[i] = s.Players.Count(o => o.Coins > me.Coins || (o.Coins == me.Coins && o.Hits > me.Hits));
            }
            return ranks;
        }

        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    public static class DerbySetup
    {
        public static DerbyState Create(int playerCount, int seed)
        {
            if (!DerbyLogic.IsValidPlayerCount(playerCount)) throw new ArgumentOutOfRangeException(nameof(playerCount));
            var s = new DerbyState(playerCount) { Seed = seed };
            DerbyRules.Begin(s);
            return s;
        }

        public static DerbyState CreateRandom(Random rng, int playerCount) => Create(playerCount, (rng ?? new Random()).Next());
    }
}
