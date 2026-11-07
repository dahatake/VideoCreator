// End-to-end UI test in headless Microsoft Edge (OS-installed). Drives the real app + real Azure services.
// Usage: node e2e.mjs <video file> [baseUrl]
// Verifies UX-01 (≤5 clicks to candidates), AT-01/AT-02 (QA on export), FR-REV-02/03 (edit + adjust), and saves screenshots.
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const video = resolve(process.argv[2] ?? '../spikes/media/talk.mp4');
const base = process.argv[3] ?? 'http://127.0.0.1:8765';
const shots = join(dirname(fileURLToPath(import.meta.url)), '..', 'docs', 'screenshots');
mkdirSync(shots, { recursive: true });
const results = [];
const check = (ok, msg) => { results.push({ ok, msg }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${msg}`); };
const t0 = Date.now(); const secs = () => ((Date.now() - t0) / 1000).toFixed(1);
let clicks = 0;
const click = async (loc) => { clicks++; await loc.click(); };

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, locale: 'ja-JP' });
page.on('pageerror', e => check(false, `page error: ${e.message}`));
const api = (p, init = {}) => page.evaluate(async ([p, init]) => { const r = await fetch(p, { ...init, headers: { 'X-VideoCreator': '1', 'Content-Type': 'application/json', ...(init.headers ?? {}) } }); return { status: r.status, body: await r.json().catch(() => null) }; }, [p, init]);

try {
  await page.goto(base + '/');
  await page.locator('#name input').fill('E2E 百円ショップ企画');
  await page.locator('form#new fluent-button').click();                       // (setup, not counted: creating a project)
  await page.waitForURL(/#\/p\//);
  await page.screenshot({ path: join(shots, '01-project.png') });

  // UX-01 clicks start here: 1) choose file, 2) rights, 3) pattern (explicitly), 4) generate, 5) open candidate
  clicks++; await page.locator('#file').setInputFiles(video);                  // file dialog = 1 click
  await page.waitForSelector('fluent-checkbox.rights');
  await click(page.locator('fluent-checkbox.rights'));
  await page.waitForFunction(() => !document.querySelector('#gen')?.hasAttribute('disabled'));
  await click(page.locator('#presets [data-id="P4"]'));
  await page.screenshot({ path: join(shots, '02-assets-patterns.png'), fullPage: true });
  await click(page.locator('#gen'));
  await page.waitForURL(/#\/job\//);
  await page.waitForTimeout(4000);
  await page.screenshot({ path: join(shots, '03-progress.png') });
  const tGen = Date.now();
  await page.waitForSelector('article.card', { timeout: 15 * 60 * 1000 });
  const genSecs = (Date.now() - tGen) / 1000;
  check(true, `candidates ready in ${genSecs.toFixed(0)} s after clicking generate`);
  const cards = await page.locator('article.card').count();
  check(cards >= 1 && cards <= 3, `${cards} candidate(s) shown (≤3, FR-SEL-08)`);
  await page.locator('.safe-toggle').click();
  await page.waitForTimeout(800);
  await page.screenshot({ path: join(shots, '04-candidates.png'), fullPage: true });
  const jobId = page.url().split('/').pop();
  const cands = (await api(`/api/jobs/${jobId}/candidates`)).body;
  check(cands.every(c => c.candidate.reason && c.candidate.scores), 'every candidate has a reason and 4-axis scores (FR-SEL-04, US-02)');
  check(cands.every(c => (c.preview?.qa ?? []).filter(q => q.check_id.startsWith('H-')).every(q => q.status === 'pass')), 'every preview passes hard QA');

  await click(page.locator('article.card').first().locator('fluent-anchor-button'));
  check(clicks <= 5, `UX-01: ${clicks} clicks from file selection to a candidate (≤5)`);
  await page.waitForSelector('input.telop');
  const epId = page.url().split('/').pop();

  // FR-REV-02 / AT-09: edit a caption, QA re-evaluated immediately
  const firstCaption = page.locator('tr:has(.tag.caption) input.telop').first();
  const original = await firstCaption.inputValue();
  const tEdit = Date.now();
  await firstCaption.fill('あいうえおかきくけこさしすせそたちつてとなに'); await firstCaption.press('Tab');
  await page.waitForFunction(() => [...document.querySelectorAll('.qa-row')].some(r => r.innerText.startsWith('S-02') && r.innerText.includes('警告')));
  check(Date.now() - tEdit < 2000, `AT-09: S-02 warning appeared ${Date.now() - tEdit} ms after editing (incl. UI round-trip)`);
  await page.locator('tr:has(.tag.caption) input.telop').first().fill(original); await page.locator('tr:has(.tag.caption) input.telop').first().press('Tab');
  await page.waitForTimeout(500);

  // FR-REV-03: extend the end by one sentence
  const before = (await api(`/api/editplans/${epId}`)).body.duration;
  await page.locator('[data-adj="end:1"]').click();
  await page.waitForSelector('text=プレビューが古くなっています');
  const after = (await api(`/api/editplans/${epId}`)).body.duration;
  check(after !== before, `FR-REV-03: adjusting the end changed duration ${before.toFixed(1)} → ${after.toFixed(1)} s`);
  if (after > 35.05) { await page.locator('[data-adj="end:-1"]').click(); await page.waitForTimeout(800); }
  await page.locator('#repreview').click();
  await page.waitForFunction(() => !document.body.innerText.includes('プレビューが古くなっています'), null, { timeout: 120000 });
  await page.locator('.safe-toggle').click();
  await page.waitForTimeout(800);
  await page.screenshot({ path: join(shots, '05-review.png'), fullPage: true });

  // Export (final render + QA)
  await page.locator('#export').click();
  await page.waitForURL(/#\/render\//);
  await page.waitForSelector('text=書き出しが完了しました', { timeout: 10 * 60 * 1000 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: join(shots, '06-export.png'), fullPage: true });
  const renderId = page.url().split('/').pop();
  const r = (await api(`/api/renders/${renderId}`)).body.render;
  const hard = r.qa.filter(q => q.check_id.startsWith('H-'));
  check(hard.length >= 9 && hard.every(q => q.status === 'pass'), `AT-01/02: final export hard QA ${hard.filter(q => q.status === 'pass').length}/${hard.length} pass: ${hard.map(q => `${q.check_id}=${q.measured}`).join('; ')}`);
  check(['mp4', 'srt', 'editplan', 'metadata', 'thumb1'].every(k => k in r.outputs), `FR-EXP-01/03: outputs ${Object.keys(r.outputs).join(', ')}`);
} catch (e) {
  check(false, `exception: ${e.message}`);
  await page.screenshot({ path: join(shots, 'error.png'), fullPage: true }).catch(() => { });
} finally {
  await browser.close();
  const failed = results.filter(r => !r.ok).length;
  console.log(`\n${results.length - failed}/${results.length} checks passed in ${secs()} s`);
  process.exit(failed ? 1 : 0);
}
