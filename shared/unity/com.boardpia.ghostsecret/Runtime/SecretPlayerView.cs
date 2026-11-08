using System.Collections.Generic;
using BoardGames.Core;

namespace BoardGames.GhostSecret
{
    /// <summary>
    /// あるプレイヤーから見た局面。ほかの人の手札・山札・伏せて脇に置いたカードは見えない(-1 にする)。
    /// ウラナイで見ている 2 枚は、見ている本人だけに見える。知っている手札は、自分が知っているぶんだけ残す。
    /// 画面と CPU はこれだけを見る。
    /// </summary>
    public sealed class SecretPlayerView
    {
        /// <summary>局面に入れる、最近の出来事の数。</summary>
        public const int RecentEventCount = 64;

        static readonly SecretRules Rules = new SecretRules();

        readonly SecretState _state;
        readonly List<SecretEvent> _events;

        public int Viewer { get; }
        public int PlayerCount => _state.PlayerCount;
        public int CurrentPlayer => _state.CurrentPlayer;
        public int RoundStarter => _state.RoundStarter;
        public int Round => _state.Round;
        public SecretPhase Phase => _state.Phase;
        public int PendingTarget => _state.PendingTarget;
        public int Ply => _state.Ply;
        public GameResult Result => _state.Result;
        public bool IsMyTurn => !_state.Result.IsOver && _state.CurrentPlayer == Viewer;
        public int DeckCount => _state.Deck.Count;
        /// <summary>伏せて脇に置いたカードが、まだ脇にあるか(フキトバシで引かれていないか)。</summary>
        public bool HasSetAside => _state.SetAside != SecretState.SetAsideTaken;
        public IReadOnlyList<int> FaceUpAside => _state.FaceUpAside;
        /// <summary>ウラナイで見ている 2 枚(自分が見ているときだけ。上にくる順)。</summary>
        public IReadOnlyList<int> Peek => _state.Peek;
        public IReadOnlyList<SecretEvent> Events => _events;
        public int LastEventSeq => _events.Count > 0 ? _events[_events.Count - 1].Seq : -1;

        SecretPlayerView(SecretState masked, int viewer, List<SecretEvent> events)
        {
            _state = masked;
            Viewer = viewer;
            _events = events;
        }

        public static SecretPlayerView Create(SecretState state, int viewer)
        {
            var s = state.Clone();
            for (int p = 0; p < s.PlayerCount; p++)
            {
                var pl = s.Players[p];
                if (p != viewer)
                {
                    for (int i = 0; i < pl.Hand.Count; i++) pl.Hand[i] = -1;
                    for (int i = 0; i < pl.Known.Length; i++) pl.Known[i] = -1;
                }
            }
            for (int i = 0; i < s.Deck.Count; i++) s.Deck[i] = -1;
            if (s.SetAside >= 0) s.SetAside = -1;
            if (s.CurrentPlayer != viewer || s.Phase != SecretPhase.Fortune)
                for (int i = 0; i < s.Peek.Count; i++) s.Peek[i] = -1;

            int from = System.Math.Max(0, s.Events.Count - RecentEventCount);
            var events = new List<SecretEvent>(s.Events.Count - from);
            for (int i = from; i < s.Events.Count; i++) events.Add(s.Events[i].MaskedFor(viewer));
            s.Events.Clear();
            return new SecretPlayerView(s, viewer, events);
        }

        /// <summary>伏せ済みの局面から作る(オンラインで届いた局面を組み立て直すとき)。</summary>
        public static SecretPlayerView FromMasked(SecretState masked, int viewer, IEnumerable<SecretEvent> events)
        {
            masked.Events.Clear();
            return new SecretPlayerView(masked, viewer, new List<SecretEvent>(events));
        }

        /// <summary>伏せ済みの局面(通信用に文字列にするときだけ使う)。</summary>
        internal SecretState Masked => _state;

        public SecretPlayer PlayerAt(int p) => _state.Players[p];
        public SecretPlayer Me => _state.Players[Viewer];
        public IReadOnlyList<int> MyHand => Me.Hand;
        public int Tokens(int p) => _state.Players[p].Tokens;
        public bool IsOut(int p) => _state.Players[p].Out;
        public bool IsProtected(int p) => _state.Players[p].Protected;
        public int HandCount(int p) => _state.Players[p].Hand.Count;
        public IReadOnlyList<int> PlayedBy(int p) => _state.Players[p].Played;
        /// <summary>自分が知っている、p の今の手札(知らなければ -1)。</summary>
        public int KnownHandOf(int p) => p == Viewer ? -1 : Me.Known[p];
        public int TokensToWin => SecretLogic.TokensToWin(PlayerCount);

        /// <summary>自分の手番の合法手(手番でなければ空)。</summary>
        public IReadOnlyList<SecretMove> GetLegalMoves() =>
            IsMyTurn ? Rules.GetLegalMoves(_state) : (IReadOnlyList<SecretMove>)new List<SecretMove>();

        public bool IsLegal(SecretMove move)
        {
            foreach (var m in GetLegalMoves())
                if (m.Equals(move)) return true;
            return false;
        }

        /// <summary>カードを出すときに選べる人(画面用)。</summary>
        public List<int> TargetsFor(int card) =>
            SecretRules.Targets(_state, Viewer, includeSelf: SecretCards.ValueOf(card) == Ghost.Blow);

        /// <summary>
        /// まだ見えていないカードの数字ごとの枚数(全 18 枚から、自分の手札・全員の出したカード・表向きのカード・
        /// ウラナイで見ているカードを引いたもの)。知っている手札もここに含まれる。
        /// </summary>
        public int[] UnseenCounts()
        {
            var counts = new int[Ghost.Kinds];
            for (int v = 0; v < Ghost.Kinds; v++) counts[v] = SecretCards.CountOf(v);
            void Remove(int card)
            {
                if (card >= 0) counts[SecretCards.ValueOf(card)]--;
            }
            foreach (int c in Me.Hand) Remove(c);
            foreach (var pl in _state.Players)
                foreach (int c in pl.Played) Remove(c);
            foreach (int c in _state.FaceUpAside) Remove(c);
            foreach (int c in _state.Peek) Remove(c);
            return counts;
        }

        public List<int> Ranking() => SecretRules.Ranking(_state);
    }
}
