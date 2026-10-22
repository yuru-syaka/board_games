#!/usr/bin/env node
/**
 * web/ を手元で確認するための小さなサーバー（http://localhost:8080）。
 *
 * - ゲームのビルド（web/play/<slug>/Build/*.br）は Brotli で圧縮してあり、ブラウザ自身に展開させるので、
 *   本番（CloudFront）と同じく Content-Encoding: br と元の Content-Type を付けて返す。
 *   普通の静的サーバー（http-server など）ではこのヘッダーが付かず、ゲームが起動しない。
 * - "/blog/" のようなフォルダの URL は index.html を返す。無いファイルは 404.html を返す。
 * - /api/… は本番の API に中継する（ランキング・マイページの確認用）。
 *
 * 使い方: node scripts/serve-web.js [port]
 */
const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..', 'web');
const port = Number(process.argv[2] || 8080);

const types = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
  '.xml': 'application/xml',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
};

http.createServer((req, res) => {
  // ランキングなどの API（/api/…）は、本番（https://boardgamepia.com）に中継する（手元に API はないため。ステップ 5-4）
  if (req.url.startsWith('/api/')) return proxyApi(req, res);
  let rel = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  if (rel.endsWith('/')) rel += 'index.html';
  let file = path.join(root, rel);
  if (!file.startsWith(root)) return send(res, 403, 'forbidden');
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!fs.existsSync(file)) {
    const notFound = path.join(root, '404.html');
    res.writeHead(404, { 'Content-Type': types['.html'] });
    return res.end(fs.existsSync(notFound) ? fs.readFileSync(notFound) : 'not found');
  }

  const headers = { 'Cache-Control': 'no-store' };
  if (file.endsWith('.br')) {
    headers['Content-Encoding'] = 'br';
    headers['Content-Type'] = types[path.extname(file.slice(0, -3))] || 'application/octet-stream';
  } else {
    headers['Content-Type'] = types[path.extname(file)] || 'application/octet-stream';
  }
  res.writeHead(200, headers);
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`serving ${root} at http://localhost:${port}`));

function proxyApi(req, res) {
  const headers = { accept: 'application/json' };
  if (req.headers.authorization) headers.authorization = req.headers.authorization;
  if (req.headers['content-type']) headers['content-type'] = req.headers['content-type'];
  const upstream = https.request({ host: 'boardgamepia.com', path: req.url, method: req.method, headers }, (up) => {
    res.writeHead(up.statusCode || 502, {
      'Content-Type': up.headers['content-type'] || 'application/json',
      'Cache-Control': 'no-store',
    });
    up.pipe(res);
  });
  upstream.on('error', () => send(res, 502, 'api proxy error'));
  req.pipe(upstream);
}

function send(res, status, text) {
  res.writeHead(status, { 'Content-Type': 'text/plain' });
  res.end(text);
}
