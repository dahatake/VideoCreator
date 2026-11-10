// Browser fixtures exercise actual MP4 decoding; API availability/range/staleness contracts are tested by ExportArtifactTests.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const base = process.argv[2] ?? 'http://127.0.0.1:18788';
const temp = await mkdtemp(path.join(tmpdir(), 'vc-artifact-ui-'));
const repo = fileURLToPath(new URL('../', import.meta.url));
let browser;
try {
  const mediaPath = path.join(temp, 'final.mp4');
  execFileSync(path.join(repo, 'tools', 'ffmpeg', 'bin', 'ffmpeg.exe'), [
    '-hide_banner', '-loglevel', 'error', '-nostdin', '-y', '-f', 'lavfi', '-i', 'testsrc2=s=180x320:r=10:d=3',
    '-an', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', mediaPath,
  ]);
  const media = await readFile(mediaPath);
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1100 } });
  const errors = [], folderRequests = [], postRequests = [], previewRequests = [], artifactRequests = [];
  let thirdDone = false, missing = false, folderFails = false, postVariant = 0, stale = false, failArtifacts = false;
  const savedPath = n => `C:\\完成 動画\\動画${n}.mp4`;
  const kit = n => ({ youtube: `動画${n}の説明\n<script>not HTML</script>\n${postVariant ? '別の案' : '保存済み'}`,
    engage: `動画${n}の社内投稿`, chapters: '00:00 はじめに\n00:10 本題\n00:20 まとめ', source: 'template', notes: [] });
  const candidate = n => ({
    candidate: { id: `c-${n}`, rank: n, edit_plan_id: `ep-${n}`, headline: `見出し${n}`, segments: [{ t0: 0, t1: 3 }] },
    preview: { duration: 3, qa: [] }, approval: { at: '2026-11-11T00:00:00Z', stale: n === 1 && stale },
    final_render_id: n < 3 || thirdDone ? `r-${n}` : null, final_render_stale: n === 1 && stale,
    export: thirdDone && n === 3 ? { render_id: 'r-3', state: 'done' } : null,
  });
  const artifacts = n => ({
    path: savedPath(n), available: !(missing && n === 1),
    files: [
      { key: 'mp4', path: savedPath(n), available: !(missing && n === 1), preview_type: 'video' },
      { key: 'srt', path: savedPath(n) + '.srt', available: true, preview_type: 'text' },
      { key: 'metadata', path: savedPath(n) + '.metadata.md', available: true, preview_type: 'text' },
      { key: 'editplan', path: savedPath(n) + '.editplan.json', available: true, preview_type: 'text' },
      { key: 'thumb1', path: savedPath(n) + '.png', available: true, preview_type: 'image' },
      { key: 'ass', path: savedPath(n) + '.ass', available: false, preview_type: 'text' },
      ...(n < 3 ? [{ key: 'post', path: savedPath(n) + '.post.json', available: true, preview_type: 'text' }] : []),
    ],
  });
  let checks = 0;
  const check = (ok, name) => { assert.ok(ok, name); checks++; console.log(`PASS ${name}`); };
  page.on('pageerror', e => errors.push(e.message));
  // Capture the clipboard API boundary without overwriting the user's system clipboard.
  await page.addInitScript(() => {
    window.copied = [];
    window.copyFails = false;
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
      writeText: async text => { if (window.copyFails) throw new Error('denied'); window.copied.push(text); },
    } });
  });
  await page.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()), p = url.pathname;
    if (p === '/api/jobs/job-e') return route.fulfill({ json: { id: 'job-e', project_id: 'p-e', status: 'ready' } });
    if (p === '/api/jobs/job-e/candidates') return route.fulfill({ json: [1, 2, 3].map(candidate) });
    if (p === '/api/projects/p-e/workflow-state') return route.fulfill({ json: {} });
    if (p === '/api/jobs/job-e/exports') {
      assert.deepEqual(request.postDataJSON().candidate_ids, ['c-3']);
      thirdDone = true;
      return route.fulfill({ status: 202, json: { exports: [{ candidate_id: 'c-3', render_id: 'r-3' }] } });
    }
    if (p === '/api/renders/r-slow') {
      await new Promise(resolve => setTimeout(resolve, 300));
      return route.fulfill({ json: { state: 'done', render: { qa: [] }, editplan_id: 'ep-1', project_id: 'p-e' } });
    }
    let m = p.match(/^\/api\/renders\/r-(\d)$/);
    if (m) return route.fulfill({ json: { state: 'done', render: { qa: [] }, editplan_id: `ep-${m[1]}`, project_id: 'p-e' } });
    if (/^\/api\/editplans\/ep-\d$/.test(p)) return route.fulfill({ json: { plan: { provenance: { job_id: 'job-e' } } } });
    m = p.match(/^\/api\/renders\/r-(\d)\/artifacts$/);
    if (m) {
      artifactRequests.push(p);
      if (failArtifacts && m[1] === '1') return route.fulfill({ status: 500, json: { error: { code: 'E-INTERNAL', message: '取得失敗のテスト' } } });
      return route.fulfill({ json: artifacts(Number(m[1])) });
    }
    m = p.match(/^\/api\/renders\/r-(\d)\/post$/);
    if (m) {
      postRequests.push({ n: Number(m[1]), method: request.method() });
      if (request.method() === 'POST') postVariant++;
      return route.fulfill({ json: kit(Number(m[1])) });
    }
    m = p.match(/^\/api\/renders\/r-(\d)\/open-folder$/);
    if (m) {
      folderRequests.push(Number(m[1]));
      return folderFails
        ? route.fulfill({ status: 500, json: { error: { code: 'E-OPEN-FOLDER', message: 'フォルダーを開けませんでした' } } })
        : route.fulfill({ status: 202, body: '' });
    }
    m = p.match(/^\/api\/renders\/r-(\d)\/files\/([\w-]+)\/preview$/);
    if (m) {
      previewRequests.push(p);
      return route.fulfill({ json: { text: m[2] === 'srt' ? '1\n00:00:00,000 --> 00:00:02,000\n<script>字幕</script>' : '{"preview":"確認"}', truncated: m[2] === 'editplan' } });
    }
    if (/\/files\/thumb1$/.test(p)) return route.fulfill({
      contentType: 'image/png', body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64'),
    });
    if (/^\/api\/media\/r-\d$/.test(p)) {
      const range = request.headers().range?.match(/bytes=(\d+)-(\d*)/);
      if (range) {
        const start = Number(range[1]), end = range[2] ? Math.min(Number(range[2]), media.length - 1) : media.length - 1;
        return route.fulfill({ status: 206, headers: { 'Content-Type': 'video/mp4', 'Accept-Ranges': 'bytes', 'Content-Range': `bytes ${start}-${end}/${media.length}` }, body: media.subarray(start, end + 1) });
      }
      return route.fulfill({ contentType: 'video/mp4', body: media });
    }
    return route.continue();
  });
  await page.goto(`${base}/#/job/job-e/exports`);
  const panels = page.locator('.export-result');
  await page.waitForFunction(() => document.querySelectorAll('.post-text').length === 6);
  check(await page.locator('.final-video').count() === 2, 'Each completed video has its own final-file player');
  check(await panels.nth(0).locator('.artifact-path').inputValue() === savedPath(1), 'Absolute path with Japanese and spaces is shown verbatim');
  check(await panels.nth(1).locator('.artifact-path').inputValue() === savedPath(2), 'Second video has its own path');
  check(await page.locator('.post-text:not([readonly])').count() === 0, 'Saved post snippets are read-only');
  check(await panels.nth(1).locator('.post-text[data-key="engage"]').inputValue() === kit(2).engage, 'Post text belongs to the correct video');
  check(previewRequests.length === 0, 'Text artifacts load only when expanded');
  await panels.nth(0).locator('.final-video').evaluate(async video => {
    await video.play();
    video.pause();
    video.currentTime = 1.5;
    await new Promise((resolve, reject) => {
      video.addEventListener('seeked', resolve, { once: true });
      video.addEventListener('error', reject, { once: true });
    });
  });
  check(await panels.nth(0).locator('.final-video').evaluate(v => v.readyState >= 2 && Math.abs(v.currentTime - 1.5) < 0.1), 'Real H.264 MP4 decodes, plays and seeks');
  await panels.nth(1).locator('.path-copy').click();
  check(await page.evaluate(() => window.copied.at(-1)) === savedPath(2), 'Path-copy passes the exact path to the clipboard API');
  await panels.nth(1).locator('.post-copy[data-key="youtube"]').click();
  check(await page.evaluate(() => window.copied.at(-1)) === kit(2).youtube, 'Snippet-copy preserves line breaks and literal markup');
  await page.evaluate(() => { window.copyFails = true; });
  await panels.nth(0).locator('.post-copy[data-key="engage"]').click();
  check((await panels.nth(0).locator('.post-feedback').allTextContents()).some(t => t.includes('Ctrl+C')), 'Clipboard denial shows manual-copy guidance, not success');
  await page.evaluate(() => { window.copyFails = false; });
  await panels.nth(1).locator('.folder-open').click();
  await page.waitForFunction(() => document.querySelectorAll('.artifact-feedback')[1]?.textContent.includes('依頼'));
  check(folderRequests.join() === '2', 'Open-folder action targets only the corresponding export');
  folderFails = true;
  await panels.nth(0).locator('.folder-open').click();
  await page.waitForFunction(() => document.querySelector('.artifact-feedback')?.textContent.includes('E-OPEN-FOLDER'));
  check(true, 'Folder-opening failure is visible');
  await panels.nth(0).locator('.artifact-files > summary').click();
  const srt = panels.nth(0).locator('.artifact-file[data-key="srt"]');
  await srt.locator('summary').click();
  await srt.locator('pre').waitFor();
  check((await srt.locator('pre').textContent()).includes('<script>字幕</script>'), 'Subtitle preview renders literal text rather than HTML');
  check(await srt.locator('script').count() === 0, 'Preview cannot introduce script elements');
  check(await srt.locator('a[download]').getAttribute('href') === '/api/renders/r-1/files/srt', 'Artifact download uses the recorded output key');
  check(await srt.locator('a[download]').getAttribute('download') === '動画1.mp4.srt', 'Saving preserves the original artifact filename and extension');
  await panels.nth(0).locator('.artifact-file[data-key="editplan"] summary').click();
  await page.waitForFunction(() => document.querySelector('.artifact-file[data-key="editplan"]')?.textContent.includes('65,536'));
  check(true, 'Large text previews disclose truncation and link to full files');
  check((await panels.nth(0).locator('.artifact-file[data-key="ass"]').textContent()).includes('見つかりません'), 'Missing sidecar file is explicitly labeled');
  const thumb = panels.nth(0).locator('.artifact-image');
  await thumb.scrollIntoViewIfNeeded();
  await page.waitForFunction(() => document.querySelector('.artifact-image')?.naturalWidth > 0);
  check(true, 'Thumbnail preview loads an actual image');
  await panels.nth(0).locator('.post-again').click();
  await page.waitForFunction(() => document.querySelector('.post-text')?.value.includes('別の案'));
  check((await panels.nth(1).locator('.post-text[data-key="youtube"]').inputValue()).includes('保存済み'), 'Regeneration updates only the selected video');
  await page.locator('#batchExport').click();
  await page.waitForFunction(() => document.querySelectorAll('.final-video').length === 3);
  check(true, 'Newly finished batch export gains a player without navigating or reloading');
  check(!postRequests.some(r => r.n === 3), 'Missing post text does not trigger implicit generation');
  await panels.nth(2).locator('.post-create').click();
  await panels.nth(2).locator('.post-text').first().waitFor();
  check(postRequests.some(r => r.n === 3), 'Missing post text can be explicitly generated');
  check(await panels.nth(2).locator('a[href="/api/renders/r-3/files/post"]').count() === 1, 'New post data is linked immediately');
  if (process.argv[3]) await page.screenshot({ path: process.argv[3], fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Narrow screens have no horizontal overflow');
  await page.setViewportSize({ width: 1440, height: 1100 });
  missing = true;
  await panels.nth(0).locator('.artifact-reload').click();
  await page.waitForFunction(() => document.querySelector('.export-result')?.textContent.includes('動画ファイルが見つかりません'));
  check(await panels.nth(0).locator('video').count() === 0 && await panels.nth(0).locator('.folder-open').evaluate(b => b.disabled), 'Missing final video has no broken player and disables folder action');
  failArtifacts = true;
  await panels.nth(0).locator('.artifact-reload').click();
  await panels.nth(0).locator('.artifact-retry').waitFor();
  failArtifacts = false; missing = false;
  await panels.nth(0).locator('.artifact-retry').click();
  await panels.nth(0).locator('.final-video').waitFor();
  check(true, 'Artifact-fetch errors are visible and recover with retry');
  await page.goto(`${base}/#/render/r-2`);
  await page.waitForFunction(() => document.querySelector('h1')?.textContent === '書き出しが完了しました');
  await page.locator('.final-video').waitFor();
  check(await page.locator('.artifact-path').inputValue() === savedPath(2), 'Single-export detail reuses the same final-file and path panel');
  stale = true;
  const before = artifactRequests.filter(p => p.includes('r-1/')).length;
  await page.goto(`${base}/#/job/job-e/exports`);
  await page.waitForSelector('.export-item');
  check((await page.locator('.export-item').first().textContent()).includes('失効'), 'Edited-plan exports show a stale-result warning');
  check(await panels.nth(0).locator('video').count() === 0 && artifactRequests.filter(p => p.includes('r-1/')).length === before, 'Stale exports are not offered as current results');
  const slowRequest = page.waitForRequest('**/api/renders/r-slow');
  await page.goto(`${base}/#/render/r-slow`);
  await slowRequest;
  await page.evaluate(() => { location.hash = '#/job/job-e/exports'; });
  await page.waitForSelector('.export-item');
  await page.waitForTimeout(600);
  check(await page.locator('.export-item').count() === 3, 'Slow result requests cannot overwrite a newer route');
  check(errors.length === 0, `No browser errors: ${errors.join('; ')}`);
  console.log(`${checks}/${checks} browser checks passed (fixture API, real MP4 decoding, clipboard API captured without changing system clipboard)`);
} finally {
  await browser?.close();
  await rm(temp, { recursive: true, force: true });
}
