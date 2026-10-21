using System.Collections.Generic;

namespace BoardGames.BattleLine
{
    /// <summary>
    /// フラッグ 1 本。両者の隊列(置いた順)・環境戦術(霧・泥沼)・確保者・隊列を完成させた順番を持つ。
    /// ルールの外からは読み取り専用として扱い、変更はルール(クローンした局面)の中だけで行う。
    /// </summary>
    public sealed class Flag
    {
        public const int NormalSize = 3;
        public const int MudSize = 4;

        readonly List<int>[] _cards = { new List<int>(4), new List<int>(4) };
        readonly List<int> _env = new List<int>(2);
        readonly List<int> _envOwners = new List<int>(2);
        readonly int[] _completedSeq = { -1, -1 };

        public int Index { get; }
        /// <summary>確保したプレイヤー(未確保は −1)。</summary>
        public int Claimer { get; internal set; } = -1;

        public Flag(int index)
        {
            Index = index;
        }

        public bool IsClaimed => Claimer >= 0;
        public IReadOnlyList<int> CardsOf(int player) => _cards[player];
        /// <summary>置かれている環境戦術(霧・泥沼)。</summary>
        public IReadOnlyList<int> Environment => _env;
        public int EnvironmentOwner(int envIndex) => _envOwners[envIndex];
        public bool HasFog => _env.Contains(Card.Fog);
        public bool HasMud => _env.Contains(Card.Mud);
        /// <summary>隊列に必要な枚数(泥沼があれば 4)。</summary>
        public int Required => HasMud ? MudSize : NormalSize;
        public bool IsComplete(int player) => _cards[player].Count >= Required;
        public bool HasSpace(int player) => !IsClaimed && _cards[player].Count < Required;
        /// <summary>隊列を完成させた順番(小さいほど先。未完成は −1)。</summary>
        public int CompletedSeq(int player) => _completedSeq[player];

        public bool Contains(int card) => _cards[0].Contains(card) || _cards[1].Contains(card) || _env.Contains(card);

        /// <summary>カードの持ち主(このフラッグに無ければ −1)。</summary>
        public int OwnerOf(int card)
        {
            if (_cards[0].Contains(card)) return 0;
            if (_cards[1].Contains(card)) return 1;
            int e = _env.IndexOf(card);
            return e >= 0 ? _envOwners[e] : -1;
        }

        public FormationRank Rank(int player) => Formation.Evaluate(_cards[player], HasFog);

        // ---------- 変更(ルール内部・局面の組み立て用。テストからは InternalsVisibleTo で使う) ----------

        internal void AddCard(int player, int card) => _cards[player].Add(card);

        internal void AddEnvironment(int card, int owner)
        {
            _env.Add(card);
            _envOwners.Add(owner);
        }

        internal bool Remove(int card)
        {
            if (_cards[0].Remove(card) || _cards[1].Remove(card)) return true;
            int e = _env.IndexOf(card);
            if (e < 0) return false;
            _env.RemoveAt(e);
            _envOwners.RemoveAt(e);
            return true;
        }

        internal void SetCompletedSeq(int player, int seq) => _completedSeq[player] = seq;

        internal void SetClaimer(int player) => Claimer = player;

        internal Flag Clone()
        {
            var f = new Flag(Index) { Claimer = Claimer };
            for (int p = 0; p < 2; p++)
            {
                f._cards[p].AddRange(_cards[p]);
                f._completedSeq[p] = _completedSeq[p];
            }
            f._env.AddRange(_env);
            f._envOwners.AddRange(_envOwners);
            return f;
        }
    }
}
