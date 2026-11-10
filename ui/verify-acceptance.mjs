// UI fixtures only; backend/FFmpeg acceptance is verified separately by the .NET tests.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:18766';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1200 } });
const errors = [], calls = [];
page.on('pageerror', error => errors.push(error.message));
let checks = 0, stale = false;
const check = (ok, name) => { assert.ok(ok, name); console.log(`PASS ${name}`); checks++; };
const plan = {
  editplan_id: 'ep-at', project_id: 'p-at', preset: { id: 'P4', version: '1.0' },
  timeline: [{ clip_id: 'c', asset_id: 'a', src_in: 0, src_out: 30, speed: 1, role: 'body', reframe: {} }],
  audio: { master: 'a', sfx_enabled: false, sfx: [] }, inserts: [],
  telops: [{ id: 'amount', type: 'emphasis', start: 1, end: 3, text: 'A社100万円', source_word_ids: ['w'], speaker: 1, enabled: true, approved: true }],
  provenance: { job_id: 'job-at', candidate_id: 'c-at' },
};
const transcript = {
  words: [{ id: 'w', t0: 1, t1: 3, text: 'A社は100万円、B社は500万円', speaker: 1 }],
  sentences: [{ index: 0, t0: 1, t1: 3, text: 'A社は100万円、B社は500万円', speaker: 1, first_word: 0, last_word: 0 }],
};
const approval = () => ({ at: '2026-11-09T00:00:00Z', by: 'UI fixture', stale });
const qa = () => [{
  check_id: 'H-11', status: plan.telops[0].text === 'A社500万円' ? 'fail' : 'pass', stage: 'plan',
  message: 'テロップと発話の対応', measured: plan.telops[0].text === 'A社500万円' ? '数字・単位・対象が不一致' : '0', threshold: '根拠発話と一致',
}, ...(stale ? [{ check_id: 'H-08', status: 'fail', stage: 'plan', message: '候補承認が失効', measured: '承認ハッシュが不一致', threshold: '現在の計画と一致' }] : [])];
const view = () => ({ plan, qa: qa(), approval: approval(), final_render_stale: stale, preview_stale: stale });
try {
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path === '/api/jobs/job-at') return route.fulfill({ json: { id: 'job-at', project_id: 'p-at', status: 'ready', warnings: ['条件を満たす重複しない区間が1件だけのため、候補は1本です'] } });
    if (path === '/api/jobs/job-at/candidates') return route.fulfill({ json: [{
      candidate: { id: 'c-at', rank: 1, edit_plan_id: 'ep-at', segments: [{ t0: 0, t1: 30 }], reason: 'UI fixture', notes: [],
        scores: { hook: 5, standalone: 5, payoff: 5, emotion: 5, signal: 5, final: 5 } },
      preview: { qa: qa() }, preview_stale: stale, approval: approval(), final_render_stale: stale,
    }] });
    if (path === '/api/projects/p-at') return route.fulfill({ json: { assets: [{ id: 'a', path: 'source.mp4' }] } });
    if (path === '/api/projects/p-at/workflow-state') return route.fulfill({ json: {} });
    if (path === '/api/assets/a/transcript') return route.fulfill({ json: transcript });
    if (path === '/api/bgm') return route.fulfill({ json: [] });
    if (path === '/api/editplans/ep-at') {
      if (request.method() === 'PATCH') {
        const body = request.postDataJSON();
        calls.push({ path, body });
        for (const op of body.ops) {
          if (op.op === 'replace_text') plan.telops[0].text = op.text;
          if (op.op === 'set_evidence') plan.telops[0].source_word_ids = op.source_word_ids;
        }
        stale = true;
      }
      return route.fulfill({ json: view() });
    }
    if (path === '/api/jobs/job-at/candidates/c-at/approval') {
      calls.push({ path });
      stale = false;
      return route.fulfill({ json: { approval: approval() } });
    }
    if (path === '/api/editplans/ep-at/render') {
      calls.push({ path });
      return route.fulfill({ status: 202, json: { render_id: 'r-new' } });
    }
    if (path === '/api/renders/r-old') return route.fulfill({ json: { state: 'stale', editplan_id: 'ep-at', error_code: 'E-PLAN-STALE' } });
    if (path === '/api/renders/r-new') return route.fulfill({ json: { state: 'queued', message: 'UI fixture', progress: 0 } });
    return route.continue();
  });
  await page.goto(`${base}/#/job/job-at`);
  await page.waitForSelector('article.card');
  check(await page.locator('article.card').count() === 1, 'Only one candidate is shown');
  check((await page.locator('main').textContent()).includes('候補は1本'), 'Insufficient-candidate reason is visible');
  await page.goto(`${base}/#/ep/ep-at`);
  await page.waitForSelector('[data-evidence="amount"]');
  check((await page.locator('[data-evidence="amount"]').textContent()).includes('話者1・1.0–3.0秒「A社は100万円、B社は500万円」'), 'Evidence includes original utterance, speaker and source time');
  await page.locator('input.telop').fill('A社500万円');
  await page.locator('input.telop').dispatchEvent('change');
  await page.waitForFunction(() => document.querySelector('#review-approval')?.textContent.includes('再承認'));
  check((await page.locator('main').textContent()).includes('本番QA・書き出し結果は失効'), 'Approval and final QA/render invalidation are visible after editing');
  check(await page.locator('#export').getAttribute('disabled') !== null, 'Failed correspondence disables export');
  await page.locator('select.evidence-select').selectOption('["w"]');
  await page.waitForFunction(() => document.querySelector('[data-evidence="amount"]')?.textContent.includes('A社は100万円'));
  check(calls.some(c => c.body?.ops.some(op => op.op === 'set_evidence' && op.source_word_ids[0] === 'w')), 'Evidence selection sends explicit word IDs');
  await page.goto(`${base}/#/job/job-at`);
  await page.waitForSelector('article.card');
  check((await page.locator('article.card .badges').textContent()).includes('プレビュー・QAは編集前の結果'), 'Edited candidate does not present historical preview QA as current');
  await page.goto(`${base}/#/render/r-old`);
  await page.waitForFunction(() => document.querySelector('h1')?.textContent.includes('失効'));
  check(await page.locator('a[href*="/files/"]').count() === 0, 'Stale render has no download links');
  await page.goto(`${base}/#/ep/ep-at`);
  await page.waitForSelector('input.telop');
  await page.locator('input.telop').fill('A社100万円');
  await page.locator('input.telop').dispatchEvent('change');
  await page.waitForFunction(() => document.querySelector('#export')?.hasAttribute('disabled') === false);
  check((await page.locator('#review-approval').textContent()).includes('再承認'), 'Corrected claim can be reapproved despite historical approval invalidation');
  await page.locator('#export').click();
  await page.waitForURL(/#\/render\/r-new$/);
  const approveIndex = calls.findIndex(c => c.path.endsWith('/approval'));
  const exportIndex = calls.findIndex(c => c.path.endsWith('/render'));
  check(approveIndex >= 0 && exportIndex > approveIndex, 'Explicit approve-and-export action approves the current plan before export');
  check(errors.length === 0, `No browser runtime errors: ${errors.join('; ')}`);
  console.log(`${checks}/${checks} UI checks passed (mock API fixtures; not quality-gate evidence)`);
} finally {
  await browser.close();
}
