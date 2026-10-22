/* マイページ（/mypage/）。名前を決める・変える、自分の成績、ログアウト、アカウントの削除（ステップ 5-4）。
   API はログインのトークン付きで呼ぶ（BoardPiaAuth.fetchApi）。名前は文字として入れる（textContent / value）。 */
(function () {
  'use strict';
  const auth = window.BoardPiaAuth;
  const $ = function (id) { return document.getElementById(id); };
  const params = new URLSearchParams(location.search);

  /** 戻り先は、このサイトの中のパスだけ（よそのサイトに飛ばされないように）。 */
  function safePath(p) {
    return typeof p === 'string' && p.charAt(0) === '/' && p.charAt(1) !== '/' && p.charAt(1) !== '\\' ? p : null;
  }

  function showSignedOut() {
    $('mypage-status').hidden = true;
    $('signed-out').hidden = false;
    $('login-button').addEventListener('click', function () { auth.signIn('/mypage/'); });
  }

  function setMessage(el, text, kind) {
    el.textContent = text;
    el.className = 'form-message' + (kind ? ' ' + kind : '');
  }

  async function load() {
    if (!auth || !auth.isConfigured() || !auth.isSignedIn()) return showSignedOut();
    const res = await auth.fetchApi('/api/me');
    if (res.status === 401) {
      auth.forget(); // ログインの期限が切れていた
      return showSignedOut();
    }
    if (res.status !== 200 || !res.data || !res.data.player) {
      $('mypage-status').textContent = '読み込めませんでした。時間をおいて、ページを読み込み直してください。';
      $('mypage-status').classList.add('error');
      return;
    }
    const player = res.data.player;
    $('mypage-status').hidden = true;
    $('signed-in').hidden = false;

    // 名前
    const input = $('name-input');
    input.value = player.name || '';
    const next = safePath(params.get('next'));
    $('welcome').hidden = !!player.name; // 名前がまだなら、まず決めてもらう

    // 成績
    const count = window.boardpiaStatsRows($('stats-body'), player.games);
    $('stats-table').hidden = count === 0;
    $('stats-empty').hidden = count > 0;
    $('profile-link').href = '/player/?id=' + encodeURIComponent(player.id);

    // ログアウト
    const claims = auth.getClaims();
    $('signed-in-as').textContent = claims && claims.email ? claims.email + ' でログインしています。' : '';
    $('logout-button').addEventListener('click', function () { auth.signOut(); });

    // 名前の保存
    $('name-form').addEventListener('submit', async function (e) {
      e.preventDefault();
      const save = $('name-save');
      save.disabled = true;
      setMessage($('name-message'), '保存しています…');
      const r = await auth.fetchApi('/api/me/name', { method: 'PUT', body: { name: input.value } });
      save.disabled = false;
      if (r.status === 200 && r.data) {
        input.value = r.data.name;
        setMessage($('name-message'), '保存しました。ランキングへの反映には 1 分ほどかかることがあります。', 'ok');
        $('welcome').hidden = true;
        if (next) {
          $('next-anchor').href = next;
          $('next-link').hidden = false;
        }
      } else if (r.status === 401) {
        setMessage($('name-message'), 'ログインの期限が切れました。もう一度ログインしてください。', 'error');
      } else {
        setMessage($('name-message'), (r.data && r.data.message) || '保存できませんでした。時間をおいてお試しください。', 'error');
      }
    });

    // アカウントの削除
    const confirmBox = $('delete-confirm');
    const del = $('delete-button');
    confirmBox.addEventListener('change', function () { del.disabled = !confirmBox.checked; });
    del.addEventListener('click', async function () {
      if (!confirmBox.checked) return;
      if (!window.confirm('アカウントを削除します。元に戻せません。よろしいですか？')) return;
      del.disabled = true;
      setMessage($('delete-message'), '削除しています…');
      const r = await auth.fetchApi('/api/me', { method: 'DELETE' });
      if (r.status === 200) {
        setMessage($('delete-message'), 'アカウントを削除しました。トップページに戻ります。', 'ok');
        setTimeout(function () { auth.signOut(); }, 1500); // ログイン画面側の記憶も消してトップへ
      } else {
        del.disabled = false;
        setMessage($('delete-message'), (r.data && r.data.message) || '削除できませんでした。時間をおいてお試しください。', 'error');
      }
    });
  }

  load();
})();
