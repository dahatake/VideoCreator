// Local UI verification only: candidate responses are fixtures, not a cloud E2E run.
import assert from 'node:assert/strict';
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:18765';
const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ locale: 'ja-JP', viewport: { width: 1440, height: 1200 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
let checks = 0;
function check(ok, name) {
  assert.ok(ok, name);
  checks++;
  console.log(`PASS ${name}`);
}
try {
  await page.goto(base);
  await page.waitForFunction(() => document.querySelector('#provider')?.textContent.includes('LLM不使用モード'));
  check(!(await page.locator('#provider').textContent()).includes('ローカルのみ'), 'Provider uses the non-misleading mode name');
  await page.locator('#name input').fill('PRD verification');
  await page.locator('form#new fluent-button').click();
  await page.waitForURL(/#\/p\//);
  await page.waitForSelector('#transmission');
  const notice = await page.locator('#transmission').textContent();
  check(notice.includes('https://speech.example.invalid'), 'Configured ASR destination is visible before generation');
  check(notice.includes('Entra ID'), 'Authentication destination is disclosed');
  check(notice.includes('完全オフラインではありません'), 'Audio transmission is explicitly disclosed');
  check(notice.includes('LLMには送信しません'), 'LLM transmission is explicitly disabled');
  await page.locator('#settings-btn').click();
  await page.waitForSelector('#settings-body #llm');
  const settings = await page.locator('#settings-body').textContent();
  check(!settings.includes('ローカルのみ'), 'Settings use the non-misleading mode name');
  check(settings.includes('https://speech.example.invalid'), 'Settings display the ASR destination');
  for (const value of ['azure-openai', 'copilot'])
    check(await page.locator(`#llm fluent-radio[value="${value}"]`).getAttribute('disabled') !== null, `${value} is disabled in LocalOnly mode`);
  await page.locator('#settings-close').click();

  const projectId = page.url().split('/').pop();
  const qa = [
    ...['H-01', 'H-05', 'H-06', 'H-07', 'H-08', 'H-09'].map(check_id => ({ check_id, stage: 'plan', status: 'pass' })),
    ...['H-02', 'H-03', 'H-04'].map(check_id => ({ check_id, stage: 'final', status: 'not_run' })),
  ];
  await page.route('**/api/jobs/prd-fixture', route => route.fulfill({ json: { id: 'prd-fixture', project_id: projectId, status: 'ready' } }));
  await page.route('**/api/jobs/prd-fixture/candidates', route => route.fulfill({ json: [{
    candidate: { id: 'c-prd', rank: 1, edit_plan_id: 'ep-prd', segments: [{ t0: 0, t1: 30 }],
      scores: { hook: 5, standalone: 5, payoff: 5, emotion: 5, signal: 5, final: 5 }, reason: 'UI fixture', notes: [] },
    preview: { qa },
  }] }));
  await page.evaluate(() => { location.hash = '#/job/prd-fixture'; });
  await page.waitForSelector('article.card');
  const badges = page.locator('article.card .badges');
  await page.waitForFunction(() => document.querySelector('article.card .badges')?.textContent.includes('未実施 3'));
  check((await badges.textContent()).includes('未実施 3（書き出し時に検査）'), 'Final-only checks are displayed as pending, not passed');
  check(await badges.locator('fluent-badge[color="success"]').count() === 0, 'Pending QA is not presented as an all-passed success badge');
  check(errors.length === 0, `No browser runtime errors: ${errors.join('; ')}`);
  console.log(`${checks}/${checks} UI checks passed (fixture candidates; no cloud requests)`);
} finally {
  await browser.close();
}
