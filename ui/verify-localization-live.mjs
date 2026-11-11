// Run against an isolated, already-running VideoCreator instance. No network/API mocks.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const target = new URL(process.argv[2] ?? 'http://127.0.0.1:18769');
assert.ok(['127.0.0.1', 'localhost', '[::1]'].includes(target.hostname), 'Use a local isolated test server.');
const base = target.origin;
const output = fileURLToPath(new URL('./localization-artifacts/', import.meta.url));
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const context = await browser.newContext({ viewport: { width: 1540, height: 1100 } });
const page = await context.newPage();
const errors = [];
const created = new Set();
page.on('pageerror', error => errors.push(error.message));
const headers = { 'X-VideoCreator': '1' };
const switchLocale = async locale => {
  await page.locator('#ui-language').selectOption(locale);
  await page.waitForFunction(value => document.documentElement.lang === value && !document.querySelector('#ui-language').disabled, locale);
};
try {
  assert.equal((await context.request.get(`${base}/api/health`)).status(), 200);
  await page.goto(`${base}/#/`);
  await page.locator('#new').waitFor();
  assert.equal(await page.locator('html').getAttribute('lang'), 'ja', 'Japanese remains the initial interface');
  for (const [locale, videoLanguage] of [['ja', 'ko-KR'], ['en', 'ja-JP'], ['zh-CN', 'en-US'], ['ko', 'zh-CN']]) {
    await switchLocale(locale);
    assert.equal(await page.locator('#new-video-language').inputValue(), 'ja-JP', 'creation default independent of UI');
    const name = `UI-live-${locale}-${Date.now()} 日本語 <name>`;
    await page.locator('#name').evaluate((el, text) => { el.value = text; }, name);
    await page.locator('#new-video-language').selectOption(videoLanguage);
    const responsePromise = page.waitForResponse(r => r.url() === `${base}/api/projects` && r.request().method() === 'POST');
    await page.locator('#new fluent-button').click();
    const response = await responsePromise;
    assert.ok(response.ok(), `create ${locale}`);
    const project = await response.json();
    created.add(project.id);
    await page.locator('#project-video-language').waitFor();
    assert.equal(await page.locator('#pname').textContent(), name);
    assert.equal(await page.locator('#project-video-language').inputValue(), videoLanguage);
    const changed = videoLanguage === 'ja-JP' ? 'en-US' : 'ja-JP';
    await page.locator('#project-video-language').selectOption(changed);
    await page.waitForFunction(() => !document.querySelector('#project-video-language').disabled);
    const saved = await (await context.request.get(`${base}/api/projects/${project.id}`)).json();
    assert.equal(saved.project.language, changed);
    assert.equal(saved.project.name, name, 'language-only edit keeps name');
    const unsupported = await context.request.patch(`${base}/api/projects/${project.id}`, { headers, data: { language: 'fr-FR' } });
    assert.equal(unsupported.status(), 422, 'actual backend validates video locale');
    await page.reload();
    await page.locator('#gen').waitFor();
    await page.waitForFunction(() => document.querySelector('#provider').textContent.length > 0);
    assert.equal(await page.locator('html').getAttribute('lang'), locale, 'actual-server locale persists');
    assert.equal(await page.locator('#project-video-language').inputValue(), changed);
    await page.screenshot({ path: path.join(output, `live-${locale}-project.png`), fullPage: true, animations: 'disabled' });
    await page.locator('#settings-btn').click();
    await page.locator('#pdirPath').waitFor();
    await page.screenshot({ path: path.join(output, `live-${locale}-settings.png`), fullPage: true, animations: 'disabled' });
    await page.locator('#settings-close').click();
    await page.goto(`${base}/#/`);
    const card = page.locator('.project-card').filter({ hasText: name });
    await card.locator('.del').click();
    await page.locator('#delete-confirm').click();
    await page.waitForFunction(() => !document.querySelector('#delete-dialog').dialog.open);
    assert.equal((await context.request.get(`${base}/api/projects/${project.id}`)).status(), 404, 'UI deletion persists');
    created.delete(project.id);
    await page.locator('#new').waitFor();
  }
  assert.deepEqual(errors, [], 'live browser JS errors');
  console.log('PASS: live Edge × 4 UI locales; Japanese defaults, independent video-language create/edit, actual 422 validation, persistence, escaped user names, settings and UI deletion. 8 live screenshots.');
} finally {
  for (const id of created) await context.request.delete(`${base}/api/projects/${id}`, { headers }).catch(() => {});
  await browser.close();
}
