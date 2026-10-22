/* プロフィールのページ（/player/?id=<ユーザー ID>）。API から読んで表を作る（ステップ 5-4）。
   マイページからも同じ形の表を作るので、表を作る関数を window.boardpiaStatsRows として出しておく。 */
(function () {
  'use strict';

  /** ゲームごとの成績の行を tbody に入れる（ゲームは一覧の順。ランキングへのリンク付き）。 */
  function statsRows(tbody, games) {
    const byId = {};
    (games || []).forEach(function (g) { byId[g.game] = g; });
    let count = 0;
    window.BOARDPIA_GAMES.forEach(function (game) {
      const s = byId[game.id];
      if (!s) return;
      count++;
      const tr = document.createElement('tr');
      const nameCell = document.createElement('td');
      nameCell.className = 'name';
      const link = document.createElement('a');
      link.href = '/ranking/?game=' + game.id;
      link.textContent = game.name;
      nameCell.appendChild(link);
      tr.appendChild(nameCell);
      [s.ratedGames > 0 ? String(s.rating) : '－', s.ratedGames, s.games, s.wins, s.losses, s.draws].forEach(function (v, i) {
        const td = document.createElement('td');
        if (i === 0) td.className = 'rating';
        td.textContent = String(v);
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    return count;
  }
  window.boardpiaStatsRows = statsRows;

  const status = document.getElementById('player-status');
  if (!status) return; // マイページから読み込まれたとき（表を作る関数だけを使う）

  const heading = document.getElementById('player-name');
  const table = document.getElementById('player-table');
  const body = document.getElementById('player-body');
  const id = new URLSearchParams(location.search).get('id') || '';

  async function load() {
    if (!/^[0-9A-Za-z-]{1,64}$/.test(id)) {
      status.textContent = 'プレイヤーが見つかりません。';
      return;
    }
    let res;
    try {
      const r = await fetch('/api/players/' + encodeURIComponent(id), { headers: { Accept: 'application/json' } });
      res = { status: r.status, data: await r.json().catch(function () { return null; }) };
    } catch (e) {
      res = { status: 0, data: null };
    }
    if (res.status !== 200 || !res.data) {
      status.textContent = 'プロフィールを読み込めませんでした。時間をおいて、ページを読み込み直してください。';
      status.classList.add('error');
      return;
    }
    const player = res.data.player;
    if (!player) {
      status.textContent = 'プレイヤーが見つかりません（アカウントが削除された可能性があります）。';
      return;
    }
    const name = player.name || window.BOARDPIA_NO_NAME;
    heading.textContent = name;
    document.title = name + ' のプロフィール - BOARD PIA';
    if (statsRows(body, player.games) === 0) {
      status.textContent = 'まだオンライン対戦の記録がありません。';
      return;
    }
    status.hidden = true;
    table.hidden = false;
  }

  load();
})();
