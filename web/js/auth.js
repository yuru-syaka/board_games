/* =========================================================================
   auth.js … ログイン（Amazon Cognito）。ステップ 5
   -------------------------------------------------------------------------
   - ログイン画面は AWS の画面（https://auth.boardgamepia.com）。パスワードはこのサイトでは扱わない。
   - 流れ（認可コード ＋ PKCE。ブラウザに秘密を置かない方式）:
       1. 「ログイン」→ 合言葉（verifier）を作って sessionStorage に置き、ログイン画面へ移る
       2. ログイン画面から /auth/callback.html?code=… に戻る
       3. code と合言葉をトークンに引き換え、localStorage に保存して、元のページに戻る
   - ID トークンは 1 時間で切れるので、getIdToken() が自動で更新する（更新用のトークンは 30 日）。
   - 設定（ドメインとクライアント ID）は js/auth-config.js（デプロイのときに WebStack が書き出す）。
     読み込めないとき（手元の確認など）は、ログインのリンクを出さないだけで、ほかは今までどおり動く。
   - 読み込み: <script src="/js/auth-config.js" defer></script><script src="/js/auth.js" defer></script>
   - ほかのスクリプト（ゲームなど）からは window.BoardPiaAuth を使う。API は BoardPiaAuth.fetchApi('/api/…') で呼ぶ。
   ========================================================================= */
