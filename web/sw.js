/**
 * Service Worker（PWA 用）。
 *
 * 役割は「通信できないときに、ブラウザのエラー画面の代わりに offline.html を出す」ことだけ。
 * - ページ・ゲーム本体・API・ログインは、これまでどおり毎回ネットワークから読む（古い内容が残らないように）。
 * - 保存するのは offline.html だけ。
 * - offline.html を変えたら CACHE_NAME の番号を上げる。
 */
const CACHE_NAME = 'boardpia-offline-v1';
const OFFLINE_URL = '/offline.html';

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.add(new Request(OFFLINE_URL, { cache: 'reload' })))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((names) => Promise.all(
        names.filter((n) => n.startsWith('boardpia-') && n !== CACHE_NAME).map((n) => caches.delete(n)),
      ))
      .then(() => self.clients.claim()),
  );
});

// ページを開くときだけ関わる。画像・JS・ゲームのファイル・API には触らない
self.addEventListener('fetch', (event) => {
  if (event.request.mode !== 'navigate') return;
  event.respondWith(
    fetch(event.request).catch(async () => {
      const cached = await caches.match(OFFLINE_URL);
      return cached || Response.error();
    }),
  );
});
