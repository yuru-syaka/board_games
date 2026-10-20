#!/usr/bin/env node
/**
 * web/sitemap.xml を作り直すスクリプト。
 *
 * - web/ の中の HTML ページ（play/ のプレイページを除く）を集め、検索エンジン向けの一覧にする。
 *   プレイページは noindex（検索結果に出さない）なので載せない。
 * - index.html は「フォルダの URL」（例: /blog/）として載せる。
 * - 更新日（lastmod）は、そのファイルを最後に変更したコミットの日付。
 *   まだコミットしていないファイルは今日の日付にする。
 *
 * 使い方（ページを追加・変更したら実行して、sitemap.xml もコミットする）:
 *   node scripts/generate-sitemap.js
 */
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const SITE = 'https://boardgamepia.com';
const repo = path.join(__dirname, '..');
const web = path.join(repo, 'web');

// web/ の中の HTML を集める（play/ は除く）
function collect(dir, rel = '') {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const relPath = rel ? `${rel}/${entry.name}` : entry.name;
    if (entry.isDirectory()) {
      if (relPath === 'play') continue;
      out.push(...collect(path.join(dir, entry.name), relPath));
    } else if (entry.name.endsWith('.html') && relPath !== '404.html') { // 404 ページは noindex なので載せない
      out.push(relPath);
    }
  }
  return out;
}

// そのファイルを最後に変更したコミットの日付（YYYY-MM-DD）。未コミットなら今日
function lastModified(relPath) {
  const file = path.join('web', relPath);
  let committed = '';
  try {
    committed = execFileSync('git', ['log', '-1', '--format=%cs', '--', file], { cwd: repo, encoding: 'utf8' }).trim();
    const dirty = execFileSync('git', ['status', '--porcelain', '--', file], { cwd: repo, encoding: 'utf8' }).trim();
    if (dirty) committed = '';
  } catch {
    committed = '';
  }
  return committed || new Date().toISOString().slice(0, 10);
}

// 並び順: トップ → ゲーム紹介 → 開発日記の一覧 → 記事（新しい順）→ その他
function rank(relPath) {
  if (relPath === 'index.html') return [0, ''];
  if (relPath.startsWith('games/')) return [1, relPath];
  if (relPath === 'blog/index.html') return [2, ''];
  if (relPath.startsWith('blog/')) return [3, '~' + relPath.split('').reverse().join('')];
  return [4, relPath];
}
const pages = collect(web).sort((a, b) => {
  const [ra, ka] = rank(a), [rb, kb] = rank(b);
  if (ra !== rb) return ra - rb;
  if (ra === 3) return b.localeCompare(a); // 記事はファイル名（日付）の新しい順
  return ka.localeCompare(kb);
});

const urls = pages.map((relPath) => {
  const urlPath = relPath === 'index.html' ? '/' : '/' + relPath.replace(/(^|\/)index\.html$/, '$1');
  return `  <url>\n    <loc>${SITE}${urlPath}</loc>\n    <lastmod>${lastModified(relPath)}</lastmod>\n  </url>`;
});

const xml = `<?xml version="1.0" encoding="UTF-8"?>
<!-- 検索エンジン向けのページ一覧。scripts/generate-sitemap.js で作る（手で編集しない） -->
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${urls.join('\n')}
</urlset>
`;
fs.writeFileSync(path.join(web, 'sitemap.xml'), xml, 'utf8');
console.log(`web/sitemap.xml: ${pages.length} pages`);
