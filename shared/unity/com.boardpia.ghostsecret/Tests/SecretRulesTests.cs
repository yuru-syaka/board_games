using System;
using System.Linq;
using NUnit.Framework;

namespace BoardGames.GhostSecret.Tests
{
    /// <summary>テスト用の局面を作る(カード ID: 0 ノロイ / 1〜5 ササヤキ / 6〜7 ウラナイ / 8〜9 ウワサ / 10〜11 クラベ / 12〜13 カクレ / 14〜15 フキトバシ / 16 アカリ / 17 ヒミツ)。</summary>
    static class TestState
    {
        public const int Curse = 0, Whisper = 1, Whisper2 = 2, Fortune = 6, Rumor = 8, Compare = 10, Compare2 = 11, Hide = 12,
            Blow = 14, Lamp = 16, Secret = 17;

        /// <summary>hands[p] = 手札。current の手札は 2 枚(引いたあと)。deck は末尾がいちばん上。</summary>
        public static SecretState Make(int[][] hands, int[] deck, int current = 0, int setAside = 13)
        {
            int n = hands.Length;
            var s = new SecretState(n) { Round = 1, Phase = SecretPhase.Play, CurrentPlayer = current, RoundStarter = current, SetAside = setAside, Seed = 5 };
            for (int p = 0; p < n; p++) s.Players[p].Hand.AddRange(hands[p]);
            s.Deck.AddRange(deck);
            return s;
        }
    }

    public class SecretRulesTests
    {
        readonly SecretRules _rules = new SecretRules();

        static SecretEvent Last(SecretState s, EventKind kind) => s.Events.Last(e => e.Kind == kind);

        [Test]
        public void Cards_Are18CardsIn9Kinds()
        {
            Assert.AreEqual(18, SecretCards.Count);
            Assert.AreEqual(new[] { 1, 5, 2, 2, 2, 2, 2, 1, 1 }, Enumerable.Range(0, 9).Select(SecretCards.CountOf).ToArray());
            Assert.AreEqual(Ghost.Curse, SecretCards.ValueOf(TestState.Curse));
            Assert.AreEqual(Ghost.Secret, SecretCards.ValueOf(TestState.Secret));
            Assert.IsFalse(SecretCards.IsGuessable(Ghost.Whisper));
        }

        [Test]
        public void Setup_DealsOneCardEachAndTheStarterDrawsSecond([Values(2, 3, 4)] int n)
        {
            var s = SecretSetup.CreateRandom(new Random(1), n);
            Assert.AreEqual(1, s.Round);
            Assert.AreEqual(2, s.Current.Hand.Count);
            Assert.AreEqual(n == 2 ? 2 : 0, s.FaceUpAside.Count);
            Assert.IsTrue(s.SetAside >= 0);
            for (int p = 0; p < n; p++)
                if (p != s.CurrentPlayer) Assert.AreEqual(1, s.Players[p].Hand.Count);
            int total = s.Players.Sum(p => p.Hand.Count) + s.Deck.Count + s.FaceUpAside.Count + 1;
            Assert.AreEqual(18, total);
        }

