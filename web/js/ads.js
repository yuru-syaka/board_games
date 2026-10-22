/* =========================================================================
   ads.js … ランキング・プロフィールのページの広告枠（広告④。ステップ 5-5）
   -------------------------------------------------------------------------
   - ページの <div class="ad-slot" data-ad-name="ranking" hidden> に、AdSense のディスプレイ広告を入れる。
   - 広告ユニットの ID（data-ad-slot）は、AdSense の承認後に管理画面で広告ユニットを作って、下の SLOTS に書く。
     ID が空のあいだは何もしない（枠は隠れたまま。ページの見た目は変わらない）。
   - 置き場所は表の下など、ボタンやリンクから離れた場所だけにする（誤クリックは規約違反）。
     広告を自動で読み込み直さない。クリックをお願いする文を書かない。
   - adsbygoogle.js 自体は、各ページの <head> で読み込んでいる（AdSense の審査とサイトの確認にも使う）。
   ========================================================================= */
(function () {
  'use strict';

  const CLIENT = 'ca-pub-9604866179636920';
  // 広告ユニットの ID（AdSense の承認後に書く。例: '1234567890'）
  const SLOTS = {
    ranking: '',
    player: '',
  };

  function render() {
    document.querySelectorAll('.ad-slot[data-ad-name]').forEach(function (box) {
      const slot = SLOTS[box.getAttribute('data-ad-name')];
      if (!slot) return;
      const label = document.createElement('p');
      label.className = 'ad-label';
      label.textContent = '広告';
      const ins = document.createElement('ins');
      ins.className = 'adsbygoogle';
      ins.style.display = 'block';
      ins.setAttribute('data-ad-client', CLIENT);
      ins.setAttribute('data-ad-slot', slot);
      ins.setAttribute('data-ad-format', 'auto');
      ins.setAttribute('data-full-width-responsive', 'true');
      box.appendChild(label);
      box.appendChild(ins);
      box.hidden = false;
      try {
        (window.adsbygoogle = window.adsbygoogle || []).push({});
      } catch (e) {
        box.hidden = true; // 広告を読み込めない（広告ブロックなど）: 枠ごと隠す
      }
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', render);
  else render();
})();
