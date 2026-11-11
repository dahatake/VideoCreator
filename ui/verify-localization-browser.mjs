// Deterministic UI integration test: real Fluent components and installed Edge, isolated API fixtures.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = fileURLToPath(new URL('../src/VideoCreator.App/wwwroot/', import.meta.url));
const output = fileURLToPath(new URL('./localization-artifacts/', import.meta.url));
await fs.mkdir(output, { recursive: true });
const presets = await Promise.all(Array.from({ length: 10 }, (_, i) => fs.readFile(new URL(`../presets/P${i + 1}.json`, import.meta.url), 'utf8').then(text => ({ examples: [], ...JSON.parse(text) }))));
const writes = [];
let busyLanguageChange = false;
let delayProjectRead = false;
const project = { id: 'p1', name: 'USER 日本語 <name>', directory: 'C:\\Videos\\fixture', language: 'ja-JP', proper_nouns: [], created_at: '2026-11-11T00:00:00Z' };
const qa = [
  { check_id: 'H-07', status: 'fail', message: 'テロップの未出現語（内容の忠実性は別途確認）', measured: '1', threshold: '0 ungrounded claims', stage: 'plan', items: [{ target: 't1', kind: 'telop', detail: '「MEDIA 日本語」が発話にありません', start: 0, end: 2 }], advice: { repair: 'ai', fixable: 1, recommendation: '発話に出てこない数字・固有名詞をテロップから外し、発話どおりの表現にします。' } },
  { check_id: 'S-01', status: 'pass', message: '字幕速度', measured: 'ok', threshold: '≤6 文字/秒', stage: 'plan', items: [] },
];
const job = { id: 'j1', project_id: 'p1', status: 'ready', message: '1 件の候補ができました', preset_ids: ['P4'], created_at: '2026-11-11T00:00:00Z', options: {}, warnings: ['このパターンは基準の素材（最初に選んだ動画）だけを使います'], llm_model: 'fixture', stage_seconds: {}, cost: { estimated_jpy: 0 } };
const candidate = { id: 'c1', rank: 1, edit_plan_id: 'ep1', headline: 'MEDIA 日本語 <headline>', hook_text: '', reason: 'GENERATED 日本語 <reason>', segments: [{ t0: 0, t1: 30 }], scores: { hook: 8, standalone: 8, payoff: 7, emotion: 6, signal: 5, final: 7 }, notes: [] };
const candidates = [{ candidate, preview: { qa, duration: 30 }, preview_url: '', approval: null }];
const plan = {
  editplan_id: 'ep1', project_id: 'p1', language: 'ja-JP', preset: { id: 'P4' }, provenance: { job_id: 'j1' },
  canvas: { width: 1080, height: 1920 }, audio: { master: 'a1', bgm: null, sfx: [], bgm_cues: [] },
  timeline: [{ clip_id: 'clip1', asset_id: 'a1', src_in: 0, src_out: 30, audio_in: 0, speed: 1, role: 'body' }],
  inserts: [], telops: [{ id: 't1', type: 'caption', text: 'MEDIA 日本語 <caption>', start: 0, end: 3, enabled: true, approved: true, source_word_ids: ['w1'] }],
};
const settings = { local_only: true, llm_provider: 'none', azure_deployment: 'fixture', asr_configured: false, transmission: { llm_disabled: true, asr_endpoint: null } };
const json = (response, value, status = 200) => { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(value)); };
const server = createServer(async (request, response) => {
  const url = new URL(request.url, 'http://fixture.test');
  if (url.pathname.startsWith('/api/')) {
    let body;
    if (request.method !== 'GET') {
      let raw = ''; for await (const chunk of request) raw += chunk;
      body = raw ? JSON.parse(raw) : {};
      writes.push({ path: url.pathname, method: request.method, body });
    }
    if (url.pathname === '/api/projects' && request.method === 'POST') {
      project.language = body.language; project.name = body.name;
      return json(response, project);
    }
    if (url.pathname === '/api/projects/p1' && request.method === 'PATCH') {
      if (body.language && busyLanguageChange) return json(response, { error: { code: 'E-PROJECT-BUSY', message: '生成または書き出しの終了後に動画の言語を変更してください' } }, 409);
      Object.assign(project, body); return json(response, project);
    }
    if (url.pathname === '/api/editplans/ep1' && request.method === 'PATCH') {
      await new Promise(resolve => setTimeout(resolve, 150));
      for (const op of body.ops) if (op.op === 'replace_text') plan.telops.find(t => t.id === op.target).text = op.text;
      return json(response, { plan, qa, plan_hash: 'hash-edited', approval: null });
    }
    if (url.pathname === '/api/projects/p1' && request.method === 'GET' && delayProjectRead) {
      delayProjectRead = false;
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    const fixtures = {
      '/api/health': { version: 'test', informational_version: 'fixture', ffmpeg: 'available', missing_filters: [], presets: 10, llm_provider: 'none', asr: 'none', local_only: true },
      '/api/settings': settings,
      '/api/settings/projects-dir': { source: 'default', path: 'C:\\Videos', projects: 1 },
      '/api/projects': [{ ...project, assets: 0, last_job: job }],
      '/api/projects/p1': { project, assets: [{ id: 'a1', path: 'C:\\Media\\USER 日本語.mp4', media: { duration: 300, width: 1920, height: 1080, fps: 30, audio_channels: 2, video_codec: 'h264' }, role: 'main', warnings: [] }], rights: [], rights_statement: 'この素材の著作権・肖像権などの権利を自分が持っている、または権利者から利用の許諾を得ています。', jobs: [job], resume_state: { step: 'project' } },
      '/api/projects/p1/estimate': { duration: 300, scopes: ['economy', 'balanced', 'full'].map(scope => ({ scope, share: 1, cost: { estimated_jpy: 2.5, asr_minutes: 5, llm_input_tokens: 1200, basis: 'Azure Retail Prices API (JPY, japaneast, 2026:11-07)' } })) },
      '/api/presets': presets,
      '/api/bgm': [{ id: 'upbeat', name: 'アップテンポ（120 BPM）', source: 'library', bpm: 120 }],
      '/api/brandkit': { registered: false, opening: null, closing: null, card_images: 0 },
      '/api/projects/p1/patterns': [{ job, preset_names: ['切り抜き（長尺→30秒）'], candidates: 1, approved: 0, exported: 0 }],
      '/api/jobs/j1': job,
      '/api/jobs/j1/candidates': candidates,
      '/api/jobs/jrun': { ...job, id: 'jrun', status: 'analyzing', message: 'ショット・音量・無音を解析しています' },
      '/api/editplans/ep1': { plan, qa, plan_hash: 'hash', approval: null },
      '/api/assets/a1/transcript': { words: [{ id: 'w1', text: 'MEDIA 日本語', t0: 0, t1: 3, speaker: 1 }], sentences: [] },
      '/api/renders/r1': { state: 'done', project_id: 'p1', editplan_id: 'ep1', render: { qa } },
      '/api/renders/r1/artifacts': { available: false, path: 'C:\\Videos\\fixture\\output.mp4', files: [{ key: 'post', available: true, path: 'C:\\Videos\\post.json', preview_type: 'text' }] },
      '/api/renders/r1/post': { source: 'ai', youtube: 'MEDIA 日本語 <YouTube>', engage: 'MEDIA 日本語 <Engage>', chapters: null, notes: [] },
    };
    if (url.pathname.endsWith('/events')) {
      response.writeHead(200, { 'Content-Type': 'text/event-stream' });
      response.write(`data: ${JSON.stringify({ stage: 'analyzing', progress: 0.4, message: 'ショット・音量・無音を解析しています', step: 'ショットの切り替わり検出', step_progress: 0.5 })}\n\n`);
      return;
    }
    return json(response, fixtures[url.pathname] ?? {});
  }
  try {
    const relative = url.pathname === '/' ? 'index.html' : decodeURIComponent(url.pathname.slice(1));
    const file = path.resolve(root, relative);
    if (!file.startsWith(root)) { response.writeHead(403); return response.end(); }
    const data = await fs.readFile(file);
    const mime = { '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.html': 'text/html' }[path.extname(file)] ?? 'application/octet-stream';
    response.writeHead(200, { 'Content-Type': mime }); response.end(data);
  } catch { response.writeHead(404); response.end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const context = await browser.newContext({ viewport: { width: 1540, height: 1100 } });
const page = await context.newPage();
const errors = [];
page.on('pageerror', error => errors.push(error.message));
const screenshot = async name => {
  assert.ok(!(await page.locator('body').innerText()).includes('NaN'), `${name}: valid numeric displays`);
  await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: true, animations: 'disabled' });
};
const route = async (hash, selector) => {
  await page.goto(`${base}/${hash}`);
  await page.locator(selector).first().waitFor();
};
const switchLocale = async locale => {
  await page.locator('#ui-language').selectOption(locale);
  await page.waitForFunction(value => document.documentElement.lang === value && !document.querySelector('#ui-language').disabled, locale);
};
try {
  await route('#/', '#new');
  assert.equal(await page.locator('html').getAttribute('lang'), 'ja');
  await page.locator('#name').evaluate(el => { el.value = 'UNSAVED 日本語'; });
  await switchLocale('en');
  assert.equal(await page.locator('#name').evaluate(el => el.value), 'UNSAVED 日本語');
  await page.locator('#new-video-language').selectOption('zh-CN');
  await page.locator('#new fluent-button').click();
  await page.locator('#project-video-language').waitFor();
  assert.equal(writes.find(w => w.method === 'POST' && w.path === '/api/projects').body.language, 'zh-CN');
  assert.equal(await page.locator('#project-video-language').inputValue(), 'zh-CN');
  await page.locator('#project-video-language').selectOption('en-US');
  await page.waitForFunction(() => !document.querySelector('#project-video-language').disabled);
  assert.equal(project.language, 'en-US');
  assert.equal(project.name, 'UNSAVED 日本語');
  busyLanguageChange = true;
  await page.locator('#project-video-language').selectOption('ko-KR');
  await page.waitForFunction(() => !document.querySelector('#project-video-language').disabled);
  assert.equal(await page.locator('#project-video-language').inputValue(), 'en-US', 'busy API rejection restores selection');
  assert.ok((await page.locator('#video-language-message').textContent()).includes('Wait for generation'));
  busyLanguageChange = false;
  const languageWriteCount = writes.filter(w => w.path === '/api/projects/p1' && w.body.language).length;
  for (const locale of ['ja', 'en', 'zh-CN', 'ko']) {
    await switchLocale(locale);
    assert.equal(await page.locator('#project-video-language').inputValue(), 'en-US', `${locale}: UI independent of video`);
    assert.equal(await page.locator('#pname').textContent(), 'UNSAVED 日本語', `${locale}: user name untouched`);
    await screenshot(`${locale}-project`);
    assert.ok((await page.locator('.asset').textContent()).includes('USER 日本語.mp4'), 'uploaded filename preserved');
    if (locale !== 'ja') assert.ok(!(await page.locator('.asset').textContent()).includes('肖像権'), 'rights statement translated');
    const presetName = await page.locator('[data-id="P4"] b').textContent();
    assert.equal(presetName.includes('切り抜き'), locale === 'ja', `${locale}: stable preset metadata`);
    await page.reload(); await page.locator('#gen').waitFor();
    assert.equal(await page.locator('html').getAttribute('lang'), locale, 'persisted UI language');
    await page.locator('#settings-btn').click();
    await page.locator('#pdirPath').waitFor();
    await screenshot(`${locale}-settings`);
    await page.locator('#settings-close').click();
    await route('#/job/jrun', '#jobBar');
    await page.waitForTimeout(100);
    if (locale !== 'ja') assert.ok(!(await page.locator('#jobMsg').textContent()).includes('解析しています'));
    await screenshot(`${locale}-progress`);
    await route('#/job/j1', '.cols-cands');
    assert.equal(await page.locator('.headline').textContent(), 'MEDIA 日本語 <headline>');
    assert.ok((await page.locator('.cols-cands').textContent()).includes('GENERATED 日本語 <reason>'));
    await screenshot(`${locale}-candidates`);
    await route('#/ep/ep1', 'input.telop');
    assert.equal(await page.locator('input.telop').inputValue(), 'MEDIA 日本語 <caption>');
    if (locale !== 'ja') assert.ok(!(await page.locator('.qa-panel').textContent()).includes('未出現語'));
    await screenshot(`${locale}-review`);
    await route('#/job/j1/exports', '.export-list');
    await screenshot(`${locale}-exports`);
    await route('#/render/r1', '.post-text');
    assert.equal(await page.locator('.post-text').first().inputValue(), 'MEDIA 日本語 <YouTube>');
    await screenshot(`${locale}-artifacts`);
    await route('#/', '#new');
    await screenshot(`${locale}-home`);
    await page.locator('.project-card .del').click();
    await page.locator('#delete-confirm').waitFor();
    await screenshot(`${locale}-delete`);
    await page.locator('#delete-cancel').click();
    await page.waitForFunction(() => !document.querySelector('#delete-dialog').dialog.open);
    await route('#/p/p1/settings', '#project-video-language');
  }
  await page.locator('#rename').click();
  await page.locator('#rename-input').evaluate(el => { el.value = 'DRAFT 日本語 rename'; });
  await switchLocale('en');
  assert.equal(await page.locator('#rename-input').evaluate(el => el.value), 'DRAFT 日本語 rename', 'unsubmitted rename preserved');
  assert.equal(await page.locator('#title-edit').evaluate(el => el.hidden), false);
  await page.locator('#rename-cancel').click();
  await page.locator('#properNounsDetails').evaluate(el => { el.open = true; });
  await page.locator('#addProperNoun').click();
  await page.locator('[data-proper-field="display_text"]').fill('DRAFT 日本語 noun');
  await switchLocale('zh-CN');
  assert.equal(await page.locator('[data-proper-field="display_text"]').inputValue(), 'DRAFT 日本語 noun', 'new proper noun draft preserved');
  assert.equal(await page.locator('#properNounsDetails').evaluate(el => el.open), true, 'expanded editor preserved');
  await route('#/ep/ep1', 'input.telop');
  await page.locator('input.telop').fill('EDIT 日本語 <caption>');
  await page.locator('input.telop').dispatchEvent('change');
  await switchLocale('en');
  assert.equal(await page.locator('input.telop').inputValue(), 'EDIT 日本語 <caption>', 'in-flight review edits committed before language rerender');
  assert.equal(plan.telops[0].text, 'EDIT 日本語 <caption>');
  delayProjectRead = true;
  const projectRead = page.waitForRequest(r => new URL(r.url()).pathname === '/api/projects/p1');
  await page.evaluate(() => { location.hash = '#/p/p1/settings'; });
  await projectRead;
  await page.evaluate(() => { location.hash = '#/'; });
  await page.locator('#new').waitFor();
  await page.waitForTimeout(300);
  assert.equal(await page.locator('#new').count(), 1, 'stale project response cannot overwrite newer route');
  const restricted = await context.newPage();
  restricted.on('pageerror', error => errors.push(error.message));
  await restricted.addInitScript(() => Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('Storage denied', 'SecurityError'); } }));
  await restricted.goto(`${base}/#/p/p1/settings`);
  await restricted.locator('#gen').waitFor();
  assert.equal(await restricted.locator('html').getAttribute('lang'), 'ja');
  await restricted.locator('#ui-language').selectOption('en');
  await restricted.waitForFunction(() => document.documentElement.lang === 'en' && !document.querySelector('#ui-language').disabled);
  assert.equal(await restricted.locator('#gen').count(), 1, 'language switching works when storage is denied');
  await restricted.close();
  assert.equal(writes.filter(w => w.path === '/api/projects/p1' && w.body.language).length, languageWriteCount, 'UI switching never writes video language');
  assert.deepEqual(errors, [], 'browser JS errors');
  console.log(`PASS: 4 languages × home, project, settings, progress, candidates, review, exports, artifacts, delete; language contract/rollback, persistence, draft/media preservation, in-flight saves, stale routes and blocked storage. Screenshots: ${output}`);
} catch (error) {
  console.error('Browser errors:', errors, '\nPage:', await page.locator('body').innerText());
  await screenshot('failure');
  throw error;
} finally {
  await browser.close();
  server.closeAllConnections();
  await new Promise(resolve => server.close(resolve));
}
