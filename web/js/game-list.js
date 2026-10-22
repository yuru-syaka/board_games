/* =========================================================================
   game-list.js … 6 本のゲームの一覧（ランキング・プロフィール・マイページで使う。ステップ 5-4）
   -------------------------------------------------------------------------
   - id   … 対戦サーバー・API でのゲームの ID（server/BoardGames.Server/Games と同じ）
   - name … 画面に出す名前
   - slug … 紹介ページ（/games/<slug>.html）とプレイページ（/play/<slug>/）の名前
   ゲームを足したら、ここにも足す。
   ========================================================================= */
window.BOARDPIA_GAMES = [
  { id: 'vanish', name: 'ゴースト・マルバツゲーム', slug: 'ghost-tic-tac-toe' },
  { id: 'gobblet', name: 'ゴースト・ゴブル', slug: 'ghost-gobble' },
  { id: 'geister', name: 'ゴースト・ブラフ', slug: 'ghost-bluff' },
  { id: 'quarto', name: 'ゴースト・クアルト', slug: 'ghost-quarto' },
  { id: 'battleline', name: 'ゴースト・ライン', slug: 'ghost-line' },
  { id: 'centuryghost', name: 'センチュリー・ゴースト', slug: 'century-ghost' },
];

/** ゲームの ID から一覧の 1 件を探す（無ければ null）。 */
window.boardpiaGame = function (id) {
  return window.BOARDPIA_GAMES.find(function (g) { return g.id === id; }) || null;
};

/** 名前を決めていない人の表示名。 */
window.BOARDPIA_NO_NAME = '名前未設定';
