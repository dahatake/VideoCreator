// Hover help and first-run tour in a real browser (Edge). Usage: node ui/verify-help.mjs [baseUrl]
import { chromium } from 'playwright';

const base = process.argv[2] ?? 'http://127.0.0.1:8765';
const failures = [];
const check = (ok, what) => { console.log(`${ok ? 'PASS' : 'FAIL'}: ${what}`); if (!ok) failures.push(what); };

const browser = await chromium.launch({ channel: 'msedge' });
const page = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
await page.goto(base);

// first launch: the tour opens by itself, walks through every step and is not shown again
await page.waitForSelector('.vc-tour-card', { timeout: 10000 });
const steps = Number((await page.textContent('#vc-tour-progress')).split('/')[1]);
check(steps === 6, `tour has ${steps} steps`);
check(!(await page.isVisible('.vc-tour-spot')), 'step 1 is a centered card');
await page.click('#vc-tour-next');
check(await page.isVisible('.vc-tour-spot'), 'step 2 highlights the create-project form');
for (let i = 1; i < steps - 1; i++) { await page.click('#vc-tour-next'); await page.waitForTimeout(80); }
check(/はじめる/.test(await page.textContent('#vc-tour-next')), 'last step offers "get started"');
await page.click('#vc-tour-next');
check(await page.locator('.vc-tour-card').count() === 0, 'tour closes');
await page.reload();
await page.waitForSelector('#settings-btn');
await page.waitForTimeout(800);
check(await page.locator('.vc-tour-card').count() === 0, 'tour is not shown again after it was seen');

// hover help
await page.hover('#settings-btn');
await page.waitForSelector('#vc-tip:popover-open', { timeout: 3000 });
check((await page.textContent('#vc-tip')).includes('AI'), 'hovering the settings button shows its explanation');
await page.mouse.move(5, 400);
await page.waitForTimeout(200);
check(!(await page.locator('#vc-tip:popover-open').count()), 'tip closes when the pointer leaves');

// settings: switches, and the tip shows above the modal dialog
await page.click('#settings-btn');
await page.waitForSelector('#helpTips');
await page.hover('#replay-tour');
await page.waitForSelector('#vc-tip:popover-open', { timeout: 3000 });
const tip = await page.locator('#vc-tip').boundingBox();
check(tip.y > 0 && tip.x >= 0 && (await page.locator('#vc-tip:popover-open').count()) === 1, 'tip is shown in the top layer while the modal settings dialog is open');
await page.evaluate(() => { document.querySelector('#helpTips').click(); });
await page.mouse.move(5, 400);
await page.hover('#settings-close');
await page.waitForTimeout(800);
check(!(await page.locator('#vc-tip:popover-open').count()), 'with the help switch off no tip appears');
check(await page.evaluate(() => localStorage.getItem('vc.help.tips')) === 'off', 'switch is stored');
await page.evaluate(() => { document.querySelector('#helpTips').click(); });

// replay from settings
await page.click('#replay-tour');
await page.waitForSelector('.vc-tour-card');
await page.waitForFunction(() => !document.querySelector('#helpTips').checkVisibility(), null, { timeout: 3000 }).then(() => check(true, 'replay closes the settings dialog'), () => check(false, 'replay closes the settings dialog'));
await page.keyboard.press('Escape');
check(await page.locator('.vc-tour-card').count() === 0, 'Escape closes the tour');

// tour switched off: a fresh profile never shows it
await page.evaluate(() => { localStorage.removeItem('vc.help.tourSeen'); localStorage.setItem('vc.help.tour', 'off'); });
await page.reload();
await page.waitForTimeout(800);
check(await page.locator('.vc-tour-card').count() === 0, 'tour switched off in settings is not shown on launch');

await browser.close();
if (failures.length) { console.error(`\n${failures.length} check(s) failed`); process.exit(1); }
console.log('\nAll help checks passed');
