/* =========================================================================
   game-list.js … 8 本のゲームの一覧（ランキング・プロフィール・マイページで使う。ステップ 5-4）
   -------------------------------------------------------------------------
   - id   … 対戦サーバー・API でのゲームの ID（server/BoardGames.Server/Games と同じ）
   - name … 画面に出す名前
   - slug … 紹介ページ（/games/<slug>.html）とプレイページ（/play/<slug>/）の名前
   ゲームを足したら、ここにも足す。
   ゴースト・トレンドは人数ごとにマッチング・レートが分かれる（ID も人数ごと）ので、3 件に分けて載せる。
   ========================================================================= */
window.BOARDPIA_GAMES = [
  { id: 'vanish', name: 'ゴースト・マルバツゲーム', slug: 'ghost-tic-tac-toe' },
  { id: 'gobblet', name: 'ゴースト・ゴブル', slug: 'ghost-gobble' },
  { id: 'geister', name: 'ゴースト・ブラフ', slug: 'ghost-bluff' },
  { id: 'quarto', name: 'ゴースト・クアルト', slug: 'ghost-quarto' },
  { id: 'battleline', name: 'ゴースト・ライン', slug: 'ghost-line' },
  { id: 'centuryghost', name: 'センチュリー・ゴースト', slug: 'century-ghost' },
  { id: 'ghostreversa', name: 'ゴースト・リバーサ', slug: 'ghost-reversa' },
  { id: 'ghosttrendthree', name: 'ゴースト・トレンド（3人）', slug: 'ghost-trend' },
  { id: 'ghosttrendfour', name: 'ゴースト・トレンド（4人）', slug: 'ghost-trend' },
  { id: 'ghosttrendfive', name: 'ゴースト・トレンド（5人）', slug: 'ghost-trend' },
];

/** ゲームの ID から一覧の 1 件を探す（無ければ null）。 */
window.boardpiaGame = function (id) {
  return window.BOARDPIA_GAMES.find(function (g) { return g.id === id; }) || null;
};

/** 名前を決めていない人の表示名。 */
window.BOARDPIA_NO_NAME = '名前未設定';
