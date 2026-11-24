// Renders the social preview images (1200x630 per language) and the apple-touch-icon into site/static/.
// Run manually after changing the hero copy: node tools/make-og.mjs   (uses the installed Microsoft Edge)
import { mkdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';
import { languages, t } from '../src/i18n.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const staticDir = path.resolve(here, '../static');
const logo = readFileSync(path.join(staticDir, 'favicon.svg'), 'utf8');
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fonts = { ja: "'Yu Gothic UI','Meiryo'", en: "'Segoe UI Variable Display','Segoe UI'", ko: "'Malgun Gothic'", 'zh-CN': "'Microsoft YaHei UI','Microsoft YaHei'" };

const card = lang => {
  const H = t[lang].home;
  const bars = Array.from({ length: 36 }, (_, i) => `<i style="height:${Math.round(18 + 70 * Math.abs(Math.sin(i * 1.7) * Math.cos(i * 0.45)))}px"></i>`).join('');
  return `<!doctype html><html lang="${lang}"><meta charset="utf-8"><style>
*{box-sizing:border-box;margin:0}
body{width:1200px;height:630px;overflow:hidden;font-family:${fonts[lang]},sans-serif;color:#fff;background:#0b0d1a}
.bg{position:absolute;inset:0;background:radial-gradient(600px 420px at 12% 10%,#7c5cff99,transparent 70%),radial-gradient(700px 500px at 95% 90%,#2fe3c477,transparent 70%),radial-gradient(500px 400px at 70% 0%,#3aa0ff66,transparent 70%)}
.wrap{position:relative;padding:72px 80px;height:100%;display:flex;flex-direction:column}
.brand{display:flex;gap:18px;align-items:center;font:600 34px 'Segoe UI',sans-serif;letter-spacing:.3px}.brand svg{width:64px;height:64px}
h1{margin-top:58px;font-size:${lang === 'en' ? 70 : 66}px;line-height:1.14;font-weight:800;max-width:760px;letter-spacing:-.5px}
h1 span{display:block;background:linear-gradient(90deg,#b6a4ff,#6cc3ff 55%,#5ff0d6);-webkit-background-clip:text;color:transparent}
p{margin-top:auto;font-size:26px;color:#c9cde6}
.phone{position:absolute;right:92px;top:92px;width:250px;height:444px;border-radius:34px;padding:12px;background:linear-gradient(160deg,#ffffff33,#ffffff0d);border:1px solid #ffffff40;box-shadow:0 40px 90px #0009}
.screen{height:100%;border-radius:24px;background:linear-gradient(170deg,#24264a,#121428);position:relative;overflow:hidden}
.cap{position:absolute;left:16px;right:16px;bottom:120px;padding:10px;border-radius:10px;background:#000a;font-size:21px;font-weight:700;text-align:center}
.wave{position:absolute;left:18px;right:18px;bottom:26px;height:80px;display:flex;gap:3px;align-items:center}.wave i{flex:1;border-radius:3px;background:linear-gradient(#7c5cff,#2fe3c4)}
.qa{position:absolute;top:18px;left:18px;font:600 16px 'Segoe UI',sans-serif;padding:6px 12px;border-radius:99px;background:#2fe3c433;border:1px solid #2fe3c4}
</style><div class="bg"></div><div class="wrap"><div class="brand">${logo}VideoCreator</div>
<h1>${esc(H.h1a)}<span>${esc(H.h1b)}</span></h1><p>${esc(H.eyebrow)}</p></div>
<div class="phone"><div class="screen"><span class="qa">✓ ${esc(H.scene.qa)}</span><span class="cap">${esc(H.scene.caption)}</span><div class="wave">${bars}</div></div></div></html>`;
};

const browser = await chromium.launch({ channel: 'msedge' });
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 630 } });
  mkdirSync(path.join(staticDir, 'og'), { recursive: true });
  for (const { code } of languages) {
    await page.setContent(card(code), { waitUntil: 'load' });
    await page.screenshot({ path: path.join(staticDir, 'og', `og-${code}.png`) });
  }
  const icon = await browser.newPage({ viewport: { width: 180, height: 180 } });
  await icon.setContent(`<body style="margin:0;background:#0b0d1a">${logo.replace('<svg ', '<svg width="180" height="180" ')}</body>`);
  await icon.screenshot({ path: path.join(staticDir, 'apple-touch-icon.png') });
  console.log('OG images and apple-touch-icon written to', staticDir);
} finally {
  await browser.close();
}
