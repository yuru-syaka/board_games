/* =========================================================================
   トップページ（index.html）のゲーム一覧を作るスクリプト
   -------------------------------------------------------------------------
   - index.html の <main id="games"> の中に、ゲームごとの「カード」を並べる。
   - カードの中身は、下の games 配列から作る（HTML を直接書き換える必要はない）。
   - 読み込みは index.html の <script ... defer>。defer なので、HTML を
     読み終わってから実行され、document.getElementById("games") が必ず見つかる。
   - アイコンは SVG を文字列で持っているので、画像ファイルを用意しなくてよい。
     色はネオンの 4 色（シアン #22f3ff / ピンク #ff2fd6 / 紫 #9b5cff / 黄緑 #b6ff3b）。
   ========================================================================= */

// ---------------------------------------------------------------------------
// ゲームのアイコン（SVG）
//   viewBox="0 0 200 120" の中に描いている。表示する大きさは CSS（style.css の
//   .thumb svg）で決めるので、ここでは形と色だけを書く。
//   光って見える効果（drop-shadow）も CSS 側で付けている。
// ---------------------------------------------------------------------------

// ネオン風のおばけ＋三目並べアイコン（ゴースト・マルバツゲーム用）
//   左：おばけ（白）、右：3×3 の盤（シアン）、×（ピンク）、○（黄緑）
const ghostIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <g stroke="#22f3ff" stroke-width="3">
      <line x1="110" y1="20" x2="110" y2="104"/>
      <line x1="138" y1="20" x2="138" y2="104"/>
      <line x1="84" y1="48" x2="166" y2="48"/>
      <line x1="84" y1="76" x2="166" y2="76"/>
    </g>
    <g stroke="#ff2fd6" stroke-width="4">
      <line x1="90" y1="26" x2="104" y2="42"/><line x1="104" y1="26" x2="90" y2="42"/>
      <line x1="144" y1="82" x2="158" y2="98"/><line x1="158" y1="82" x2="144" y2="98"/>
    </g>
    <circle cx="124" cy="62" r="8" stroke="#b6ff3b" stroke-width="4"/>
    <path d="M22 100 V52 a28 28 0 0 1 56 0 V100 l-9 -8 -9 8 -10 -8 -10 8 -9 -8 z"
          stroke="#ffffff" stroke-width="4"/>
    <circle cx="40" cy="54" r="4" fill="#ffffff"/>
    <circle cx="60" cy="54" r="4" fill="#ffffff"/>
  </svg>`;

// ネオン風の大・中・小のおばけアイコン（ゴースト・ゴブル用）
const gobulIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <path d="M18 104 V74 a14 14 0 0 1 28 0 V104 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z"
          stroke="#b6ff3b" stroke-width="3.5"/>
    <circle cx="27" cy="75" r="2.6" fill="#ffffff"/>
    <circle cx="37" cy="75" r="2.6" fill="#ffffff"/>
    <path d="M62 104 V62 a20 20 0 0 1 40 0 V104 l-6.7 -6 -6.6 6 -6.7 -6 -6.7 6 -6.6 -6 z"
          stroke="#ff2fd6" stroke-width="3.5"/>
    <circle cx="75" cy="63" r="3.2" fill="#ffffff"/>
    <circle cx="89" cy="63" r="3.2" fill="#ffffff"/>
    <path d="M118 104 V48 a28 28 0 0 1 56 0 V104 l-9.3 -8 -9.4 8 -9.3 -8 -9.4 8 -9.3 -8 z"
          stroke="#22f3ff" stroke-width="4"/>
    <circle cx="136" cy="50" r="4" fill="#ffffff"/>
    <circle cx="156" cy="50" r="4" fill="#ffffff"/>
  </svg>`;