        [Test]
        public void Curse_CannotBePlayed()
        {
            var s = TestState.Make(new[] { new[] { TestState.Curse, TestState.Hide }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
            var moves = _rules.GetLegalMoves(s);
            Assert.IsTrue(moves.All(m => m.Card == TestState.Hide));
        }

        [Test]
        public void Whisper_HitEliminatesTheTarget()
        {
            var s = TestState.Make(new[] { new[] { TestState.Whisper, TestState.Hide }, new[] { TestState.Compare }, new[] { TestState.Lamp } },
                new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Whisper, 1, Ghost.Compare));
            Assert.IsTrue(s.Players[1].Out);
            Assert.AreEqual(EliminationReason.Guessed, Last(s, EventKind.Eliminated).Amount);
            Assert.AreEqual(2, s.CurrentPlayer, "脱落した人は飛ばす");
        }

        [Test]
        public void Whisper_MissLetsTheTargetSeeYourHand()
        {
            var s = TestState.Make(new[] { new[] { TestState.Whisper, TestState.Hide }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Whisper, 1, Ghost.Lamp));
            Assert.IsFalse(s.Players[1].Out);
            Assert.AreEqual(TestState.Hide, s.Players[1].Known[0], "言われた人は、言った人の手札を知る");
            var miss = Last(s, EventKind.GuessMiss);
            Assert.IsTrue(miss.VisibleTo(1) && miss.VisibleTo(0));
        }

        [Test]
        public void Whisper_CannotTargetAHiddenPlayer()
        {
            var s = TestState.Make(new[] { new[] { TestState.Whisper, TestState.Rumor }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
            s.Players[1].Protected = true;
            var whispers = _rules.GetLegalMoves(s).Where(m => m.Card == TestState.Whisper).ToList();
            Assert.AreEqual(1, whispers.Count);
            Assert.AreEqual(-1, whispers[0].Target);
            s = _rules.ApplyMove(s, whispers[0]);
            Assert.AreEqual(EventKind.NoEffect, s.Events[s.Events.Count - 2].Kind);
        }

        [Test]
        public void Compare_LowerIsOutAndTieDoesNothing()
        {
            var s = TestState.Make(new[] { new[] { TestState.Compare, TestState.Lamp }, new[] { TestState.Hide } }, new[] { 3, 4, 5 });
            var after = _rules.ApplyMove(s, SecretMove.Play(TestState.Compare, 1));
            Assert.AreEqual(1, Last(after, EventKind.Eliminated).Player, "数字の小さい P1 が脱落");
            Assert.AreEqual(1, after.Players[0].Tokens, "残り 1 人でラウンドの勝ち");
            Assert.AreEqual(2, after.Round, "2 人なので、すぐ次のラウンドが始まる");

            // P0 はクラベを出してカクレ(13)を残す。P1 もカクレ(12)なので同じ数字
            var tie = TestState.Make(new[] { new[] { TestState.Compare, 13 }, new[] { TestState.Hide }, new[] { 9 } }, new[] { 3, 4, 5 });
            var t = _rules.ApplyMove(tie, SecretMove.Play(TestState.Compare, 1));
            Assert.IsFalse(t.Players[0].Out);
            Assert.IsFalse(t.Players[1].Out);
        }

        [Test]
        public void Hide_ProtectsUntilYourNextTurn()
        {
            var s = TestState.Make(new[] { new[] { TestState.Hide, TestState.Rumor }, new[] { TestState.Whisper } }, new[] { 3, 4, 5, 7 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Hide));
            Assert.IsTrue(s.Players[0].Protected);
            Assert.IsTrue(_rules.GetLegalMoves(s).Where(m => m.Card == TestState.Whisper).All(m => m.Target == -1));
            // 相手が何か出して、自分の番に戻ると守りが外れる
            var m2 = _rules.GetLegalMoves(s).First(m => m.Card != TestState.Whisper || m.Target == -1);
            s = _rules.ApplyMove(s, m2);
            if (s.Phase == SecretPhase.Fortune) s = _rules.ApplyMove(s, SecretMove.FortuneOrder(false));
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.IsFalse(s.Players[0].Protected);
        }

        [Test]
        public void Blow_OnSecretEliminatesTheHolder()
        {
            var s = TestState.Make(new[] { new[] { TestState.Blow, TestState.Hide }, new[] { TestState.Secret }, new[] { TestState.Compare } },
                new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Blow, 1));
            Assert.IsTrue(s.Players[1].Out);
            Assert.AreEqual(EliminationReason.SecretLost, Last(s, EventKind.Eliminated).Amount);
        }

        [Test]
        public void Blow_OnCurseEliminatesTheBlowerAndTheTargetRedraws()
        {
            var s = TestState.Make(new[] { new[] { TestState.Blow, TestState.Hide }, new[] { TestState.Curse }, new[] { TestState.Compare } },
                new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Blow, 1));
            Assert.IsTrue(s.Players[0].Out, "フキトバシを出した人が脱落");
            Assert.IsFalse(s.Players[1].Out);
            Assert.IsTrue(s.Events.Any(e => e.Kind == EventKind.Drew && e.Player == 1 && e.Card == 5), "捨てた人は山札から引き直す");
            Assert.AreEqual(EliminationReason.Cursed, Last(s, EventKind.Eliminated).Amount);
        }

        [Test]
        public void Blow_DrawsTheSetAsideCardWhenTheDeckIsEmpty()
        {
            // 自分が最後の 1 枚を引いた直後(山札 0 枚)にフキトバシ
            var s = TestState.Make(new[] { new[] { TestState.Blow, TestState.Hide }, new[] { TestState.Compare }, new[] { 3 } }, new int[0], setAside: 7);
            var before = s;
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Blow, 1));
            Assert.AreEqual(7, before.SetAside);
            Assert.IsTrue(s.Events.Any(e => e.Kind == EventKind.Drew && e.Player == 1 && e.Card == 7), "伏せて脇に置いたカードを引く");
            Assert.IsTrue(s.Events.Any(e => e.Kind == EventKind.Showdown), "山札が尽きたので公開");
        }

