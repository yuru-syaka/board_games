using NUnit.Framework;
using static BoardGames.BattleLine.Tests.TestBoard;

namespace BoardGames.BattleLine.Tests
{
    public class FormationTests
    {
        static FormationRank Eval(params int[] cards) => Formation.Evaluate(cards, false);

        [Test]
        public void Types_AreRecognized()
        {
            Assert.AreEqual(FormationType.Wedge, Eval(T(R, 7), T(R, 8), T(R, 9)).Type);
            Assert.AreEqual(FormationType.Phalanx, Eval(T(R, 6), T(B, 6), T(G, 6)).Type);
            Assert.AreEqual(FormationType.Battalion, Eval(T(R, 1), T(R, 5), T(R, 9)).Type);
            Assert.AreEqual(FormationType.Skirmish, Eval(T(R, 4), T(B, 5), T(G, 6)).Type);
            Assert.AreEqual(FormationType.Host, Eval(T(R, 1), T(B, 5), T(G, 9)).Type);
        }

        [Test]
        public void Order_IsIndependentOfPlayOrder()
        {
            Assert.AreEqual(FormationType.Wedge, Eval(T(Y, 9), T(Y, 7), T(Y, 8)).Type);
            Assert.AreEqual(24, Eval(T(Y, 9), T(Y, 7), T(Y, 8)).Sum);
        }

        [Test]
        public void HigherType_BeatsHigherSum()
        {
            var lowWedge = Eval(T(R, 1), T(R, 2), T(R, 3));
            var highPhalanx = Eval(T(R, 10), T(B, 10), T(G, 10));
            Assert.Greater(lowWedge.Score, highPhalanx.Score);

            var lowBattalion = Eval(T(R, 1), T(R, 2), T(R, 4));
            var highSkirmish = Eval(T(R, 8), T(B, 9), T(G, 10));
            Assert.Greater(lowBattalion.Score, highSkirmish.Score);
        }

        [Test]
        public void SameType_ComparesSum()
        {
            Assert.Greater(Eval(T(R, 8), T(R, 9), T(R, 10)).Score, Eval(T(B, 7), T(B, 8), T(B, 9)).Score);
            Assert.AreEqual(Eval(T(R, 8), T(R, 9), T(R, 10)).Score, Eval(T(B, 8), T(B, 9), T(B, 10)).Score);
        }

        [Test]
        public void Run_DoesNotWrapAround()
        {
            Assert.AreEqual(FormationType.Battalion, Eval(T(R, 9), T(R, 10), T(R, 1)).Type);
            Assert.AreEqual(FormationType.Host, Eval(T(R, 9), T(B, 10), T(G, 1)).Type);
        }

        [Test]
        public void Leader_IsBestWildcard()
        {
            var r = Eval(T(R, 8), T(R, 9), Card.LeaderA);
            Assert.AreEqual(FormationType.Wedge, r.Type);
            Assert.AreEqual(27, r.Sum, "8-9-10 が最大");

            var gap = Eval(T(R, 8), T(R, 10), Card.LeaderA);
            Assert.AreEqual(FormationType.Wedge, gap.Type);
            Assert.AreEqual(27, gap.Sum);

            var phalanx = Eval(T(R, 5), T(B, 5), Card.LeaderB);
            Assert.AreEqual(FormationType.Phalanx, phalanx.Type);
        }

        [Test]
        public void Cavalry_IsAlwaysEight()
        {
            Assert.AreEqual(FormationType.Wedge, Eval(T(G, 9), T(G, 10), Card.Cavalry).Type);
            Assert.AreEqual(FormationType.Phalanx, Eval(T(G, 8), T(B, 8), Card.Cavalry).Type);
            Assert.AreEqual(FormationType.Battalion, Eval(T(G, 2), T(G, 5), Card.Cavalry).Type);
            Assert.AreEqual(15, Eval(T(G, 2), T(G, 5), Card.Cavalry).Sum);
        }

        [Test]
        public void Shield_IsOneToThree()
        {
            var r = Eval(T(B, 1), T(B, 2), Card.Shield);
            Assert.AreEqual(FormationType.Wedge, r.Type);
            Assert.AreEqual(6, r.Sum);
            Assert.AreEqual(FormationType.Battalion, Eval(T(B, 5), T(B, 6), Card.Shield).Type, "4 にはなれない");
            Assert.AreEqual(FormationType.Phalanx, Eval(T(B, 3), T(R, 3), Card.Shield).Type);
        }

        [Test]
        public void Fog_UsesSumOnly()
        {
            var wedge = Formation.Evaluate(new[] { T(R, 1), T(R, 2), T(R, 3) }, true);
            var host = Formation.Evaluate(new[] { T(R, 10), T(B, 2), T(G, 7) }, true);
            Assert.Greater(host.Score, wedge.Score);
        }

        [Test]
        public void Mud_FourCardFormations()
        {
            Assert.AreEqual(FormationType.Wedge, Eval(T(P, 3), T(P, 4), T(P, 5), T(P, 6)).Type);
            Assert.AreEqual(FormationType.Phalanx, Eval(T(P, 3), T(R, 3), T(B, 3), T(G, 3)).Type);
            Assert.AreEqual(FormationType.Skirmish, Eval(T(P, 3), T(R, 4), T(B, 5), T(G, 6)).Type);
        }

        [Test]
        public void TryBest_UsesPool()
        {
            var pool = new bool[Card.TroopCount];
            pool[T(R, 10)] = true;
            Assert.IsTrue(Formation.TryBest(new[] { T(R, 8), T(R, 9) }, 3, false, pool, out var best));
            Assert.AreEqual(FormationType.Wedge, best.Type);
            Assert.AreEqual(27, best.Sum);

            pool[T(R, 10)] = false;
            pool[T(B, 5)] = true;
            Assert.IsTrue(Formation.TryBest(new[] { T(R, 8), T(R, 9) }, 3, false, pool, out best));
            Assert.AreEqual(FormationType.Host, best.Type);

            Assert.IsFalse(Formation.TryBest(new[] { T(R, 8) }, 3, false, pool, out _), "足りない");
        }
    }
}