// ネオン風の「良いおばけ・悪いおばけ・正体不明」アイコン（ゴースト・ブラフ用）
const bluffIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <path d="M14 100 V58 a22 22 0 0 1 44 0 V100 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#22f3ff" stroke-width="3.5"/>
    <circle cx="28" cy="60" r="3.4" fill="#ffffff"/>
    <circle cx="44" cy="60" r="3.4" fill="#ffffff"/>
    <path d="M28 72 q8 6 16 0" stroke="#ffffff" stroke-width="2.5"/>
    <path d="M78 104 V48 a22 22 0 0 1 44 0 V104 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#9b5cff" stroke-width="3.5"/>
    <path d="M91 44 a9 9 0 1 1 13 8 c-3 2 -4 4 -4 8" stroke="#ffffff" stroke-width="3.5"/>
    <circle cx="100" cy="70" r="2.6" fill="#ffffff"/>
    <path d="M142 100 V58 a22 22 0 0 1 44 0 V100 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#ff2fd6" stroke-width="3.5"/>
    <path d="M152 54 l10 5 M176 54 l-10 5" stroke="#ffffff" stroke-width="3"/>
    <circle cx="157" cy="63" r="3" fill="#ffffff"/>
    <circle cx="171" cy="63" r="3" fill="#ffffff"/>
    <path d="M156 76 q8 -6 16 0" stroke="#ffffff" stroke-width="2.5"/>
  </svg>`;

// ネオン風の「特徴の違う4体のおばけが一列にそろう」アイコン（ゴースト・クアルト用）
const quartIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <path d="M18 92 V50 a14 14 0 0 1 28 0 V92 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z"
          stroke="#22f3ff" stroke-width="3.5"/>
    <circle cx="27" cy="52" r="2.6" fill="#ffffff"/>
    <circle cx="37" cy="52" r="2.6" fill="#ffffff"/>
    <path d="M62 92 V68 a14 14 0 0 1 28 0 V92 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z"
          stroke="#ff2fd6" stroke-width="3.5" fill="rgba(255, 47, 214, 0.35)"/>
    <circle cx="71" cy="70" r="2.6" fill="#ffffff"/>
    <circle cx="81" cy="70" r="2.6" fill="#ffffff"/>
    <path d="M110 92 V50 a14 14 0 0 1 28 0 V92 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z"
          stroke="#b6ff3b" stroke-width="3.5" fill="rgba(182, 255, 59, 0.3)"/>
    <circle cx="119" cy="52" r="2.6" fill="#ffffff"/>
    <circle cx="129" cy="52" r="2.6" fill="#ffffff"/>
    <path d="M154 92 V68 a14 14 0 0 1 28 0 V92 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z"
          stroke="#9b5cff" stroke-width="3.5"/>
    <circle cx="163" cy="70" r="2.6" fill="#ffffff"/>
    <circle cx="173" cy="70" r="2.6" fill="#ffffff"/>
    <line x1="14" y1="106" x2="186" y2="106" stroke="#ffffff" stroke-width="3"/>
  </svg>`;

