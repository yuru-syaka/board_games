/* ログインのあとに戻ってくるページ（auth/callback.html）の処理。
   トークンに引き換えたら、ログインする前のページに戻る（履歴には残さない）。 */
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
    .then(function (returnTo) { location.replace(returnTo); })
    .catch(function (e) { fail(e.message); });
})();
