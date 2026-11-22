// PWA の Service Worker（/sw.js）を登録する。対応していないブラウザでは何もしない（サイトはそのまま使える）
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch((e) => console.warn('Service Worker を登録できませんでした', e));
  });
}
