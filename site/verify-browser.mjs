// Browser verification of site/dist with an installed Chromium browser (playwright-core) and axe-core.
// Usage: node verify-browser.mjs [screenshotDir]   (BROWSER_CHANNEL=msedge|chrome, default msedge)
import { createServer } from 'node:http';
import { existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(here, 'dist');
const shots = process.argv[2] ? path.resolve(process.argv[2]) : null;
if (shots) mkdirSync(shots, { recursive: true });
const { siteUrl } = JSON.parse(readFileSync(path.join(dist, 'build-info.json'), 'utf8'));
const base = new URL(`${siteUrl}/`).pathname;
const axeSource = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.xml': 'application/xml', '.txt': 'text/plain' };

const server = createServer((req, res) => {
  const url = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (!url.startsWith(base)) { res.writeHead(404).end(); return; }
  let file = path.join(dist, url.slice(base.length));
  if (existsSync(file) && statSync(file).isDirectory()) file = path.join(file, 'index.html');
  if (!existsSync(file)) { res.writeHead(404, { 'content-type': types['.html'] }).end(readFileSync(path.join(dist, '404.html'))); return; }
  res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' }).end(readFileSync(file));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}${base}`;

const failures = [];
const check = (ok, message) => { if (!ok) failures.push(message); console.log(`${ok ? '✓' : '✗'} ${message}`); };
const browser = await chromium.launch({ channel: process.env.BROWSER_CHANNEL || 'msedge' });

async function open(context, url) {
  const page = await context.newPage();
  const problems = [];
  page.on('console', m => { if (m.type() === 'error') problems.push(`console: ${m.text()}`); });
  page.on('pageerror', e => problems.push(`pageerror: ${e.message}`));
  page.on('requestfailed', r => problems.push(`requestfailed: ${r.url()}`));
  page.on('response', r => { if (r.status() >= 400 && r.url().startsWith(origin)) problems.push(`HTTP ${r.status()}: ${r.url()}`); });
  await page.goto(origin + url, { waitUntil: 'load' });
  return { page, problems };
}

async function axe(page) {
  await page.addScriptTag({ content: axeSource });
  return page.evaluate(async () => {
    const result = await window.axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] } });
    return result.violations.map(v => `${v.id} (${v.impact}): ${v.nodes.length} node(s) e.g. ${v.nodes[0]?.target.join(' ')}`);
  });
}

const pages = ['', 'users/', 'engineers/', 'patterns/', 'guide/quickstart/', 'guide/screens/', 'guide/technical-reference/', 'docs/prd/',
  'en/', 'en/patterns/', 'en/engineers/', 'ko/', 'ko/guide/quickstart/', 'zh-CN/', 'zh-CN/users/'];
