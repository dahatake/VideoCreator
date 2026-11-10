// UI fixtures only: one job with several videos — review each (step 5), return to candidates, export the approved ones together (step 6).
// Approval binding, all-or-nothing batch validation and the export queue are tested by the .NET tests.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:18768';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
const errors = [], approvals = [], workflow = [];
let exportBody = null, polls = 0, count = 0, refused = false;
page.on('pageerror', e => errors.push(e.message));
const check = (ok, name) => { assert.ok(ok, name); count++; console.log(`PASS ${name}`); };
const ids = [1, 2, 3];
const approved = new Map();
const plan = (n) => ({
  editplan_id: `ep-${n}`, project_id: 'p-m', preset: { id: 'P4', version: '1.0' },
  timeline: [{ clip_id: 'c', asset_id: 'a', src_in: 0, src_out: 30, speed: 1, role: 'body', reframe: {} }],
  audio: { master: 'a', sfx_enabled: false, sfx: [] }, inserts: [],
  telops: [{ id: 't', type: 'caption', start: 1, end: 3, text: `動画${n}の字幕`, enabled: true, approved: true, source_word_ids: [] }],
  provenance: { job_id: 'job-m', candidate_id: `c-${n}` },
});
const approvalOf = (n) => approved.get(n) ? { at: '2026-11-10T00:00:00Z', by: 'UI fixture', stale: false } : null;
const exportOf = (n) => {
  if (!exportBody?.candidate_ids.includes(`c-${n}`)) return null;
  return polls < 2 ? { render_id: `r-${n}`, state: 'rendering', progress: 0.5, step: '映像' } : { render_id: `r-${n}`, state: 'done', progress: 1 };
};
const item = (n) => ({
  candidate: { id: `c-${n}`, rank: n, edit_plan_id: `ep-${n}`, headline: `見出し${n}`, hook_text: '', reason: 'UI fixture', notes: [],
    segments: [{ t0: n * 100, t1: n * 100 + 30 }], scores: { hook: 5, standalone: 5, payoff: 5, emotion: 5, signal: 5, final: 5 } },
  preview: { duration: 30, qa: [] }, preview_stale: false, approval: approvalOf(n), export: exportOf(n),
  final_render_id: exportOf(n)?.state === 'done' ? `r-${n}` : null, final_render_stale: false,
});
try {
  await page.route('**/api/**', async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path === '/api/jobs/job-m') return route.fulfill({ json: { id: 'job-m', project_id: 'p-m', status: 'ready', warnings: [] } });
    if (path === '/api/jobs/job-m/candidates') { if (exportBody) polls++; return route.fulfill({ json: ids.map(item) }); }
    let m = path.match(/^\/api\/jobs\/job-m\/candidates\/c-(\d)\/approval$/);
    if (m) {
      const n = Number(m[1]), body = request.postDataJSON();
      approvals.push({ n, approved: body.approved });
      approved.set(n, body.approved);
      return route.fulfill({ json: { candidate_id: `c-${n}`, approval: approvalOf(n) } });
    }
    if (path === '/api/jobs/job-m/exports') {
      // first attempt: refused like the server does for a hard QA failure (nothing started, per-video code and failed checks)
      if (!refused) {
        refused = true;
        return route.fulfill({ status: 422, json: { error: { code: 'E-QA-HARDFAIL', message: '書き出せない候補があるため、どれも書き出していません',
          detail: [{ candidate_id: 'c-2', name: '#2', code: 'E-QA-HARDFAIL', reason: '自動チェックで不合格の項目があります',
            detail: [{ check_id: 'H-08', status: 'fail', message: '未承認テロップ', measured: '1', threshold: '0' }] }] } } });
      }
      exportBody = request.postDataJSON();
      return route.fulfill({ status: 202, json: { exports: exportBody.candidate_ids.map(c => ({ candidate_id: c, render_id: `r-${c.slice(2)}` })) } });
    }
    m = path.match(/^\/api\/editplans\/ep-(\d)$/);
    if (m) { const n = Number(m[1]); return route.fulfill({ json: { plan: plan(n), plan_hash: `h${n}`, qa: [], approval: approvalOf(n), final_render_stale: false, preview_stale: false } }); }
    if (path === '/api/projects/p-m') return route.fulfill({ json: { assets: [{ id: 'a', path: 'source.mp4' }] } });
    if (path === '/api/projects/p-m/workflow-state') { workflow.push(request.postDataJSON()); return route.fulfill({ json: {} }); }
    if (path === '/api/assets/a/transcript') return route.fulfill({ json: { words: [], sentences: [] } });
    if (path === '/api/bgm') return route.fulfill({ json: [] });
    return route.continue();
  });

  // step 4: candidates show the per-video progress and a way into step 6
  await page.goto(`${base}/#/job/job-m`);
  await page.waitForSelector('#workProgress');
  check((await page.locator('#workProgress').textContent()).includes('承認 0 / 3'), 'Candidates show how many videos are approved');
  check(await page.locator('#nextReview').getAttribute('href') === '#/ep/ep-1', 'Next-to-review points at the first unapproved video');
  check(await page.locator('#steps a[href="#/job/job-m/exports"]').count() === 1, 'Step 6 (書き出し) is reachable from the step bar');

  // step 5, video 1: approve and go straight to the next video
  await page.goto(`${base}/#/ep/ep-1`);
  await page.waitForSelector('#approveNext');
  check(await page.locator('#videoSel option').count() === 3, 'Review has a menu to switch between the videos of the job');
  check((await page.locator('.video-nav').textContent()).includes('動画 1 / 3'), 'Review shows which video is being reviewed');
  check((await page.locator('#approveNext').textContent()).includes('候補 #2'), 'Primary action names the next video to review');
  check((await page.locator('#export').textContent()).includes('この1本だけ'), 'Immediate single export is a secondary action');
  await page.locator('#approveNext').click();
  await page.waitForURL(/#\/ep\/ep-2$/);
  check(approvals.length === 1 && approvals[0].n === 1 && approvals[0].approved, 'Approve-and-next approves only the reviewed video');
  await page.waitForSelector('#prevVideo');
  check((await page.locator('#videoSel option').first().textContent()).includes('承認済み'), 'Video menu shows each video status');

  // video 2: approve and return to candidates
  await page.locator('#approveBack').click();
  await page.waitForURL(/#\/job\/job-m$/);
  await page.waitForSelector('#workProgress');
  check((await page.locator('#workProgress').textContent()).includes('承認 2 / 3'), 'Back on candidates the progress reflects the approvals');
  check(await page.locator('#nextReview').getAttribute('href') === '#/ep/ep-3', 'Next-to-review skips approved videos');

  // revisiting an approved video and returning does not re-approve it
  await page.goto(`${base}/#/ep/ep-1`);
  await page.waitForSelector('#approveBack');
  await page.locator('#approveBack').click();
  await page.waitForURL(/#\/job\/job-m$/);
  await page.waitForSelector('#workProgress');
  check(approvals.length === 2, 'An up-to-date approval is not stamped again');

  // the queue is re-read on click: video 3 approved elsewhere after this screen opened -> step 6, not video 3
  await page.goto(`${base}/#/ep/ep-2`);
  await page.waitForFunction(() => document.querySelector('#approveNext')?.textContent.includes('候補 #3'));
  approved.set(3, true);
  await page.locator('#approveNext').click();
  await page.waitForURL(/#\/job\/job-m\/exports$/);
  await page.waitForSelector('.export-item');
  check(approvals.length === 2, 'A stale review queue does not send the user back to an already-approved video');
  approved.delete(3);

  // video 3 (last one left): the primary action leads to step 6
  await page.goto(`${base}/#/ep/ep-3`);
  await page.waitForFunction(() => document.querySelector('.video-nav')?.textContent.includes('動画 3 / 3') && document.querySelector('#approveNext'));
  check((await page.locator('#approveNext').textContent()).includes('書き出しへ進む'), 'When every video is approved the primary action is to go to export');
  await page.locator('#approveNext').click();
  await page.waitForURL(/#\/job\/job-m\/exports$/);

  // step 6: the approved videos are preselected and exported in one go
  await page.waitForSelector('.export-item');
  check(await page.locator('.export-item').count() === 3, 'Export screen lists every video of the job');
  check(workflow.some(w => w.step === 'exports' && w.job_id === 'job-m'), 'Export screen is saved as the resume point');
  check(await page.locator('#steps [aria-current="step"]').textContent().then(t => t.includes('書き出し')), 'Step bar marks 書き出し as current');
  await page.waitForFunction(() => document.querySelector('#batchExport')?.textContent.includes('3 本'));
  check(true, 'Approved, not-yet-exported videos are preselected');
  await page.locator('#batchExport').click();
  await page.waitForFunction(() => document.querySelector('#batchMsg')?.textContent.includes('E-QA-HARDFAIL'));
  const refusal = await page.locator('#batchMsg').textContent();
  check(refusal.includes('#2') && refusal.includes('H-08') && refusal.includes('不合格'), 'A refused batch lists each video with its failed hard checks (same code as single export)');
  check(exportBody === null, 'Nothing is exported when the batch is refused');
  await page.waitForFunction(() => document.querySelector('#batchExport')?.textContent.includes('3 本') && !document.querySelector('#batchExport').disabled);
  await page.locator('#batchExport').click();
  await page.waitForFunction(() => document.querySelector('#batchLabel')?.textContent.includes('3 / 3'), null, { timeout: 10000 });
  check(exportBody.candidate_ids.join() === 'c-1,c-2,c-3', 'One batch request exports all selected videos');
  await page.waitForFunction(() => document.querySelector('#workProgress')?.textContent.includes('書き出し済み 3 / 3'), null, { timeout: 10000 });
  check(await page.locator('.exp-text a[href="#/render/r-2"]').count() === 1, 'Each exported video links to its result');
  check(errors.length === 0, `No browser errors: ${errors.join('; ')}`);
  console.log(`${count}/${count} UI checks passed (mock API fixtures; backend batch export tested separately)`);
} finally {
  await browser.close();
}
