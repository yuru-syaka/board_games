/* ランキングのページ（/ranking/?game=<ID>）。API から読んで表を作る（ステップ 5-4）。
   名前は利用者が決めたものなので、HTML としてではなく文字として入れる（textContent）。 */
(function () {
  'use strict';
  const games = window.BOARDPIA_GAMES;
  const params = new URLSearchParams(location.search);
  const current = window.boardpiaGame(params.get('game')) || games[0];

  const tabs = document.getElementById('game-tabs');
  const title = document.getElementById('ranking-title');
  const status = document.getElementById('ranking-status');
  const table = document.getElementById('ranking-table');
  const body = document.getElementById('ranking-body');

  // ゲームの切り替え
  games.forEach(function (g) {
    const li = document.createElement('li');
    const a = document.createElement('a');
    a.href = '?game=' + g.id;
    a.textContent = g.name;
    if (g.id === current.id) a.setAttribute('aria-current', 'true');
    li.appendChild(a);
    tabs.appendChild(li);
  });
  title.textContent = current.name;
  document.title = current.name + ' ランキング - BOARD PIA';

  function cell(row, text, className) {
    const td = document.createElement('td');
    if (className) td.className = className;
    td.textContent = text;
    row.appendChild(td);
    return td;
  }

  async function load() {
    // ランキングはだれでも見られる（ログインのトークンは付けない: CloudFront のキャッシュをみんなで使うため）
    let res;
    try {
      const r = await fetch('/api/ranking/' + encodeURIComponent(current.id), { headers: { Accept: 'application/json' } });
      res = { status: r.status, data: await r.json().catch(function () { return null; }) };
    } catch (e) {
      res = { status: 0, data: null };
    }
    if (res.status !== 200 || !res.data || !Array.isArray(res.data.players)) {
      status.textContent = 'ランキングを読み込めませんでした。時間をおいて、ページを読み込み直してください。';
      status.classList.add('error');
      return;
    }
    const players = res.data.players;
    if (players.length === 0) {
      status.textContent = 'まだ誰も載っていません。ログインしてランダムマッチで遊ぶと、ここに載ります。';
      return;
    }
    const me = window.BoardPiaAuth && window.BoardPiaAuth.getClaims();
    players.forEach(function (p) {
      const tr = document.createElement('tr');
      if (me && me.sub === p.id) tr.className = 'me';
      cell(tr, String(p.rank), 'rank');
      const nameCell = cell(tr, '', 'name');
      const link = document.createElement('a');
      link.href = '/player/?id=' + encodeURIComponent(p.id);
      link.textContent = p.name || window.BOARDPIA_NO_NAME;
      nameCell.appendChild(link);
      cell(tr, String(p.rating), 'rating');
      cell(tr, String(p.ratedGames));
      cell(tr, String(p.wins));
      cell(tr, String(p.losses));
      cell(tr, String(p.draws));
      body.appendChild(tr);
    });
    status.hidden = true;
    table.hidden = false;
  }

  load();
})();
