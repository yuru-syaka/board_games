using System;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.GhostJewel.Tests
{
    public class JewelCpuTests
    {
        readonly JewelRules _rules = new JewelRules();

        /// <summary>CPU だけで最後まで遊び、合法手しか打たないこと・宝石が増減しないことを確かめる。</summary>
        [Test]
        public void CpuGames_FinishWithLegalMovesOnly([Values(2, 3, 4)] int players)
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var s = JewelSetup.CreateRandom(new Random(seed * 13 + players), players);
                var cpus = Enumerable.Range(0, players).Select(i => new JewelCpu(new Random(seed * 7 + i))).ToArray();
                int total = s.Bank.Sum();
                while (!s.Result.IsOver)
                {
                    var move = cpus[s.CurrentPlayer].ChooseMove(JewelPlayerView.Create(s, s.CurrentPlayer));
                    Assert.IsTrue(_rules.IsLegal(s, move), $"seed {seed}: {move}");
                    s = _rules.ApplyMove(s, move);
                    Assert.AreEqual(total, s.Bank.Sum() + s.Players.Sum(p => p.Gems.Sum()), "宝石の総数");
                    Assert.IsTrue(s.Phase == JewelPhase.Return || s.Players.All(p => p.GemTotal <= JewelLogic.MaxGems));
                }
                Assert.Less(s.Ply, JewelLogic.MaxPlies, $"seed {seed}: 打ち切りにならない");
                Assert.GreaterOrEqual(s.Players.Max(p => p.Points), JewelLogic.WinPoints, $"seed {seed}: だれかが目標に届く");
            }
        }

        [Test]
        public void LocalCpuMatch_CpuPlaysUntilItIsTheHumansTurn()
        {
            var match = new LocalCpuMatch(new Random(3), 3);
            match.StartNewGame();
            int guard = 0;
            while (match.IsCpuTurn && guard++ < 10) match.PlayCpuTurn();
            Assert.IsFalse(match.IsCpuTurn);
            Assert.IsTrue(match.Snapshot.View.IsMyTurn);
            Assert.AreEqual(JewelPhase.Main, match.Snapshot.View.Phase);
        }
    }
}
