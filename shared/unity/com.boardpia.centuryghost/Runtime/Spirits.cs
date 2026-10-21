using System;
using System.Text;

namespace BoardGames.CenturyGhost
{
    /// <summary>
    /// 魂の個数の組(不変)。ランク 0 = 黄(Y)、1 = 青(C)、2 = 桃(M)、3 = 紫(V)。
    /// </summary>
    public readonly struct Spirits : IEquatable<Spirits>
    {
        public const int Ranks = 4;
        public const int TopRank = Ranks - 1;
        const string Letters = "YCMV";

        readonly int _y;
        readonly int _c;
        readonly int _m;
        readonly int _v;

        public Spirits(int y, int c, int m, int v)
        {
            if (y < 0 || c < 0 || m < 0 || v < 0) throw new ArgumentOutOfRangeException(nameof(y), "Negative spirits");
            _y = y;
            _c = c;
            _m = m;
            _v = v;
        }

        public static Spirits Empty => default;

        public int this[int rank]
        {
            get
            {
                switch (rank)
                {
                    case 0: return _y;
                    case 1: return _c;
                    case 2: return _m;
                    case 3: return _v;
                    default: throw new ArgumentOutOfRangeException(nameof(rank));
                }
            }
        }

        public int Total => _y + _c + _m + _v;
        public bool IsEmpty => Total == 0;

        /// <summary>終了時の得点(黄以外 1 個 1 点)。</summary>
        public int Score => _c + _m + _v;

        /// <summary>"YYCM" 形式から作る(順不同)。</summary>
        public static Spirits Parse(string s)
        {
            var counts = new int[Ranks];
            foreach (char ch in s ?? "")
            {
                int r = Letters.IndexOf(ch);
                if (r < 0) throw new FormatException($"Unknown spirit '{ch}'");
                counts[r]++;
            }
            return FromArray(counts);
        }

        public static Spirits FromArray(int[] counts) => new Spirits(counts[0], counts[1], counts[2], counts[3]);

        public int[] ToArray() => new[] { _y, _c, _m, _v };

        public static Spirits One(int rank) => Empty.With(rank, 1);

        public Spirits With(int rank, int delta)
        {
            var a = ToArray();
            a[rank] += delta;
            return FromArray(a);
        }

        public bool Contains(Spirits other) =>
            _y >= other._y && _c >= other._c && _m >= other._m && _v >= other._v;

        public Spirits Plus(Spirits o) => new Spirits(_y + o._y, _c + o._c, _m + o._m, _v + o._v);

        public Spirits Minus(Spirits o) => new Spirits(_y - o._y, _c - o._c, _m - o._m, _v - o._v);

        public Spirits Times(int k) => new Spirits(_y * k, _c * k, _m * k, _v * k);

        /// <summary>other を何回払えるか(other が空なら int.MaxValue)。</summary>
        public int CountOf(Spirits other)
        {
            int n = int.MaxValue;
            for (int r = 0; r < Ranks; r++)
                if (other[r] > 0) n = Math.Min(n, this[r] / other[r]);
            return n;
        }

        /// <summary>ランクの低いものから n 個取り出したもの。</summary>
        public Spirits Lowest(int n)
        {
            var a = new int[Ranks];
            for (int r = 0; r < Ranks && n > 0; r++)
            {
                int take = Math.Min(this[r], n);
                a[r] = take;
                n -= take;
            }
            return FromArray(a);
        }

        /// <summary>低いランクから並べたときの index 番目の魂のランク。</summary>
        public int RankAt(int index)
        {
            for (int r = 0; r < Ranks; r++)
            {
                if (index < this[r]) return r;
                index -= this[r];
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public bool Equals(Spirits o) => _y == o._y && _c == o._c && _m == o._m && _v == o._v;
        public override bool Equals(object obj) => obj is Spirits s && Equals(s);
        public override int GetHashCode() => ((_y * 31 + _c) * 31 + _m) * 31 + _v;
        public static bool operator ==(Spirits a, Spirits b) => a.Equals(b);
        public static bool operator !=(Spirits a, Spirits b) => !a.Equals(b);

        /// <summary>"YYCM" 形式(低いランクから)。空なら "".</summary>
        public override string ToString()
        {
            var sb = new StringBuilder(Total);
            for (int r = 0; r < Ranks; r++) sb.Append(Letters[r], this[r]);
            return sb.ToString();
        }
    }
}