(function () {
  'use strict';

  const config = window.BOARDPIA_AUTH || null;
  const STORE_KEY = 'boardpia.auth';        // localStorage: トークン
  const PKCE_KEY = 'boardpia.auth.pkce';    // sessionStorage: ログイン中の合言葉と戻り先
  const CALLBACK_PATH = '/auth/callback.html';
  // 期限の少し前に更新する（通信の時間と、端末の時計のずれの分）
  const EARLY_REFRESH_MS = 2 * 60 * 1000;

  const authOrigin = config ? 'https://' + config.domain : '';
  const redirectUri = location.origin + CALLBACK_PATH;

  // ---------- 保存（使えないブラウザでも止まらないように） ----------
  function read(storage, key) {
    try { return JSON.parse(storage.getItem(key) || 'null'); } catch (e) { return null; }
  }
  function write(storage, key, value) {
    try {
      if (value === null) storage.removeItem(key);
      else storage.setItem(key, JSON.stringify(value));
    } catch (e) { /* 保存できなくても続ける（ログインが残らないだけ） */ }
  }

  // ---------- 小さな道具 ----------
  function randomString(bytes) {
    const a = new Uint8Array(bytes);
    crypto.getRandomValues(a);
    return base64Url(a);
  }
  function base64Url(bytes) {
    let s = '';
    bytes.forEach(function (b) { s += String.fromCharCode(b); });
    return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
  async function sha256(text) {
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
    return base64Url(new Uint8Array(digest));
  }
  /** JWT の中身（署名は確かめない。表示に使うだけ。サーバーは必ず署名を確かめる） */
  function decodeJwt(token) {
    try {
      const part = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const bin = atob(part + '==='.slice((part.length + 3) % 4));
      const bytes = Uint8Array.from(bin, function (c) { return c.charCodeAt(0); });
      return JSON.parse(new TextDecoder().decode(bytes));
    } catch (e) { return null; }
  }
  /** 戻り先は、このサイトの中のパスだけを受け付ける（よそのサイトに飛ばされないように） */
  function safeReturnPath(path) {
    return typeof path === 'string' && path.charAt(0) === '/' && path.charAt(1) !== '/' && path.charAt(1) !== '\\'
      ? path : '/';
  }

  async function tokenRequest(params) {
    params.client_id = config.clientId;
    const res = await fetch(authOrigin + '/oauth2/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams(params).toString(),
    });
    const body = await res.json().catch(function () { return {}; });
    if (!res.ok) {
      const err = new Error(body.error || ('token_' + res.status));
      err.code = body.error || '';
      throw err;
    }
    return body;
  }

  function saveTokens(body, previousRefreshToken) {
    const tokens = {
      idToken: body.id_token,
      accessToken: body.access_token,
      // 更新のときは新しい更新用トークンが返らないので、前のものを使い続ける
      refreshToken: body.refresh_token || previousRefreshToken,
      expiresAt: Date.now() + (body.expires_in || 3600) * 1000,
    };
    write(localStorage, STORE_KEY, tokens);
    return tokens;
  }

  // ---------- 外から使うもの ----------
  let refreshing = null; // 同時に何度も更新しないように

  const auth = {
    /** 設定が読めて、ログインが使えるか */
    isConfigured: function () { return !!config; },

    /** ログインしているか（トークンの期限は見ない。更新できるかは getIdToken() で分かる） */
    isSignedIn: function () { return !!read(localStorage, STORE_KEY); },

    /** ID トークンの中身（sub = ユーザー ID、email）。ログインしていなければ null */
    getClaims: function () {
      const t = read(localStorage, STORE_KEY);
      return t ? decodeJwt(t.idToken) : null;
    },

    /** 期限内の ID トークン。必要なら更新する。ログインしていない・更新できないときは null */
    getIdToken: async function () {
      const t = read(localStorage, STORE_KEY);
      if (!config || !t) return null;
      if (Date.now() < t.expiresAt - EARLY_REFRESH_MS) return t.idToken;
      if (!t.refreshToken) { write(localStorage, STORE_KEY, null); return null; }
      if (!refreshing) {
        refreshing = tokenRequest({ grant_type: 'refresh_token', refresh_token: t.refreshToken })
          .then(function (body) { return saveTokens(body, t.refreshToken).idToken; })
          .catch(function (e) {
            // 更新用トークンの期限切れ・取り消し → ログアウトした扱い。通信の失敗なら今回だけ null
            if (e.code === 'invalid_grant') write(localStorage, STORE_KEY, null);
            return null;
          })
          .finally(function () { refreshing = null; });
      }
      return refreshing;
    },

    /** ログイン画面へ移る。終わったら returnTo（このサイトのパス）に戻る */
    signIn: async function (returnTo) {
      if (!config) return;
      const verifier = randomString(48);
      const state = randomString(16);
      write(sessionStorage, PKCE_KEY, {
        verifier: verifier,
        state: state,
        returnTo: safeReturnPath(returnTo || (location.pathname + location.search)),
      });
      const params = new URLSearchParams({
        response_type: 'code',
        client_id: config.clientId,
        redirect_uri: redirectUri,
        scope: 'openid email',
        state: state,
        code_challenge: await sha256(verifier),
        code_challenge_method: 'S256',
        lang: 'ja', // ログイン画面を日本語にする
      });
      location.assign(authOrigin + '/oauth2/authorize?' + params.toString());
    },

    /** ログアウトする（このブラウザのトークンを消し、ログイン画面側の記憶も消してトップに戻る） */
    signOut: async function () {
      const t = read(localStorage, STORE_KEY);
      write(localStorage, STORE_KEY, null);
      if (!config) return;
      if (t && t.refreshToken) {
        // 更新用トークンを取り消す（失敗しても、このブラウザからは消えている）
        try {
          await fetch(authOrigin + '/oauth2/revoke', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams({ token: t.refreshToken, client_id: config.clientId }).toString(),
          });
        } catch (e) { /* 何もしない */ }
      }
      const params = new URLSearchParams({ client_id: config.clientId, logout_uri: location.origin + '/' });
      location.assign(authOrigin + '/logout?' + params.toString());
    },

    /**
     * サイトの API（/api/…）を呼ぶ。ログインしていれば ID トークンを付ける（ステップ 5-4）。
     * 返り値は { status, data }（data は JSON。読めなければ null）。通信できなければ status 0。
     */
    fetchApi: async function (path, options) {
      options = options || {};
      const headers = { Accept: 'application/json' };
      const token = await auth.getIdToken();
      if (token) headers.Authorization = 'Bearer ' + token;
      if (options.body !== undefined) headers['Content-Type'] = 'application/json';
      try {
        const res = await fetch(path, {
          method: options.method || 'GET',
          headers: headers,
          body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
          cache: 'no-store',
        });
        const data = await res.json().catch(function () { return null; });
        return { status: res.status, data: data };
      } catch (e) {
        return { status: 0, data: null };
      }
    },

    /** このブラウザのログインを消す（アカウントを削除したあとなど。ログイン画面には移らない） */
    forget: function () { write(localStorage, STORE_KEY, null); },

    /**
     * /auth/callback.html で呼ぶ。code をトークンに引き換えて保存し、戻り先のパスを返す。
     * うまくいかなかったら、利用者に見せる日本語のメッセージ付きの Error を投げる。
     */
    handleCallback: async function () {
      const q = new URLSearchParams(location.search);
      const pending = read(sessionStorage, PKCE_KEY);
      write(sessionStorage, PKCE_KEY, null); // 1 回だけ使う
      if (!config) throw new Error('ログインの設定を読み込めませんでした。');
      if (q.get('error')) throw new Error('ログインを中止しました。');
      if (!pending || !q.get('code') || q.get('state') !== pending.state) {
        throw new Error('ログインの手続きが見つかりませんでした。もう一度ログインしてください。');
      }
      try {
        const body = await tokenRequest({
          grant_type: 'authorization_code',
          code: q.get('code'),
          redirect_uri: redirectUri,
          code_verifier: pending.verifier,
        });
        saveTokens(body);
      } catch (e) {
        throw new Error('ログインに失敗しました。時間をおいて、もう一度お試しください。');
      }
      return safeReturnPath(pending.returnTo);
    },
  };
  window.BoardPiaAuth = auth;

  // ---------- ヘッダーに「ログイン / マイページ」を出す ----------
  // ログイン中は「マイページ」（ログアウトはマイページから）。ログインしていなければ「ログイン」
  function addHeaderLink() {
    if (!config || location.pathname === CALLBACK_PATH) return;
    const link = document.createElement('a');
    link.href = '#';
    link.className = 'auth-link';
    if (auth.isSignedIn()) {
      const claims = auth.getClaims();
      link.textContent = 'マイページ';
      link.href = '/mypage/';
      if (claims && claims.email) link.title = claims.email + ' でログイン中';
      if (location.pathname === '/mypage/' || location.pathname === '/mypage/index.html') link.setAttribute('aria-current', 'page');
    } else {
      link.textContent = 'ログイン';
      link.addEventListener('click', function (e) { e.preventDefault(); auth.signIn(); });
    }
    // プレイページ（/play/<slug>/）は上部のバーのボタンの先頭（ほかのボタンと同じ見た目）
    const playActions = document.querySelector('.play-bar .actions');
    if (playActions) {
      link.className = 'btn auth-link';
      playActions.insertBefore(link, playActions.firstChild);
      return;
    }
    // サブページは上部のメニューの右端、トップページはヘッダーの右上
    const menu = document.querySelector('.site-nav ul');
    if (menu) {
      const li = document.createElement('li');
      li.appendChild(link);
      menu.appendChild(li);
      return;
    }
    const header = document.querySelector('body > header');
    if (header) {
      // トップページ: ヘッダーの右上の欄（「ランキング」のリンクがあれば、その右）
      let box = header.querySelector('.header-auth');
      if (!box) {
        box = document.createElement('div');
        box.className = 'header-auth';
        header.appendChild(box);
      }
      box.appendChild(link);
    }
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', addHeaderLink);
  else addHeaderLink();
})();
