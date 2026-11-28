import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = path.resolve(fileURLToPath(new URL('../src/VideoCreator.App/wwwroot/', import.meta.url)));
const preset = JSON.parse(await fs.readFile(new URL('../presets/P4.json', import.meta.url), 'utf8'));
const project = { id: 'p-trim', name: 'Trim fixture', directory: 'C:\\Videos\\fixture', language: 'ja-JP', generation_settings: {} };
const asset = { id: 'a-main', path: 'C:\\Videos\\talk.mp4', media: { duration: 20, width: 1920, height: 1080, fps: 30, audio_channels: 1, video_codec: 'h264' }, role: 'main', warnings: [] };
const analysis = { asset_id: asset.id, duration: 20, suggested: { t0: 2.75, t1: 15.5 }, automatic: { t0: 1.75, t1: 17.5 }, first_speech: '最初の発話。', last_speech: '結論。', notes: [], sound_before: 0, sound_after: 2 };
const writes = [], errors = [];
let analyzeCount = 0, slowResolve;
const json = (res, value, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(value)); };
const server = createServer(async (req, res) => {
  const url = new URL(req.url, 'http://fixture');
  if (url.pathname.startsWith('/api/')) {
    let body = {};
    if (req.method !== 'GET') {
      let raw = ''; for await (const chunk of req) raw += chunk;
      body = raw ? JSON.parse(raw) : {};
      writes.push({ path: url.pathname, body });
    }
    if (url.pathname.endsWith('/body-trim/analyze')) {
      analyzeCount++;
      if (slowResolve) { const wait = slowResolve; slowResolve = null; await wait; }
      return json(res, analysis);
    }
    if (url.pathname.endsWith('/body-trim')) {
      project.body_trim = body.mode === 'off' ? null : {
        asset_id: asset.id, mode: body.mode,
        start: body.mode === 'auto' ? analysis.automatic.t0 : body.start,
        end: body.mode === 'auto' ? analysis.automatic.t1 : body.end,
      };
      return json(res, { body_trim: project.body_trim });
    }
    if (url.pathname.endsWith('/generation-settings')) { project.generation_settings = body; return json(res, body); }
    const fixtures = {
      '/api/health': { version: 'test', presets: 1, llm_provider: 'none', asr: 'none', local_only: true },
      '/api/settings': { local_only: true, llm_provider: 'none', asr_configured: false, transmission: { llm_disabled: true } },
      '/api/projects': [],
      '/api/projects/p-trim': { project, assets: [asset], rights: [{ asset_id: asset.id }], rights_statement: '', jobs: [], resume_state: { step: 'project' } },
      '/api/projects/p-trim/estimate': { duration: 20, scopes: ['economy', 'balanced', 'full'].map(scope => ({ scope, share: 1, cost: { estimated_jpy: 0, asr_minutes: 0 } })) },
      '/api/presets': [preset],
      '/api/bgm': [], '/api/bgm/shared': [],
      '/api/brandkit': { registered: false, opening: null, closing: null },
      '/api/projects/p-trim/layouts': [],
      '/api/projects/p-trim/patterns': [],
    };
    return json(res, fixtures[url.pathname] ?? {});
  }
  try {
    const file = path.resolve(root, url.pathname === '/' ? 'index.html' : decodeURIComponent(url.pathname.slice(1)));
    if (!file.startsWith(root + path.sep) && file !== path.join(root, 'index.html')) { res.writeHead(403); return res.end(); }
    const data = await fs.readFile(file);
    res.writeHead(200, { 'Content-Type': { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml' }[path.extname(file)] ?? 'application/octet-stream' });
    res.end(data);
  } catch { res.writeHead(404); res.end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1540, height: 1100 } });
  page.on('pageerror', e => errors.push(e.message));
  await page.addInitScript(() => localStorage.setItem('vc.help.tourSeen', '1'));
  const base = `http://127.0.0.1:${server.address().port}`;
  await page.goto(`${base}/#/p/p-trim`);
  try { await page.locator('#trim-analyze').waitFor(); }
  catch (e) {
    console.error('UI errors:', errors, 'page:', await page.locator('main').innerText(), 'writes:', writes);
    throw e;
  }
  // Manual saving must analyze even when the user has not explicitly asked for suggestions yet.
  await page.locator('#trim-start').fill('4');
  await page.locator('#trim-end').fill('16');
  await page.locator('#trim-save').click();
  await page.waitForFunction(() => document.querySelector('#trim-message').textContent.includes('4〜16'));
  assert.equal(analyzeCount, 1);
  assert.equal(project.body_trim.start, 4);
  assert.equal(project.body_trim.end, 16);
  assert.match(await page.locator('#trim-analysis').innerText(), /2.75.*15.5/);
  assert.match(await page.locator('#trim-analysis').innerText(), /最後の発話の後に2秒、途切れない音/);
  await page.locator('#trim-suggest').click();
  assert.equal(await page.locator('#trim-start').inputValue(), '2.75');
  assert.equal(await page.locator('#trim-end').inputValue(), '15.5');
  await page.locator('#gen').click();
  assert.match(await page.locator('#genMsg').innerText(), /未保存/);
  assert.ok(!writes.some(w => w.path.endsWith('/jobs')), 'unsaved edit points cannot accidentally generate the older range');
  await page.locator('#trim-auto').click();
  await page.waitForFunction(() => document.querySelector('#trim-message').textContent.includes('1.75〜17.5'));
  assert.equal(project.body_trim.mode, 'auto');
  await page.reload();
  await page.locator('#trim-analyze').waitFor();
  assert.equal(await page.locator('#trim-start').inputValue(), '1.75');
  assert.equal(await page.locator('#trim-end').inputValue(), '17.5');
  const before = writes.length;
  await page.locator('#trim-start').fill('18');
  await page.locator('#trim-end').fill('10');
  await page.locator('#trim-save').click();
  assert.match(await page.locator('#trim-message').innerText(), /開始より終了/);
  assert.equal(writes.length, before);
  await page.locator('#trim-reset').click();
  await page.waitForFunction(() => document.querySelector('#trim-message').textContent.includes('削除を解除'));
  assert.equal(project.body_trim, null);
  for (const locale of ['en', 'ko', 'zh-CN', 'ja']) {
    await page.locator(`#ui-language [data-language="${locale}"]`).click();
    await page.waitForFunction(l => document.documentElement.lang === l && !document.querySelector('#trim-analyze').disabled, locale);
    assert.ok(await page.locator('#trim-title').innerText());
  }
  // A late result must not overwrite a different route.
  let release;
  slowResolve = new Promise(resolve => { release = resolve; });
  await page.locator('#trim-analyze').click();
  await page.evaluate(() => { location.hash = '#/'; });
  await page.locator('#new').waitFor();
  release();
  await page.waitForTimeout(300);
  assert.equal(await page.locator('#trim-analysis').count(), 0);
  assert.deepEqual(errors, []);
  console.log('PASS: trim UI — analysis before manual save, measured proposals, automatic apply, persistence, invalid input, reset, four languages, route cancellation.');
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
