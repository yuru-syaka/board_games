// ネオン風のおばけ＋三目並べアイコン（画像ファイル不要）
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

// ▼ ゲームを追加するときは、ここに { ... } を1つ増やすだけ
//   image に "images/xxx.png" を指定するとアイコンの代わりに画像が表示されます
const games = [
  {
    title: "ゴースト・マルバツゲーム",
    description: "自分の置いた駒が消えていくマルバツゲーム。",
    url: "https://unityroom.com/games/ghost_tic_tac_toe",
    detail: "games/ghost-tic-tac-toe.html", // 詳細ページ（なければ "" ）
    image: "",
    icon: ghostIcon
  },
  {
    title: "ゴースト・ゴブル",
    description: "大・中・小のコマを置いたり動かしたりして、小さいコマにかぶせて戦う三目並べ。動かしたコマの下から相手のコマが現れることも。",
    url: "https://unityroom.com/games/ghost_gobul",
    image: "",
    icon: gobulIcon
  },
  {
    title: "ゴースト・ブラフ",
    description: "6×6の盤で、良いゴーストと悪いゴーストを動かして戦う心理戦。相手のゴーストの正体は、取るまでわからない。",
    url: "https://unityroom.com/games/ghost_bluff",
    image: "",
    icon: bluffIcon
  },
  {
    title: "ゴースト・クアルト",
    description: "相手が選んだゴーストを置き、自分も相手に置かせるゴーストを選んで渡す。4体そろえたら勝ち。相手を「渡せるゴーストがない」状況に追い込め。",
    url: "https://unityroom.com/games/ghost_quart",
    image: "",
    icon: quartIcon
  },
  {
    title: "ゴースト・ライン",
    description: "並んだ9本の旗をめぐって戦う陣取りゲーム。どこでもいいから5本とるか、となり合った3本をまとめてとれば勝ち。",
    url: "https://unityroom.com/games/ghost_line",
    image: "",
    icon: lineIcon
  }
];

const container = document.getElementById("games");
for (const g of games) {
  const card = document.createElement("article");

  if (g.comingSoon) {
    card.className = "card soon";
    card.innerHTML = `
      <div class="thumb"><span>?</span></div>
      <div class="card-body">
        <h2>Coming soon</h2>
        <p>新作を開発中です。お楽しみに！</p>
        <span class="play">COMING SOON</span>
      </div>`;
  } else {
    card.className = "card";
    const thumb = g.image ? `<img src="${g.image}" alt="${g.title}" />` : (g.icon || "");
    card.innerHTML = `
      <div class="thumb">${thumb}</div>
      <div class="card-body">
        <h2>${g.title}</h2>
        <p>${g.description}</p>
        <a class="play" href="${g.url}" target="_blank" rel="noopener">▶ PLAY</a>
      </div>`;
  }
  container.appendChild(card);
}
