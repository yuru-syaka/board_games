using System;
using System.Linq;
using BoardGames.Core;
using NUnit.Framework;

namespace BoardGames.Geister.Tests
{
    public class GeisterCpuTests
    {
        static readonly GhostType G = GhostType.Good;
        static readonly GhostType E = GhostType.Evil;

        readonly GeisterRules _rules = new GeisterRules();

        static Ghost On(int id, int owner, GhostType type, int x, int y) =>
            new Ghost(id, owner, type, x, y, GhostStatus.OnBoard);

        static Ghost Captured(int id, int owner, GhostType type) =>
            new Ghost(id, owner, type, -1, -1, GhostStatus.Captured);

        [Test]
        public void Cpu_AlwaysReturnsLegalMove()
        {
            var rng = new Random(5);
            var cpu = new GeisterCpu(new Random(9));
            for (int game = 0; game < 10; game++)
            {
                var s = GeisterSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    GeisterMove move;
                    if (s.CurrentPlayer == 1)
                    {
                        move = cpu.ChooseMove(GeisterPlayerView.Create(s, 1));
                        Assert.IsTrue(_rules.IsLegal(s, move), $"illegal CPU move {move}");
                    }
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
            }
        }

        [Test]
        public void Cpu_EscapesWhenPossible()
        {
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 3, 3),
                On(8, 1, G, 0, 0), On(9, 1, E, 4, 4),
            }, 1);
            var move = new GeisterCpu(new Random(1)).ChooseMove(GeisterPlayerView.Create(s, 1));
            Assert.IsTrue(move.IsEscape);
        }

        [Test]
        public void Cpu_CapturesOpponentSittingOnItsCorner()
        {
            // P0 の駒が P1 の角 (5,5) に居座っている。放置すると善玉なら次の手で脱出される。
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 5, 5), On(1, 0, E, 1, 1), On(2, 0, G, 2, 0),
                On(8, 1, E, 5, 4), On(9, 1, G, 2, 4), On(10, 1, G, 1, 3),
            }, 1);
            var move = new GeisterCpu(new Random(1)).ChooseMove(GeisterPlayerView.Create(s, 1));
            Assert.AreEqual(5, move.ToX);
            Assert.AreEqual(5, move.ToY);
        }

        [Test]
        public void Cpu_HandlesEndgameWithOneOfEachLeft()
        {
            // P0 は善玉・悪玉とも 3 体ずつ取られ済み。どちらの駒を取っても即決着する局面でも合法手を返す。
            var s = GeisterSetup.CreateCustom(new[]
            {
                On(0, 0, G, 2, 2), On(1, 0, E, 4, 1),
                Captured(2, 0, G), Captured(3, 0, G), Captured(4, 0, G),
                Captured(5, 0, E), Captured(6, 0, E), Captured(7, 0, E),
                On(8, 1, G, 2, 3), On(9, 1, E, 4, 4), On(10, 1, G, 0, 5),
            }, 1);
            var move = new GeisterCpu(new Random(2)).ChooseMove(GeisterPlayerView.Create(s, 1));
            Assert.IsTrue(_rules.IsLegal(s, move));
        }

        [Test]
        public void Cpu_DoesNotSeeHiddenTypes()
        {
            // 同じ盤面で人間側の種類だけ入れ替えても、CPU に渡る View は同一になる。
            var a = GeisterSetup.Create(new[] { G, G, G, G, E, E, E, E }, new[] { G, E, G, E, G, E, G, E }, 1);
            var b = GeisterSetup.Create(new[] { E, E, E, E, G, G, G, G }, new[] { G, E, G, E, G, E, G, E }, 1);
            var va = GeisterPlayerView.Create(a, 1);
            var vb = GeisterPlayerView.Create(b, 1);
            CollectionAssert.AreEqual(va.Ghosts.ToArray(), vb.Ghosts.ToArray());

            var ma = new GeisterCpu(new Random(4)).ChooseMove(va);
            var mb = new GeisterCpu(new Random(4)).ChooseMove(vb);
            Assert.AreEqual(ma, mb);
        }

        [Test]
        public void Cpu_BeatsRandomPlayerMostOfTheTime()
        {
            var rng = new Random(11);
            int cpuWins = 0, games = 40;
            for (int game = 0; game < games; game++)
            {
                var cpu = new GeisterCpu(new Random(game));
                var s = GeisterSetup.CreateRandom(rng);
                while (!s.Result.IsOver)
                {
                    GeisterMove move;
                    if (s.CurrentPlayer == 1) move = cpu.ChooseMove(GeisterPlayerView.Create(s, 1));
                    else
                    {
                        var moves = _rules.GetLegalMoves(s);
                        move = moves[rng.Next(moves.Count)];
                    }
                    s = _rules.ApplyMove(s, move);
                }
                if (s.Result.Status == GameStatus.Win && s.Result.Winner == 1) cpuWins++;
            }
            Assert.GreaterOrEqual(cpuWins, games * 3 / 4, $"CPU won only {cpuWins}/{games}");
        }
    }
}
