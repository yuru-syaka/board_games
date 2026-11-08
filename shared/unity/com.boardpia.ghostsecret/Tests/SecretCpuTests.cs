using System;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.GhostSecret.Tests
{
    public class SecretCpuTests
    {
        readonly SecretRules _rules = new SecretRules();

        static int CardsInPlay(SecretState s) =>
            s.Players.Sum(p => p.Hand.Count + p.Played.Count) + s.Deck.Count + s.FaceUpAside.Count + s.Peek.Count + (s.SetAside >= 0 ? 1 : 0);

        /// <summary>CPU だけで最後まで遊び、合法手しか打たないこと・カードが増減しないこと・終局することを確かめる。</summary>
        [Test]
        public void CpuGames_FinishWithLegalMovesOnly([Values(2, 3, 4)] int players)
        {
            for (int seed = 0; seed < 300; seed++)
            {
                var s = SecretSetup.CreateRandom(new Random(seed * 13 + players), players);
                var cpus = Enumerable.Range(0, players).Select(i => new SecretCpu(new Random(seed * 7 + i))).ToArray();
                int guard = 0;
                while (!s.Result.IsOver)
                {
                    Assert.Less(guard++, 5000, $"seed {seed}: 止まらない");
                    var move = cpus[s.CurrentPlayer].ChooseMove(SecretPlayerView.Create(s, s.CurrentPlayer));
                    Assert.IsTrue(_rules.IsLegal(s, move), $"seed {seed}: {move}");
                    s = _rules.ApplyMove(s, move);
                    Assert.AreEqual(SecretLogic.CardCount, CardsInPlay(s), $"seed {seed}: カードの総数");
                    Assert.GreaterOrEqual(s.ActiveCount, 1);
                    if (s.Phase == SecretPhase.Play && !s.Result.IsOver) Assert.AreEqual(2, s.Current.Hand.Count, $"seed {seed}: 手番の人は 2 枚");
                }
                Assert.LessOrEqual(s.Round, SecretLogic.MaxRounds);
                Assert.IsTrue(s.Players.Any(p => p.Tokens >= SecretLogic.TokensToWin(players)) || s.Round == SecretLogic.MaxRounds);
            }
        }

        [Test]
        public void Cpu_GuessesAKnownCard()
        {
            var s = TestState.Make(new[] { new[] { TestState.Whisper, TestState.Hide }, new[] { TestState.Lamp }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
            s.Players[0].Known[1] = TestState.Lamp;
            var move = new SecretCpu(new Random(1)).ChooseMove(SecretPlayerView.Create(s, 0));
            Assert.AreEqual(SecretMove.Play(TestState.Whisper, 1, Ghost.Lamp), move);
        }

        [Test]
        public void Cpu_NeverPlaysTheSecret()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var s = TestState.Make(new[] { new[] { TestState.Secret, TestState.Whisper }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
                var move = new SecretCpu(new Random(seed)).ChooseMove(SecretPlayerView.Create(s, 0));
                Assert.AreNotEqual(TestState.Secret, move.Card);
            }
        }

        [Test]
        public void LocalCpuMatch_CpuPlaysUntilItIsTheHumansTurn()
        {
            var match = new LocalCpuMatch(new Random(3), 3);
            match.StartNewGame();
            int guard = 0;
            while (match.IsCpuTurn && guard++ < 20) match.PlayCpuTurn();
            Assert.IsFalse(match.IsCpuTurn);
            Assert.IsTrue(match.Snapshot.View.IsMyTurn || match.Snapshot.Result.IsOver);
        }
    }
}