        [Test]
        public void Lamp_LooksThenMaySwap()
        {
            var s = TestState.Make(new[] { new[] { TestState.Lamp, TestState.Whisper }, new[] { TestState.Compare } }, new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Lamp, 1));
            Assert.AreEqual(SecretPhase.Lamp, s.Phase);
            Assert.AreEqual(0, s.CurrentPlayer);
            Assert.AreEqual(TestState.Compare, s.Players[0].Known[1]);
            s = _rules.ApplyMove(s, SecretMove.LampSwap(true));
            Assert.AreEqual(TestState.Compare, s.Players[0].Hand[0]);
            Assert.AreEqual(TestState.Whisper, s.Players[1].Hand[0]);
            Assert.AreEqual(TestState.Compare, s.Players[1].Known[0], "相手は、渡したカードを知っている");
            Assert.AreEqual(1, s.CurrentPlayer);
        }

        [Test]
        public void Fortune_PutsTheChosenCardOnTop()
        {
            var s = TestState.Make(new[] { new[] { TestState.Fortune, TestState.Hide }, new[] { TestState.Compare } }, new[] { 3, 4, 16, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Fortune));
            Assert.AreEqual(SecretPhase.Fortune, s.Phase);
            Assert.AreEqual(new[] { 5, 16 }, s.Peek.ToArray(), "上から順");
            s = _rules.ApplyMove(s, SecretMove.FortuneOrder(true));
            // 入れ替えたので 16 が上。相手(P1)が引く
            Assert.IsTrue(s.Players[1].Hand.Contains(16));
        }

        [Test]
        public void Rumor_PassesHandsAndSkipsHiddenPlayers()
        {
            var s = TestState.Make(new[] { new[] { TestState.Rumor, TestState.Hide }, new[] { TestState.Compare }, new[] { TestState.Lamp }, new[] { TestState.Whisper } },
                new[] { 3, 4, 5 });
            s.Players[2].Protected = true;
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Rumor));
            Assert.AreEqual(TestState.Whisper, s.Players[0].Hand[0], "P3 → P0");
            Assert.AreEqual(TestState.Hide, s.Players[1].Hand.First(c => c != 5), "P0 → P1(P1 は次の手番で 1 枚引く)");
            Assert.AreEqual(TestState.Lamp, s.Players[2].Hand[0], "カクレの P2 は渡さない");
            Assert.AreEqual(TestState.Compare, s.Players[3].Hand[0], "P1 → P3(P2 を飛ばす)");
            Assert.AreEqual(TestState.Hide, s.Players[0].Known[1]);
        }

        [Test]
        public void PlayingTheSecret_EliminatesYou()
        {
            var s = TestState.Make(new[] { new[] { TestState.Secret, TestState.Hide }, new[] { TestState.Compare }, new[] { 3 } }, new[] { 3, 4, 5 });
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Secret));
            Assert.IsTrue(s.Players[0].Out);
        }

        [Test]
        public void Showdown_HighestWinsThenPlayedSum()
        {
            // 山札 0 枚の状態で P0 がカクレを出すと公開。P1 と P2 は同じ数字(4)なので、出したカードの合計で決まる
            var s = TestState.Make(new[] { new[] { TestState.Hide, 3 }, new[] { TestState.Compare }, new[] { TestState.Compare2 } }, new int[0]);
            s.Players[1].Played.Add(6);
            s.Players[2].Played.Add(8);
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Hide));
            Assert.AreEqual(1, s.Players[2].Tokens, "数字が同じなら、出したカードの合計が大きい P2");
            Assert.AreEqual(0, s.Players[1].Tokens);
            Assert.AreEqual(2, s.Round, "次のラウンドが始まる");
            Assert.AreEqual(2, s.RoundStarter, "勝者から始める");
        }

        [Test]
        public void ReachingTheTokenGoal_EndsTheGame()
        {
            var s = TestState.Make(new[] { new[] { TestState.Compare, TestState.Lamp }, new[] { TestState.Hide } }, new[] { 3, 4, 5 });
            s.Players[0].Tokens = SecretLogic.TokensToWin(2) - 1;
            s = _rules.ApplyMove(s, SecretMove.Play(TestState.Compare, 1));
            Assert.IsTrue(s.Result.IsOver);
            Assert.AreEqual(0, s.Result.Winner);
            Assert.AreEqual(SecretPhase.Over, s.Phase);
            Assert.AreEqual(0, _rules.GetLegalMoves(s).Count);
        }

        [Test]
        public void View_HidesOtherHandsTheDeckAndPrivateEvents()
        {
            var s = SecretSetup.CreateRandom(new Random(4), 3);
            var other = (s.CurrentPlayer + 1) % 3;
            var v = SecretPlayerView.Create(s, other);
            Assert.IsTrue(v.PlayerAt(s.CurrentPlayer).Hand.All(c => c == -1));
            Assert.AreEqual(1, v.MyHand.Count);
            Assert.IsTrue(v.MyHand.All(SecretCards.IsCard));
            Assert.IsTrue(v.Events.Where(e => e.Kind == EventKind.Drew && e.Player != other).All(e => e.Card == -1));
            Assert.IsTrue(v.Events.Where(e => e.Kind == EventKind.Drew && e.Player == other).All(e => e.Card >= 0));
            Assert.IsFalse(v.IsMyTurn);
            Assert.AreEqual(0, v.GetLegalMoves().Count);
        }
    }
}
