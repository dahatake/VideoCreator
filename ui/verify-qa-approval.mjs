// UI fixtures only; persistence, concurrency validation and export gates are tested by QaApprovalTests.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:18767';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
const errors = [], calls = [];
page.on('pageerror', e => errors.push(e.message));
let count = 0, rejectNext = false, revision = 1;
const check = (ok, name) => { assert.ok(ok, name); count++; console.log(`PASS ${name}`); };
const plan = {
  editplan_id: 'ep-qa', project_id: 'p-qa', preset: { id: 'P4', version: '1.0' },
  timeline: [{ clip_id: 'c', asset_id: 'a', src_in: 0, src_out: 30, speed: 1, role: 'body', reframe: {} }],
  audio: { master: 'a', sfx_enabled: false, sfx: [] }, inserts: [],
  telops: [{ id: 't', type: 'caption', start: 1, end: 1.1, text: 'これは長い字幕です', enabled: true, approved: false, source_word_ids: [] }],
  provenance: { job_id: 'job-qa', candidate_id: 'c-qa' },
};
const longEvidence = `${'長い根拠発話'.repeat(18)}${'UnbrokenText'.repeat(16)}`;
plan.telops.push({ id: 'em', type: 'emphasis', start: 1.2, end: 1.8, text: '強調', enabled: true, approved: true, source_word_ids: ['w'] });
plan.telops.push(...Array.from({ length: 28 }, (_, n) => ({
  ...plan.telops[0], id: `row-${n}`, type: n === 0 ? 'tsukkomi' : 'caption', start: n + 3, end: n + 3.9, approved: n !== 0,
})));
plan.inserts.push({ id: 'card', kind: 'card', at: 2, duration: 2, text: '場面転換', path: `${'longfilename'.repeat(16)}.png`, enabled: true, approved: false });
const results = [
  { check_id: 'H-08', status: 'fail', message: '未承認テロップ', measured: '1', threshold: '0',
    items: [{ target: 't', kind: 'telop', detail: '未承認', start: 1, end: 1.1 }], advice: { repair: 'manual', recommendation: '内容を確認してください' } },
  { check_id: 'S-01', status: 'warn', message: '字幕速度', measured: '100cps', threshold: '6cps' },
  { check_id: 'H-01', status: 'pass', message: '尺', measured: '30', threshold: 'range' },
  { check_id: 'H-02', status: 'not_run', message: '解像度', measured: '未実施', threshold: '1080x1920' },
].map(q => ({ ...q, review_hash: `${q.check_id}-result` }));
const view = () => ({ plan, plan_hash: `plan-${revision}`, qa: results, final_render_stale: false, preview_stale: false });
try {
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path === '/api/jobs/job-qa') return route.fulfill({ json: { id: 'job-qa', project_id: 'p-qa', status: 'ready' } });
    if (path === '/api/jobs/job-qa/candidates') return route.fulfill({ json: [{
      candidate: { id: 'c-qa', rank: 1, edit_plan_id: 'ep-qa' },
    }] });
    if (path === '/api/projects/p-qa') return route.fulfill({ json: { assets: [{ id: 'a', path: 'source.mp4' }] } });
    if (path === '/api/projects/p-qa/workflow-state') return route.fulfill({ json: {} });
    if (path === '/api/assets/a/transcript') return route.fulfill({ json: {
      words: [{ id: 'w', speaker: 0, text: longEvidence, t0: 1.2, t1: 1.8 }],
      sentences: [{ speaker: 0, text: longEvidence, t0: 1.2, t1: 1.8, first_word: 0, last_word: 0 }],
    } });
    if (path === '/api/bgm' || path === '/api/presets' || path === '/api/health' || path === '/api/settings') {
      return route.fulfill({ json: ['/api/bgm', '/api/presets'].includes(path) ? [] : {} });
    }
    if (path === '/api/editplans/ep-qa/qa/approval') {
      const body = request.postDataJSON();
      calls.push(body);
      assert.equal(request.headers()['x-videocreator'], '1');
      assert.equal(request.method(), 'PUT');
      assert.equal(body.plan_hash, `plan-${revision}`);
      if (rejectNext) {
        rejectNext = false;
        return route.fulfill({ status: 409, json: { error: { code: 'E-QA-STALE', message: 'チェック結果が変更されています。再読み込みしてください' } } });
      }
      for (const target of body.checks) {
        const q = results.find(q => q.check_id === target.check_id);
        assert.ok(q && q.status !== 'not_run');
        assert.equal(q.review_hash, target.result_hash);
        if (body.approved) q.review_approval = { at: '2026-11-10T00:00:00Z', by: 'UI fixture' };
        else delete q.review_approval;
      }
      return route.fulfill({ json: view() });
    }
    if (path === '/api/editplans/ep-qa') {
      if (request.method() === 'PATCH') {
        const [op] = request.postDataJSON().ops;
        if (op.op === 'replace_text' && op.target === 't') {
          plan.telops[0].text = op.text;
          revision++;
          results.forEach(q => delete q.review_approval);
        } else if (op.op === 'approve' && op.target === 't') {
          plan.telops[0].approved = true;
        } else if (op.op === 'approve_card' && op.target === 'card') {
          plan.inserts[0].approved = true;
        } else if (op.op === 'approve_all_tsukkomi') {
          plan.telops.find(t => t.id === 'row-0').approved = true;
        } else {
          assert.fail(`Unexpected edit: ${JSON.stringify(op)}`);
        }
      }
      return route.fulfill({ json: view() });
    }
    return route.continue();
  });
  await page.goto(`${base}/#/ep/ep-qa`);
  await page.waitForSelector('#qaApproveAll');
  check((await page.locator('#qaApprovalCount').textContent()).includes('0 / 3'), 'Only executed checks count toward approvals');
  check((await page.locator('.qa-review').textContent()).includes('書き出し条件やテロップ・カードの承認は変わりません'), 'Acknowledgement semantics are explicit');
  await page.locator('[data-qa-approve="S-01"]').click();
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('1 / 3'));
  check(calls[0].checks.length === 1 && calls[0].checks[0].check_id === 'S-01', 'Individual approval submits only the selected check');
  check((await page.locator('[data-check="S-01"]').textContent()).includes('承認済み'), 'Approved warning stays visible with approval state');
  check((await page.locator('[data-check="S-01"] .qa-status').textContent()) === '警告', 'Approval does not turn a warning into a pass');
  await page.reload();
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('1 / 3'));
  check((await page.locator('[data-qa-approve="S-01"]').textContent()).includes('取り消す'), 'Reload restores the saved approval control');
  await page.locator('[data-qa-approve="S-01"]').click();
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('0 / 3'));
  check(calls.at(-1).approved === false, 'Approval can be withdrawn');
  await page.locator('#qaApproveAll').click();
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('3 / 3'));
  check(calls.at(-1).checks.length === 3 && !calls.at(-1).checks.some(q => q.check_id === 'H-02'), 'Bulk approval includes passed checks and excludes unrun checks');
  check(await page.locator('#qaApproveAll').getAttribute('disabled') !== null, 'Bulk approval is disabled when no checks remain');
  check(await page.locator('#export').getAttribute('disabled') !== null, 'Acknowledged hard failures still block export');
  check(await page.locator('[data-approve="t"]').count() === 1, 'QA approval does not approve the telop');
  await page.locator('.qa-passed summary').click();
  check(await page.locator('[data-qa-approve="H-01"]').isVisible(), 'Passed checks have individual approval controls');
  check(await page.locator('[data-qa-approve="H-02"]').count() === 0, 'Unrun checks cannot be approved');
  rejectNext = true;
  await page.locator('[data-qa-approve="S-01"]').click();
  await page.waitForFunction(() => document.querySelector('#qaApprovalMsg')?.textContent.includes('チェック結果が変更'));
  check((await page.locator('#qaApprovalCount').textContent()).includes('3 / 3'), 'Failed requests show errors without fake success');
  await page.locator('input.telop[data-id="t"]').fill('変更された字幕です');
  await page.locator('input.telop[data-id="t"]').press('Tab');
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('0 / 3'));
  await page.locator('#qaApproveAll').click();
  await page.waitForFunction(() => document.querySelector('#qaApprovalCount')?.textContent.includes('3 / 3'));
  check(calls.at(-1).plan_hash === 'plan-2', 'Approval after editing uses the new content hash');
  const list = page.locator('.cols-review .scroll:has(input.telop)');
  const panel = list.locator('..');
  for (const width of [1440, 991, 800, 360]) {
    await page.setViewportSize({ width, height: 900 });
    const sizes = await list.evaluate(e => ({
      width: e.clientWidth, contentWidth: e.scrollWidth, height: e.clientHeight, contentHeight: e.scrollHeight,
    }));
    console.log(`Viewport ${width}: ${JSON.stringify(sizes)}`);
    check(sizes.contentWidth === sizes.width && sizes.contentHeight > sizes.height, `Telop list scrolls vertically only at ${width}px`);
    const panelBox = await panel.boundingBox();
    check(panelBox.x + panelBox.width <= width, `Telop panel fits the viewport at ${width}px`);
    const qa = await page.locator('.qa-panel').boundingBox();
    check(width > 1240 ? qa.x < panelBox.x : qa.y < panelBox.y, `QA actions precede the telop panel at ${width}px`);
    const bulkBox = await panel.locator('#approveAll').boundingBox();
    const tableBox = await list.boundingBox();
    check(Math.abs(bulkBox.x - tableBox.x) < 2, `Bulk actions start at the left edge at ${width}px`);
    for (const [action, input] of [['[data-approve="t"]', 'input[data-id="t"]'], ['[data-approve-card="card"]', 'input[data-id="card"]']]) {
      const button = list.locator(action);
      check((await button.boundingBox()).x < (await list.locator(input).boundingBox()).x, `Row action ${action} is left of the editor at ${width}px`);
      await button.click({ trial: true });
    }
    check(await list.evaluate(e => { e.scrollTop = e.scrollHeight; return e.scrollTop > 0 && e.scrollLeft === 0; }), `Last rows can be reached without horizontal scrolling at ${width}px`);
  }
  await page.locator('#approveAll').focus();
  await page.keyboard.press('Tab');
  check(await page.locator('#approveCards').evaluate(e => e.matches(':focus-within')), 'Bulk approval buttons are consecutive in keyboard order');
  for (const [action, pending] of [
    ['[data-approve="t"]', '[data-approve="t"]'],
    ['[data-approve-card="card"]', '[data-approve-card="card"]'],
    ['#approveAll', '[data-approve="row-0"]'],
  ]) {
    const button = page.locator(action);
    await button.click();
    await page.locator(pending).waitFor({ state: 'detached' });
    check(await button.count() === 0, `${action} updates the approval state at a narrow width`);
  }
  check(errors.length === 0, `No browser errors: ${errors.join('; ')}`);
  console.log(`${count}/${count} UI checks passed (mock API fixtures; backend persistence tested separately)`);
} finally {
  await browser.close();
}
