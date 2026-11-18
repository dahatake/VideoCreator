import assert from 'node:assert/strict';
import { chromium } from 'playwright';

// Settings dialog: width (65vw clamped to 600–1140 px, never wider than the viewport, no horizontal scroll at any size or 200% zoom)
// and drafts (unsaved input survives a reload, keys are never kept, discarding clears it). Needs a running app with an empty data dir.
const base = process.argv[2] ?? 'http://127.0.0.1:18779';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const errors = [];
const headers = { 'X-VideoCreator': '1' };
const open = async (page) => {
  await page.locator('#settings-btn').click();
  await page.locator('#sEndpoint').waitFor();
  await page.waitForFunction(() => customElements.get('fluent-radio-group') && document.querySelector('#sAuth fluent-radio')?.shadowRoot);
};
try {
  // ---- width ----
  for (const [w, h, zoom] of [[1280, 800, 1], [1920, 1080, 1], [2560, 1440, 1], [800, 600, 1], [1280, 800, 2]]) {
    const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: w / zoom, height: h / zoom }, deviceScaleFactor: zoom });
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(base); await open(page);
    const m = await page.evaluate(() => {
      const d = document.querySelector('#settings-dialog').shadowRoot.querySelector('[part~=dialog]');
      const body = document.querySelector('#settings-body');
      const p = [...body.querySelectorAll('p.caption')].find(x => x.textContent.length > 120);
      return { vw: innerWidth, dialog: d.offsetWidth /* layout width: the open animation scales the box */, scroll: body.scrollWidth - body.clientWidth, docScroll: document.documentElement.scrollWidth - innerWidth,
        measure: p ? p.getBoundingClientRect().width / parseFloat(getComputedStyle(p).fontSize) : null };
    });
    const expected = Math.min(m.vw - 32, Math.min(1140, Math.max(600, m.vw * 0.65)));
    console.log(`${w}x${h} @${zoom * 100}%: viewport ${m.vw}px → dialog ${Math.round(m.dialog)}px (${Math.round(100 * m.dialog / m.vw)}%), prose ${m.measure?.toFixed(1)} em, overflow ${m.scroll}/${m.docScroll}`);
    assert.ok(Math.abs(m.dialog - expected) <= 1, `dialog width ${m.dialog} != ${expected}`);
    assert.ok(m.scroll <= 0 && m.docScroll <= 0, 'no horizontal scrolling');
    if (m.measure) assert.ok(m.measure <= 41, `prose lines stay within ~40 CJK glyphs (${m.measure} em)`);
    await page.close();
  }

  // ---- drafts ----
  const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
  page.on('pageerror', e => errors.push(e.message));
  for (const s of ['speech', 'foundry', 'copilot', 'projects_dir']) await page.request.delete(`${base}/api/settings/drafts/${s}`, { headers });
  await page.goto(base); await open(page);
  assert.equal(await page.locator('#sDraft fluent-message-bar').count(), 0, 'no draft notice when nothing is pending');
  const original = await page.locator('#sEndpoint').evaluate(e => e.value);
  await page.locator('#sEndpoint').evaluate(e => { e.value = ''; });
  await page.locator('#sEndpoint').pressSequentially('https://draft.cognitiveservices.azure.com/');
  await page.locator('#sAuth fluent-radio[value=key]').click();
  await page.locator('#sKey').pressSequentially('secret-typed-but-never-kept');
  await page.locator('#pdirPath').pressSequentially('D:\\Drafts\\Videos');
  await page.locator('#pdirMove fluent-radio[value=keep]').click();
  assert.equal(await page.locator('#sDraft fluent-message-bar').count(), 1, 'the notice says the input is kept as a draft');
  await page.locator('#settings-close').click();
  await page.waitForTimeout(300);
  const stored = await (await page.request.get(`${base}/api/settings/drafts`, { headers })).json();
  assert.equal(stored.speech.endpoint, 'https://draft.cognitiveservices.azure.com/');
  assert.equal(stored.speech.auth, 'key');
  assert.equal(stored.projects_dir.path, 'D:\\Drafts\\Videos');
  assert.ok(!JSON.stringify(stored).includes('secret-typed'), 'a key is never part of a draft');
  const settings = await (await page.request.get(`${base}/api/settings`, { headers })).json();
  assert.notEqual(settings.speech?.endpoint, 'https://draft.cognitiveservices.azure.com/', 'a draft is not a connection');

  await page.reload(); await open(page);
  assert.equal(await page.locator('#sEndpoint').evaluate(e => e.value), 'https://draft.cognitiveservices.azure.com/');
  assert.equal(await page.locator('#sAuth fluent-radio[value=key]').evaluate(e => e.checked), true);
  assert.equal(await page.locator('#sKeyBox').evaluate(e => e.hidden), false, 'the key box follows the restored auth');
  assert.equal(await page.locator('#sKey').evaluate(e => e.value), '');
  assert.equal(await page.locator('#pdirPath').evaluate(e => e.value), 'D:\\Drafts\\Videos');
  assert.equal(await page.locator('#pdirMove fluent-radio[value=keep]').evaluate(e => e.checked), true);
  assert.match(await page.locator('#sDraft').innerText(), /復元/);
  assert.match(await page.locator('#pdirDraft').innerText(), /復元/);

  // putting the saved values back removes the draft
  await page.locator('#sEndpoint').evaluate((e, v) => { e.value = v; e.dispatchEvent(new Event('input')); }, original);
  await page.locator('#sAuth fluent-radio[value=entra]').click();
  await page.waitForFunction(() => !document.querySelector('#sDraft fluent-message-bar'));
  await page.waitForTimeout(800);
  assert.equal((await (await page.request.get(`${base}/api/settings/drafts`, { headers })).json()).speech, undefined);

  await page.locator('#pdirDraft [data-draft-discard]').click();
  await page.waitForFunction(() => !document.querySelector('#pdirDraft fluent-message-bar'));
  assert.equal(await page.locator('#pdirPath').evaluate(e => e.value), '');
  assert.deepEqual(await (await page.request.get(`${base}/api/settings/drafts`, { headers })).json(), {});
  assert.deepEqual(errors, []);
  console.log('PASS: settings width and drafts');
} finally { await browser.close(); }
