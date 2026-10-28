using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostJewel
{
    /// <summary>
    /// あるプレイヤーから見た局面。相手が山札から予約したカードと、山札の並びは見えない(-1 にする)。
    /// 画面と CPU はこれだけを見る。
    /// </summary>
    public sealed class JewelPlayerView
    {
        static readonly JewelRules Rules = new JewelRules();

        readonly JewelState _state;
        readonly List<JewelEvent> _events;

        public int Viewer { get; }
        public int PlayerCount => _state.PlayerCount;
        public int CurrentPlayer => _state.CurrentPlayer;
        public int StartPlayer => _state.StartPlayer;
        public JewelPhase Phase => _state.Phase;
        public bool FinalRound => _state.FinalRound;
        public int Ply => _state.Ply;
        public GameResult Result => _state.Result;
        public bool IsMyTurn => !_state.Result.IsOver && _state.CurrentPlayer == Viewer;
        public IReadOnlyList<int> Bank => _state.Bank;
        public IReadOnlyList<int> Nobles => _state.Nobles;
        public IReadOnlyList<JewelEvent> Events => _events;
        public int LastEventSeq => _events.Count > 0 ? _events[_events.Count - 1].Seq : -1;

        JewelPlayerView(JewelState masked, int viewer, List<JewelEvent> events)
        {
            _state = masked;
            Viewer = viewer;
            _events = events;
        }

        public static JewelPlayerView Create(JewelState state, int viewer)
        {
            var s = state.Clone();
            // 相手が山札から予約したカードは裏向き
            for (int p = 0; p < s.PlayerCount; p++)
            {
                if (p == viewer) continue;
                var pl = s.Players[p];
                for (int i = 0; i < pl.Reserved.Count; i++)
                    if (pl.ReservedFromDeck[i]) pl.Reserved[i] = -1;
            }
            // 山札は枚数だけ(中身は見せない)
            for (int t = 0; t < JewelLogic.Tiers; t++)
                for (int i = 0; i < s.Decks[t].Count; i++) s.Decks[t][i] = -1;

            var events = new List<JewelEvent>(s.Events.Count);
            foreach (var e in s.Events)
                events.Add(e.Kind == EventKind.Reserved && e.Slot < 0 && e.Player != viewer ? e.WithCard(-1) : e);
            s.Events.Clear();
            return new JewelPlayerView(s, viewer, events);
        }

        /// <summary>
        /// 伏せ済みの局面から作る(オンラインで届いた局面を組み立て直すとき)。
        /// masked の山札は枚数ぶんの -1、相手の山札からの予約は -1 になっていること。
        /// </summary>
        public static JewelPlayerView FromMasked(JewelState masked, int viewer, IEnumerable<JewelEvent> events)
        {
            masked.Events.Clear();
            return new JewelPlayerView(masked, viewer, new List<JewelEvent>(events));
        }

        /// <summary>伏せ済みの局面(通信用に文字列にするときだけ使う)。</summary>
        internal JewelState Masked => _state;

        public JewelPlayer PlayerAt(int p) => _state.Players[p];
        public JewelPlayer Me => _state.Players[Viewer];
        public int MarketAt(int tier, int slot) => _state.Market[tier][slot];
        public int DeckCount(int tier) => _state.Decks[tier].Count;
        public bool FindInMarket(int card, out int tier, out int slot) => _state.FindInMarket(card, out tier, out slot);

        /// <summary>自分の手番の合法手(手番でなければ空)。</summary>
        public IReadOnlyList<JewelMove> GetLegalMoves() =>
            IsMyTurn ? Rules.GetLegalMoves(_state) : (IReadOnlyList<JewelMove>)new List<JewelMove>();

        public bool IsLegal(JewelMove move)
        {
            foreach (var m in GetLegalMoves())
                if (m.Equals(move)) return true;
            return false;
        }

        public bool CanAfford(int card) => JewelRules.CanAfford(Me, card);

        public List<int> Ranking() => JewelRules.Ranking(_state);
    }
}