try {
  for (const scheme of ['dark', 'light']) {
    const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, colorScheme: scheme, locale: 'ja-JP' });
    for (const url of pages) {
      if (scheme === 'light' && !['', 'patterns/', 'guide/quickstart/', 'en/engineers/'].includes(url)) continue;
      const { page, problems } = await open(context, url);
      await page.evaluate(() => document.querySelectorAll('.reveal').forEach(el => el.classList.add('in')));
      await page.waitForTimeout(url === 'docs/prd/' ? 2500 : 900);
      if (url === 'docs/prd/') {
        await page.locator('pre.mermaid').first().scrollIntoViewIfNeeded();
        await page.waitForFunction(() => document.querySelectorAll('pre.mermaid svg').length > 0, null, { timeout: 15000 }).catch(() => {});
        check(await page.locator('pre.mermaid svg').count() > 0, `${scheme} ${url}: Mermaid diagrams rendered`);
      }
      const violations = await axe(page);
      check(violations.length === 0, `${scheme} /${url}: axe WCAG A/AA ${violations.length ? violations.join('; ') : 'no violations'}`);
      check(problems.length === 0, `${scheme} /${url}: no console/network errors ${problems.join('; ')}`);
      if (shots && (scheme === 'dark' || url === '')) await page.screenshot({ path: path.join(shots, `${scheme}-${url.replace(/\//g, '_') || 'home'}.png`), fullPage: url === '' || url === 'patterns/' || url === 'en/' });
      await page.close();
    }
    await context.close();
  }

  // interactions
  const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, locale: 'en-US' });
  const { page } = await open(context, '');
  check(await page.locator('.lang-suggest:not([hidden]) a[lang="en"]').count() === 1, 'language suggestion offered to an en-US browser on the ja home (no auto-redirect)');
  check(page.url() === origin, 'no automatic redirect');
  const stages = new Set();
  for (let i = 0; i < 12; i++) { stages.add(await page.locator('.studio').getAttribute('data-stage')); await page.waitForTimeout(500); }
  check(stages.size >= 3, `hero animates through stages (${[...stages].join(',')})`);
  await page.click('.motion-toggle');
  const frozen = await page.locator('.studio').getAttribute('data-stage');
  await page.waitForTimeout(2200);
  check(frozen === '4' && await page.locator('.studio').getAttribute('data-stage') === '4', 'pause button freezes the hero on the final frame');
  await page.click('#step-tab-2');
  check(await page.locator('#step-2.active').count() === 1, 'step tab click shows its panel');
  await page.keyboard.press('ArrowRight');
  check(await page.locator('#step-3.active').count() === 1 && await page.evaluate(() => document.activeElement.id) === 'step-tab-3', 'arrow keys move between step tabs');
  await page.click('.theme-toggle');
  const theme = await page.evaluate(() => document.documentElement.dataset.theme);
  await page.reload();
  check(await page.evaluate(() => document.documentElement.dataset.theme) === theme, `theme choice persists (${theme})`);
  await page.keyboard.press('Control+k');
  await page.keyboard.type('PRD');
  await page.waitForSelector('.search-results a');
  check(await page.locator('.search-results a').count() > 0, 'Ctrl+K search returns results');
  await Promise.all([page.waitForURL(/\/docs\/prd\//, { timeout: 5000 }).catch(() => {}), page.keyboard.press('Enter')]);
  check(page.url().includes('/docs/prd/'), `search Enter navigates (${page.url().replace(origin, '/')})`);
  await page.goto(`${origin}patterns/`);
  await page.click('[data-sort="len"]');
  await page.waitForTimeout(700);
  const firstBar = await page.locator('.bars .bar-row').first().getAttribute('data-id');
  check(firstBar === 'P9', `chart sorts by length (first: ${firstBar})`);
  const genreButton = page.locator('.genre-filter [data-genre]').nth(1);
  await genreButton.click();
  const visible = await page.locator('.pattern:not([hidden])').count();
  check(visible > 0 && visible < 12, `genre filter narrows patterns (${visible})`);
  check(await page.locator('.bar-row.dim').count() === 12 - visible, 'chart dims filtered-out patterns');
  await page.goto(`${origin}en/engineers/`);
  const rows = await page.locator('.map-table tbody tr').count();
  await page.fill('[data-filter-table]', 'preset');
  const shown = await page.locator('.map-table tbody tr:not([hidden])').count();
  check(shown > 0 && shown < rows, `customization map filter (${shown}/${rows})`);
  await page.goto(`${origin}en/guide/quickstart/`);
  await page.click('.lang-menu summary');
  await page.click('.lang-menu a[lang="ja"]');
  await page.waitForLoadState('load');
  check(page.url() === `${origin}guide/quickstart/`, 'language menu keeps the same page');
  check(await page.evaluate(() => localStorage.getItem('vc-lang')) === 'ja', 'language choice stored');
  const { page: missing } = await open(context, 'no/such/page/');
  check(await missing.locator('h1').textContent() === '404' && await missing.locator('link[rel=stylesheet]').evaluate(l => l.sheet !== null), '404 page is styled under a deep path');
  await context.close();

  // reduced motion
  const calm = await browser.newContext({ viewport: { width: 1366, height: 900 }, reducedMotion: 'reduce' });
  const { page: still } = await open(calm, '');
  const seen = new Set();
  for (let i = 0; i < 8; i++) { seen.add(await still.locator('.studio').getAttribute('data-stage')); await still.waitForTimeout(400); }
  check(seen.size === 1 && seen.has('4'), 'reduced motion: hero stays on the static final frame');
  check(await still.evaluate(() => getComputedStyle(document.querySelector('.aurora i')).animationName) === 'none', 'reduced motion: ambient animation off');
  check(await still.evaluate(() => getComputedStyle(document.querySelector('.reveal')).opacity) === '1', 'reduced motion: content visible without scroll reveal');
  await calm.close();

  // mobile + no-JS
  const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  for (const url of ['', 'patterns/', 'guide/technical-reference/']) {
    const { page: p } = await open(phone, url);
    const overflow = await p.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    check(overflow <= 1, `mobile /${url}: no horizontal page overflow (${overflow}px)`);
    const settled = await p.evaluate(async () => {
      const h = document.querySelector('h1');
      await Promise.all(h.getAnimations({ subtree: true }).map(a => a.finished));
      return [h, ...h.querySelectorAll('span')].every(e => { const s = getComputedStyle(e); return s.opacity === '1' && /^(none|blur\(0px\))$/.test(s.filter); });
    });
    check(settled, `mobile /${url}: h1 entrance animation settles (opaque, unblurred)`);
    if (shots) await p.screenshot({ path: path.join(shots, `mobile-${url.replace(/\//g, '_') || 'home'}.png`) });
    await p.close();
  }
  await phone.close();
  const nojs = await browser.newContext({ javaScriptEnabled: false });
  const { page: plain } = await open(nojs, 'en/');
  check(await plain.locator('#step-4').isVisible() && await plain.locator('.feature').first().isVisible(), 'without JavaScript all content is visible');
  await nojs.close();
} finally {
  await browser.close();
  server.close();
}
console.log(failures.length ? `\n${failures.length} failure(s)` : '\nAll browser checks passed.');
process.exit(failures.length ? 1 : 0);
