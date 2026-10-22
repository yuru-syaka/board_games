/* ログインのあとに戻ってくるページ（auth/callback.html）の処理。
   トークンに引き換えたら、ログインする前のページに戻る（履歴には残さない）。
   名前をまだ決めていなければ（初めてのログイン）、マイページで名前を決めてもらってから戻れるようにする（ステップ 5-4）。 */
(function () {
  'use strict';
  const status = document.getElementById('auth-status');
  const back = document.getElementById('auth-back');

  function fail(message) {
    status.textContent = message;
    back.hidden = false;
  }

  if (!window.BoardPiaAuth) {
    fail('ログインの準備ができませんでした。ページを読み込み直してください。');
    return;
  }
  window.BoardPiaAuth.handleCallback()
    .then(async function (returnTo) {
      const me = await window.BoardPiaAuth.fetchApi('/api/me');
      if (me.status === 200 && me.data && me.data.player && !me.data.player.name && !returnTo.startsWith('/mypage/')) {
        location.replace('/mypage/?welcome=1&next=' + encodeURIComponent(returnTo));
        return;
      }
      location.replace(returnTo);
    })
    .catch(function (e) { fail(e.message); });
})();
