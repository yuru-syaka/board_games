using System;
using System.Collections.Generic;
using System.Linq;
using BoardGames.Core;

namespace BoardGames.GhostGold
{
    /// <summary>
    /// あるプレイヤー(Viewer)から見た局面。山札の順番と、ほかの人のまだ公開していない決定は伏せる。
    /// 画面と CPU にはこれだけを渡す。山札の「内訳」は、見えているカードから数えられるので出す。
    /// </summary>
    public sealed class GoldPlayerView
    {
        /// <summary>画面・通信に渡す出来事の数(新しいものから)。</summary>
        public const int RecentEventCount = 80;

        static readonly GoldRules Rules = new GoldRules();
        readonly GoldState _state;

        public int Viewer { get; }
        public IReadOnlyList<GoldEvent> Events { get; }

        GoldPlayerView(GoldState masked, int viewer, IReadOnlyList<GoldEvent> events)
        {
            _state = masked;
            Viewer = viewer;
            Events = events;
        }

        public static GoldPlayerView Create(GoldState state, int viewer)
        {
            if (viewer < 0 || viewer >= state.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            var s = state.Clone();
            for (int i = 0; i < s.Deck.Count; i++) s.Deck[i] = -1;
            for (int i = 0; i < s.PlayerCount; i++)
                if (i != viewer) s.Players[i].Choice = Choice.None;
            var events = state.Events.Skip(Math.Max(0, state.Events.Count - RecentEventCount)).ToList();
            s.Events.Clear();
            return new GoldPlayerView(s, viewer, events);
        }

        /// <summary>通信で受け取った、伏せ済みの局面から作る。</summary>
        public static GoldPlayerView FromMasked(GoldState masked, int viewer, IReadOnlyList<GoldEvent> events) =>
            new GoldPlayerView(masked, viewer, events);

        /// <summary>いちばん新しい出来事の番号(出来事がなければ -1)。</summary>
        public int LastEventSeq => Events.Count > 0 ? Events[Events.Count - 1].Seq : -1;

        /// <summary>伏せ済みの局面(通信用)。書き換えないこと。</summary>
        public GoldState Masked => _state;

        public int PlayerCount => _state.PlayerCount;
        public int Night => _state.Night;
        public GoldPhase Phase => _state.Phase;
        public int CurrentPlayer => _state.CurrentPlayer;
        public int Ply => _state.Ply;
        public GameResult Result => _state.Result;
        public bool IsMyTurn => !_state.Result.IsOver && _state.CurrentPlayer == Viewer;

        public int DeckCount => _state.Deck.Count;
        public IReadOnlyList<int> Path => _state.Path;
        public int Leftover => _state.Leftover;
        public int HazardRemoved(int kind) => _state.HazardRemoved[kind];
        public RelicStatus RelicStatusOf(int night) => _state.Relics[night - 1];

        public bool IsExploring(int p) => _state.Players[p].Exploring;
        public int Carry(int p) => _state.Players[p].Carry;
        public int Safe(int p) => _state.Players[p].Safe;
        public IReadOnlyList<int> RelicsOf(int p) => _state.Players[p].Relics;
        public int Score(int p) => _state.Players[p].Score;
        public bool HasAmulet(int p) => _state.Players[p].HasAmulet;
        public bool HasDecided(int p) => _state.Players[p].Decided;
        /// <summary>自分の決定(ほかの人の分は公開まで None)。</summary>
        public Choice MyChoice => _state.Players[Viewer].Choice;
        public int ExploringCount => _state.ExploringCount;

        /// <summary>この夜の通路にある、kind の怪異の枚数。</summary>
        public int HazardOnPath(int kind) => _state.HazardOnPath(kind);

        /// <summary>今の順位(0 = 1 位)。</summary>
        public int[] Ranking() => GoldRules.Ranking(_state);

        public IReadOnlyList<GoldMove> GetLegalMoves() =>
            IsMyTurn ? Rules.GetLegalMoves(_state) : (IReadOnlyList<GoldMove>)Array.Empty<GoldMove>();

        public bool IsLegal(GoldMove move) => IsMyTurn && Rules.IsLegal(_state, move);

        // ---------- 山札の内訳(見えているカードから数える) ----------

        /// <summary>山札に残っている金貨の部屋の、金貨の枚数の一覧。</summary>
        public List<int> TreasuresInDeck()
        {
            var onPath = new HashSet<int>(_state.Path);
            var list = new List<int>();
            for (int c = 0; c < GoldLogic.TreasureCount; c++)
                if (!onPath.Contains(c)) list.Add(GoldCards.TreasureOf(c));
            return list;
        }

        /// <summary>山札に残っている、kind の怪異の枚数。</summary>
        public int HazardsInDeck(int kind) =>
            Math.Max(0, GoldLogic.HazardCopies - _state.HazardRemoved[kind] - _state.HazardOnPath(kind));

        /// <summary>山札に残っている秘宝の枚数。</summary>
        public int RelicsInDeck() => _state.Relics.Count(r => r == RelicStatus.InDeck);

        /// <summary>通路に出ている(まだだれのものでもない)秘宝の値打ちの合計。</summary>
        public int RelicValueOnPath()
        {
            int sum = 0;
            for (int n = 1; n <= GoldLogic.RelicCount; n++)
                if (_state.Relics[n - 1] == RelicStatus.OnPath) sum += GoldCards.RelicValue(GoldCards.RelicCard(n));
            return sum;
        }

        /// <summary>次の 1 枚で「おどかされる」確率(通路に 1 枚出ている種類の怪異が、山札に残っている割合)。</summary>
        public double ScareChance()
        {
            if (DeckCount == 0) return 0;
            int danger = 0;
            for (int k = 0; k < GoldLogic.HazardKinds; k++)
                if (HazardOnPath(k) > 0) danger += HazardsInDeck(k);
            return Math.Min(1.0, (double)danger / DeckCount);
        }
    }
}