// ネオン風の「9本の旗」アイコン（ゴースト・ライン用）
//   シアン＝自分がとった旗、ピンク＝相手の旗、グレー＝まだ誰もとっていない旗
//   下の黄緑の線は「となり合った3本」
const lineIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <g stroke-width="3">
      <line x1="18" y1="36" x2="18" y2="92" stroke="#5a5480"/>
      <line x1="38" y1="36" x2="38" y2="92" stroke="#ff2fd6"/>
      <line x1="58" y1="36" x2="58" y2="92" stroke="#22f3ff"/>
      <line x1="78" y1="36" x2="78" y2="92" stroke="#22f3ff"/>
      <line x1="98" y1="36" x2="98" y2="92" stroke="#22f3ff"/>
      <line x1="118" y1="36" x2="118" y2="92" stroke="#5a5480"/>
      <line x1="138" y1="36" x2="138" y2="92" stroke="#ff2fd6"/>
      <line x1="158" y1="36" x2="158" y2="92" stroke="#5a5480"/>
      <line x1="178" y1="36" x2="178" y2="92" stroke="#5a5480"/>
    </g>
    <g stroke-width="2.5">
      <path d="M18 36 L31 43 L18 50 z" stroke="#5a5480"/>
      <path d="M38 36 L51 43 L38 50 z" stroke="#ff2fd6" fill="#ff2fd6"/>
      <path d="M58 36 L71 43 L58 50 z" stroke="#22f3ff" fill="#22f3ff"/>
      <path d="M78 36 L91 43 L78 50 z" stroke="#22f3ff" fill="#22f3ff"/>
      <path d="M98 36 L111 43 L98 50 z" stroke="#22f3ff" fill="#22f3ff"/>
      <path d="M118 36 L131 43 L118 50 z" stroke="#5a5480"/>
      <path d="M138 36 L151 43 L138 50 z" stroke="#ff2fd6" fill="#ff2fd6"/>
      <path d="M158 36 L171 43 L158 50 z" stroke="#5a5480"/>
      <path d="M178 36 L191 43 L178 50 z" stroke="#5a5480"/>
    </g>
    <path d="M52 100 V106 H104 V100" stroke="#b6ff3b" stroke-width="3"/>
  </svg>`;

// ネオン風の「おばけと 4 色の魂」アイコン（センチュリー・ゴースト用）
//   左：おばけ（白）、右：黄 → 青 → 桃 → 紫 と、強化されて大きくなっていく魂
const centuryIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <path d="M18 104 V58 a26 26 0 0 1 52 0 V104 l-8.7 -7 -8.6 7 -8.7 -7 -8.7 7 -8.6 -7 z"
          stroke="#ffffff" stroke-width="4"/>
    <circle cx="35" cy="60" r="4" fill="#ffffff"/>
    <circle cx="53" cy="60" r="4" fill="#ffffff"/>
    <circle cx="98" cy="96" r="9" stroke="#ffd84a" stroke-width="3.5" fill="rgba(255, 216, 74, 0.3)"/>
    <circle cx="126" cy="78" r="10" stroke="#22f3ff" stroke-width="3.5" fill="rgba(34, 243, 255, 0.3)"/>
    <circle cx="154" cy="58" r="11" stroke="#ff2fd6" stroke-width="3.5" fill="rgba(255, 47, 214, 0.3)"/>
    <circle cx="182" cy="36" r="12" stroke="#9b5cff" stroke-width="3.5" fill="rgba(155, 92, 255, 0.35)"/>
    <path d="M108 90 L114 85 M136 70 L142 66 M164 48 L170 44" stroke="#b6ff3b" stroke-width="3"/>
  </svg>`;

// ネオン風の「青おばけ・裏向きのおばけ・赤おばけ」アイコン（ゴースト・リバーサ用）
//   左：青（シアン）おばけ＋↔（横の列）、まん中：裏向きで眠っているおばけ（紫の点線）、
//   右：赤（ピンク）おばけ＋↕（縦の列）、上：裏返しの矢印（黄緑）
const reversaIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <path d="M66 24 q34 -18 68 0" stroke="#b6ff3b" stroke-width="3"/>
    <path d="M134 24 l-8 -1 M134 24 l-3 -7" stroke="#b6ff3b" stroke-width="3"/>
    <path d="M14 100 V58 a22 22 0 0 1 44 0 V100 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#22f3ff" stroke-width="3.5"/>
    <circle cx="28" cy="60" r="3.4" fill="#ffffff"/>
    <circle cx="44" cy="60" r="3.4" fill="#ffffff"/>
    <path d="M26 76 h20 M26 76 l4 -3 M26 76 l4 3 M46 76 l-4 -3 M46 76 l-4 3" stroke="#ffffff" stroke-width="2.5"/>
    <path d="M78 104 V56 a22 22 0 0 1 44 0 V104 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#9b5cff" stroke-width="3.5" stroke-dasharray="6 4"/>
    <path d="M88 60 q5 4 10 0 M102 60 q5 4 10 0" stroke="#ffffff" stroke-width="2.5"/>
    <path d="M142 100 V58 a22 22 0 0 1 44 0 V100 l-7.3 -6 -7.4 6 -7.3 -6 -7.3 6 -7.4 -6 z"
          stroke="#ff2fd6" stroke-width="3.5"/>
    <circle cx="157" cy="60" r="3.4" fill="#ffffff"/>
    <circle cx="171" cy="60" r="3.4" fill="#ffffff"/>
    <path d="M164 68 v18 M164 68 l-3 4 M164 68 l3 4 M164 86 l-3 -4 M164 86 l3 -4" stroke="#ffffff" stroke-width="2.5"/>
  </svg>`;

// ネオン風の「額縁の絵とおばけ画家・せり値」アイコン（ゴースト・トレンド用）
//   左：額縁に入ったおばけの絵（シアン）、まん中：右上がりの流行の矢印（黄緑）、
//   右：札を上げてせるおばけ（ピンク）と値札（紫）
const trendIcon = `
  <svg viewBox="0 0 200 120" fill="none" stroke-linecap="round" stroke-linejoin="round">
    <rect x="12" y="22" width="64" height="78" rx="4" stroke="#22f3ff" stroke-width="3.5"/>
    <rect x="20" y="30" width="48" height="62" rx="2" stroke="#22f3ff" stroke-width="2" opacity="0.6"/>
    <path d="M30 86 V60 a14 14 0 0 1 28 0 V86 l-4.7 -4 -4.6 4 -4.7 -4 -4.7 4 -4.6 -4 z" stroke="#ffffff" stroke-width="3"/>
    <circle cx="39" cy="61" r="2.6" fill="#ffffff"/>
    <circle cx="49" cy="61" r="2.6" fill="#ffffff"/>
    <path d="M84 92 L100 74 L110 82 L128 56" stroke="#b6ff3b" stroke-width="3.5"/>
    <path d="M128 56 l-9 1 M128 56 l-1 9" stroke="#b6ff3b" stroke-width="3.5"/>
    <path d="M140 104 V64 a20 20 0 0 1 40 0 V104 l-6.7 -6 -6.6 6 -6.7 -6 -6.7 6 -6.6 -6 z" stroke="#ff2fd6" stroke-width="3.5"/>
    <circle cx="153" cy="65" r="3.2" fill="#ffffff"/>
    <circle cx="167" cy="65" r="3.2" fill="#ffffff"/>
    <path d="M180 74 L190 40" stroke="#ff2fd6" stroke-width="3"/>
    <rect x="178" y="18" width="20" height="22" rx="3" stroke="#9b5cff" stroke-width="3" fill="rgba(155, 92, 255, 0.35)"/>
  </svg>`;

// ---------------------------------------------------------------------------
// ゲームの一覧（この順番でトップページに並ぶ）
//   ▼ ゲームを追加するときは、ここに { ... } を1つ増やすだけ
//
//   title       … カードの見出し（ゲーム名）
//   description … カードの説明文
//   url         … 「▶ PLAY」の移動先
//                   サイト内のプレイページなら "play/<slug>/"（同じタブで開く）
//                   外部サイトなら "https://..."（別タブで開く）
//   detail      … 「遊び方・ルールを見る」の移動先（紹介ページ）。"" ならリンクを出さない
//   image       … "images/xxx.png" を指定すると、アイコンの代わりに画像を表示する
//   icon        … 上で定義した SVG アイコン（image が空のときに使う）
//   players     … 遊べる人数 [最小, 最大]。CPU の席も数える（例: 2 人用は [2, 2]、3〜5 人は [3, 5]）
//                 トップの「遊ぶ人数」の絞り込みと、カードの人数の表示に使う
//   comingSoon  … true にすると「Coming soon」のカードになる（url などは不要）
//
//   ※ <slug> はプレイページのフォルダ名（web/play/<slug>/）。Unity でビルドするときの
//     出力先と同じ名前にする（unity/README.md を参照）。
// ---------------------------------------------------------------------------
const games = [
  {
    title: "ゴースト・マルバツゲーム",
    description: "自分の置いた駒が消えていくマルバツゲーム。",
    url: "play/ghost-tic-tac-toe/", // サイト内のプレイページ（外部サイトなら https:// から書く）
    detail: "games/ghost-tic-tac-toe.html", // 詳細ページ（なければ "" ）
    players: [2, 2], // 遊べる人数 [最小, 最大]（CPU の席も数える。トップの人数の絞り込みとカードの表示に使う）
    image: "",
    icon: ghostIcon
  },
  {
    title: "ゴースト・ゴブル",
    description: "大・中・小のコマを置いたり動かしたりして、小さいコマにかぶせて戦う三目並べ。動かしたコマの下から相手のコマが現れることも。",
    url: "play/ghost-gobble/",
    detail: "games/ghost-gobble.html",
    players: [2, 2],
    image: "",
    icon: gobulIcon
  },
  {
    title: "ゴースト・ブラフ",
    description: "6×6の盤で、良いゴーストと悪いゴーストを動かして戦う心理戦。相手のゴーストの正体は、取るまでわからない。",
    url: "play/ghost-bluff/",
    detail: "games/ghost-bluff.html",
    players: [2, 2],
    image: "",
    icon: bluffIcon
  },
  {
    title: "ゴースト・クアルト",
    description: "相手が選んだゴーストを置き、自分も相手に置かせるゴーストを選んで渡す。4体そろえたら勝ち。相手を「渡せるゴーストがない」状況に追い込め。",
    url: "play/ghost-quarto/",
    detail: "games/ghost-quarto.html",
    players: [2, 2],
    image: "",
    icon: quartIcon
  },
  {
    title: "ゴースト・ライン",
    description: "並んだ9本の旗をめぐって戦う陣取りゲーム。どこでもいいから5本とるか、となり合った3本をまとめてとれば勝ち。",
    url: "play/ghost-line/",
    detail: "games/ghost-line.html",
    players: [2, 2],
    image: "",
    icon: lineIcon
  },
  {
    title: "センチュリー・ゴースト",
    description: "術カードで4色の魂を集め、交換し、強化して、おばけを呼び出すカードゲーム。術を覚えるほど魂の集まり方が強くなっていく。",
    url: "play/century-ghost/",
    detail: "games/century-ghost.html",
    players: [2, 2],
    image: "",
    icon: centuryIcon
  },
  {
    title: "ゴースト・リバーサ",
    description: "3×3の盤におばけを表か裏で置き、横や縦の列をまとめて裏返す。自分の同じ色を3体表にしたら勝ち。相手の狙う色は最後まで分からない。",
    url: "play/ghost-reversa/",
    detail: "games/ghost-reversa.html",
    players: [2, 2],
    image: "",
    icon: reversaIcon
  },
  {
    title: "ゴースト・トレンド",
    description: "おばけ画家5人の絵を5種類の競りで売り買いし、流行を読んでお金を増やす3〜5人のゲーム。人気の画家の絵ほど高く売れる。",
    url: "play/ghost-trend/",
    detail: "games/ghost-trend.html",
    players: [3, 5],
    image: "",
    icon: trendIcon
  }
];

// ---------------------------------------------------------------------------
// カードを作って <main id="games"> に追加する
//   1 件ごとに <article class="card"> を作り、innerHTML で中身を入れる。
//   カードの枠の色（シアン → ピンク → 紫 の繰り返し）は CSS の
//   .card:nth-child(...) で決まるので、ここでは指定しない。
// ---------------------------------------------------------------------------
const container = document.getElementById("games");

// 人数の表示（[2, 2] →「2人」、[3, 5] →「3〜5人」）
function playersText(p) {
  return p[0] === p[1] ? `${p[0]}人` : `${p[0]}〜${p[1]}人`;
}

for (const g of games) {
  const card = document.createElement("article");
  // 人数の絞り込みで使う（Coming soon のカードは人数がないので、絞り込むと隠れる）
  if (g.players) {
    card.dataset.minPlayers = g.players[0];
    card.dataset.maxPlayers = g.players[1];
  }

  if (g.comingSoon) {
    // 開発中のゲーム：「?」のサムネイルと、押せない「COMING SOON」ボタン
    card.className = "card soon";
    card.innerHTML = `
      <div class="thumb"><span>?</span></div>
      <div class="card-body">
        <h2>Coming soon</h2>
        <p>新作を開発中です。お楽しみに！</p>
        <span class="play">COMING SOON</span>
      </div>`;
  } else {
    // 公開中のゲーム：サムネイル、名前、説明、PLAY ボタン、遊び方へのリンク
    card.className = "card";
    // 画像があれば画像、なければ SVG アイコンをサムネイルにする
    const thumb = g.image ? `<img src="${g.image}" alt="${g.title}" />` : (g.icon || "");
    // 外部サイト（unityroom）は別タブ、サイト内のプレイページは同じタブで開く
    const external = /^https?:/.test(g.url) ? ` target="_blank" rel="noopener"` : "";
    card.innerHTML = `
      <div class="thumb">${thumb}</div>
      <div class="card-body">
        <h2>${g.title}</h2>
        ${g.players ? `<span class="players-badge">${playersText(g.players)}</span>` : ""}
        <p>${g.description}</p>
        <a class="play" href="${g.url}"${external}>▶ PLAY</a>
        ${g.detail ? `<a class="detail" href="${g.detail}">遊び方・ルールを見る</a>` : ""}
      </div>`;
  }
  // 作ったカードをページに追加する（games 配列の順番で並ぶ）
  container.appendChild(card);
}

// ---------------------------------------------------------------------------
// 「遊ぶ人数」の絞り込み
//   - ボタンは games 配列の players から作る（ゲームを足せば、人数のボタンも自動で増える）。
//   - 選んだ人数で遊べるゲームだけを残し、ほかは hidden で隠す。カードは消さないので、
//     カードの色（.card:nth-child）はゲームごとに変わらない。
//   - 選んだ人数は URL の ?players=3 にも書く（そのまま共有・ブックマークできる）。
//   - 一覧（<main id="games">）の直前に置く。index.html は書き換えない。
// ---------------------------------------------------------------------------
(function setUpPlayerFilter() {
  const counts = new Set();
  for (const g of games) {
    if (!g.players) continue;
    for (let n = g.players[0]; n <= g.players[1]; n++) counts.add(n);
  }
  const options = [...counts].sort((a, b) => a - b);
  if (options.length < 2) return; // 人数が 1 種類しかなければ、絞り込む意味がない

  const bar = document.createElement("nav");
  bar.className = "player-filter";
  bar.setAttribute("aria-label", "遊ぶ人数で絞り込む");
  bar.innerHTML = `
    <span class="player-filter-label">遊ぶ人数</span>
    <div class="player-filter-buttons"></div>
    <p class="player-filter-note">どのゲームも、足りない席は CPU が入るので 1 人でも遊べます。</p>`;
  const buttons = bar.querySelector(".player-filter-buttons");
  const empty = document.createElement("p");
  empty.className = "player-filter-empty";
  empty.hidden = true;

  // 「すべて」（0）と、人数ごとのボタン
  for (const n of [0, ...options]) {
    const b = document.createElement("button");
    b.type = "button";
    b.dataset.players = n;
    b.textContent = n === 0 ? "すべて" : `${n}人`;
    b.addEventListener("click", () => apply(n, true));
    buttons.appendChild(b);
  }

  container.parentNode.insertBefore(bar, container);
  container.parentNode.insertBefore(empty, container.nextSibling);

  function apply(n, fromClick) {
    let shown = 0;
    for (const card of container.children) {
      const min = Number(card.dataset.minPlayers);
      const max = Number(card.dataset.maxPlayers);
      const ok = n === 0 || (card.dataset.minPlayers !== undefined && min <= n && n <= max);
      card.hidden = !ok;
      if (ok) shown++;
    }
    for (const b of buttons.children) b.setAttribute("aria-pressed", String(Number(b.dataset.players) === n));
    empty.hidden = shown > 0;
    empty.textContent = shown > 0 ? "" : `${n}人で遊べるゲームは、まだありません。`;
    if (fromClick) {
      // URL を書き換える（ページは読み込み直さない）
      const url = new URL(location.href);
      if (n === 0) url.searchParams.delete("players");
      else url.searchParams.set("players", n);
      history.replaceState(null, "", url);
    }
  }

  // URL の ?players= を読んで、最初の表示を決める（知らない数なら「すべて」）
  const fromUrl = Number(new URLSearchParams(location.search).get("players"));
  apply(options.includes(fromUrl) ? fromUrl : 0, false);
})();
