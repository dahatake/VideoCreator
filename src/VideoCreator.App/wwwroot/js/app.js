// VideoCreator UI — vanilla ES modules + Fluent 2 web components.
// Every dynamic string is escaped with esc() before reaching innerHTML (LLM/user text is untrusted).
import { trUi, getLanguage, setLanguage, formatDate, formatNumber, applyShell, VIDEO_LANGUAGES } from './i18n.js';
import { serverText, localizePreset, localizeQa, jobMessage, errorMessage, libraryTrackName } from './server-i18n.js';
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
// same wording as Planning.BgmPlanner.SceneWord / TrackWord
const energyWord = (e) => e < 0.35 ? trUi('m001') : e < 0.6 ? trUi('m002') : trUi('m003');
const main = $('#main');
function videoLanguageOptions(selected) {
  return VIDEO_LANGUAGES.map(value => `<option value="${value}" ${value === selected ? 'selected' : ''}>${trUi(`video.${value}`)}</option>`).join('');
}

const stepLabels = () => [trUi('m004'), trUi('m005'), trUi('m006'), trUi('m007'), trUi('m008'), trUi('m009')];
const stageLabels = () => [
  ['ingesting', trUi('m010')], ['transcribing', trUi('m011')], ['analyzing', trUi('m012')],
  ['selecting', trUi('m013')], ['planning', trUi('m014')], ['previewing', trUi('m015')],
];
const errorHints = () => ({ // UX-04: plain-language explanation + what to do next
  'E-RIGHTS-REQUIRED': trUi('m016'),
  'E-ING-UNSUPPORTED': trUi('m017'),
  'E-ASR-PROVIDER': trUi('m018'),
  'E-SOL-INFEASIBLE': trUi('m019'),
  'E-QA-HARDFAIL': trUi('m020'),
  'E-PRESET-UNAVAILABLE': trUi('m021'),
  'E-RND-FFMPEG': trUi('m022'),
  'E-PROJECT-BUSY': trUi('m023'),
  'E-FILE-IN-USE': trUi('m024'),
});

async function api(path, opts = {}) {
  const init = { method: opts.method ?? 'GET', headers: { 'X-VideoCreator': '1' }, signal: opts.signal };
  if (opts.body !== undefined) { init.headers['Content-Type'] = 'application/json'; init.body = JSON.stringify(opts.body); }
  const r = await fetch(path, init);
  const text = await r.text();
  const data = text ? JSON.parse(text) : null;
  if (!r.ok) { const e = new Error(data?.error?.message ?? r.statusText); e.code = data?.error?.code; e.detail = data?.error?.detail; e.status = r.status; throw e; }
  return data;
}

function errorBar(e) {
  const hint = errorHints()[e.code] ?? '';
  return `<fluent-message-bar intent="error" layout="multiline"><b>${esc(errorMessage(e))}</b>${hint ? `<br>${esc(hint)}` : ''}${e.code ? `<span class="caption"> (${esc(e.code)})</span>` : ''}</fluent-message-bar>`;
}
function info(msg, intent = 'info') { return `<fluent-message-bar intent="${intent}" layout="multiline">${esc(msg)}</fluent-message-bar>`; }
function setSteps(current, done = current, links = {}) {
  $('#steps').innerHTML = current < 0 ? '' : stepLabels().map((s, i) => {
    const cls = `step ${i === current ? 'current' : i < done ? 'done' : ''}`;
    const inner = `<span class="n">${i < done && i !== current ? '✓' : i + 1}</span>${s}`;
    // a finished step is a link back to it: redoing it starts a new pattern, the earlier results stay
    return links[i] && i !== current
      ? `<a class="${cls}" href="${esc(links[i])}" aria-label="${trUi('m025', [esc(s)])}">${inner}</a>`
      : `<span class="${cls}" ${i === current ? 'aria-current="step"' : ''}>${inner}</span>`;
  }).join('');
}
function phone(src, id = '') {
  return `<div class="phone" ${id ? `id="${id}"` : ''}><video src="${esc(src)}" controls playsinline preload="metadata"></video><div class="safezone" aria-hidden="true"><i class="t"></i><i class="b"></i><i class="l"></i><i class="r"></i></div></div>`;
}
function field(label, control) { return `<fluent-field label-position="after"><label slot="label">${label}</label>${control}</fluent-field>`; }
// WAI-ARIA radio group on custom role=radio elements: one tab stop (the checked radio), arrows move and select, disabled radios are skipped.
// Returns a function that re-syncs the tab stop after a selection made elsewhere.
function radioGroup(radios, pick) {
  const enabled = () => radios.filter(r => r.getAttribute('aria-disabled') !== 'true');
  const sync = () => { const en = enabled(); const cur = en.find(r => r.getAttribute('aria-checked') === 'true') ?? en[0]; radios.forEach(r => { r.tabIndex = r === cur ? 0 : -1; }); };
  const select = (r) => { if (r.getAttribute('aria-disabled') === 'true') return; pick(r); sync(); };
  radios.forEach(r => {
    r.addEventListener('click', () => select(r));
    r.addEventListener('keydown', (e) => {
      if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); select(r); return; }
      const d = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 }[e.key];
      if (!d) return;
      e.preventDefault();
      const en = enabled(); if (!en.length) return;
      const next = en[(en.indexOf(r) + d + en.length) % en.length];
      select(next); next.focus();
    });
  });
  sync();
  return sync;
}
function safeToggle() { return field(trUi('m026'), '<fluent-switch slot="input" class="safe-toggle"></fluent-switch>'); }
function wireSafeToggle(root = main) {
  $$('.safe-toggle', root).forEach(sw => sw.addEventListener('change', () => root.classList.toggle('show-safe', sw.checked)));
}
function qaList(qa) {
  qa = qa.map(localizeQa);
  return qa.map(q => `<div class="qa-row"><b>${esc(q.check_id)}</b><span class="${q.status === 'pass' ? 'pass' : q.status === 'fail' ? 'fail' : q.status === 'not_run' ? 'muted' : 'warnc'}">${q.status === 'pass' ? trUi('m027') : q.status === 'fail' ? trUi('m028') : q.status === 'not_run' ? trUi('m029') : trUi('m030')}</span><span>${esc(q.message)}: ${esc(q.measured)} <span class="muted">${trUi('m032', [esc(q.threshold), q.stage ? trUi('m031', [esc(q.stage)]) : ''])}</span></span></div>`).join('');
}
function qaBadges(qa) {
  if (!qa?.length) return '';
  const hard = qa.filter(q => q.check_id.startsWith('H-'));
  const fails = hard.filter(q => q.status === 'fail').length, warns = qa.filter(q => q.status === 'warn').length;
  const pending = hard.filter(q => q.status === 'not_run').length;
  return `<fluent-badge appearance="tint" color="${fails ? 'danger' : pending ? 'warning' : 'success'}">QA ${fails ? trUi('m033', [fails]) : trUi('m034', [hard.filter(q => q.status === 'pass').length, hard.length])}</fluent-badge>${pending ? `<fluent-badge appearance="tint" color="warning">${trUi('m035', [pending])}</fluent-badge>` : ''}${warns ? `<fluent-badge appearance="tint" color="warning">${trUi('m036', [warns])}</fluent-badge>` : ''}`;
}

// ---------------- settings / provider ----------------
async function refreshProvider() {
  try {
    const h = await api('/api/health');
    const names = { 'azure-openai': `Microsoft Foundry（${h.llm_model ?? ''}）`, copilot: trUi('m037'), none: trUi('m038') };
    const sel = h.llm_provider !== 'none' && !h.llm_model ? trUi('m039') : names[h.llm_provider] ?? h.llm_provider;
    $('#provider').textContent = trUi('m042', [h.local_only ? trUi('m040') : '', h.asr === 'none' ? trUi('m041') : 'Azure AI Speech', sel]);
    return h;
  } catch { $('#provider').textContent = ''; }
}
function llmChoice(value, title, lines, unavailable) {
  return `<fluent-field label-position="after" class="llm-choice">
    <label slot="label"><b>${esc(title)}</b>${lines.map(l => `<span class="caption">${esc(l)}</span>`).join('')}${unavailable ? `<span class="caption warnc">${trUi('m043', [esc(unavailable)])}</span>` : ''}</label>
    <fluent-radio slot="input" value="${value}" ${unavailable ? 'disabled' : ''}></fluent-radio>
  </fluent-field>`;
}
function transmissionNotice(s) {
  const t = s.transmission;
  if (!t) return info(trUi('m044'), 'warning');
  return info(`${t.llm_disabled ? trUi('m045') : trUi('m047', [t.llm_destination ?? trUi('m046')])}
    ${t.asr_endpoint ? trUi('m048', [t.asr_endpoint, serverText(t.authentication)]) : trUi('m049')}`);
}
// Same disclosure as transmissionNotice, condensed to a caption so it stays visible above the generate button.
function transmissionCompact(s) {
  const t = s.transmission;
  if (!t) return transmissionNotice(s);
  const same = !t.llm_disabled && t.asr_endpoint && t.llm_destination && t.llm_destination.replace(/\/+$/, '') === t.asr_endpoint.replace(/\/+$/, '');
  const off = trUi('m050', [esc(serverText(t.authentication))]);
  if (same) return `<p class="caption send-to"><b>${trUi('m051')}</b> ${trUi('m052', [esc(t.asr_endpoint), off])}</p>`;
  const llm = t.llm_disabled ? trUi('m053') : trUi('m054', [esc(t.llm_destination ?? trUi('m046'))]);
  const asr = t.asr_endpoint ? trUi('m055', [esc(t.asr_endpoint), off]) : trUi('m056');
  return `<p class="caption send-to"><b>${trUi('m051')}</b> ${llm}／${asr}</p>`;
}
async function openSettings() {
  const s = await api('/api/settings'); const h = await api('/api/health'); const pd = await api('/api/settings/projects-dir');
  const local = s.local_only ? trUi('m057') : '';
  const choices = [
    ['azure-openai', `Microsoft Foundry（${s.azure_deployment}）`, [
      trUi('m058'),
      trUi('m059'),
      trUi('m060'),
    ], local || (s.azure_configured ? '' : trUi('m061'))],
    ['copilot', `GitHub Copilot（${s.copilot_model ?? trUi('m062')}）`, [
      trUi('m063'),
      trUi('m064'),
      trUi('m065'),
    ], local || (s.copilot_cli ? '' : trUi('m066'))],
    ['none', trUi('m067'), [
      trUi('m068'),
      trUi('m069'),
    ], ''],
  ];
  const cur = choices.find(c => c[0] === s.llm_provider);
  $('#settings-body').innerHTML = `
    <p id="llm-label">${trUi('m072', [s.asr_configured ? '（Azure AI Speech）' : ''])}</p>
    ${transmissionNotice(s)}
    ${!s.local_only && cur?.[3] ? info(trUi('m070', [cur[1]]), 'warning') : ''}
    <fluent-radio-group id="llm" orientation="vertical" aria-labelledby="llm-label" value="${esc(s.llm_provider)}">
      ${choices.map(([v, t, l, u]) => llmChoice(v, t, l, u)).join('')}
    </fluent-radio-group>
    <p class="caption">${trUi('m073')}</p>
    <fluent-divider style="margin:16px 0"></fluent-divider>
    ${projectsDirSection(pd)}
    <fluent-divider style="margin:16px 0"></fluent-divider>
    <dl class="facts"><dt>${trUi('m074')}</dt><dd id="app-version">${esc(h.version)}</dd><dt>${trUi('m075')}</dt><dd>${esc(h.informational_version)}</dd><dt>FFmpeg</dt><dd>${esc(h.ffmpeg)}</dd><dt>${trUi('m076')}</dt><dd>${esc(h.missing_filters.join(', ') || trUi('m041'))}</dd><dt>Copilot CLI</dt><dd>${esc(s.copilot_cli ?? trUi('m071'))}</dd><dt>${trUi('m077')}</dt><dd>${h.presets}</dd></dl>`;
  wireProjectsDir(pd);
  $('#llm')?.addEventListener('change', async () => {
    const radios = $$('#llm fluent-radio');
    const value = radios.find(r => r.checked)?.value;
    if (!value || value === s.llm_provider) return;
    try { await api('/api/settings', { method: 'PUT', body: { llm_provider: value } }); s.llm_provider = value; $('#settings-body fluent-message-bar[intent=warning]')?.remove(); await refreshProvider(); const current = await api('/api/settings'); if ($('#transmission')) $('#transmission').innerHTML = transmissionCompact(current); }
    catch (e) { radios.forEach(r => { r.checked = r.value === s.llm_provider; }); $('#settings-body').insertAdjacentHTML('afterbegin', errorBar(e)); }
  });
  $('#settings-dialog').show();
}
$('#settings-btn').addEventListener('click', () => openSettings().catch(e => {
  $('#settings-body').innerHTML = errorBar(e);
  $('#settings-dialog').show();
}));
$('#settings-close').addEventListener('click', () => $('#settings-dialog').hide());

// ---------------- settings: projects folder ----------------
const bytes = (b) => b >= 2 ** 30 ? `${(b / 2 ** 30).toFixed(1)} GB` : b >= 2 ** 20 ? `${(b / 2 ** 20).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`;
function projectsDirSection(pd) {
  const source = { default: trUi('m078'), config: trUi('m079'), app: trUi('m080') }[pd.source] ?? '';
  return `<h3 id="pdirLabel" style="margin:0 0 4px">${trUi('m083')}</h3>
    <p class="caption">${trUi('m084')} <b id="pdirNow">${esc(pd.path)}</b>${trUi('m085', [esc(source), pd.projects])}<br>${trUi('m086')}</p>
    <div class="row" style="align-items:center"><fluent-text-input id="pdirPath" style="flex:1" placeholder="D:\\Videos\\VideoCreator" aria-label="${trUi('m087')}"></fluent-text-input><fluent-button id="pdirBrowse" type="button">${trUi('m088')}</fluent-button></div>
    <p class="caption" id="pdirMoveLabel">${trUi('m089')}</p>
    <fluent-radio-group id="pdirMove" orientation="vertical" aria-labelledby="pdirMoveLabel">
      ${field(trUi('m081'), '<fluent-radio slot="input" value="move"></fluent-radio>')}
      ${field(trUi('m082'), '<fluent-radio slot="input" value="keep"></fluent-radio>')}
    </fluent-radio-group>
    <div><fluent-button id="pdirCheck" type="button">${trUi('m090')}</fluent-button></div>
    <div id="pdirPlan"></div>
    <div class="progress-row" id="pdirRow" hidden><span class="caption" id="pdirStep"></span><fluent-progress-bar id="pdirBar" max="100" aria-label="${trUi('m091')}"></fluent-progress-bar><span class="caption pct" id="pdirPct"></span></div>
    <div id="pdirMsg" aria-live="polite"></div>`;
}
function wireProjectsDir(pd) {
  const choice = () => $$('#pdirMove fluent-radio').find(r => r.checked)?.value;
  const reset = () => { $('#pdirPlan').innerHTML = ''; };
  $('#pdirPath').addEventListener('input', reset);
  $('#pdirMove').addEventListener('change', reset);
  $('#pdirBrowse').addEventListener('click', async () => {
    const b = $('#pdirBrowse'); b.disabled = true;
    $('#pdirMsg').innerHTML = info(trUi('m092'));
    try {
      const r = await api('/api/dialogs/folder', { method: 'POST', body: { initial_path: $('#pdirPath').value || pd.path } });
      if (r.path != null) { $('#pdirPath').value = r.path; reset(); }
      $('#pdirMsg').innerHTML = r.path == null ? info(trUi('m093')) : '';
    } catch (e) { $('#pdirMsg').innerHTML = errorBar(e); }
    finally { b.disabled = false; }
  });
  $('#pdirCheck').addEventListener('click', async () => {
    $('#pdirMsg').innerHTML = '';
    const path = $('#pdirPath').value ?? '', how = choice();
    if (!path.trim()) { $('#pdirMsg').innerHTML = info(trUi('m094'), 'warning'); return; }
    if (!how) { $('#pdirMsg').innerHTML = info(trUi('m095'), 'warning'); return; }
    let plan;
    try { plan = await api('/api/settings/projects-dir/plan', { method: 'POST', body: { path, move: how === 'move' } }); }
    catch (e) { $('#pdirMsg').innerHTML = errorBar(e); return; }
    const projects = plan.items.filter(i => i.project_id), others = plan.items.filter(i => !i.project_id);
    const list = (xs) => `<ul class="notes">${xs.slice(0, 8).map(i => `<li>${esc(i.project_name ?? i.name)}${i.project_name ? `<span class="caption">（${esc(i.name)}）</span>` : ''} · ${bytes(i.bytes)}${i.is_directory ? trUi('m096', [i.files]) : ''}</li>`).join('')}${xs.length > 8 ? `<li>${trUi('m097', [xs.length - 8])}</li>` : ''}</ul>`;
    $('#pdirPlan').innerHTML = `<section class="card stack" style="margin-top:8px">
      <p><b>${esc(plan.from)}</b><br>→ <b>${esc(plan.to)}</b></p>
      ${plan.move ? `
        <p class="caption">${trUi('m105', [projects.length, others.length ? trUi('m098', [others.length]) : '', bytes(plan.total_bytes), plan.same_volume ? trUi('m099') : trUi('m101', [plan.free_bytes != null ? bytes(plan.free_bytes) : trUi('m100')])])}</p>
        ${projects.length ? `<details open><summary class="caption">${trUi('m102')}</summary>${list(projects)}</details>` : ''}
        ${others.length ? `<details><summary class="caption">${trUi('m103')}</summary>${list(others)}</details>` : ''}
        ${!plan.items.length ? `<p class="caption">${trUi('m104')}</p>` : ''}`
      : `<p class="caption">${trUi('m106', [esc(plan.from)])}</p>`}
      ${plan.projects_elsewhere ? `<p class="caption">${trUi('m107', [plan.projects_elsewhere])}</p>` : ''}
      ${plan.problems.length ? `<fluent-message-bar intent="error" layout="multiline"><b>${trUi('m108')}</b><ul class="notes">${plan.problems.map(p => `<li>${esc(serverText(p))}</li>`).join('')}</ul></fluent-message-bar>`
        : `<div><fluent-button appearance="primary" id="pdirApply">${plan.move ? trUi('m109') : trUi('m110')}</fluent-button></div>`}
    </section>`;
    $('#pdirApply')?.addEventListener('click', () => apply(path, plan.move));
  });
  const apply = async (path, move) => {
    const controls = ['#pdirApply', '#pdirCheck', '#pdirBrowse', '#settings-close'].map(s => $(s)).filter(Boolean);
    controls.forEach(c => { c.disabled = true; });
    $('#pdirMsg').innerHTML = '';
    const row = $('#pdirRow');
    const tick = setInterval(async () => {
      try {
        const p = (await api('/api/settings/projects-dir')).progress;
        if (!p.running) return;
        row.hidden = false;
        const pct = p.total_bytes > 0 ? Math.round(100 * p.done_bytes / p.total_bytes) : Math.round(100 * p.done_items / Math.max(1, p.total_items));
        $('#pdirBar').setAttribute('value', String(pct)); $('#pdirPct').textContent = `${pct}%`;
        $('#pdirStep').textContent = trUi('m111', [p.done_items, p.total_items, p.current ? `（${p.current}）` : '']);
      } catch { }
    }, 500);
    try {
      const r = await api('/api/settings/projects-dir', { method: 'PUT', body: { path, move } });
      $('#pdirNow').textContent = r.projects_dir; $('#pdirPlan').innerHTML = ''; $('#pdirPath').value = '';
      const lines = [r.moved ? trUi('m112', [r.moved_items.length, r.documents_updated]) : trUi('m113'), trUi('m114', [r.projects_dir])];
      $('#pdirMsg').innerHTML = info(lines.join(''), r.failed.length ? 'warning' : 'success')
        + (r.failed.length ? `<fluent-message-bar intent="warning" layout="multiline"><b>${trUi('m115')}</b><ul class="notes">${r.failed.map(f => `<li>${esc(f.name)}: ${esc(serverText(f.reason))}</li>`).join('')}</ul>${trUi('m116')}</fluent-message-bar>` : '')
        + (r.left_behind.length ? `<fluent-message-bar intent="warning" layout="multiline">${trUi('m117', [r.left_behind.map(esc).join('、')])}</fluent-message-bar>` : '');
      if (location.hash.startsWith('#/p/') || location.hash === '' || location.hash === '#/') router();
    } catch (e) { $('#pdirMsg').innerHTML = errorBar(e); }
    finally { clearInterval(tick); row.hidden = true; controls.forEach(c => { c.disabled = false; }); }
  };
}

// ---------------- routes ----------------
let workflowSaveChain = Promise.resolve();
function workflowRoute(id, state) {
  if (state.step === 'project') return `#/p/${id}`;
  if (state.step === 'settings') return `#/p/${id}/settings`;
  if (state.step === 'job' || state.step === 'candidates') return state.job_id ? `#/job/${state.job_id}` : null;
  if (state.step === 'exports') return state.job_id ? `#/job/${state.job_id}/exports` : null;
  if (state.step === 'review') return state.edit_plan_id ? `#/ep/${state.edit_plan_id}` : null;
  if (state.step === 'render') return state.render_id ? `#/render/${state.render_id}` : state.edit_plan_id ? `#/ep/${state.edit_plan_id}` : null;
  return null;
}
function saveWorkflowState(projectId, state) {
  const save = () => api(`/api/projects/${encodeURIComponent(projectId)}/workflow-state`, { method: 'PUT', body: state });
  workflowSaveChain = workflowSaveChain.then(save, save);
  return workflowSaveChain;
}
const routes = [
  [/^#?\/?$/, home],
  [/^#\/p\/([\w-]+)$/, (id) => project(id)],
  [/^#\/p\/([\w-]+)\/settings$/, (id) => project(id, true)],
  [/^#\/job\/([\w-]+)$/, job],
  [/^#\/job\/([\w-]+)\/exports$/, exportsView],
  [/^#\/ep\/([\w-]+)$/, review],
  [/^#\/render\/([\w-]+)$/, exportView],
];
let cleanup = null;
// a route that awaited the API after the user navigated elsewhere must not overwrite the newer page
let routeSeq = 0;
let localeDraft = null;
let flushLocaleWrites = async () => {};
function routeGuard() { const seq = routeSeq; return () => seq !== routeSeq; }
async function router() {
  const seq = ++routeSeq;
  flushLocaleWrites = async () => {};
  if (cleanup) { cleanup(); cleanup = null; }
  document.body.classList.remove('fit');
  const hash = location.hash || '#/';
  for (const [re, fn] of routes) { const m = hash.match(re); if (m) { try { await fn(...m.slice(1)); } catch (e) { if (seq === routeSeq) main.innerHTML = errorBar(e); } if (seq === routeSeq) main.focus(); return; } }
  main.innerHTML = info(trUi('m118'));
}
window.addEventListener('hashchange', router);

// ---------------- home ----------------
let flash = '';   // one-shot message shown on the next home render (e.g. after a delete)
async function home() {
  const gone = routeGuard();
  setSteps(-1);
  const projects = await api('/api/projects');
  if (gone()) return;
  const notice = flash; flash = '';
  main.innerHTML = `
    <div class="row" style="justify-content:space-between;margin-bottom:16px">
      <div><h1>${trUi('m102')}</h1><p class="muted">${trUi('m126')}</p></div>
      <form id="new" class="row"><fluent-text-input id="name" placeholder="${trUi('m127')}" aria-label="${trUi('m127')}"></fluent-text-input>
        <label class="caption">${trUi('video.language')} <select class="native" id="new-video-language" aria-label="${trUi('video.language')}">${videoLanguageOptions('ja-JP')}</select></label>
        <fluent-button appearance="primary" type="submit">${trUi('m128')}</fluent-button></form>
    </div>
    <div id="homeMsg" role="status">${notice}</div>
    ${projects.length === 0 ? `<div class="card empty"><img src="/img/icon.svg" alt=""><h2>${trUi('m119')}</h2><p class="muted">${trUi('m120')}</p></div>` :
      `<div class="grid cols-auto">${projects.map(p => `
        <article class="card selectable project-card">
          <div class="card-head"><h3><a class="stretch" href="#/p/${esc(p.id)}">${esc(p.name)}</a></h3>
            <fluent-button appearance="subtle" size="small" class="del" data-id="${esc(p.id)}" aria-label="${trUi('m121', [esc(p.name)])}">${trUi('m122')}</fluent-button></div>
          <dl class="facts"><dt>${trUi('m123')}</dt><dd>${esc(formatDate(p.created_at))}</dd><dt>${trUi('m004')}</dt><dd>${trUi('m124', [p.assets])}</dd>
          <dt>${trUi('video.language')}</dt><dd>${trUi(`video.${p.language ?? 'ja-JP'}`)}</dd>
          <dt>${trUi('m125')}</dt><dd>${p.last_job ? esc(jobMessage(p.last_job)) : '—'}</dd></dl>
        </article>`).join('')}</div>`}`;
  $('#new').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const button = $('#new fluent-button'); button.disabled = true;
    try {
      const p = await api('/api/projects', { method: 'POST', body: { name: $('#name').value || trUi('project.defaultName'), language: $('#new-video-language').value } });
      location.hash = `#/p/${p.id}`;
    } catch (e) { $('#homeMsg').innerHTML = errorBar(e); }
    finally { button.disabled = false; }
  });
  $$('.project-card .del').forEach(b => b.addEventListener('click', () => confirmDelete(projects.find(p => p.id === b.dataset.id), b)));
}

const RUNNING = ['queued', 'ingesting', 'transcribing', 'analyzing', 'selecting', 'planning', 'previewing'];
function confirmDelete(p, opener) {
  const dlg = $('#delete-dialog'), ok = $('#delete-confirm'), cancel = $('#delete-cancel');
  const busy = p.last_job && RUNNING.includes(p.last_job.status);
  $('#delete-body').innerHTML = `
    <p>${trUi('delete.projectLine', [`<b>${esc(p.name)}</b>`])}</p>
    <ul class="del-list">
      <li>${trUi('delete.folderLine')}</li>
      <li>${trUi('m134')}</li>
      <li>${trUi('delete.originalLine')}</li>
    </ul>
    <p class="caption">${trUi('m137', [esc(p.directory)])}</p>
    ${busy ? info(trUi('m129'), 'warning') : ''}
    <div id="deleteMsg"></div>`;
  ok.disabled = false;
  let done = false;
  const close = () => { ok.removeEventListener('click', onOk); cancel.removeEventListener('click', onCancel); dlg.removeEventListener('toggle', onToggle); };
  const onCancel = () => dlg.hide();
  const onToggle = (e) => { if (e.detail?.newState === 'closed') { close(); if (!done) opener?.focus(); } };
  const onOk = async () => {
    ok.disabled = true;
    try {
      const r = await api(`/api/projects/${encodeURIComponent(p.id)}`, { method: 'DELETE' });
      done = true;
      const where = r.folder === 'removed' ? trUi('m138') : r.folder === 'kept' ? trUi('m139', [r.folder_path]) : '';
      flash = info(trUi('m140', [r.name, where]), 'success');
      dlg.hide();
      await home();
      main.focus();
    } catch (e) { $('#deleteMsg').innerHTML = errorBar(e); ok.disabled = false; }
  };
  ok.addEventListener('click', onOk); cancel.addEventListener('click', onCancel); dlg.addEventListener('toggle', onToggle);
  dlg.show();
}

// ---------------- project: assets + rights + pattern + generate ----------------
async function project(id, showSettings = false) {
  const gone = routeGuard();
  const [d, rawPresets, rawBgmList, kitView, settings] = await Promise.all([api(`/api/projects/${id}`), api('/api/presets'), api(`/api/bgm?project_id=${encodeURIComponent(id)}`), api('/api/brandkit'), api('/api/settings')]);
  if (gone()) return;
  const presets = rawPresets.map(localizePreset), bgmList = rawBgmList.map(b => ({ ...b, name: libraryTrackName(b) }));
  const resumeRoute = workflowRoute(id, d.resume_state ?? { step: 'project' });
  const userBgm = bgmList.filter(b => b.source === 'user');
  if (!showSettings && resumeRoute && resumeRoute !== `#/p/${id}`) { location.hash = resumeRoute; return; }
  await saveWorkflowState(id, { step: showSettings ? 'settings' : 'project' });
  if (gone()) return;
  const assets = d.assets, rights = new Set(d.rights.map(r => r.asset_id));
  const allRights = assets.length > 0 && assets.every(a => rights.has(a.id));
  // the generate button stays enabled (a disabled button is unreadable and unfocusable); clicking it early explains what is missing
  const notReady = !assets.length ? trUi('m141') : !allRights ? trUi('m142') : '';
  const saved = d.project.generation_settings ?? {};
  let selected = saved.preset_id ?? 'P4';
  const opts = { scope: saved.scope ?? 'balanced', bgm: saved.bgm ?? 'auto', sfx: saved.sfx ?? true, tsukkomi: saved.tsukkomi ?? false, tsukkomi_style: ['auto', 'telop', 'card'].includes(saved.tsukkomi_style) ? saved.tsukkomi_style : 'auto', series: saved.series ?? false, series_by: saved.series_by ?? 'parts', parts: saved.parts ?? 2, part_minutes: saved.part_minutes ?? 15,
    opening: (saved.opening ?? true) && !!kitView.opening, closing: (saved.closing ?? true) && !!kitView.closing, scene_cards: saved.scene_cards ?? false, mask_words: Array.isArray(saved.mask_words) ? saved.mask_words : [] };
  setSteps(assets.length && allRights ? 1 : 0);
  let estimate = null;
  if (assets.length) try { estimate = await api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`); } catch { }
  if (gone()) return;
  const lastJob = d.jobs[0];
  const roleLabel = { main: trUi('m143'), cam_a: trUi('m144'), cam_b: trUi('m145'), wide: trUi('m146'), audio_master: trUi('m147'), screen: trUi('m148') };
  const syncText = (a) => {
    if (a.role === 'main') return trUi('m149');
    if (a.sync_method === 'manual' || (a.sync_method == null && a.sync_confidence === 9999)) return trUi('m150', [(a.sync_offset * 1000).toFixed(0)]);
    if (a.sync_method === 'audio') return trUi('m151', [a.sync_offset >= 0 ? '+' : '', (a.sync_offset * 1000).toFixed(1)]) +
      (a.sync_drift_ppm ? trUi('m152', [a.sync_drift_ppm > 0 ? '+' : '', a.sync_drift_ppm.toFixed(1), (Math.abs(a.sync_drift_ppm) * 3.6).toFixed(0)]) : '') +
      (a.sync_windows ? trUi('m153', [a.sync_windows, a.sync_residual_ms]) : '');
    if (a.sync_method === 'metadata') return trUi('m154', [a.sync_offset.toFixed(1)]);
    if (a.sync_method === 'none') return trUi('m155');
    return a.sync_confidence != null ? trUi('m156', [a.sync_confidence]) : trUi('m157');
  };
  const scopeLabel = { economy: [trUi('m158'), trUi('m159')], balanced: [trUi('m160'), trUi('m161')], full: [trUi('m162'), trUi('m163')] };
  const jpy = (c) => `¥${Number(c.estimated_jpy).toFixed(1)}`;
  const long = estimate && estimate.duration > 1800;

  // Windows Settings-style expanders: the header always shows the current value, so a collapsed group still reads at a glance
  let groupState = {};
  try {
    const savedGroups = JSON.parse(localStorage.getItem('vc.genGroups') ?? '{}');
    if (savedGroups && typeof savedGroups === 'object' && !Array.isArray(savedGroups)) groupState = savedGroups;
  } catch { /* Storage restrictions must not prevent editing a project. */ }
  const group = (key, title, body, { open = true, id = '', titleId = '' } = {}) => `
        <details class="setgroup" data-group="${key}" ${id ? `id="${id}"` : ''} ${(groupState[key] ?? open) ? 'open' : ''}>
          <summary><h3 ${titleId ? `id="${titleId}"` : ''}>${title}</h3><span class="caption group-sum" id="sum-${key}"></span></summary>
          <div class="stack setgroup-body">${body}</div>
        </details>`;

  main.innerHTML = `
    <div class="fit-top">
    <div class="row" style="justify-content:space-between">
      <div class="row title-row" id="title-view"><h1 id="pname">${esc(d.project.name)}</h1><fluent-button id="rename" appearance="subtle" size="small">${trUi('m276')}</fluent-button></div>
      <form class="row title-row" id="title-edit" hidden>
        <fluent-text-input id="rename-input" maxlength="80" aria-label="${trUi('m127')}" aria-describedby="rename-note"></fluent-text-input>
        <fluent-button appearance="primary" type="submit">${trUi('m277')}</fluent-button><fluent-button id="rename-cancel" type="button">${trUi('m278')}</fluent-button>
        <span id="rename-note" class="caption">${trUi('m279')}</span>
      </form>
      ${lastJob ? `<fluent-anchor-button href="#/job/${esc(lastJob.id)}" appearance="subtle">${trUi('m164')}</fluent-anchor-button>` : ''}</div>
    <div id="renameMsg"></div>
    <p class="caption">${trUi('m280', [esc(d.project.directory)])}</p>
    <div class="video-language">
      <label>${trUi('video.language')} <select class="native" id="project-video-language" aria-describedby="video-language-help">${videoLanguageOptions(d.project.language ?? 'ja-JP')}</select></label>
      <p class="caption" id="video-language-help">${trUi('video.languageHelp')}</p><div id="video-language-message" role="status"></div>
    </div>
    <div class="grid cols-steps">
      <section class="card step-col" aria-labelledby="h-src">
        <h2 id="h-src">${trUi('m281')}</h2>
        <div class="col-body">
        <div class="drop" id="drop">
          <p><b>${trUi('m282')}</b></p>
          <p class="caption">${trUi('m283')}</p>
          <input type="file" id="file" accept="video/*,.mkv" hidden>
          <fluent-button id="pick">${trUi('m284')}</fluent-button>
          <div id="upload" class="stack" style="margin-top:12px"></div>
        </div>
        <details style="margin-top:12px"><summary class="caption">${trUi('m285')}</summary>
          <form id="byPath" class="row" style="margin-top:8px"><fluent-text-input id="path" placeholder="C:\\Videos\\stream.mp4" style="flex:1" aria-label="${trUi('m286')}"></fluent-text-input><fluent-button type="submit">${trUi('m287')}</fluent-button></form>
        </details>
        <div id="assetMsg"></div>
        ${assets.map(a => `
          <div class="asset">
            <div>
              <b>${esc(a.path.split(/[\\/]/).pop())}</b>
              <div class="caption">${fmt(a.media.duration)} · ${a.media.width}×${a.media.height} · ${a.media.fps.toFixed(2)}fps${a.media.vfr ? '（VFR）' : ''} · ${a.media.audio_channels ? trUi('m165') : trUi('m166')} · ${esc(a.media.video_codec)}</div>
              ${a.warnings.map(w => `<div class="caption warn">${esc(serverText(w))}</div>`).join('')}
              ${assets.length > 1 ? `<div class="row" style="margin:8px 0"><label class="caption">${trUi('m167')} <select class="native role" data-asset="${esc(a.id)}">${Object.entries(roleLabel).map(([k, v]) => `<option value="${k}" ${a.role === k ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
                <label class="caption">${trUi('m168')} <input class="native spk" type="number" min="1" max="8" style="width:64px" data-asset="${esc(a.id)}" value="${a.speaker ?? ''}" placeholder="${trUi('m169')}"></label>
                <label class="caption">${trUi('m170')} <input class="native off" type="number" step="0.001" style="width:96px" data-asset="${esc(a.id)}" value="${a.sync_offset ?? 0}"></label>
                <span class="caption">${esc(syncText(a))}</span></div>` : ''}
              <div style="margin-top:8px">${field(esc(getLanguage() === 'ja' ? d.rights_statement : trUi('rights.statement')), `<fluent-checkbox slot="input" class="rights" data-asset="${esc(a.id)}" ${rights.has(a.id) ? 'checked disabled' : ''}></fluent-checkbox>`)}</div>
            </div>
            <fluent-badge appearance="tint" color="${rights.has(a.id) ? 'success' : 'warning'}">${rights.has(a.id) ? trUi('m171') : trUi('m172')}</fluent-badge>
          </div>`).join('')}
        </div>
      </section>
      <section class="card step-col" aria-labelledby="h-pat">
        <h2 id="h-pat">${trUi('m288')}</h2>
        <div class="grid col-body" id="presets" role="radiogroup" aria-label="${trUi('m289')}">
          ${presets.map(p => `
            <div class="card selectable preset ${p.available ? '' : 'disabled'} ${p.id === selected ? 'selected' : ''}" style="padding:16px">
              <div class="preset-radio" role="radio" aria-checked="${p.id === selected}" aria-disabled="${!p.available}" tabindex="-1" data-id="${esc(p.id)}">
              <div class="card-head"><span><span class="pid">${esc(p.id)}</span> <b>${esc(p.name)}</b></span>
                <span class="badges"><fluent-badge appearance="tint" color="informative">${esc(p.genre)}</fluent-badge>${p.available ? `<fluent-badge appearance="tint" color="brand">${trUi('m173', [p.target.duration_sec[0], p.target.duration_sec[1]])}</fluent-badge>` : `<fluent-badge appearance="tint" color="subtle">${trUi('m174')}</fluent-badge>`}</span></div>
              ${p.card ? `<dl class="facts"><dt>${trUi('m175')}</dt><dd>${esc(p.card.opening)}</dd><dt>${trUi('m176')}</dt><dd>${esc(p.card.tempo)}</dd><dt>${trUi('m177')}</dt><dd>${esc(p.card.telop)}</dd><dt>${trUi('m178')}</dt><dd>${esc(p.card.sound)}</dd></dl>` : ''}
              ${p.available ? '' : `<div class="caption">${esc(p.unavailable_reason)}</div>`}
              </div>
              <div class="caption">${trUi('m179', [p.examples.map(x => `<a href="${esc(x.url)}" target="_blank" rel="noopener noreferrer">${esc(x.title)}</a>`).join(' / ')])}</div>
            </div>`).join('')}
        </div>
      </section>
      <section class="card step-col" aria-labelledby="h-gen">
        <h2 id="h-gen">${trUi('m290')}</h2>
        <div id="settingsSaveMsg" aria-live="polite"></div>
        <div class="col-body stack" style="gap:var(--s8)">
        ${group('series', trUi('m180'), `
          ${field(trUi('m181'), `<fluent-switch slot="input" id="seriesSw" ${opts.series ? 'checked' : ''}></fluent-switch>`)}
          <fieldset class="series-by" aria-describedby="seriesInfo"><legend class="caption">${trUi('m182')}</legend>
            <label class="caption"><input type="radio" name="seriesBy" value="parts" ${opts.series_by === 'parts' ? 'checked' : ''}> ${trUi('m183')} <input class="native" type="number" id="partCount" min="2" max="20" step="1" value="${esc(opts.parts)}" style="width:64px" aria-label="${trUi('m184')}"> ${trUi('m185')}</label>
            <label class="caption"><input type="radio" name="seriesBy" value="minutes" ${opts.series_by !== 'parts' ? 'checked' : ''}> ${trUi('m186')} <input class="native" type="number" id="partMin" min="1" max="60" step="0.5" value="${esc(opts.part_minutes)}" style="width:72px" aria-label="${trUi('m187')}"> ${trUi('m188')}</label>
          </fieldset>
          <p class="caption" id="seriesInfo" aria-live="polite"></p>
          <details class="help" id="seriesHow"><summary class="caption">${trUi('m189')}</summary>
            <p class="caption">${trUi('m190')}</p></details>`)}
        ${group('scope', trUi('m191'), `
          <div class="grid" id="scopes" role="radiogroup" aria-label="${trUi('m197')}">
            ${(estimate?.scopes ?? [{ scope: 'economy' }, { scope: 'balanced' }, { scope: 'full' }]).map(s => `
              <div class="card selectable scope ${s.scope === opts.scope ? 'selected' : ''}" role="radio" aria-checked="${s.scope === opts.scope}" tabindex="-1" data-scope="${s.scope}">
                <b>${scopeLabel[s.scope][0]}</b>${s.cost ? `<span class="caption">${trUi('m192', [Math.round(s.share * 100)])}</span><fluent-badge appearance="tint" color="brand" class="scope-cost">${trUi('m193', [jpy(s.cost)])}</fluent-badge>` : ''}
                <span class="sr-only">${scopeLabel[s.scope][1]}</span>
              </div>`).join('')}
          </div>
          <p class="caption" id="scopeDesc" aria-hidden="true">${scopeLabel[opts.scope]?.[1] ?? ''}</p>
          <p class="caption">${estimate ? (long ? trUi('m194') : trUi('m195')) : trUi('m196')}</p>`, { id: 'scopeBox' })}
        ${group('sound', trUi('m178'), `
          <label class="caption">BGM <select class="native" id="bgm">
            <option value="auto" ${opts.bgm === 'auto' ? 'selected' : ''}>${trUi('m215')}</option><option value="none" ${opts.bgm === 'none' ? 'selected' : ''}>${trUi('m041')}</option>
            <option value="scene" ${opts.bgm === 'scene' ? 'selected' : ''}>${trUi('m216', [userBgm.length ? trUi('m198', [userBgm.length]) : trUi('m199')])}</option>
            ${bgmList.map(b => `<option value="${esc(b.id)}" ${opts.bgm === b.id ? 'selected' : ''}>${trUi('m201', [esc(b.name), b.source === 'user' ? trUi('m200') : '', b.bpm ? ` ${Math.round(b.bpm)}BPM` : ''])}</option>`).join('')}
          </select></label>
          <p class="caption">${trUi('m217')}</p>
          ${field(trUi('m202'), `<fluent-switch slot="input" id="sfx" ${opts.sfx ? 'checked' : ''}></fluent-switch>`)}
          ${field(trUi('m203'), `<fluent-switch slot="input" id="tsukkomi" ${opts.tsukkomi ? 'checked' : ''}></fluent-switch>`)}
          <div class="stack" id="tsukkomiStyleBox" style="margin-left:16px" ${opts.tsukkomi ? '' : 'hidden'}>
            <span class="caption" id="tsukkomiStyleLabel">${trUi('m218')}</span>
            <fluent-radio-group id="tsukkomiStyle" orientation="vertical" aria-labelledby="tsukkomiStyleLabel" value="${esc(opts.tsukkomi_style)}" ${opts.tsukkomi ? '' : 'disabled'}>
              ${field(trUi('m204'), '<fluent-radio slot="input" value="auto"></fluent-radio>')}
              ${field(trUi('m205'), '<fluent-radio slot="input" value="telop"></fluent-radio>')}
              ${field(trUi('m206'), '<fluent-radio slot="input" value="card"></fluent-radio>')}
            </fluent-radio-group>
          </div>
          <label class="caption">${trUi('m219')}<input type="text" class="native" id="maskWords" maxlength="400" style="display:block;width:100%;margin-top:4px" value="${esc(opts.mask_words.join('、'))}" placeholder="${trUi('m220')}"></label>
          <details ${userBgm.length ? 'open' : ''}><summary class="caption">${trUi('m221', [userBgm.length ? trUi('m207', [userBgm.length]) : trUi('m208')])}</summary>
            ${userBgm.length ? `<ul class="notes bgm-list">${userBgm.map(b => `<li><b>${esc(b.name)}</b> <span class="caption">${b.analysis ? trUi('m210', [Number(b.analysis.lufs).toFixed(1), b.analysis.bpm ? trUi('m209', [Math.round(b.analysis.bpm)]) : '', energyWord(b.analysis.energy), Number(b.analysis.energy).toFixed(2), fmt(b.analysis.start), fmt(b.analysis.end)]) : trUi('m211')}</span>
              <fluent-button size="small" appearance="subtle" data-del-bgm="${esc(b.id)}" aria-label="${trUi('m212', [esc(b.name)])}">${trUi('m213')}</fluent-button></li>`).join('')}</ul>` : ''}
            <form id="bgmForm" class="stack" style="margin-top:8px">
              <input type="file" id="bgmFile" accept="audio/*,.mp3,.wav,.m4a,.aac,.flac,.ogg" class="native" multiple aria-label="${trUi('m222')}">
              ${field(trUi('m214'), '<fluent-checkbox slot="input" id="bgmRights"></fluent-checkbox>')}
              <div><fluent-button type="submit" size="small">${trUi('m223')}</fluent-button></div>
              <p class="caption">${trUi('m224')}</p>
              <div id="bgmMsg" aria-live="polite"></div>
            </form></details>`)}
        ${group('pack', trUi('m225'), `
          ${(() => {
            const sign = (v, d) => v == null || Number.isNaN(v) ? '—' : `${v >= 0 ? '+' : ''}${Number(v).toFixed(d)}`;
            const enh = (x) => {
              const e = x?.enhancement; if (!e) return '';
              const g = e.gain ? trUi('m226', [sign(e.gain.vmaf, 1), sign(e.gain.xpsnr, 2)]) : '';
              return e.path ? `<br><span class="caption pass">${trUi('m227', [e.width, e.height, esc(e.device), Math.round(e.seconds), esc(g)])}</span>` : `<br><span class="caption">→ ${esc(serverText(e.note))}（${esc(g)}）</span>`;
            };
            const f = (x) => x ? trUi('m229', [esc(x.path.split(/[\\/]/).pop()), x.width, x.height, Number(x.duration).toFixed(1), x.upscale > 1.05 && !x.enhancement?.path ? trUi('m228', [x.upscale]) : '', enh(x)]) : trUi('m230');
            const pending = [kitView.opening, kitView.closing].filter(x => x?.needs && !x.enhancement?.path);
            return `<p class="caption" id="kitInfo">${trUi('m235', [f(kitView.opening)])}<br>${trUi('m236', [f(kitView.closing)])}<br>${trUi('m237', [kitView.card_images ? trUi('m231', [kitView.card_images]) : trUi('m232')])}</p>
              <div id="enhStatus" aria-live="polite"></div>
              ${pending.length && kitView.enhance?.state !== 'running' && !pending.every(x => x.enhancement) ? `<p class="caption warnc">${trUi('m233')}<fluent-button size="small" id="enhanceBtn">${trUi('m234')}</fluent-button></p>` : ''}`;
          })()}
          ${field(trUi('m238'), `<fluent-switch slot="input" id="openingSw" ${opts.opening ? 'checked' : ''} ${kitView.opening ? '' : 'disabled'}></fluent-switch>`)}
          ${field(trUi('m239'), `<fluent-switch slot="input" id="closingSw" ${opts.closing ? 'checked' : ''} ${kitView.closing ? '' : 'disabled'}></fluent-switch>`)}
          ${field(trUi('m240'), `<fluent-switch slot="input" id="cardsSw" ${opts.scene_cards ? 'checked' : ''}></fluent-switch>`)}
          <details><summary class="caption">${kitView.registered ? trUi('m241') : trUi('m242')}</summary>
            <form id="kitForm" class="stack" style="margin-top:8px">
              <p class="caption">${trUi('m265')}</p>
              <fluent-text-input id="kitPath" placeholder="C:\\Work\\HubRadio\\OpeningClosing" value="${esc(kitView.kit?.sources?.[0] ?? '')}" aria-label="${trUi('m266')}"></fluent-text-input>
              <fluent-button id="kitBrowse" type="button" size="small">${trUi('m088')}</fluent-button>
              <label class="caption">${trUi('m267')} <input class="native" type="number" id="kitCardSec" min="2" max="3" step="0.1" value="${esc(Math.max(2, Math.min(3, kitView.kit?.card_sec ?? 2.5)))}" style="width:72px"></label>
              ${field(trUi('m243'), `<fluent-checkbox slot="input" id="kitUpscale" ${kitView.kit?.upscale === false ? '' : 'checked'}></fluent-checkbox>`)}
              ${field(trUi('m244'), '<fluent-checkbox slot="input" id="kitRights"></fluent-checkbox>')}
              <div class="row"><fluent-button type="submit" size="small">${trUi('m268')}</fluent-button>${kitView.registered ? `<fluent-button size="small" id="kitClear" type="button">${trUi('m245')}</fluent-button>` : ''}</div><div id="kitMsg"></div>
            </form></details>
          <div class="stack" style="margin-top:4px">
            <b id="cardMakeLabel">${trUi('m269')}</b>
            <details class="help"><summary class="caption">${trUi('m270')}</summary>
            <p class="caption">${trUi('m271')}</p></details>
            ${(() => {
              if (kitView.card_images) return `<p class="caption">${trUi('m246')}</p>`;
              const made = d.project.scene_card_images ?? [];
              const make = d.project.make_scene_card_images ?? made.length > 0;
              const name = (p) => esc(p.split(/[\\/]/).pop());
              const bumpers = (kitView.kit?.files ?? []).filter(f => f.kind === 'opening' || f.kind === 'closing');
              const videos = assets.filter(a => a.media.duration > 0);
              const at = (t) => t < 60 ? trUi('m247', [t.toFixed(1)]) : fmt(t);
              const origin = (img) => (img.brand_path ?? assets.find(a => a.id === img.asset_id)?.path ?? trUi('m248')).split(/[\\/]/).pop();
              return `<fluent-radio-group id="cardMake" orientation="horizontal" aria-labelledby="cardMakeLabel" value="${make ? 'make' : 'skip'}">
                  ${field(trUi('m249'), '<fluent-radio slot="input" value="make"></fluent-radio>')}
                  ${field(trUi('m250'), '<fluent-radio slot="input" value="skip"></fluent-radio>')}
                </fluent-radio-group>
                <p class="caption" id="cardSkipNote" ${make ? 'hidden' : ''}>${trUi('m264', [made.length ? trUi('m251', [made.length]) : ''])}</p>
                <div class="stack" id="cardMakeBody" ${make ? '' : 'hidden'}>
                ${made.length ? `<div class="row" style="flex-wrap:wrap">${made.map((img, i) => `<figure style="margin:0;width:120px"><img src="/api/projects/${encodeURIComponent(id)}/scene-cards/${i}" alt="${trUi('card.imageAlt', [i + 1, esc(at(img.source_t))])}" style="width:120px;height:90px;object-fit:contain"><figcaption class="caption">${esc(origin(img))} · ${esc(at(img.source_t))}</figcaption></figure>`).join('')}</div><p class="caption">${trUi('m252', [made.length])}</p>` : `
                  <label class="caption">${trUi('m260')}
                    <select class="native" id="cardSource" aria-label="${trUi('m261')}">
                      ${videos.length ? `<optgroup label="${trUi('m254')}">${videos.map(a => `<option value="asset:${esc(a.id)}">${name(a.path)}${rights.has(a.id) ? '' : trUi('m253')}</option>`).join('')}</optgroup>` : ''}
                      ${bumpers.length ? `<optgroup label="${trUi('m258')}">${bumpers.map(f => `<option value="brand:${esc(f.path)}">${trUi('m257', [f.kind === 'opening' ? trUi('m255') : trUi('m256'), name(f.path), f.width, f.height, Number(f.duration).toFixed(1)])}</option>`).join('')}</optgroup>` : ''}
                    </select>
                  </label>
                  <label class="caption">${trUi('m262')} <input class="native" type="number" id="cardCount" min="2" max="6" step="1" value="4" style="width:72px"></label>
                  <fluent-button id="makeCards" size="small" ${videos.length + bumpers.length ? '' : 'disabled'}>${trUi('m263')}</fluent-button>
                  ${videos.length + bumpers.length ? '' : `<p class="caption">${trUi('m259')}</p>`}
                `}
                </div>`;
            })()}
            <div id="cardImageMsg" aria-live="polite"></div>
          </div>`, { open: false })}
        ${group('analysis', trUi('m272'), `
          <fluent-radio-group id="analysisReuse" orientation="vertical" aria-labelledby="analysisLabel" aria-describedby="analysisInfo" value="reuse">
            ${field(trUi('m273'), '<fluent-radio slot="input" value="reuse"></fluent-radio>')}
            ${field(trUi('m274'), '<fluent-radio slot="input" value="redo"></fluent-radio>')}
          </fluent-radio-group>
          <p class="caption" id="analysisInfo" aria-live="polite"></p>`, { id: 'analysisBox', titleId: 'analysisLabel' })}
        </div>
        <div class="col-foot">
          <div id="transmission">${transmissionCompact(settings)}</div>
          ${estimate ? `<details class="help" id="costInfo"><summary class="caption">${trUi('m275')}</summary><p class="caption" id="costLine"></p></details>` : ''}
          <div id="genMsg"></div>
          <div class="row">
            <fluent-button appearance="primary" id="gen" ${notReady ? 'aria-describedby="genHint"' : ''}>${trUi('m291')}</fluent-button>
            ${estimate ? '<span class="caption" id="costShort"></span>' : ''}
            ${notReady ? `<span class="caption warnc" id="genHint">${notReady}</span>` : ''}
          </div>
        </div>
      </section>
    </div>
    </div>
    <section class="card" id="patterns" aria-labelledby="patterns-h" style="margin-top:16px">
      <h2 id="patterns-h">${trUi('m292')} <span class="caption">（<span id="patternCount">0</span>${trUi('m293')}</span></h2>
      <p class="caption">${trUi('m294')}</p>
      <div id="patternList"></div>
      <div id="patternMsg" role="status" aria-live="polite"></div>
    </section>
    <section class="card" aria-labelledby="proper-nouns-heading" style="margin-top:16px">
      <details id="properNounsDetails">
        <summary id="proper-nouns-heading"><b>${trUi('m295')}</b> <span class="caption">（<span id="properNounCount">${d.project.proper_nouns?.length ?? 0}</span>${trUi('m293')}</span></summary>
        <p class="caption" style="margin-top:8px">${trUi('m296')}</p>
        <div id="properNounRows" class="stack"></div>
        <div class="row" style="margin-top:8px"><fluent-button id="addProperNoun" type="button" size="small">${trUi('m297')}</fluent-button><fluent-button id="saveProperNouns" type="button" appearance="primary" size="small">${trUi('m277')}</fluent-button></div>
        <div id="properNounsMsg" role="status" aria-live="polite"></div>
      </details>
    </section>`;
  document.body.classList.add('fit');
  loadPatterns(id, presets);
  $('#project-video-language').addEventListener('change', async e => {
    const select = e.currentTarget, previous = d.project.language ?? 'ja-JP';
    select.disabled = true;
    try {
      const p = await api(`/api/projects/${id}`, { method: 'PATCH', body: { language: select.value } });
      d.project.language = p.language;
      $('#video-language-message').innerHTML = info(trUi('video.saved'), 'success');
    } catch (err) {
      select.value = previous;
      $('#video-language-message').innerHTML = errorBar(err);
    } finally { select.disabled = false; }
  });

  let properNouns = (localeDraft?.projectId === id ? localeDraft.properNouns : d.project.proper_nouns ?? []).map(n => ({ ...n, variants: n.variants ?? [] }));
  localeDraft = null;
  const properNounTypes = { person: trUi('m298'), place: trUi('m299'), organization: trUi('m300'), product: trUi('m301'), other: trUi('m302') };
  const renderProperNouns = () => {
    $('#properNounCount').textContent = String(properNouns.length);
    $('#properNounRows').innerHTML = properNouns.length ? properNouns.map((n, i) => `
      <fieldset class="proper-noun-row">
        <legend class="caption">${trUi('m303', [i + 1])}</legend>
        <div class="row">
          <label class="caption">${trUi('m304')} <select class="native" data-proper-index="${i}" data-proper-field="type">${Object.entries(properNounTypes).map(([v, label]) => `<option value="${v}" ${n.type === v ? 'selected' : ''}>${label}</option>`).join('')}</select></label>
          <label class="caption">${trUi('m305')} <input class="native" data-proper-index="${i}" data-proper-field="display_text" maxlength="100" value="${esc(n.display_text)}" placeholder="${trUi('m306')}"></label>
          <label class="caption">${trUi('m307')} <input class="native" data-proper-index="${i}" data-proper-field="reading" maxlength="100" value="${esc(n.reading)}" placeholder="${trUi('m308')}"></label>
          <label class="caption">${trUi('m309')} <input class="native proper-variants" data-proper-index="${i}" data-proper-field="variants" maxlength="1000" value="${esc(n.variants.join('、'))}" placeholder="${trUi('m310')}"></label>
          <fluent-button type="button" size="small" appearance="subtle" class="removeProperNoun" data-proper-index="${i}" aria-label="${trUi('m311', [i + 1])}">${trUi('m122')}</fluent-button>
        </div>
      </fieldset>`).join('') : `<p class="caption">${trUi('m312')}</p>`;
  };
  renderProperNouns();
  $('#addProperNoun').addEventListener('click', () => {
    properNouns.push({ display_text: '', reading: '', variants: [], type: 'person' });
    renderProperNouns();
    $('#properNounRows fieldset:last-child [data-proper-field="display_text"]')?.focus();
  });
  const updateProperNoun = e => {
    const el = e.target.closest('[data-proper-index][data-proper-field]');
    if (!el) return;
    const row = properNouns[Number(el.dataset.properIndex)];
    row[el.dataset.properField] = el.dataset.properField === 'variants'
      ? el.value.split(/[,、，\n]/).map(v => v.trim()).filter(Boolean)
      : el.value;
  };
  $('#properNounRows').addEventListener('input', updateProperNoun);
  $('#properNounRows').addEventListener('change', updateProperNoun);
  $('#properNounRows').addEventListener('click', e => {
    const button = e.target.closest('.removeProperNoun');
    if (!button) return;
    properNouns.splice(Number(button.dataset.properIndex), 1);
    renderProperNouns();
  });
  $('#saveProperNouns').addEventListener('click', async () => {
    const button = $('#saveProperNouns'), msg = $('#properNounsMsg');
    button.disabled = true;
    try {
      const p = await api(`/api/projects/${encodeURIComponent(id)}/proper-nouns`, { method: 'PUT', body: { proper_nouns: properNouns } });
      properNouns = (p.proper_nouns ?? []).map(n => ({ ...n, variants: n.variants ?? [] }));
      d.project.proper_nouns = properNouns;
      renderProperNouns();
      msg.innerHTML = info(trUi('m313'), 'success');
    } catch (e) { msg.innerHTML = errorBar(e); }
    finally { button.disabled = false; }
  });

  const titleView = $('#title-view'), titleEdit = $('#title-edit'), nameInput = $('#rename-input');
  let renaming = false;
  const showTitleEditor = (on) => {
    titleView.hidden = on; titleEdit.hidden = !on;
    if (on) { nameInput.value = d.project.name; requestAnimationFrame(() => { nameInput.focus(); nameInput.select?.(); }); }
    else { $('#renameMsg').innerHTML = ''; $('#rename').focus(); }
  };
  const saveName = async () => {
    if (renaming) return;
    renaming = true;
    try {
      const p = await api(`/api/projects/${id}`, { method: 'PATCH', body: { name: nameInput.value ?? '' } });
      d.project.name = p.name;
      $('#pname').textContent = p.name;
      showTitleEditor(false);
    } catch (e) { $('#renameMsg').innerHTML = errorBar(e); nameInput.focus(); }
    finally { renaming = false; }
  };
  $('#rename').addEventListener('click', () => showTitleEditor(true));
  $('#rename-cancel').addEventListener('click', () => showTitleEditor(false));
  titleEdit.addEventListener('submit', (ev) => { ev.preventDefault(); saveName(); });
  nameInput.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { e.preventDefault(); showTitleEditor(false); }
    else if (e.key === 'Enter' && !e.isComposing) { e.preventDefault(); saveName(); }
  });

  let saveChain = Promise.resolve(), saveRevision = 0, maskSaveTimer;
  flushLocaleWrites = async () => { clearTimeout(maskSaveTimer); return await saveOpts(); };
  const saveOpts = (delay = 0) => {
    updateSums();
    const revision = ++saveRevision;
    const settings = { ...opts, preset_id: selected };
    const status = $('#settingsSaveMsg');
    status.textContent = trUi('m314');
    const save = async () => {
      if (delay) await new Promise(resolve => setTimeout(resolve, delay));
      try {
        await api(`/api/projects/${id}/generation-settings`, { method: 'PUT', body: settings });
        if (revision === saveRevision) status.textContent = trUi('m315');
        return true;
      } catch (e) {
        if (revision === saveRevision) status.innerHTML = errorBar(e);
        return false;
      }
    };
    saveChain = saveChain.then(save, save);
    return saveChain;
  };
  // not saved with the settings: redoing the analysis costs time (and the transcript again), so every generation starts from "reuse"
  let reuseAnalysis = true;
  const updateSums = () => {
    const set = (k, t) => { const el = $(`#sum-${k}`); if (el) el.textContent = t; };
    set('series', !opts.series ? trUi('m316') : opts.series_by === 'parts' ? trUi('m317', [opts.parts]) : trUi('m318', [opts.part_minutes]));
    set('scope', opts.series ? trUi('m319') : `${scopeLabel[opts.scope]?.[0] ?? ''}${$('#scopes .selected .scope-cost')?.textContent ? trUi('m031', [$('#scopes .selected .scope-cost').textContent]) : ''}`);
    const bgm = $('#bgm'), bgmName = opts.bgm === 'auto' ? trUi('m169') : opts.bgm === 'none' ? trUi('m041') : opts.bgm === 'scene' ? trUi('m320') : bgm?.selectedOptions[0]?.textContent.trim() ?? trUi('m321');
    set('sound', [`BGM ${bgmName}`, trUi('m324', [opts.sfx ? trUi('m322') : trUi('m323')]), opts.tsukkomi ? trUi('m326', [{ telop: trUi('m177'), card: trUi('m325') }[opts.tsukkomi_style] ?? trUi('m169')]) : trUi('m327'), ...(opts.mask_words.length ? [trUi('m328', [opts.mask_words.length])] : [])].join(trUi('m329')));
    set('pack', [trUi('m331', [!kitView.opening ? trUi('m230') : opts.opening ? trUi('m330') : trUi('m041')]), trUi('m332', [!kitView.closing ? trUi('m230') : opts.closing ? trUi('m330') : trUi('m041')]), trUi('m333', [opts.scene_cards ? trUi('m330') : trUi('m041')])].join(trUi('m329')));
    set('analysis', reuseAnalysis ? trUi('m334') : trUi('m335'));
  };
  $$('.setgroup').forEach(g => g.addEventListener('toggle', () => {
    groupState[g.dataset.group] = g.open;
    try { localStorage.setItem('vc.genGroups', JSON.stringify(groupState)); } catch { /* Keep in-memory expander state. */ }
  }));
  const scopeCost = (list) => (list.find(s => s.scope === (opts.series ? 'full' : opts.scope)) ?? list[1]).cost;
  const showAnalysis = () => {
    const el = $('#analysisInfo'); if (!el) return;
    const a = estimate?.analysis;
    if (!a?.cached) { el.textContent = trUi('m336'); return; }
    const when = formatDate(a.updated_at);
    const extra = scopeCost(a.redo_scopes).estimated_jpy - scopeCost(estimate.scopes).estimated_jpy;
    el.textContent = reuseAnalysis
      ? trUi('m337', [when])
      : trUi('m340', [when, extra > 0.05 ? trUi('m338', [extra.toFixed(1)]) : '', a.transcript_cached ? trUi('m339') : '']);
  };
  const showCost = () => {
    showAnalysis(); updateSums();
    const el = $('#costLine'); if (!el || !estimate) return;
    const list = reuseAnalysis || !estimate.analysis ? estimate.scopes : estimate.analysis.redo_scopes;
    $$('#scopes [data-scope]').forEach(card => { const b = $('.scope-cost', card), sc = list.find(s => s.scope === card.dataset.scope); if (b && sc) b.textContent = trUi('m193', [jpy(sc.cost)]); });
    const c = scopeCost(list);
    $('#costShort').textContent = trUi('m341', [jpy(c)]);
    el.textContent = trUi('m343', [jpy(c), c.asr_minutes, reuseAnalysis && estimate.analysis?.transcript_cached ? trUi('m342') : '', formatNumber(c.llm_input_tokens), serverText(c.basis)]);
    updateSums();
  };
  showCost();
  $('#analysisReuse').addEventListener('change', () => {
    reuseAnalysis = $$('#analysisReuse fluent-radio').find(r => r.checked)?.value !== 'redo';
    showCost();
  });
  const seriesLabels = (n) => n === 2 ? [trUi('m344'), trUi('m345')] : n === 3 ? [trUi('m344'), trUi('m346'), trUi('m345')] : Array.from({ length: n }, (_, i) => trUi('m347', [i + 1]));
  const showSeries = () => {
    const on = opts.series, byParts = opts.series_by === 'parts', min = Number(opts.part_minutes), parts = Number(opts.parts);
    $('#partMin').disabled = !on || byParts; $('#partCount').disabled = !on || !byParts;
    $$('input[name=seriesBy]').forEach(r => { r.disabled = !on; });
    $('#scopeBox').classList.toggle('muted-box', on); $('#scopeBox').inert = on; $('#scopeBox > .setgroup-body').hidden = on;
    $('#seriesHow').hidden = !on; updateSums();
    const el = $('#seriesInfo');
    if (!on) { $('#gen').textContent = trUi('m291'); el.textContent = trUi('m348'); return; }
    $('#gen').textContent = byParts && parts >= 2 ? trUi('m349', [seriesLabels(parts).length, seriesLabels(parts).slice(0, 3).join(trUi('m329')), parts > 3 ? '…' : '']) : trUi('m350');
    if (!estimate) { el.textContent = trUi('m351'); return; }
    // the length after jump cuts is known once the video has been transcribed (any earlier generation); otherwise the raw length is used
    const se = estimate.series ?? {}, edited = se.edited_sec, base = edited ?? estimate.duration;
    const basis = edited != null ? trUi('m352', [fmt(edited)]) : trUi('m353', [fmt(estimate.duration)]);
    const max = se.max_parts ?? 0;
    $('#partCount').max = String(Math.max(2, max));
    if (byParts) {
      if (!(Number.isInteger(parts) && parts >= 2 && parts <= 20)) { el.textContent = trUi('m354'); return; }
      if (max < 2) { el.textContent = trUi('m355', [basis]); return; }
      if (parts > max) { el.textContent = trUi('m356', [parts, basis, max]); return; }
      el.textContent = trUi('m357', [parts, seriesLabels(parts).join(trUi('m329')), fmt(base / parts), basis, parts]);
      return;
    }
    if (!(min >= 1 && min <= 60)) { el.textContent = trUi('m358'); return; }
    const n = Math.round(base / (min * 60));
    el.textContent = n < 2
      ? trUi('m359', [basis, min])
      : trUi('m360', [n, seriesLabels(n).join(trUi('m329')), fmt(base / n), basis]);
  };
  showSeries();
  $('#seriesSw').addEventListener('change', () => { opts.series = $('#seriesSw').checked; saveOpts(); showSeries(); showCost(); });
  $('#partMin').addEventListener('input', () => { opts.part_minutes = Number($('#partMin').value); saveOpts(); showSeries(); });
  $('#partCount').addEventListener('input', () => { opts.parts = Number($('#partCount').value); saveOpts(); showSeries(); });
  $$('input[name=seriesBy]').forEach(r => r.addEventListener('change', () => { if (r.checked) { opts.series_by = r.value; saveOpts(); showSeries(); } }));
  const scopeRadios = $$('#scopes [role=radio]');
  radioGroup(scopeRadios, (el) => { opts.scope = el.dataset.scope; scopeRadios.forEach(c => { c.classList.toggle('selected', c === el); c.setAttribute('aria-checked', String(c === el)); }); $('#scopeDesc').textContent = scopeLabel[opts.scope][1]; saveOpts(); showCost(); });
  $('#bgm').addEventListener('change', () => { opts.bgm = $('#bgm').value; saveOpts(); });
  $('#sfx').addEventListener('change', () => { opts.sfx = $('#sfx').checked; saveOpts(); });
  $('#tsukkomi').addEventListener('change', () => { opts.tsukkomi = $('#tsukkomi').checked; $('#tsukkomiStyle').disabled = !opts.tsukkomi; $('#tsukkomiStyleBox').hidden = !opts.tsukkomi; saveOpts(); });
  $('#tsukkomiStyle').addEventListener('change', () => { const v = $$('#tsukkomiStyle fluent-radio').find(r => r.checked)?.value; if (v) { opts.tsukkomi_style = v; saveOpts(); } });
  $('#maskWords').addEventListener('input', () => {
    opts.mask_words = $('#maskWords').value.split(/[,、，\n]/).map(s => s.trim()).filter(Boolean);
    clearTimeout(maskSaveTimer); maskSaveTimer = setTimeout(saveOpts, 250);
  });
  $('#openingSw').addEventListener('change', (e) => { opts.opening = e.target.checked; saveOpts(); });
  $('#closingSw').addEventListener('change', (e) => { opts.closing = e.target.checked; saveOpts(); });
  $('#cardsSw').addEventListener('change', (e) => { opts.scene_cards = e.target.checked; saveOpts(); });
  const cardRequests = new AbortController();
  { const prev = cleanup; cleanup = () => { cardRequests.abort(); prev?.(); }; }
  $('#kitBrowse').addEventListener('click', async () => {
    const button = $('#kitBrowse'), input = $('#kitPath'), msg = $('#kitMsg');
    button.disabled = true;
    msg.innerHTML = info(trUi('m092'));
    try {
      const r = await api('/api/dialogs/folder', { method: 'POST', body: { initial_path: input.value ?? '' }, signal: cardRequests.signal });
      if (r.path != null) input.value = r.path;
      msg.innerHTML = r.path == null ? info(trUi('m093')) : info(trUi('m361'));
    } catch (e) { if (e.name !== 'AbortError') msg.innerHTML = errorBar(e); }
    finally { button.disabled = false; }
  });
  $('#cardMake')?.addEventListener('change', async () => {
    const group = $('#cardMake'), radios = $$('#cardMake fluent-radio');
    const make = radios.find(r => r.checked)?.value === 'make';
    const saved = d.project.make_scene_card_images ?? (d.project.scene_card_images?.length ?? 0) > 0;
    if (make === saved) return;
    radios.forEach(r => { r.disabled = true; });
    try {
      const p = await api(`/api/projects/${id}/scene-cards/settings`, { method: 'PUT', body: { make }, signal: cardRequests.signal });
      d.project.make_scene_card_images = p.make_scene_card_images;
      $('#cardMakeBody').hidden = !make; $('#cardSkipNote').hidden = make;
      $('#cardImageMsg').innerHTML = info(make ? trUi('m362') : trUi('m363'));
    } catch (e) {
      if (e.name === 'AbortError') return;
      group.value = saved ? 'make' : 'skip';
      $('#cardImageMsg').innerHTML = errorBar(e);
    } finally { radios.forEach(r => { r.disabled = false; }); }
  });
  $('#makeCards')?.addEventListener('click', async () => {
    const button = $('#makeCards'), msg = $('#cardImageMsg'), [kind, ...rest] = $('#cardSource').value.split(':'), ref = rest.join(':');
    if (kind === 'asset' && !rights.has(ref)) { msg.innerHTML = info(trUi('m364'), 'warning'); return; }
    const count = Number($('#cardCount').value);
    if (!Number.isInteger(count) || count < 2 || count > 6) { msg.innerHTML = info(trUi('m365'), 'warning'); return; }
    button.disabled = true; $('#gen').disabled = true; $$('#cardMake fluent-radio').forEach(r => { r.disabled = true; });
    msg.innerHTML = `<fluent-spinner size="small"></fluent-spinner> ${trUi('m367', [kind === 'brand' ? trUi('m366') : trUi('m248')])}`;
    try {
      const r = await api(`/api/projects/${id}/scene-cards`, { method: 'POST', body: kind === 'brand' ? { brand_path: ref, count } : { asset_id: ref, count }, signal: cardRequests.signal });
      opts.scene_cards = true; saveOpts();
      sessionStorage.setItem(`cardImageMessage:${id}`, r.message);
      router();
    } catch (e) { if (e.name !== 'AbortError') msg.innerHTML = errorBar(e); }
    finally { button.disabled = false; const gen = $('#gen'); if (gen) gen.disabled = false; $$('#cardMake fluent-radio').forEach(r => { r.disabled = false; }); }
  });
  { const message = sessionStorage.getItem(`cardImageMessage:${id}`); sessionStorage.removeItem(`cardImageMessage:${id}`); if (message) $('#cardImageMsg').innerHTML = info(serverText(message), 'success'); }
  $('#kitForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    if (!$('#kitRights').checked) { $('#kitMsg').innerHTML = info(trUi('m368'), 'warning'); $('#kitRights').focus(); return; }
    $('#kitMsg').innerHTML = `<fluent-spinner size="small"></fluent-spinner> ${trUi('m369')}`;
    try {
      const r = await api('/api/brandkit', { method: 'PUT', body: { paths: [$('#kitPath').value ?? ''], rights_confirmed: true, card_sec: Number($('#kitCardSec').value) || 2.5, upscale: !!$('#kitUpscale').checked } });
      opts.opening = !!r.opening; opts.closing = !!r.closing; saveOpts();
      sessionStorage.setItem('kitProblems', JSON.stringify(r.problems ?? []));
      router();
    } catch (e) { $('#kitMsg').innerHTML = errorBar(e); }
  });
  $('#kitClear')?.addEventListener('click', async () => {
    try { await api('/api/brandkit', { method: 'DELETE' }); opts.opening = false; opts.closing = false; saveOpts(); router(); } catch (e) { $('#kitMsg').innerHTML = errorBar(e); }
  });
  { const probs = JSON.parse(sessionStorage.getItem('kitProblems') ?? '[]'); sessionStorage.removeItem('kitProblems'); if (probs.length) { $('#kitForm').closest('details').open = true; $('#kitMsg').innerHTML = probs.map(p => info(serverText(p), 'warning')).join(''); } }
  $('#enhanceBtn')?.addEventListener('click', async () => { try { await api('/api/brandkit/enhance', { method: 'POST' }); router(); } catch (e) { $('#kitMsg').innerHTML = errorBar(e); } });
  // registration-time upscale runs in the background: show its progress, then redraw with the measured result
  const showEnh = (s) => {
    const el = $('#enhStatus'); if (!el || !s) return;
    if (s.state === 'running') el.innerHTML = `<p class="caption">${trUi('m370', [esc(serverText(s.message))])}</p><fluent-progress-bar max="100" value="${Math.round((s.progress ?? 0) * 100)}" aria-label="${trUi('m371')}"></fluent-progress-bar>`;
    else if (s.state === 'failed') el.innerHTML = info(serverText(s.message), 'warning');
    else el.innerHTML = '';
  };
  showEnh(kitView.enhance);
  if (kitView.enhance?.state === 'running') {
    const t = setInterval(async () => {
      let v; try { v = await api('/api/brandkit'); } catch { return; }
      if (!document.body.contains($('#enhStatus'))) { clearInterval(t); return; }
      if (v.enhance?.state === 'running') showEnh(v.enhance); else { clearInterval(t); router(); }
    }, 2000);
    const prev = cleanup; cleanup = () => { clearInterval(t); prev?.(); };
  }
  $('#bgmForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const files = [...$('#bgmFile').files];
    if (!files.length) { $('#bgmMsg').innerHTML = info(trUi('m372'), 'warning'); return; }
    const fd = new FormData(); files.forEach(f => fd.append('file', f)); fd.append('rights_confirmed', $('#bgmRights').checked ? 'true' : 'false');
    $('#bgmMsg').innerHTML = info(trUi('m373', [files.length]));
    try {
      const r = await fetch(`/api/projects/${id}/bgm`, { method: 'POST', body: fd, headers: { 'X-VideoCreator': '1' } });
      const data = await r.json(); if (!r.ok) throw Object.assign(new Error(data?.error?.message ?? r.statusText), { code: data?.error?.code });
      // registering tracks means choosing per scene from them
      opts.bgm = 'scene'; await saveOpts();
      sessionStorage.setItem('vc.bgmFlash', trUi('m375', [data.tracks.length, data.skipped.length ? trUi('m374', [data.skipped.map(s => `${s.name}（${s.reason}）`).join('、')]) : '']));
      router();
    } catch (e) { $('#bgmMsg').innerHTML = errorBar(e); }
  });
  $$('[data-del-bgm]').forEach(b => b.addEventListener('click', async () => {
    try {
      await api(`/api/projects/${id}/bgm/${encodeURIComponent(b.dataset.delBgm)}`, { method: 'DELETE' });
      if (opts.bgm === b.dataset.delBgm) { opts.bgm = userBgm.length > 1 ? 'scene' : 'auto'; await saveOpts(); }
      router();
    } catch (e) { $('#bgmMsg').innerHTML = errorBar(e); }
  }));
  { const flash = sessionStorage.getItem('vc.bgmFlash'); if (flash) { sessionStorage.removeItem('vc.bgmFlash'); $('#bgmMsg').innerHTML = info(flash, 'success'); } }
  const patchAsset = async (aid, body) => { try { await api(`/api/assets/${aid}`, { method: 'PATCH', body }); router(); } catch (e) { $('#assetMsg').innerHTML = errorBar(e); } };
  $$('select.role').forEach(s => s.addEventListener('change', () => patchAsset(s.dataset.asset, { role: s.value })));
  $$('input.spk').forEach(i => i.addEventListener('change', () => patchAsset(i.dataset.asset, i.value ? { speaker: Number(i.value) } : { clear_speaker: true })));
  $$('input.off').forEach(i => i.addEventListener('change', () => patchAsset(i.dataset.asset, { sync_offset: Number(i.value) })));

  // file input / drag & drop upload with progress (streams into the project folder)
  const upload = (file) => new Promise((resolve, reject) => {
    const fd = new FormData(); fd.append('file', file);
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `/api/projects/${id}/assets/upload`); xhr.setRequestHeader('X-VideoCreator', '1');
    $('#upload').innerHTML = `<span class="caption">${trUi('m376', [esc(file.name)])}</span><fluent-progress-bar id="up" max="100" value="0"></fluent-progress-bar>`;
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) $('#up')?.setAttribute('value', String(Math.round(e.loaded / e.total * 100))); };
    xhr.onload = () => { const data = xhr.responseText ? JSON.parse(xhr.responseText) : {}; xhr.status < 300 ? resolve(data) : reject(Object.assign(new Error(data?.error?.message ?? xhr.statusText), { code: data?.error?.code })); };
    xhr.onerror = () => reject(new Error(trUi('m377')));
    xhr.send(fd);
  });
  const handle = async (file) => { try { await upload(file); router(); } catch (e) { $('#assetMsg').innerHTML = errorBar(e); $('#upload').innerHTML = ''; } };
  $('#pick').addEventListener('click', () => $('#file').click());
  $('#file').addEventListener('change', () => $('#file').files[0] && handle($('#file').files[0]));
  const drop = $('#drop');
  drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('over'); });
  drop.addEventListener('dragleave', () => drop.classList.remove('over'));
  drop.addEventListener('drop', (e) => { e.preventDefault(); drop.classList.remove('over'); const f = e.dataTransfer.files[0]; if (f) handle(f); });
  $('#byPath').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    try { await api(`/api/projects/${id}/assets`, { method: 'POST', body: { path: $('#path').value } }); router(); }
    catch (e) { $('#assetMsg').innerHTML = errorBar(e); }
  });
  $$('.rights').forEach(cb => cb.addEventListener('change', async () => {
    if (!cb.checked) return;
    try { await api(`/api/projects/${id}/rights`, { method: 'POST', body: { asset_id: cb.dataset.asset, confirmed: true } }); router(); }
    catch (e) { $('#assetMsg').innerHTML = errorBar(e); }
  }));
  const presetRadios = $$('#presets [role=radio]');
  const choose = (el) => {
    selected = el.dataset.id; saveOpts();
    presetRadios.forEach(c => { c.closest('.preset').classList.toggle('selected', c === el); c.setAttribute('aria-checked', String(c === el)); });
    // the pattern's pause threshold changes the length after jump cuts, so refresh the series preview
    if (assets.length) api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`).then(e => { if (!gone()) { estimate = e; showSeries(); } }).catch(() => { });
  };
  const syncPresets = radioGroup(presetRadios, choose);
  // the whole card is the click target; the example links below the radio keep working as links
  $$('#presets .preset').forEach(card => card.addEventListener('click', (e) => {
    const r = $('[role=radio]', card);
    if (e.target.closest('a') || r.contains(e.target) || r.getAttribute('aria-disabled') === 'true') return;
    choose(r); syncPresets();
  }));
  $('#gen').addEventListener('click', async () => {
    if (notReady) {
      $('#genMsg').innerHTML = info(`${notReady}。`, 'warning');
      (!assets.length ? $('#pick') : $$('.rights').find(c => !c.checked))?.focus();
      return;
    }
    const { series, series_by, parts, part_minutes, ...rest } = opts;
    const options = { ...rest, parts: series && series_by === 'parts' ? Number(parts) : null, part_minutes: series && series_by !== 'parts' ? Number(part_minutes) : null, reuse_analysis: reuseAnalysis };
    try {
      clearTimeout(maskSaveTimer);
      if (!await saveOpts()) return;
      const r = await api(`/api/projects/${id}/jobs`, { method: 'POST', body: { preset_ids: [selected], options, derived_from_job_id: derivedFrom?.project === id ? derivedFrom.job : null } }); derivedFrom = null; location.hash = `#/job/${r.job_id}`;
    }
    catch (e) { $('#genMsg').innerHTML = errorBar(e); }
  });
}

// ---------------- patterns: every generation of a project, side by side ----------------
let derivedFrom = null;   // { project, job }: the pattern whose settings were carried over; sent with the next generation
const JOB_STATE = { ready: 'm378', done: 'm378', failed: 'm379', cancelled: 'm380' };
function patternSummary(j, presets) {
  const o = j.options ?? {};
  const scope = { economy: trUi('m158'), balanced: trUi('m160'), full: trUi('m162') }[o.scope] ?? o.scope;
  const bits = [trUi('m381', [scope])];
  if (o.parts) bits.push(trUi('m382', [o.parts])); else if (o.part_minutes) bits.push(trUi('m383', [o.part_minutes]));
  bits.push(`BGM: ${o.bgm === 'auto' ? trUi('m169') : o.bgm === 'none' ? trUi('m041') : trUi('m321')}`);
  if (o.opening) bits.push(trUi('m255')); if (o.closing) bits.push(trUi('m256'));
  if (o.scene_cards) bits.push(trUi('m384')); if (o.tsukkomi) bits.push(trUi('m385', [{ telop: trUi('m177'), card: trUi('m325') }[o.tsukkomi_style] ?? trUi('m169')]));
  if (o.mask_words?.length) bits.push(trUi('m386', [o.mask_words.length]));
  if (j.instruction) bits.push(trUi('m387', [j.instruction]));
  if (o.reuse_analysis === false) bits.push(trUi('m388'));
  return bits.join(' · ');
}
async function loadPatterns(projectId, presets) {
  const gone = routeGuard();
  const box = $('#patternList'); if (!box) return;
  const items = await api(`/api/projects/${projectId}/patterns`);
  if (gone() || !$('#patternList')) return;
  for (const x of items) {
    x.preset_names = x.job.preset_ids.map((id, i) => presets.find(p => p.id === id)?.name ?? x.preset_names[i] ?? id);
  }
  $('#patternCount').textContent = String(items.length);
  const ids = new Set(items.map(x => x.job.id));
  box.innerHTML = items.length ? items.map(x => {
    const j = x.job, finished = j.status in JOB_STATE;
    const state = JOB_STATE[j.status] ? trUi(JOB_STATE[j.status]) : trUi('m389');
    const parent = j.derived_from_job_id && ids.has(j.derived_from_job_id) ? `<span class="caption">${trUi('m390', [esc(items.find(y => y.job.id === j.derived_from_job_id).preset_names.join(trUi('m329')))])}</span>` : '';
    return `<div class="pattern" data-job="${esc(j.id)}">
      <div>
        <div><b>${esc(x.preset_names.join(trUi('m329')))}</b> <fluent-badge appearance="tint" color="${j.status === 'failed' ? 'danger' : finished ? 'success' : 'informative'}">${state}</fluent-badge>
          <span class="caption">${esc(formatDate(j.created_at))}</span> ${parent}</div>
        <div class="caption">${esc(patternSummary(j, presets))}</div>
        <div class="caption">${trUi('m393', [x.candidates, x.approved, x.exported])}</div>
      </div>
      <div class="row">
        <fluent-anchor-button size="small" href="#/job/${esc(j.id)}">${finished ? trUi('m391') : trUi('m392')}</fluent-anchor-button>
        <fluent-button size="small" class="reuse" data-job="${esc(j.id)}" ${finished ? '' : 'disabled'} aria-label="${trUi('m394', [esc(x.preset_names.join(trUi('m329')))])}">${trUi('m395')}</fluent-button>
        <fluent-button size="small" appearance="subtle" class="rm" data-job="${esc(j.id)}" ${x.busy ? 'disabled' : ''} aria-label="${trUi('m396', [esc(x.preset_names.join(trUi('m329')))])}">${trUi('m122')}</fluent-button>
      </div>
      <div class="rm-confirm" hidden style="grid-column:1/-1"></div>
    </div>`;
  }).join('') : `<p class="caption">${trUi('m397')}</p>`;
  $$('#patternList .reuse').forEach(b => b.addEventListener('click', async () => {
    const x = items.find(y => y.job.id === b.dataset.job), o = x.job.options ?? {};
    try {
      await api(`/api/projects/${projectId}/generation-settings`, { method: 'PUT', body: {
        preset_id: x.job.preset_ids[0], scope: o.scope ?? 'balanced', bgm: o.bgm ?? 'auto', sfx: o.sfx ?? true, tsukkomi: !!o.tsukkomi, tsukkomi_style: o.tsukkomi_style ?? 'auto',
        series: !!(o.parts || o.part_minutes), series_by: o.parts ? 'parts' : 'minutes', parts: o.parts ?? 2, part_minutes: o.part_minutes ?? 15,
        opening: !!o.opening, closing: !!o.closing, scene_cards: !!o.scene_cards, mask_words: o.mask_words ?? [] } });
      derivedFrom = { project: projectId, job: x.job.id };
      await router();
      $('#gen')?.scrollIntoView({ block: 'center' });
      $('#genMsg').innerHTML = info(trUi('m398'), 'success');
    } catch (e) { $('#patternMsg').innerHTML = errorBar(e); }
  }));
  $$('#patternList .rm').forEach(b => b.addEventListener('click', () => {
    const row = b.closest('.pattern'), box = $('.rm-confirm', row), x = items.find(y => y.job.id === b.dataset.job);
    box.hidden = false;
    box.innerHTML = `<fluent-message-bar intent="warning" layout="multiline">${trUi('delete.patternLine', [esc(x.preset_names.join(trUi('m329')))])}</fluent-message-bar>
      <div class="row"><fluent-button size="small" appearance="primary" class="rm-ok">${trUi('m401')}</fluent-button><fluent-button size="small" class="rm-cancel">${trUi('m402')}</fluent-button></div>`;
    $('.rm-cancel', box).addEventListener('click', () => { box.hidden = true; box.innerHTML = ''; b.focus(); });
    $('.rm-ok', box).addEventListener('click', async () => {
      $('.rm-ok', box).disabled = true;
      try {
        await api(`/api/jobs/${encodeURIComponent(b.dataset.job)}`, { method: 'DELETE' });
        if (derivedFrom?.job === b.dataset.job) derivedFrom = null;
        await router();
        $('#patternMsg').innerHTML = info(trUi('m403', [x.preset_names.join(trUi('m329'))]), 'success');
      } catch (e) { box.insertAdjacentHTML('beforeend', errorBar(e)); $('.rm-ok', box).disabled = false; }
    });
    $('.rm-cancel', box).focus();
  }));
}

// ---------------- job: progress (SSE) + candidates ----------------
async function job(jobId) {
  const gone = routeGuard();
  const j = await api(`/api/jobs/${jobId}`);
  if (gone()) return;
  if (j.status === 'ready' || j.status === 'done') return candidates(j);
  await saveWorkflowState(j.project_id, { step: 'job', job_id: j.id });
  setSteps(2, 2, { 0: `#/p/${j.project_id}/settings`, 1: `#/p/${j.project_id}/settings` });
  const started = Date.now();
  const skeleton = (failed, message) => {
    main.innerHTML = `
      <h1>${trUi('m411')}</h1>
      <div class="grid cols-2" style="align-items:start">
        <section class="card">
          ${failed ? errorBar(failed) : `<p id="jobMsg" aria-live="polite">${esc(message)}</p>`}
          ${failed ? '' : `<div class="progress-row"><span class="caption">${trUi('m404')}</span><fluent-progress-bar id="jobBar" max="100" value="0" aria-label="${trUi('m405')}"></fluent-progress-bar><span id="jobPct" class="caption pct">0%</span></div>
          <div id="stepBox" class="progress-row" hidden><span id="stepLabel" class="caption"></span><fluent-progress-bar id="stepBar" max="100" aria-label="${trUi('m406')}"></fluent-progress-bar><span id="stepPct" class="caption pct"></span></div>`}
          <p class="caption" style="margin-top:8px">${trUi('m412')} <span id="elapsed">0:00</span><span id="eta"></span>${j.cost ? trUi('m407', [j.cost.estimated_jpy.toFixed(1)]) : ''} · ${j.options?.reuse_analysis === false ? trUi('m408') : trUi('m409')}</p>
          <div id="stages">${stageLabels().map(([k, label]) => `<div class="stage" data-stage="${k}"><span class="dot"></span><span>${label}</span><span class="caption"></span></div>`).join('')}</div>
          <div class="row" style="margin-top:12px">${failed ? `<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings">${trUi('m410')}</fluent-anchor-button>` : `<fluent-button id="cancel">${trUi('m278')}</fluent-button>`}</div>
        </section>
        <section class="card"><h2>${trUi('m413')}</h2>
          <ul class="caption" style="line-height:20px">
            <li>${trUi('m414')}</li>
            <li>${trUi('m415')}</li>
            <li>${trUi('m416')}</li>
            <li>${trUi('m417')}</li>
            <li>${trUi('m418')}</li>
            <li>${trUi('m419')}</li>
            <li>${trUi('m420')}</li>
          </ul></section>
      </div>`;
    $('#cancel')?.addEventListener('click', async () => { try { await api(`/api/jobs/${jobId}/cancel`, { method: 'POST' }); } catch { } });
  };
  // update values in place: rebuilding the DOM on every SSE message would restart the bars' animation and reset screen readers
  let stepKey = null, stepT0 = 0;
  const update = (u) => {
    const stages = stageLabels(), idx = stages.findIndex(s => s[0] === u.stage);
    if ($('#jobMsg')) $('#jobMsg').textContent = jobMessage(u);
    const pct = Math.round((u.progress ?? 0) * 100);
    $('#jobBar')?.setAttribute('value', String(pct));
    if ($('#jobPct')) $('#jobPct').textContent = `${pct}%`;
    const step = u.step_progress;
    const box = $('#stepBox');
    if (box) {
      box.hidden = idx < 0;
      $('#stepLabel').textContent = u.step ? serverText(u.step, idx >= 0 ? stages[idx][1] : trUi('m423')) : (idx >= 0 ? stages[idx][1] : '');
      const bar = $('#stepBar');
      if (typeof step === 'number') { bar.setAttribute('value', String(Math.round(step * 100))); $('#stepPct').textContent = `${Math.round(step * 100)}%`; }
      else { bar.removeAttribute('value'); $('#stepPct').textContent = ''; }
    }
    // ETA only for a measured sub-task, from its own elapsed time
    const key = `${u.stage}|${(u.step ?? '').split(':')[0]}`;
    if (key !== stepKey) { stepKey = key; stepT0 = Date.now(); }
    const eta = $('#eta');
    if (eta) eta.textContent = typeof step === 'number' && step >= 0.05 && step < 1 && Date.now() - stepT0 > 3000 ? trUi('m421', [fmt((Date.now() - stepT0) / 1000 * (1 - step) / step)]) : '';
    $$('#stages .stage').forEach((el, i) => {
      const done = i < idx || u.stage === 'ready', active = i === idx;
      el.classList.toggle('done', done); el.classList.toggle('active', active);
      el.lastElementChild.textContent = done ? trUi('m378') : active ? (typeof step === 'number' ? trUi('m422', [Math.round(step * 100)]) : trUi('m423')) : '';
    });
  };
  let last = { stage: j.status, progress: j.progress, message: j.message };
  skeleton(null, jobMessage(j));
  update(last);
  const timer = setInterval(() => { const el = $('#elapsed'); if (el) el.textContent = fmt((Date.now() - started) / 1000); }, 1000);
  const es = new EventSource(`/api/jobs/${jobId}/events`);
  es.onmessage = async (ev) => {
    if (gone()) return;
    const u = JSON.parse(ev.data);
    if (u.stage === 'ready') { es.close(); clearInterval(timer); const ready = await api(`/api/jobs/${jobId}`); if (!gone()) candidates(ready); return; }
    if (u.stage === 'failed' || u.stage === 'cancelled') {
      es.close(); clearInterval(timer);
      const cur = await api(`/api/jobs/${jobId}`);
      skeleton({ message: cur.message, code: cur.error_code ?? (u.stage === 'cancelled' ? 'CANCELLED' : '') });
      update({ ...last, step_progress: null });
      $$('#stages .stage.active').forEach(el => { el.lastElementChild.textContent = u.stage === 'cancelled' ? trUi('m380') : trUi('m379'); });
      $('#eta')?.replaceChildren();
      return;
    }
    last = u;
    update(u);
  };
  cleanup = () => { es.close(); clearInterval(timer); };
}

async function candidates(j) {
  const gone = routeGuard();
  setSteps(3, 3, { 0: `#/p/${j.project_id}/settings`, 1: `#/p/${j.project_id}/settings` });
  const list = await api(`/api/jobs/${j.id}/candidates`);
  if (gone()) return;
  await saveWorkflowState(j.project_id, { step: 'candidates', job_id: j.id });
  if (gone()) return;
  setSteps(3, 3, jobStepLinks(j, list));
  if (list.length && list[0].candidate.series) return seriesView(j, list);
  const bar = (label, v) => `<span>${label}</span><span class="bar"><span style="width:${v * 10}%"></span></span><span>${Number(v).toFixed(v % 1 ? 1 : 0)}</span>`;
  main.innerHTML = `
    <div class="row" style="justify-content:space-between"><h1>${trUi('m443', [list.length])}</h1>
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings" appearance="subtle">${trUi('m444')}</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(serverText(w), 'warning')).join('')}
    <p class="caption">${trUi('m445', [esc(j.llm_model ?? ''), esc(j.prompt_version ?? ''), (j.cost?.estimated_jpy ?? 0).toFixed(2), Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)])}</p>
    ${workProgress(j, list)}
    <div class="grid cols-cands">
      ${list.map(({ candidate: c, preview_url, preview, preview_stale }) => `
        <article class="card stack" aria-label="${trUi('m436', [c.rank])}">
          <div class="card-head"><b>#${c.rank}</b><span class="badges">${preview_stale ? `<fluent-badge appearance="tint" color="warning">${trUi('m424')}</fluent-badge>` : qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${trUi('m437', [(c.segments.reduce((a, s) => a + s.t1 - s.t0, 0)).toFixed(0)])}</fluent-badge>${c.source === 'fallback' ? `<fluent-badge appearance="tint" color="warning">${trUi('m425')}</fluent-badge>` : ''}</span></div>
          ${preview_url ? phone(preview_url) : `<p class="muted">${trUi('m426')}</p>`}
          <div class="headline">${esc(c.headline || c.hook_text || trUi('m427'))}</div>
          <div class="caption">${c.segments.map(s => `${fmt(s.t0)}–${fmt(s.t1)}`).join(' + ')}</div>
          <p>${esc(c.reason)}</p>
          <div class="scores">${bar(trUi('m428'), c.scores.hook)}${bar(trUi('m429'), c.scores.standalone)}${bar(trUi('m430'), c.scores.payoff)}${bar(trUi('m431'), c.scores.emotion)}${bar(trUi('m432'), c.scores.signal)}${bar(trUi('m433'), c.scores.final)}</div>
          ${c.notes?.length ? `<details><summary class="caption">${trUi('m434', [c.notes.length])}</summary><ul class="notes">${c.notes.map(n => `<li>${esc(serverText(n))}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button href="#/ep/${esc(c.edit_plan_id)}">${trUi('m438')}</fluent-anchor-button>
          ${approvalBlock(list.find(x => x.candidate.id === c.id), trUi('m435', [c.rank]))}
        </article>`).join('')}
    </div>
    ${batchBox()}
    <section class="card" style="margin-top:16px">
      <h2>${trUi('m446')}</h2>
      <div class="chips" id="chips">${[trUi('m439'), trUi('m440'), trUi('m441'), trUi('m442')].map(t => `<fluent-button size="small" data-t="${esc(t)}">${esc(t)}</fluent-button>`).join('')}</div>
      <div class="row" style="margin-top:8px"><fluent-text-input id="instr" maxlength="200" placeholder="${trUi('m447')}" style="flex:1" aria-label="${trUi('m448')}"></fluent-text-input><fluent-button id="regen">${trUi('m449')}</fluent-button></div>
      <div id="regenMsg"></div>
    </section>`;
  wireSafeToggle();
  wireApprovals(j, list);
  $$('#chips fluent-button').forEach(b => b.addEventListener('click', () => { $('#instr').value = b.dataset.t; }));
  $('#regen').addEventListener('click', async () => {
    try { const r = await api(`/api/jobs/${j.id}/regenerate`, { method: 'POST', body: { instruction: $('#instr').value || null, exclude_candidate_ids: [] } }); location.hash = `#/job/${r.job_id}`; }
    catch (e) { $('#regenMsg').innerHTML = errorBar(e); }
  });
}

// ---------------- series (前編/後編): the parts in order ----------------
function seriesView(j, list) {
  const first = list[0].candidate.series;
  const total = list.reduce((a, x) => a + (x.preview?.duration ?? 0), 0);
  main.innerHTML = `
    <div class="row" style="justify-content:space-between"><h1>${trUi('m459', [list.length, first.series_title ? `: ${esc(first.series_title)}` : ''])}</h1>
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings" appearance="subtle">${trUi('m444')}</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(serverText(w), 'warning')).join('')}
    <p class="caption">${trUi('m460', [first.requested_parts ? trUi('m450', [first.requested_parts, (first.target_sec / 60).toFixed(1)]) : trUi('m451', [(first.target_sec / 60).toFixed(1)]), fmt(total), esc(j.llm_model && j.llm_model !== 'none' ? j.llm_model : trUi('m452')), (j.cost?.estimated_jpy ?? 0).toFixed(2), Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)])}</p>
        ${workProgress(j, list)}
        <div class="grid cols-cands">
          ${list.map(({ candidate: c, preview_url, preview, preview_stale }) => {
            const se = c.series;
        return `
        <article class="card stack" aria-label="${esc(se.label)}">
          <div class="card-head"><b>${esc(se.label)}</b><span class="badges">${preview_stale ? `<fluent-badge appearance="tint" color="warning">${trUi('m424')}</fluent-badge>` : qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${preview ? fmt(preview.duration) : '—'}</fluent-badge></span></div>
          ${preview_url ? phone(preview_url) : `<p class="muted">${trUi('m426')}</p>`}
          <div class="headline">${esc(c.hook_text)}</div>
          <div class="caption">${trUi('m457', [fmt(se.body.t0), fmt(se.body.t1)])}</div>
          <p class="caption">${se.part < se.parts ? trUi('m453', [esc(se.boundary)]) : trUi('m454')}</p>
          ${se.chapters?.length ? `<details open><summary class="caption">${trUi('m455', [se.chapters.length])}</summary><ul class="notes">${se.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          ${c.notes?.length ? `<details><summary class="caption">${trUi('m456', [c.notes.length])}</summary><ul class="notes">${c.notes.map(n => `<li>${esc(serverText(n))}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button href="#/ep/${esc(c.edit_plan_id)}">${trUi('m458', [esc(se.label)])}</fluent-anchor-button>
          ${approvalBlock(list.find(x => x.candidate.id === c.id), se.label)}
        </article>`;
      }).join('')}
    </div>
    ${batchBox()}
    <p class="caption" style="margin-top:16px">${trUi('m461')}</p>`;
  wireSafeToggle();
  wireApprovals(j, list);
}

// ---------------- approval & export of one or more candidates (candidates / series / export screens) ----------------
// One job can yield several videos: each is reviewed and approved on its own (step 5), then the approved ones are exported together (step 6).
function videoName(x) { return x.candidate.series?.label ?? trUi('m435', [x.candidate.rank]); }
function videoStatus(x) {
  const e = x.export;
  if (e && (e.state === 'queued' || e.state === 'rendering')) return { key: 'exporting', label: trUi('m462'), color: 'informative' };
  if (x.approval?.stale) return { key: 'stale', label: trUi('m463'), color: 'warning' };
  if (x.approval && x.final_render_id) return { key: 'exported', label: trUi('m464'), color: 'success' };
  if (x.approval) return { key: 'approved', label: trUi('m465'), color: 'brand' };
  return { key: 'todo', label: trUi('m466'), color: 'informative' };
}
const isReviewed = (x) => ['approved', 'exported', 'exporting'].includes(videoStatus(x).key);
// the review queue: the next unapproved video after the current one (wrapping), never the current one itself; null = go to step 6
function nextToReview(list, epId) {
  const i = list.findIndex(x => x.candidate.edit_plan_id === epId);
  return [...list.slice(i + 1), ...list.slice(0, Math.max(0, i))].find(x => x.candidate.edit_plan_id !== epId && !isReviewed(x)) ?? null;
}
function jobStepLinks(j, list = []) {
  const settings = `#/p/${j.project_id}/settings`;
  const first = list.find(x => !isReviewed(x)) ?? list[0];
  return { 0: settings, 1: settings, 2: `#/job/${j.id}`, 3: `#/job/${j.id}`, ...(first?.candidate.edit_plan_id ? { 4: `#/ep/${first.candidate.edit_plan_id}` } : {}), 5: `#/job/${j.id}/exports` };
}
// "n of N reviewed" + where to go next; shown on the candidates screen
function workProgress(j, list, onExports = false) {
  if (list.length < 2) return '';
  const reviewed = list.filter(isReviewed).length, exported = list.filter(x => videoStatus(x).key === 'exported').length;
  const next = list.find(x => !isReviewed(x));
  return `<section class="card work-progress" id="workProgress" aria-label="${trUi('m470')}">
    <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
      <div class="stack" style="gap:4px"><b>${trUi('m471', [reviewed, list.length, exported, list.length])}</b>
        <span class="caption">${trUi('m472')}</span></div>
      <div class="row">${next ? `<fluent-anchor-button id="nextReview" href="#/ep/${esc(next.candidate.edit_plan_id)}">${trUi('m467', [esc(videoName(next))])}</fluent-anchor-button>` : ''}
        ${onExports ? '' : `<fluent-anchor-button id="toExports" href="#/job/${esc(j.id)}/exports" ${reviewed ? 'appearance="primary"' : ''}>${trUi('m469', [reviewed ? trUi('m468', [reviewed]) : ''])}</fluent-anchor-button>`}</div>
    </div>
    <ol class="video-status">${list.map(x => { const s = videoStatus(x); return `<li><a href="#/ep/${esc(x.candidate.edit_plan_id)}">${esc(videoName(x))}</a> <fluent-badge appearance="tint" color="${s.color}">${s.label}</fluent-badge></li>`; }).join('')}</ol>
  </section>`;
}
function approvalBadge(a) {
  if (!a) return `<fluent-badge appearance="tint" color="informative">${trUi('m473')}</fluent-badge>`;
  if (a.stale) return `<fluent-badge appearance="tint" color="warning">${trUi('m474')}</fluent-badge>`;
  return `<fluent-badge appearance="tint" color="success">${trUi('m465')}</fluent-badge><span class="caption">${esc(formatDate(a.at, { dateStyle: 'short', timeStyle: 'short' }))}${a.by ? ` · ${esc(a.by)}` : ''}</span>`;
}
function approvalBlock(x, name) {
  const id = esc(x.candidate.id);
  return `<div class="stack approval" data-cand="${id}" data-name="${esc(name)}">
    <div class="row" style="align-items:center;gap:8px;flex-wrap:wrap"><span class="row appr-badge" style="gap:8px;align-items:center">${approvalBadge(x.approval)}</span></div>
    <div class="row" style="align-items:center;gap:8px;flex-wrap:wrap">
      <fluent-button size="small" class="appr-btn"></fluent-button>
      ${field(trUi('m475'), `<fluent-checkbox slot="input" class="exp-pick" aria-label="${trUi('m476', [esc(name)])}"></fluent-checkbox>`)}
    </div>
    <div class="appr-msg" aria-live="polite"></div>
    <div class="progress-row exp-row" hidden><span class="caption exp-label"></span><fluent-progress-bar max="100" aria-label="${trUi('m477', [esc(name)])}"></fluent-progress-bar><span class="caption pct exp-pct"></span></div>
    <p class="caption exp-text" aria-live="polite"></p>
  </div>`;
}
function batchBox(showHeading = true) {
  return `<section class="card stack" id="batchBox" style="margin-top:16px">
    ${showHeading ? `<h2>${trUi('m478')}</h2>` : ''}
    <p class="caption">${trUi('m479')}</p>
    <div class="row"><fluent-button id="pickAll">${trUi('m480')}</fluent-button><fluent-button appearance="primary" id="batchExport" disabled>${trUi('m481')}</fluent-button></div>
    <div class="progress-row" id="batchRow" hidden><span class="caption" id="batchLabel"></span><fluent-progress-bar id="batchBar" max="100" aria-label="${trUi('m482')}"></fluent-progress-bar><span class="caption pct" id="batchPct"></span></div>
    <div id="batchMsg" aria-live="polite"></div>
  </section>`;
}
function wireApprovals(j, list, steps = [3, 3]) {
  const gone = routeGuard();
  const items = new Map(list.map(x => [x.candidate.id, x]));
  const key = `exportBatch:${j.id}`;
  let batch = JSON.parse(sessionStorage.getItem(key) ?? '[]'); // render ids of the last batch started here
  let timer = null;
  const block = (cid) => main.querySelector(`.approval[data-cand="${CSS.escape(cid)}"]`);
  const approved = (x) => x.approval && !x.approval.stale;
  const active = (x) => x.export && (x.export.state === 'queued' || x.export.state === 'rendering');
  const setHtml = (el, html) => { if (el.innerHTML !== html) el.innerHTML = html; };
  const progressSig = (all) => all.map(x => `${x.candidate.id}:${videoStatus(x).key}`).join(',');
  let progressKey = progressSig(list);
  const renderProgress = () => {
    const box = $('#workProgress'), all = [...items.values()];
    const k = progressSig(all);
    if (k === progressKey) return;
    progressKey = k;
    if (box) box.outerHTML = workProgress(j, all, steps[0] === 5);
    setSteps(...steps, jobStepLinks(j, all));
  };
  const picked = () => [...items.values()].filter(x => block(x.candidate.id)?.querySelector('.exp-pick')?.checked && approved(x) && !active(x));
  const updateButton = () => {
    const n = picked().length, b = $('#batchExport');
    b.disabled = n === 0;
    b.textContent = n ? trUi('m483', [n]) : trUi('m481');
  };
  const show = (x) => {
    const el = block(x.candidate.id); if (!el) return;
    setHtml(el.querySelector('.appr-badge'), approvalBadge(x.approval));
    const btn = el.querySelector('.appr-btn');
    btn.textContent = approved(x) ? trUi('m484') : x.approval?.stale ? trUi('m485') : trUi('m486');
    if (approved(x)) btn.removeAttribute('appearance'); else btn.setAttribute('appearance', 'primary');
    btn.disabled = active(x);
    const pick = el.querySelector('.exp-pick');
    pick.disabled = !approved(x) || active(x);
    if (pick.disabled && pick.checked) pick.checked = false;
    const e = x.export, row = el.querySelector('.exp-row'), bar = row.querySelector('fluent-progress-bar');
    row.hidden = !active(x);
    if (active(x)) {
      const pct = e.state === 'rendering' && typeof e.progress === 'number' ? Math.round(e.progress * 100) : 0;
      bar.setAttribute('value', String(pct));
      el.querySelector('.exp-label').textContent = e.state === 'queued' ? trUi('m487') : trUi('m462');
      el.querySelector('.exp-pct').textContent = `${pct}%`;
    }
    const last = x.final_render_id ? `<a href="#/render/${esc(x.final_render_id)}">${trUi('m488')}</a>` : '';
    setHtml(el.querySelector('.exp-text'),
      e?.state === 'queued' ? `${trUi('m489')}<fluent-button size="small" class="exp-cancel" data-render="${esc(e.render_id)}">${trUi('m490')}</fluent-button>`
      : e?.state === 'rendering' ? esc(e.step ? trUi('m491', [serverText(e.step)]) : jobMessage(e))
      : e?.state === 'failed' ? `<span class="fail">${trUi('m493')}</span> <a href="#/render/${esc(e.render_id)}">${trUi('m494')}</a>${last ? trUi('m492', [last]) : ''}`
      : e?.state === 'cancelled' ? trUi('m496', [last ? trUi('m495', [last]) : ''])
      : x.final_render_stale ? `<span class="warnc">${trUi('m497')}</span>`
      : last ? trUi('m498', [last]) : '');
    const result = el.closest('.export-item')?.querySelector('.export-result');
    if (result) {
      const id = x.final_render_stale ? '' : x.final_render_id ?? '';
      if (result.dataset.render !== id) {
        result.dataset.render = id;
        result.innerHTML = id ? '<div class="stack artifact-body"></div>' : '';
        if (id) exportArtifacts(id, result.firstElementChild);
      }
    }
  };
  const showBatch = () => {
    const xs = [...items.values()].filter(x => x.export && batch.includes(x.export.render_id));
    const row = $('#batchRow');
    if (!xs.length) { row.hidden = true; return; }
    const val = (x) => x.export.state === 'rendering' ? (x.export.progress ?? 0) : x.export.state === 'queued' ? 0 : 1;
    // withdrawn items are not part of the work any more; failed ones are finished (shown separately)
    const work = xs.filter(x => x.export.state !== 'cancelled');
    const pct = work.length ? Math.round(100 * work.reduce((a, x) => a + val(x), 0) / work.length) : 0;
    const n = (s) => xs.filter(x => x.export.state === s).length;
    row.hidden = false;
    $('#batchBar').setAttribute('value', String(pct));
    $('#batchPct').textContent = `${pct}%`;
    const tail = [n('failed') ? trUi('m499', [n('failed')]) : '', n('cancelled') ? trUi('m500', [n('cancelled')]) : ''].filter(Boolean).join(trUi('m329'));
    $('#batchLabel').textContent = trUi('m501', [n('done'), work.length, tail ? `（${tail}）` : '']);
    if (!xs.some(active)) { clearInterval(timer); timer = null; }
  };
  const refresh = async () => {
    const fresh = await api(`/api/jobs/${j.id}/candidates`);
    if (gone()) return;
    for (const x of fresh) { items.set(x.candidate.id, x); show(x); }
    showBatch(); updateButton(); renderProgress();
    if ([...items.values()].some(active) && !timer) timer = setInterval(() => refresh().catch(() => { }), 1000);
  };
  for (const x of items.values()) {
    show(x);
    const el = block(x.candidate.id);
    el.querySelector('.appr-btn').addEventListener('click', async () => {
      const btn = el.querySelector('.appr-btn'); btn.disabled = true;
      el.querySelector('.appr-msg').innerHTML = '';
      try {
        const r = await api(`/api/jobs/${j.id}/candidates/${encodeURIComponent(x.candidate.id)}/approval`, { method: 'PUT', body: { approved: !approved(items.get(x.candidate.id)) } });
        const cur = { ...items.get(x.candidate.id), approval: r.approval ?? null };
        items.set(x.candidate.id, cur); show(cur); renderProgress();
        if (approved(cur)) el.querySelector('.exp-pick').checked = true;
      } catch (e) { el.querySelector('.appr-msg').innerHTML = errorBar(e) + (e.detail ? qaList(e.detail) : ''); show(items.get(x.candidate.id)); }
      updateButton();
    });
    el.querySelector('.exp-pick').addEventListener('change', updateButton);
    el.querySelector('.exp-text').addEventListener('click', async (ev) => {
      const cancel = ev.target.closest?.('.exp-cancel'); if (!cancel) return;
      cancel.disabled = true;
      try { await api(`/api/renders/${encodeURIComponent(cancel.dataset.render)}/cancel`, { method: 'POST' }); }
      catch (err) { el.querySelector('.appr-msg').innerHTML = errorBar(err); }
      await refresh().catch(() => { });
    });
  }
  $('#pickAll').addEventListener('click', () => {
    for (const x of items.values()) { const p = block(x.candidate.id)?.querySelector('.exp-pick'); if (p && !p.disabled) p.checked = true; }
    updateButton();
  });
  $('#batchExport').addEventListener('click', async () => {
    const ids = picked().map(x => x.candidate.id);
    if (!ids.length) return;
    const b = $('#batchExport'); b.disabled = true; $('#batchMsg').innerHTML = '';
    try {
      const r = await api(`/api/jobs/${j.id}/exports`, { method: 'POST', body: { candidate_ids: ids } });
      batch = r.exports.map(e => e.render_id);
      sessionStorage.setItem(key, JSON.stringify(batch));
      for (const x of items.values()) { const p = block(x.candidate.id)?.querySelector('.exp-pick'); if (p) p.checked = false; }
      $('#batchMsg').innerHTML = info(trUi('m502', [batch.length]), 'success');
      await refresh();
    } catch (e) {
      // each item: video name and reason; for a hard QA failure also the failed checks (as in single export)
      const reasons = Array.isArray(e.detail) ? `<ul class="notes">${e.detail.map(d => `<li>${esc(d.name)}: ${esc(errorMessage({ code: d.code, message: d.reason }))}${d.code === 'E-QA-HARDFAIL' && Array.isArray(d.detail) ? qaList(d.detail) : ''}</li>`).join('')}</ul>` : '';
      $('#batchMsg').innerHTML = errorBar(e) + reasons;
      await refresh().catch(() => { });
    }
  });
  updateButton(); showBatch();
  if ([...items.values()].some(active) || batch.length) refresh().catch(() => { });
  { const prev = cleanup; cleanup = () => { clearInterval(timer); prev?.(); }; }
}

// ---------------- review & edit ----------------
async function review(epId) {
  const gone = routeGuard();
  setSteps(4);
  let { plan, qa, plan_hash: planHash, approval, final_render_stale: finalStale } = await api(`/api/editplans/${epId}`);
  if (gone()) return;
  await saveWorkflowState(plan.project_id, { step: 'review', edit_plan_id: epId });
  const job = await api(`/api/jobs/${plan.provenance.job_id}`);
  const cands = await api(`/api/jobs/${job.id}/candidates`);
  if (gone()) return;
  setSteps(4, 4, jobStepLinks(job, cands));
  const me = cands.find(x => x.candidate.edit_plan_id === epId);
  const meIdx = cands.indexOf(me);
  const nextTodo = cands.length < 2 ? null : nextToReview(cands, epId);
  const bgmList = (await api(`/api/bgm?project_id=${encodeURIComponent(plan.project_id)}`)).map(b => ({ ...b, name: libraryTrackName(b) }));
  const projAssets = (await api(`/api/projects/${plan.project_id}`)).assets;
  const assetName = (aid) => (projAssets.find(a => a.id === aid)?.path ?? aid).split(/[\\/]/).pop();
  let tr = await api(`/api/assets/${plan.audio.master}/transcript`);
  const capStyle = (await api('/api/presets')).find(x => x.id === plan.preset.id)?.telop?.caption ?? {};
  if (gone()) return;
  const pendingWrites = new Set();
  const writeReview = action => {
    const pending = action();
    pendingWrites.add(pending);
    pending.finally(() => pendingWrites.delete(pending));
    return pending;
  };
  flushLocaleWrites = async () => (await Promise.all([...pendingWrites])).every(result => result !== false);
  const roleName = { teaser: trUi('m503'), reaction: trUi('m504'), wide: trUi('m505'), recap: trUi('m506'), next: trUi('m507') };
  let previewUrl = me?.preview_url, stale = me?.preview_stale ?? false;
  const typeLabel = { caption: trUi('m508'), emphasis: trUi('m509'), hook: trUi('m510'), headline: trUi('m511'), tsukkomi: trUi('m512'), sfx: trUi('m513'), counter: trUi('m514') };
  const width = (s) => [...s].reduce((a, c) => a + (c.charCodeAt(0) < 0x2e80 || (c.charCodeAt(0) >= 0xff61 && c.charCodeAt(0) <= 0xffdc) ? 0.5 : 1), 0);
  // body time → time in the rendered video (mirrors Planning.InsertMap: inserts push later content back)
  const kindOrder = { opening: 0, card: 1, closing: 2 };
  const active = () => (plan.inserts ?? []).filter(i => i.enabled && i.duration > 0).sort((a, b) => a.at - b.at || kindOrder[a.kind] - kindOrder[b.kind]);
  const finalT = (t, end = false) => t + active().filter(i => i.at < t - 1e-3 || (!end && i.at <= t + 1e-3)).reduce((a, i) => a + i.duration, 0);
  const insertStart = (ins) => { let off = 0; for (const i of active()) { if (i.id === ins.id) break; off += i.duration; } return ins.at + off; };
  const insertName = { opening: trUi('m255'), closing: trUi('m256'), card: trUi('m384') };
  const llmDest = (await api('/api/settings').catch(() => null))?.transmission?.llm_destination ?? null;
  if (gone()) return;
  let lastRepair = null, repairing = false, approvingQa = false;
  Object.assign(typeLabel, insertName);
  // one list for telops and inserts; the type switches turn a whole type on/off (tsukkomi = overlay telops + pause cards)
  const isTsukkomiCard = (i) => i.kind === 'card' && i.id.startsWith('tsukkomi');
  const groupOf = (i) => isTsukkomiCard(i) ? 'tsukkomi' : i.kind;
  const filterTypes = ['caption', 'emphasis', 'headline', 'counter', 'sfx', 'tsukkomi', 'opening', 'card', 'closing'];
  const itemsOf = (type) => [...plan.telops.filter(t => t.type === type), ...(plan.inserts ?? []).filter(i => groupOf(i) === type)];
  const insertRow = (i) => {
    const start = insertStart(i), end = start + Number(i.duration), name = isTsukkomiCard(i) ? trUi('m512') : insertName[i.kind] ?? i.kind;
    const file = (i.path ?? '').split(/[\\/]/).pop();
    const notes = i.kind === 'card'
      ? [isTsukkomiCard(i) ? trUi('m515') : '', i.path ? trUi('m516', [file]) : '', trUi('m517', [Number(i.duration).toFixed(1)]), i.text_source === 'ai' ? trUi('m518') : ''].filter(Boolean)
      : [file, trUi('m519', [Number(i.duration).toFixed(1)])];
    return `<tr class="insert ${i.enabled ? '' : 'off'}" data-insert="${esc(i.id)}"><td><span class="tag ${esc(groupOf(i))}">${esc(name)}</span></td>
      <td>${i.enabled ? `<button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${trUi('m520', [start.toFixed(1), end.toFixed(1), esc(name)])}">${start.toFixed(1)}–${end.toFixed(1)}</button>` : `<span class="caption">${trUi('m521')}</span>`}</td>
      <td>${i.kind === 'card' ? `<input class="card-text" data-id="${esc(i.id)}" value="${esc(i.text)}" maxlength="20" aria-label="${trUi('m523', [esc(name), esc(i.id)])}" placeholder="${i.path ? trUi('m522') : ''}">` : ''}
        <p class="caption insert-notes">${esc(notes.join(' · '))}</p></td>
      <td>${i.kind === 'card' ? [...(i.text ?? '')].length : ''}</td><td></td>
      <td><fluent-switch class="ins-en" data-id="${esc(i.id)}" ${i.enabled ? 'checked' : ''} aria-label="${trUi('m525', [esc(name), esc(i.id)])}"></fluent-switch>${i.approved || !i.text ? '' : `<fluent-button size="small" data-approve-card="${esc(i.id)}">${trUi('m524')}</fluent-button>`}</td></tr>`;
  };

  // ---- BGM: scene-by-scene music and the level automation (Planning.BgmPlanner) ----
  const trackOf = (id) => bgmList.find(b => b.id === id);
  const trackLabel = (id) => trackOf(id)?.name ?? id;
  const cueColors = ['#2f5fb3', '#8a5300', '#2b7a50', '#8a3f8a', '#a8322a', '#1f6f7a'];
  const cueSpans = () => {
    const cues = plan.audio.bgm_cues ?? [], total = totalDur();
    const starts = cues.map((c, i) => i === 0 ? 0 : finalT(c.t0));
    return cues.map((c, i) => ({ c, start: starts[i], end: i + 1 < cues.length ? starts[i + 1] : total }));
  };
  const lvlOpts = (values, cur, label) => [...new Set([...values, cur])].sort((a, b) => b - a).map(v => `<option value="${v}" ${Math.abs(v - cur) < 0.05 ? 'selected' : ''}>${label(v)}</option>`).join('');
  const bgmPanel = () => {
    const a = plan.audio, cues = a.bgm_cues ?? [];
    const sfxSw = field(trUi('m526'), `<fluent-switch slot="input" id="sfxSw" ${a.sfx_enabled ? 'checked' : ''}></fluent-switch>`);
    if (!cues.length) return `<h3>${trUi('m178')}</h3>
          <div class="row"><label class="caption">BGM <select class="native" id="bgmSel">
            <option value="none" ${!a.bgm ? 'selected' : ''}>${trUi('m041')}</option>${bgmList.map(b => `<option value="${esc(b.id)}" ${a.bgm === b.id ? 'selected' : ''}>${esc(b.name)}</option>`).join('')}</select></label>
            ${sfxSw}</div>
          <p class="caption">${trUi('m528', [a.sfx?.length ?? 0, a.bgm ? ` · BGM ${a.duck ? trUi('m527') : trUi('m330')}` : ''])}</p>`;
    const beat = a.bgm_mode === 'beat', on = !!a.bgm, spans = cueSpans(), total = Math.max(0.1, totalDur());
    const tracks = [...new Set(cues.map(c => c.track).filter(Boolean))];
    const color = (id) => cueColors[Math.max(0, tracks.indexOf(id)) % cueColors.length];
    const single = cues.every(c => c.track === cues[0].track) && a.bgm_mode !== 'scene';
    const usable = bgmList.filter(b => b.analysis || b.source === 'library');
    const s15 = qa.map(localizeQa).find(q => q.check_id === 'S-15');
    const depths = (a.duck_spans ?? []).map(d => d.db);
    const ducks = (a.duck_spans ?? []).map(d => { const s = finalT(d.t0), e = finalT(d.t1, true); return `<span class="bgm-duck" style="left:${(s / total * 100).toFixed(2)}%;width:${Math.max(0.2, (e - s) / total * 100).toFixed(2)}%;height:${Math.min(100, 20 + d.db * 4)}%" title="${trUi('m529', [s.toFixed(1), e.toFixed(1), d.db.toFixed(1)])}"></span>`; }).join('');
    return `<h3 id="h-bgm">${trUi('m558')}</h3>
      <div class="row"><label class="caption">BGM <select class="native" id="bgmSel" ${beat ? 'disabled' : ''}>
        <option value="none" ${!on ? 'selected' : ''}>${trUi('m041')}</option>
        <option value="scene" ${on && !single ? 'selected' : ''}>${trUi('m559')}</option>
        ${usable.map(b => `<option value="${esc(b.id)}" ${on && single && cues[0].track === b.id ? 'selected' : ''}>${trUi('m530', [esc(b.name)])}</option>`).join('')}</select></label>
        ${sfxSw}</div>
      ${beat ? `<p class="caption">${trUi('m531')}</p>` : ''}
      ${on ? `<div class="bgm-strip" role="img" aria-label="${trUi('m553', [spans.map(x => trUi('m533', [x.start.toFixed(0), x.c.track ? esc(trackLabel(x.c.track)) : trUi('m532')])).join('、')])}">
          ${spans.map(x => `<span class="bgm-seg" style="left:${(x.start / total * 100).toFixed(2)}%;width:${((x.end - x.start) / total * 100).toFixed(2)}%;background:${x.c.track ? color(x.c.track) : 'transparent'}">${x.c.track ? esc(trackLabel(x.c.track)) : trUi('m041')}</span>`).join('')}
          <span class="bgm-ducks">${ducks}</span><span class="bgm-playhead"></span></div>
        <div class="row">
          <label class="caption">${trUi('m554')} <select class="native" id="bgmLd">${lvlOpts([10, 12, 14, 16, 18, 20, 24], a.bgm_speech_ld, v => trUi('m536', [v, v === 14 ? trUi('m534') : v === 10 ? trUi('m535') : '']))}</select></label>
          <label class="caption">${trUi('m555')} <select class="native" id="bgmGap">${lvlOpts([0, -4, -6, -8, -10, -12, -16, -20], a.bgm_gap_lu, v => v === 0 ? trUi('m537') : trUi('m538', [-v]))}</select></label>
        </div>
        <p class="caption">${trUi('m556', [depths.length, depths.length ? ` ${Math.min(...depths).toFixed(0)}〜${Math.max(...depths).toFixed(0)} dB` : ''])}</p>
        <p class="caption ${s15 ? (s15.status === 'pass' ? 'pass' : 'warnc') : ''}" id="bgmMeasured">${s15 ? trUi('m540', [s15.stage === 'final' ? trUi('m009') : trUi('m539'), esc(s15.measured)]) : trUi('m541')}</p>
        <div class="bgm-cues stack" role="list" aria-label="${trUi('m557')}">
          ${spans.map(({ c, start, end }) => `<div class="bgm-cue" role="listitem" data-cue="${esc(c.id)}">
            <div class="row"><span class="bgm-swatch" style="background:${c.track ? color(c.track) : 'transparent'}"></span>
              <button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${trUi('m545', [start.toFixed(1)])}">${tc(start)}–${tc(end)}</button>
              <b>${trUi('m546', [energyWord(c.scene_energy)])}</b><span class="caption">${c.auto ? trUi('m542') : trUi('m543')}</span></div>
            <p class="caption">${esc(getLanguage() === 'ja' ? c.scene : trUi('bgm.scene', [c.scene_energy?.toFixed(2) ?? '—']))}</p>
            <div class="row">
              <label class="caption">${trUi('m547')} <select class="native bgm-cue-track" data-id="${esc(c.id)}" aria-label="${trUi('m548', [tc(start)])}" ${beat ? 'disabled' : ''}>
                ${c.auto ? '' : `<option value="auto">${trUi('m544')}</option>`}
                ${usable.map(b => `<option value="${esc(b.id)}" ${c.track === b.id ? 'selected' : ''}>${esc(b.name)}${b.analysis ? `（${energyWord(b.analysis.energy)}）` : ''}</option>`).join('')}
                <option value="none" ${!c.track ? 'selected' : ''}>${trUi('m549')}</option></select></label>
              <label class="caption">${trUi('m550')} <select class="native bgm-trim" data-id="${esc(c.id)}" aria-label="${trUi('m551', [tc(start)])}">${lvlOpts([6, 3, 0, -3, -6, -9, -12], c.trim_db, v => v === 0 ? '±0 dB' : `${v > 0 ? '+' : ''}${v} dB`)}</select></label>
            </div>
            <p class="caption">${trUi('m552', [esc(getLanguage() === 'ja' ? c.reason : c.auto ? trUi('bgm.reasonAuto', [c.scene_energy?.toFixed(2) ?? '—', c.track_energy?.toFixed(2) ?? '—']) : trUi('bgm.reasonManual'))])}</p></div>`).join('')}
        </div>` : ''}
      <p class="caption">${trUi('m560', [a.sfx?.length ?? 0])}</p>`;
  };

  // ---- 自動チェック panel (right column): findings on the rendered-file timeline, recommended fix, Copilot auto-repair ----
  const totalDur = () => plan.timeline.reduce((a, c) => a + (c.src_out - c.src_in) / (c.speed || 1), 0) + active().reduce((a, i) => a + i.duration, 0);
  const tc = (x) => `${Math.floor(x / 60)}:${(x % 60).toFixed(1).padStart(4, '0')}`;
  const statusName = { pass: trUi('m027'), fail: trUi('m028'), warn: trUi('m030'), not_run: trUi('m029') };
  const repairName = { rule: trUi('m561'), ai: trUi('m562'), manual: trUi('m563'), none: trUi('m564') };
  // findings 「Copilotで自動修復」 will fix (advice.fixable from the server; every item when not assessed)
  const fixableOf = (q) => !q.advice || q.advice.repair === 'manual' || q.advice.repair === 'none' ? 0 : q.advice.fixable ?? (q.items ?? []).length;
  const targetText = (it) => {
    if (it.kind === 'telop') { const t = plan.telops.find(x => x.id === it.target); return t ? `${typeLabel[t.type] ?? t.type}「${t.text}」` : it.target; }
    if (it.kind === 'insert') { const i = (plan.inserts ?? []).find(x => x.id === it.target); return i ? `${isTsukkomiCard(i) ? trUi('m512') : insertName[i.kind] ?? i.kind}${i.text ? `「${i.text}」` : ''}` : it.target; }
    if (it.kind === 'clip') return trUi('m565', [it.target]);
    if (it.kind === 'asset') return assetName(it.target);
    return it.target;
  };
  const seekBtn = (it, check) => Number.isFinite(it.start)
    ? `<button class="qa-seek" type="button" data-time="${it.start.toFixed(3)}" data-target="${esc(it.target)}" data-kind="${esc(it.kind)}" aria-label="${trUi('m566', [esc(check), tc(it.start)])}">${tc(it.start)}</button>`
    : '<span class="qa-seek none" aria-hidden="true">—</span>';
  const SPARKLE = '<svg slot="start" class="ico" viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" fill="currentColor"><path d="M9 2.2l1.7 4.4c.2.6.7 1.1 1.3 1.3L16.4 9.6l-4.4 1.7c-.6.2-1.1.7-1.3 1.3L9 17l-1.7-4.4c-.2-.6-.7-1.1-1.3-1.3L1.6 9.6 6 7.9c.6-.2 1.1-.7 1.3-1.3z"/><path d="M15.5 1.5l.6 1.6c.1.2.2.3.4.4l1.6.6-1.6.6c-.2.1-.3.2-.4.4l-.6 1.6-.6-1.6c-.1-.2-.2-.3-.4-.4l-1.6-.6 1.6-.6c.2-.1.3-.2.4-.4z"/></svg>';
  const BULB = '<svg class="ico" viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" fill="currentColor"><path d="M8 1a5 5 0 0 0-3 9v1.5c0 .3.2.5.5.5h5c.3 0 .5-.2.5-.5V10a5 5 0 0 0-3-9zm2 8.4V11H6V9.4l-.2-.2A4 4 0 1 1 10.2 9.2zM6 13h4v1a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1z"/></svg>';
  const resultBlock = (r) => {
    // the reasons, most frequent first, so a click that fixes little still says why
    const reasons = [...r.skipped.reduce((m, s) => { const reason = serverText(s.reason); return m.set(reason, (m.get(reason) ?? 0) + 1); }, new Map())].sort((a, b) => b[1] - a[1]);
    return `<div class="qa-result" role="status" tabindex="-1">
      <div class="row" style="justify-content:space-between"><b>${r.scope ? `${esc(r.scope)}: ` : ''}${r.fixed_count ? trUi('m567', [r.fixed_count]) : trUi('m568')}${r.skipped.length ? trUi('m569', [r.skipped.length]) : ''}</b>
        ${r.repair_id ? `<fluent-button size="small" id="qaUndo">${trUi('m570')}</fluent-button>` : ''}</div>
      ${r.changes.length ? `<ul class="qa-changes">${r.changes.map(c => `<li>${seekBtn(c, c.check_id)}<span><b>${esc(c.check_id)}</b> <span class="repair-tag ${esc(c.method)}">${c.method === 'ai' ? 'AI' : trUi('m571')}</span>${c.needs_approval ? ` <span class="repair-tag manual">${trUi('m572')}</span>` : ''}
        <span class="qa-diff"><del>${esc(c.method === 'rule' && ['S-01', 'H-11'].includes(c.check_id) ? serverText(c.before) : c.before ?? '')}</del> → <ins>${esc(c.method === 'rule' && ['S-01', 'H-11'].includes(c.check_id) ? serverText(c.after) : c.after ?? '')}</ins></span></span></li>`).join('')}</ul>` : ''}
      ${r.changes.some(c => c.needs_approval) ? `<p class="caption">${trUi('m573')}</p>` : ''}
      ${reasons.length ? `<p class="caption"><b>${trUi('m575')}</b></p><ul class="qa-reasons">${reasons.slice(0, 4).map(([why, n]) => `<li><b>${trUi('m574', [n])}</b> ${esc(why)}</li>`).join('')}</ul>
        <details><summary class="caption">${trUi('m576', [r.skipped.length])}</summary><ul class="qa-changes">${r.skipped.map(s => `<li>${seekBtn(s, s.check_id)}<span><b>${esc(s.check_id)}</b> ${esc(s.target)} <span class="caption">${esc(serverText(s.reason))}</span></span></li>`).join('')}</ul></details>` : ''}
      <p class="caption">${trUi('m577')}</p></div>`;
  };
  const qaPanel = () => {
    const displayQa = qa.map(localizeQa);
    const reviewed = qa.filter(q => q.status !== 'not_run' && q.review_hash);
    const pending = reviewed.filter(q => !q.review_approval);
    const approvalControl = (q) => q.status === 'not_run' || !q.review_hash ? '' : `<div class="row qa-approval">
      <span class="caption">${q.review_approval ? trUi('m578', [esc(formatDate(q.review_approval.at))]) : trUi('m473')}</span>
      <fluent-button size="small" data-qa-approve="${esc(q.check_id)}" aria-label="${esc(q.check_id)} ${q.review_approval ? trUi('m484') : trUi('m524')}" ${approvingQa || repairing ? 'disabled' : ''}>${q.review_approval ? trUi('m484') : trUi('m524')}</fluent-button></div>`;
    const issues = displayQa.filter(q => q.status === 'fail' || q.status === 'warn').sort((a, b) => (a.status === 'fail' ? 0 : 1) - (b.status === 'fail' ? 0 : 1));
    const passed = displayQa.filter(q => q.status === 'pass' || q.status === 'not_run');
    const fails = issues.filter(q => q.status === 'fail').length, warns = issues.length - fails;
    const repairable = issues.filter(q => fixableOf(q) > 0), fixTotal = repairable.reduce((s, q) => s + fixableOf(q), 0);
    const total = Math.max(0.1, totalDur());
    const markers = issues.flatMap(q => (q.items ?? []).filter(it => Number.isFinite(it.start)).map(it => {
      const left = Math.min(100, it.start / total * 100), w = Math.max(0.8, Math.min(100 - left, ((it.end ?? it.start) - it.start) / total * 100));
      return `<button type="button" class="qa-mark ${q.status}" style="left:${left.toFixed(2)}%;width:${w.toFixed(2)}%" data-time="${it.start.toFixed(3)}" data-target="${esc(it.target)}" data-kind="${esc(it.kind)}" aria-label="${esc(q.check_id)} ${tc(it.start)}: ${esc(it.detail)}" title="${esc(q.check_id)} ${tc(it.start)} ${esc(it.detail)}"></button>`;
    })).join('');
    const issueCard = (q) => {
      const all = q.items ?? [], n = fixableOf(q);
      // when the server assessed each item, fixable ones come first and the rest are grouped with their reason
      const assessed = q.advice?.fixable != null;
      const items = assessed ? all.filter(it => it.repair) : all, stays = assessed ? all.filter(it => !it.repair) : [];
      const row = (it) => `<li class="qa-item" data-start="${it.start ?? ''}" data-end="${it.end ?? it.start ?? ''}">${seekBtn(it, q.check_id)}<span><span class="qa-target">${esc(targetText(it))}</span> <span class="caption">${esc(it.detail)}</span>${it.note ? `<span class="caption qa-note">${esc(it.note)}</span>` : ''}</span></li>`;
      return `<li class="qa-issue ${q.status}" data-check="${esc(q.check_id)}">
        <div class="qa-issue-head"><span class="qa-status ${q.status}">${statusName[q.status]}</span><b>${esc(q.check_id)}</b><span>${esc(q.message)}</span></div>
        <p class="caption qa-measure">${trUi('m586', [esc(q.measured), esc(q.threshold), q.stage ? trUi('m031', [esc(q.stage)]) : ''])}</p>
        ${approvalControl(q)}
        ${q.advice ? `<div class="qa-advice">${BULB}<div><b>${trUi('m583')}</b> <span class="repair-tag ${esc(q.advice.repair)}">${repairName[q.advice.repair] ?? ''}</span><p>${esc(q.advice.recommendation)}</p>
          ${n > 0 ? `<fluent-button size="small" appearance="subtle" data-repair-one="${esc(q.check_id)}" ${repairing ? 'disabled' : ''}>${SPARKLE}${repairing === q.check_id ? trUi('m579') : assessed && all.length > 1 ? trUi('m580', [n]) : trUi('m581')}</fluent-button>
            ${repairing === q.check_id ? `<fluent-progress-bar aria-label="${trUi('m582')}"></fluent-progress-bar>` : ''}` : ''}
          ${q.advice.remaining ? `<p class="caption">${esc(q.advice.remaining)}</p>` : ''}</div></div>` : ''}
        ${lastRepair && lastRepair.scope === q.check_id ? resultBlock(lastRepair) : ''}
        ${items.length ? `<ul class="qa-items">${items.slice(0, 12).map(row).join('')}</ul>${items.length > 12 ? `<details><summary class="caption">${trUi('m097', [items.length - 12])}</summary><ul class="qa-items">${items.slice(12).map(row).join('')}</ul></details>` : ''}` : ''}
        ${stays.length ? `<details class="qa-stays"><summary class="caption">${trUi('m585', [stays.length, /^S-0[128]$/.test(q.check_id) ? trUi('m584') : ''])}</summary><ul class="qa-items">${stays.map(row).join('')}</ul></details>` : ''}
      </li>`;
    };
    const r = lastRepair;
    const result = !r || issues.some(q => q.check_id === r.scope) ? '' : resultBlock(r);
    return `<section class="card qa-panel" aria-labelledby="qa-title">
      <div class="row" style="justify-content:space-between"><h2 id="qa-title">${trUi('m596')}</h2>
        <div class="row"><span class="qa-count fail">${trUi('m033', [fails])}</span><span class="qa-count warnc">${trUi('m036', [warns])}</span><span class="qa-count pass">${trUi('m597', [passed.filter(q => q.status === 'pass').length])}</span></div></div>
      <div class="qa-review">
        <div class="row"><fluent-button id="qaApproveAll" ${pending.length && !approvingQa && !repairing ? '' : 'disabled'}>${trUi('m598', [pending.length ? trUi('m587', [pending.length]) : ''])}</fluent-button>
          <span class="caption" id="qaApprovalCount">${trUi('m599', [reviewed.length - pending.length, reviewed.length])}</span></div>
        <p class="caption">${trUi('m600')}</p>
        <div id="qaApprovalMsg" role="status"></div>
      </div>
      <div class="qa-timeline" role="group" aria-label="${trUi('m601')}">${markers}<div class="qa-playhead" aria-hidden="true"></div></div>
      <div class="row caption" style="justify-content:space-between"><span>0:00.0</span><span id="qaNow" aria-live="off">0:00.0</span><span>${tc(total)}</span></div>
      ${issues.length ? `<div class="qa-repair">
        <fluent-button appearance="primary" id="qaRepair" ${repairable.length && !repairing ? '' : 'disabled'} aria-describedby="qaRepairNote">${SPARKLE}${repairing === 'all' ? trUi('m579') : trUi('m588')}</fluent-button>
        ${repairing === 'all' ? `<fluent-progress-bar aria-label="${trUi('m589')}"></fluent-progress-bar>` : ''}
        <p class="caption" id="qaRepairNote">${repairable.length ? trUi('m590', [repairable.length, fixTotal]) : trUi('m591')}${llmDest ? trUi('m592', [esc(llmDest)]) : trUi('m593')}</p></div>` : `<p class="pass">${trUi('m594')}</p>`}
      <div id="qaRepairMsg">${result}</div>
      ${issues.length ? `<ul class="qa-issues">${issues.map(issueCard).join('')}</ul>` : ''}
      ${passed.length ? `<details class="qa-passed"><summary class="caption">${trUi('m595', [passed.length])}</summary>${passed.map(q => `<div data-check="${esc(q.check_id)}">${qaList([q])}${approvalControl(q)}</div>`).join('')}</details>` : ''}
    </section>`;
  };
  const focusTarget = (target, kind) => {
    $$('tr.qa-hit').forEach(tr => tr.classList.remove('qa-hit'));
    const row = kind === 'telop' ? $(`tr[data-telop="${CSS.escape(target)}"]`) : kind === 'insert' ? $(`tr[data-insert="${CSS.escape(target)}"]`) : null;
    if (!row) return;
    row.classList.add('qa-hit');
    row.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  };
  const runRepair = async (checkIds) => {
    if (repairing || approvingQa) return;
    repairing = checkIds ? checkIds[0] : 'all'; draw();
    try {
      const r = await api(`/api/editplans/${epId}/qa/repair`, { method: 'POST', body: { check_ids: checkIds } });
      const v = r.result; plan = v.plan; qa = v.qa; planHash = v.plan_hash; approval = v.approval; finalStale = v.final_render_stale; stale = stale || v.preview_stale;
      lastRepair = { ...r.repair, scope: checkIds ? checkIds[0] : null };
    } catch (e) { lastRepair = null; repairing = false; draw(); $('#qaRepairMsg').innerHTML = errorBar(e); return; }
    repairing = false; draw();
    const res = $('.qa-panel .qa-result');
    if (res) { res.scrollIntoView({ block: 'nearest' }); res.focus({ preventScroll: true }); }
  };
  const wireQaPanel = () => {
    const approveChecks = async (checks, approved) => {
      if (approvingQa || repairing || !checks.length) return;
      approvingQa = true;
      $$('[data-qa-approve], #qaApproveAll, #qaRepair, [data-repair-one], #qaUndo').forEach(b => b.setAttribute('disabled', ''));
      try {
        const v = await api(`/api/editplans/${epId}/qa/approval`, { method: 'PUT', body: {
          plan_hash: planHash, checks: checks.map(q => ({ check_id: q.check_id, result_hash: q.review_hash })), approved,
        } });
        plan = v.plan; qa = v.qa; planHash = v.plan_hash; approval = v.approval; finalStale = v.final_render_stale;
        approvingQa = false; draw();
        $('#qaApprovalMsg').textContent = trUi('m604', [checks.length, approved ? trUi('m602') : trUi('m603')]);
      } catch (e) { approvingQa = false; draw(); $('#qaApprovalMsg').innerHTML = errorBar(e); }
    };
    $('#qaApproveAll')?.addEventListener('click', () => approveChecks(qa.filter(q => q.status !== 'not_run' && q.review_hash && !q.review_approval), true));
    $$('[data-qa-approve]').forEach(b => b.addEventListener('click', () => {
      const q = qa.find(q => q.check_id === b.dataset.qaApprove);
      approveChecks([q], !q.review_approval);
    }));
    const video = $('#pv video');
    const seek = (el) => {
      const time = Number(el.dataset.time);
      if (video && Number.isFinite(time)) video.currentTime = Number.isFinite(video.duration) ? Math.min(time + 0.05, video.duration) : time + 0.05;
      focusTarget(el.dataset.target, el.dataset.kind);
    };
    $$('.qa-panel .qa-seek[data-time], .qa-panel .qa-mark').forEach(b => b.addEventListener('click', () => seek(b)));
    $('#qaRepair')?.addEventListener('click', () => runRepair(null));
    $$('[data-repair-one]').forEach(b => b.addEventListener('click', () => runRepair([b.dataset.repairOne])));
    $('#qaUndo')?.addEventListener('click', async () => {
      try {
        const v = await api(`/api/editplans/${epId}/qa/repair/${encodeURIComponent(lastRepair.repair_id)}/undo`, { method: 'POST' });
        plan = v.plan; qa = v.qa; planHash = v.plan_hash; approval = v.approval; finalStale = v.final_render_stale; stale = stale || v.preview_stale; lastRepair = null; draw();
      } catch (e) { $('#qaRepairMsg').innerHTML = errorBar(e); }
    });
    if (!video) return;
    const total = Math.max(0.1, totalDur());
    const spans = new Map(plan.telops.filter(x => x.enabled).map(x => [x.id, [finalT(x.start), finalT(x.end, true)]]));
    const rows = $$('tr[data-telop]'), items = $$('.qa-panel .qa-item');
    const sync = () => {
      const t = video.currentTime;
      const head = $('.qa-playhead'); if (head) head.style.left = `${Math.min(100, t / total * 100).toFixed(2)}%`;
      const now = $('#qaNow'); if (now) now.textContent = tc(t);
      // findings on screen now, and the telop row they point at
      items.forEach(li => { const s = Number(li.dataset.start), e = Number(li.dataset.end); li.classList.toggle('now', li.dataset.start !== '' && t >= s - 0.05 && t <= Math.max(e, s + 0.5)); });
      rows.forEach(tr => { const s = spans.get(tr.dataset.telop); tr.classList.toggle('playing', !!s && t >= s[0] && t < s[1]); });
    };
    video.addEventListener('timeupdate', sync);
    video.addEventListener('seeked', sync);
    sync();
  };

  const draw = () => {
    const unapprovedText = plan.telops.some(t => t.enabled && !t.approved) || active().some(i => i.kind === 'card' && i.text && !i.approved);
    const hardFail = qa.some(q => q.check_id.startsWith('H-') && q.status === 'fail'
      && !(q.check_id === 'H-08' && approval?.stale && !unapprovedText));
    const maxW = capStyle.max_chars_per_line ?? 14, maxCps = capStyle.max_cps ?? 6, maxLines = capStyle.max_lines ?? 1;
    const dis = hardFail ? 'disabled' : '';
    const multi = !!me && cands.length > 1;
    const navItem = (i) => i === meIdx ? { ...cands[i], approval } : cands[i];
    main.innerHTML = `
      <div class="row" style="justify-content:space-between"><h1>${trUi('m008')}</h1>
        ${multi ? `<nav class="row video-nav" aria-label="${trUi('m607')}">
          ${meIdx > 0 ? `<fluent-anchor-button size="small" appearance="subtle" id="prevVideo" href="#/ep/${esc(cands[meIdx - 1].candidate.edit_plan_id)}">${trUi('m605')}</fluent-anchor-button>` : ''}
          <label class="caption">${trUi('m608', [meIdx + 1, cands.length])} <select class="native" id="videoSel" aria-label="${trUi('m609')}">${cands.map((x, i) => `<option value="${esc(x.candidate.edit_plan_id)}" ${i === meIdx ? 'selected' : ''}>${i + 1}. ${esc(videoName(x))} — ${videoStatus(navItem(i)).label}</option>`).join('')}</select></label>
          ${meIdx < cands.length - 1 ? `<fluent-anchor-button size="small" appearance="subtle" id="nextVideo" href="#/ep/${esc(cands[meIdx + 1].candidate.edit_plan_id)}">${trUi('m606')}</fluent-anchor-button>` : ''}
        </nav>` : ''}
        <div class="row">${safeToggle()}<fluent-anchor-button href="#/job/${esc(job.id)}" appearance="subtle">${trUi('m647')}</fluent-anchor-button></div></div>
      <div class="grid cols-review with-qa" style="align-items:start">
        <section class="card stack">
          ${previewUrl ? phone(previewUrl + (stale ? '' : `?v=${Date.now()}`), 'pv') : ''}
          ${stale ? info(trUi('m610'), 'warning') : ''}
          <fluent-button id="repreview" ${stale ? 'appearance="primary"' : ''}>${trUi('m648')}</fluent-button>
          <p class="caption">${trUi('m649', [plan.series ? `${esc(plan.series.label)}（${plan.series.part}/${plan.series.parts}）· ` : '', (() => { const d = plan.timeline.reduce((a, c) => a + (c.src_out - c.src_in) / (c.speed || 1), 0); const all = d + active().reduce((a, i) => a + i.duration, 0); const f = (x) => x >= 180 ? fmt(x) : trUi('m519', [x.toFixed(1)]); return all - d > 0.01 ? trUi('m611', [f(d), f(all)]) : f(d); })(), plan.timeline.length, plan.telops.filter(t => t.enabled).length])}</p>
          ${plan.series?.chapters?.length ? `<details><summary class="caption">${trUi('m612', [plan.series.chapters.length])}</summary><ul class="notes">${plan.series.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          <details><summary class="caption">${trUi('m650')}</summary>
            <ul class="notes">${plan.timeline.map(c => `<li>${trUi('m618', [esc(c.clip_id), esc(assetName(c.asset_id)), c.role !== 'body' ? `（${esc(roleName[c.role] ?? c.role)}）` : '', ((c.src_out - c.src_in) / (c.speed || 1)).toFixed(1), c.speed && c.speed !== 1 ? trUi('m613', [c.speed]) : '', c.reframe?.mode === 'face' ? trUi('m614') : '', c.reframe?.mode === 'screen' ? (c.reframe.crop_w ? trUi('m615') : trUi('m616')) : '', c.pip ? trUi('m617', [esc(assetName(c.pip.asset_id))]) : ''])}</li>`).join('')}</ul></details>
          ${bgmPanel()}
          <details><summary class="caption">${trUi('m651')}</summary>
            <p class="caption">${trUi('m652')}</p>
            <div class="words">${tr.words.filter(w => plan.timeline.some(c => (w.t0 + w.t1) / 2 >= (c.audio_in ?? c.src_in) && (w.t0 + w.t1) / 2 <= (c.audio_in ?? c.src_in) + (c.src_out - c.src_in))).map(w => `<input class="native wd" data-id="${esc(w.id)}" value="${esc(w.text)}" size="${Math.max(2, [...w.text].length + 1)}" maxlength="30" aria-label="${trUi('m619', [esc(w.text)])}">`).join('')}</div></details>
          ${plan.series ? `<p class="caption">${trUi('m620')}</p>` : `<h3>${trUi('m621')}</h3>
          <div class="row"><fluent-button size="small" data-adj="start:1">${trUi('m622')}</fluent-button><fluent-button size="small" data-adj="start:-1">${trUi('m623')}</fluent-button></div>
          <div class="row"><fluent-button size="small" data-adj="end:-1">${trUi('m624')}</fluent-button><fluent-button size="small" data-adj="end:1">${trUi('m625')}</fluent-button></div>`}
          <div id="review-approval">${approvalBadge(approval)}</div>
          ${finalStale ? info(trUi('m626'), 'warning') : ''}
          ${multi ? `<fluent-button appearance="primary" id="approveNext" ${dis}>${nextTodo ? trUi('m627', [esc(videoName(nextTodo))]) : trUi('m628')}</fluent-button>
          <fluent-button id="approveBack" ${dis}>${trUi('m629')}</fluent-button>
          <fluent-button appearance="subtle" id="export" ${dis}>${trUi('m630')}</fluent-button>
          <p class="caption">${trUi('m631')}</p>`
          : `<fluent-button appearance="primary" id="export" ${dis}>${trUi('m633')}</fluent-button>
          ${me ? `<fluent-button id="approveBack" ${dis}>${trUi('m632')}</fluent-button>` : ''}`}
          ${hardFail ? `<span class="caption fail">${trUi('m634')}</span>` : ''}
          <div id="msg"></div>
        </section>
        <section class="card">
          <div class="row" style="justify-content:space-between"><h2>${trUi('m653')}</h2>
            <div class="row">${filterTypes.filter(t => itemsOf(t).length || ['caption', 'emphasis', 'headline'].includes(t)).map(t => field(typeLabel[t], `<fluent-switch slot="input" data-type="${t}" ${itemsOf(t).some(x => x.enabled) || !itemsOf(t).length ? 'checked' : ''}></fluent-switch>`)).join('')}
              ${plan.telops.some(t => t.type === 'tsukkomi' && !t.approved) || (plan.inserts ?? []).some(i => isTsukkomiCard(i) && i.text && !i.approved) ? `<fluent-button size="small" id="approveAll">${trUi('m635')}</fluent-button>` : ''}
              ${(plan.inserts ?? []).some(i => i.kind === 'card' && !isTsukkomiCard(i) && i.text && !i.approved) ? `<fluent-button size="small" id="approveCards">${trUi('m636')}</fluent-button>` : ''}</div></div>
          <p class="caption">${trUi('m654', [maxW, maxLines > 1 ? trUi('m637', [maxLines]) : '', maxCps])}</p>
          <div class="scroll"><table class="list"><thead><tr><th>${trUi('m304')}</th><th>${trUi('m655')}</th><th style="width:55%">${trUi('m656')}</th><th>${trUi('m657')}</th><th>${trUi('m658')}</th><th>${trUi('m646')}</th></tr></thead><tbody>
            ${[...plan.telops.map(t => ({ at: finalT(t.start), t })), ...(plan.inserts ?? []).map(i => ({ at: i.enabled && i.duration > 0 ? insertStart(i) : finalT(i.at), i }))]
              .sort((a, b) => a.at - b.at || (a.i ? 0 : 1) - (b.i ? 0 : 1)).map(({ t, i }) => i ? insertRow(i) : (() => {
              const w = width(t.text), cps = w / Math.max(0.05, t.end - t.start);
              const start = finalT(t.start), end = finalT(t.end, true);
              return `<tr class="${t.enabled ? '' : 'off'}" data-telop="${esc(t.id)}"><td><span class="tag ${esc(t.type)}">${esc(typeLabel[t.type] ?? t.type)}</span></td>
                <td><button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${trUi('m644', [start.toFixed(1), end.toFixed(1)])}">${start.toFixed(1)}–${end.toFixed(1)}</button></td>
                <td><input class="telop" data-id="${esc(t.id)}" value="${esc(t.text)}" maxlength="40" aria-label="${trUi('m645', [esc(t.id)])}">
                  ${['emphasis', 'headline', 'hook', 'counter'].includes(t.type) ? (() => {
                    const words = t.source_word_ids.map(id => tr.words.find(w => w.id === id)).filter(Boolean);
                    const evidence = words.length ? trUi('m638', [[...new Set(words.map(w => w.speaker))].join('/'), words[0].t0.toFixed(1), words.at(-1).t1.toFixed(1), words.map(w => w.text).join('')]) : trUi('m639');
                    return `<p class="caption evidence" data-evidence="${esc(t.id)}">${esc(evidence)}</p>
                      <label class="caption">${trUi('m641')} <select class="native evidence-select" data-id="${esc(t.id)}" aria-label="${trUi('m642', [esc(t.id)])}"><option value="">${trUi('m643')}</option>
                      ${tr.sentences.filter(s => plan.timeline.some(c => s.t1 > (c.audio_in ?? c.src_in) && s.t0 < (c.audio_in ?? c.src_in) + c.src_out - c.src_in)).map(s => {
                        const ids = tr.words.slice(s.first_word, s.last_word + 1).filter(w => plan.timeline.some(c => w.t0 >= (c.audio_in ?? c.src_in) - 0.15 && w.t1 <= (c.audio_in ?? c.src_in) + c.src_out - c.src_in + 0.15)).map(w => w.id);
                        return `<option value="${esc(JSON.stringify(ids))}" ${JSON.stringify(ids) === JSON.stringify(t.source_word_ids) ? 'selected' : ''}>${trUi('m640', [s.speaker, s.t0.toFixed(1), esc(s.text)])}</option>`;
                      }).join('')}</select></label>`;
                  })() : ''}</td>
                <td class="${w > maxW * (t.type === 'caption' ? maxLines : 1) && t.type !== 'headline' ? 'warn' : ''}">${w}</td>
                <td class="${t.type === 'caption' && cps > maxCps ? 'warn' : 'caption'}">${t.type === 'caption' ? cps.toFixed(1) : ''}</td>
                <td><fluent-switch data-id="${esc(t.id)}" class="en" ${t.enabled ? 'checked' : ''} aria-label="${trUi('m646')}"></fluent-switch>${t.approved ? '' : `<fluent-button size="small" data-approve="${esc(t.id)}">${trUi('m524')}</fluent-button>`}</td></tr>`;
            })()).join('')}
          </tbody></table></div>
        </section>
        ${qaPanel()}
      </div>`;
    wireSafeToggle();
    wireQaPanel();
    $$('button.timestamp-seek').forEach(button => button.addEventListener('click', () => {
      const video = $('#pv video');
      const time = Number(button.dataset.time);
      if (!video || !Number.isFinite(time)) return;
      video.currentTime = Number.isFinite(video.duration) ? Math.min(time, video.duration) : time;
    }));
    const patch = ops => writeReview(async () => {
      try { const r = await api(`/api/editplans/${epId}`, { method: 'PATCH', body: { ops } }); if (gone()) return; plan = r.plan; qa = r.qa; planHash = r.plan_hash; approval = r.approval; finalStale = r.final_render_stale; stale = stale || r.preview_stale; lastRepair = null; draw(); }
      catch (e) { if (!gone()) $('#msg').innerHTML = errorBar(e); return false; }
    });
    $$('input.wd').forEach(inp => inp.addEventListener('change', () => writeReview(async () => {
      try { tr = await api(`/api/assets/${plan.audio.master}/transcript`, { method: 'PATCH', body: { edits: [{ word_id: inp.dataset.id, text: inp.value }] } }); return await patch([{ op: 'replan' }]); }
      catch (e) { if (!gone()) $('#msg').innerHTML = errorBar(e); return false; }
    })));
    $$('input.telop').forEach(inp => inp.addEventListener('change', () => patch([{ op: 'replace_text', target: inp.dataset.id, text: inp.value }])));
    $$('select.evidence-select').forEach(select => select.addEventListener('change', () => patch([{ op: 'set_evidence', target: select.dataset.id, source_word_ids: select.value ? JSON.parse(select.value) : [] }])));
    $$('fluent-switch.en').forEach(sw => sw.addEventListener('change', () => patch([{ op: 'set_enabled', target: sw.dataset.id, enabled: sw.checked }])));
    $$('fluent-switch[data-type]').forEach(sw => sw.addEventListener('change', () => patch([{ op: 'set_type_enabled', type: sw.dataset.type, enabled: sw.checked }])));
    $$('[data-approve]').forEach(b => b.addEventListener('click', () => patch([{ op: 'approve', target: b.dataset.approve }])));
    $('#approveAll')?.addEventListener('click', () => patch([{ op: 'approve_all_tsukkomi' }]));
    $$('input.card-text').forEach(inp => inp.addEventListener('change', () => { stale = true; patch([{ op: 'replace_card_text', target: inp.dataset.id, text: inp.value }]); }));
    $$('fluent-switch.ins-en').forEach(sw => sw.addEventListener('change', () => { stale = true; patch([{ op: 'set_insert_enabled', target: sw.dataset.id, enabled: sw.checked }]); }));
    $$('[data-approve-card]').forEach(b => b.addEventListener('click', () => patch([{ op: 'approve_card', target: b.dataset.approveCard }])));
    $('#approveCards')?.addEventListener('click', () => patch([{ op: 'approve_all_cards' }]));
    $('#bgmSel')?.addEventListener('change', (e) => { patch([{ op: 'set_bgm', target: e.target.value }]); stale = true; });
    $$('select.bgm-cue-track').forEach(s => s.addEventListener('change', () => patch([{ op: 'set_bgm_cue', target: s.dataset.id, text: s.value }])));
    $$('select.bgm-trim').forEach(s => s.addEventListener('change', () => patch([{ op: 'set_bgm_trim', target: s.dataset.id, value: Number(s.value) }])));
    $('#bgmLd')?.addEventListener('change', (e) => patch([{ op: 'set_bgm_level', type: 'ld', value: Number(e.target.value) }]));
    $('#bgmGap')?.addEventListener('change', (e) => patch([{ op: 'set_bgm_level', type: 'gap', value: Number(e.target.value) }]));
    { const v = $('#pv video'), head = $('.bgm-playhead'); if (v && head) { const move = () => { head.style.left = `${Math.min(100, v.currentTime / Math.max(0.1, totalDur()) * 100).toFixed(2)}%`; }; v.addEventListener('timeupdate', move); move(); } }
    $('#sfxSw')?.addEventListener('change', (e) => { patch([{ op: 'set_sfx', enabled: e.target.checked }]); stale = true; });
    $$('[data-adj]').forEach(b => b.addEventListener('click', () => { const [edge, delta] = b.dataset.adj.split(':'); patch([{ op: 'adjust', edge, delta: Number(delta) }]); }));
    $('#repreview').addEventListener('click', async () => {
      $('#repreview').setAttribute('disabled', '');
      $('#msg').innerHTML = `<div class="progress-row"><span class="caption">${trUi('m659')}</span><fluent-progress-bar id="rpBar" max="100" value="0" aria-label="${trUi('m660')}"></fluent-progress-bar><span id="rpPct" class="caption pct">0%</span></div><p class="caption" id="rpStep"></p>`;
      const tick = setInterval(async () => {
        try {
          const p = await api(`/api/editplans/${epId}/preview/progress`);
          if (!p.running || !$('#rpBar')) return;
          $('#rpBar').setAttribute('value', String(Math.round(p.progress * 100)));
          $('#rpPct').textContent = `${Math.round(p.progress * 100)}%`;
          $('#rpStep').textContent = serverText(p.step);
        } catch { }
      }, 700);
      try {
        const r = await api(`/api/editplans/${epId}/preview`, { method: 'POST' });
        clearInterval(tick); previewUrl = r.preview_url; stale = false;
        const v = await api(`/api/editplans/${epId}`);
        plan = v.plan; qa = v.qa; planHash = v.plan_hash; approval = v.approval; finalStale = v.final_render_stale; draw();
      }
      catch (e) { clearInterval(tick); $('#msg').innerHTML = errorBar(e); $('#repreview').removeAttribute('disabled'); }
    });
    const approveThen = async (go) => {
      const btns = ['#approveNext', '#approveBack', '#export'].map(s => $(s)).filter(Boolean);
      btns.forEach(b => b.setAttribute('disabled', ''));
      $('#msg').innerHTML = '';
      try {
        // approval is bound to the current plan hash; re-approving an up-to-date approval is skipped
        if (!approval || approval.stale) {
          const r = await api(`/api/jobs/${job.id}/candidates/${encodeURIComponent(me.candidate.id)}/approval`, { method: 'PUT', body: { approved: true } });
          approval = r.approval;
        }
        await go();
      }
      catch (e) { $('#msg').innerHTML = errorBar(e) + (e.detail ? qaList(e.detail) : ''); btns.forEach(b => b.removeAttribute('disabled')); }
    };
    $('#videoSel')?.addEventListener('change', (e) => { location.hash = `#/ep/${e.target.value}`; });
    // re-read the queue after approving: other videos may have been approved since this screen opened
    $('#approveNext')?.addEventListener('click', () => approveThen(async () => {
      const next = nextToReview(await api(`/api/jobs/${job.id}/candidates`).catch(() => cands), epId);
      location.hash = next ? `#/ep/${next.candidate.edit_plan_id}` : `#/job/${job.id}/exports`;
    }));
    $('#approveBack')?.addEventListener('click', () => approveThen(() => { location.hash = `#/job/${job.id}`; }));
    $('#export').addEventListener('click', () => approveThen(async () => {
      const r = await api(`/api/editplans/${epId}/render`, { method: 'POST' }); location.hash = `#/render/${r.render_id}`;
    }));
  };
  draw();
}

// ---------------- step 6: export the approved videos of one job together ----------------
async function exportsView(jobId) {
  const gone = routeGuard();
  const j = await api(`/api/jobs/${jobId}`);
  if (gone()) return;
  if (j.status !== 'ready' && j.status !== 'done') { location.hash = `#/job/${jobId}`; return; }
  const list = await api(`/api/jobs/${j.id}/candidates`);
  if (gone()) return;
  await saveWorkflowState(j.project_id, { step: 'exports', job_id: j.id });
  if (gone()) return;
  setSteps(5, 5, jobStepLinks(j, list));
  const dur = (x) => x.preview?.duration ?? x.candidate.segments.reduce((a, s) => a + s.t1 - s.t0, 0);
  main.innerHTML = `
    <div class="row" style="justify-content:space-between"><h1>${trUi('m662', [list.length])}</h1>
      <div class="row"><fluent-anchor-button href="#/job/${esc(j.id)}" appearance="subtle">${trUi('m647')}</fluent-anchor-button></div></div>
    <p class="caption">${trUi('m663')}</p>
    ${workProgress(j, list, true)}
    <div class="stack export-list">
      ${list.map(x => `
        <article class="card export-item" aria-label="${esc(videoName(x))}">
          <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
            <div class="stack" style="gap:2px"><b>${esc(videoName(x))}</b>
              <span class="headline">${esc(x.candidate.headline || x.candidate.hook_text || trUi('m427'))}</span>
              <span class="caption">${fmt(dur(x))}${x.preview_stale ? trUi('m661') : ''}</span></div>
            <div class="row"><span class="badges">${x.preview_stale ? '' : qaBadges(x.preview?.qa)}</span>
              <fluent-anchor-button size="small" href="#/ep/${esc(x.candidate.edit_plan_id)}">${trUi('m008')}</fluent-anchor-button></div>
          </div>
          ${approvalBlock(x, videoName(x))}
          <div class="export-result" data-render=""></div>
        </article>`).join('')}
    </div>
    ${batchBox(false)}`;
  wireApprovals(j, list, [5, 5]);
  // preselect what is ready and not yet exported
  for (const x of list) {
    const pick = main.querySelector(`.approval[data-cand="${CSS.escape(x.candidate.id)}"] .exp-pick`);
    const s = videoStatus(x).key;
    if (pick && !pick.disabled && s === 'approved') { pick.checked = true; pick.dispatchEvent(new Event('change')); }
  }
}

// ---------------- actual exported files (shared by the list and the single-export screen) ----------------
async function copySnippet(text, button, feedback, field) {
  button.disabled = true;
  try {
    await navigator.clipboard.writeText(text);
    feedback.innerHTML = info(trUi('m664'), 'success');
  } catch {
    if (field) { field.focus(); field.select(); }
    feedback.innerHTML = info(trUi('m665'), 'warning');
  } finally { button.disabled = false; }
}

async function exportArtifacts(renderId, box) {
  box.innerHTML = `<p class="caption" role="status">${trUi('m666')}</p>`;
  const base = `/api/renders/${encodeURIComponent(renderId)}`;
  let result;
  try { result = await api(`${base}/artifacts`); }
  catch (e) {
    if (!box.isConnected) return;
    box.innerHTML = `${errorBar(e)}<fluent-button class="artifact-retry">${trUi('m667')}</fluent-button>`;
    $('.artifact-retry', box).addEventListener('click', () => exportArtifacts(renderId, box));
    return;
  }
  if (!box.isConnected) return;
  const names = { mp4: trUi('m668'), srt: trUi('m669'), ass: trUi('m670'), editplan: trUi('m671'), metadata: trUi('m672'), post: trUi('m673') };
  const fileUrl = key => `${base}/files/${encodeURIComponent(key)}`;
  box.innerHTML = `
    <div class="grid artifact-layout">
      <section class="stack artifact-video">
        <div class="row"><h3>${trUi('m681')}</h3>${safeToggle()}</div>
        <p class="caption">${trUi('m682')}</p>
        ${result.available ? phone(`/api/media/${encodeURIComponent(renderId)}`) : info(trUi('m674'), 'warning')}
        <div class="video-feedback" role="status"></div>
        <label class="stack">${trUi('m683')}
          <textarea class="artifact-path" rows="3" readonly aria-label="${trUi('m683')}">${esc(result.path)}</textarea></label>
        <div class="row"><fluent-button class="path-copy">${trUi('m684')}</fluent-button>
          <fluent-button class="folder-open" appearance="primary" ${result.available ? '' : 'disabled'}>${trUi('m685')}</fluent-button></div>
        <div class="artifact-feedback" role="status"></div>
      </section>
      <section class="stack artifact-post"><h3>${trUi('m686')}</h3>
        <div class="post-body stack"></div>
      </section>
    </div>
    <details class="artifact-files"><summary>${trUi('m687', [result.files.length])}</summary>
      <div class="stack">${result.files.map(f => `
        <section class="artifact-file stack" data-key="${esc(f.key)}">
          <div class="row"><b>${esc(names[f.key] ?? (f.key.startsWith('thumb') ? trUi('m675', [f.key.slice(5)]) : f.key))}</b>
            ${f.available ? `<a href="${fileUrl(f.key)}" target="_blank" rel="noopener">${trUi('m676')}</a><a href="${fileUrl(f.key)}" download="${esc(f.path.split(/[\\/]/).pop())}">${trUi('m277')}</a>` : `<span class="fail">${trUi('m677')}</span>`}</div>
          <code class="artifact-file-path">${esc(f.path)}</code>
          ${!f.available ? '' : f.preview_type === 'image' ? `<a href="${fileUrl(f.key)}" target="_blank" rel="noopener"><img class="artifact-image" src="${fileUrl(f.key)}" loading="lazy" alt="${esc(names[f.key] ?? f.key)}"></a><div class="image-feedback" role="status"></div>`
            : f.preview_type === 'text' ? `<details class="text-preview"><summary>${trUi('m678')}</summary><div class="preview-body"></div></details>`
            : f.preview_type === 'video' ? `<p class="caption">${trUi('m679')}</p>` : `<p class="caption">${trUi('m680')}</p>`}
        </section>`).join('')}</div>
    </details>`;
  const feedback = $('.artifact-feedback', box), path = $('.artifact-path', box);
  $('.path-copy', box).addEventListener('click', e => copySnippet(path.value, e.currentTarget, feedback, path));
  $('.folder-open', box).addEventListener('click', async e => {
    const button = e.currentTarget;
    button.disabled = true;
    try {
      await api(`${base}/open-folder`, { method: 'POST' });
      if (box.isConnected) feedback.innerHTML = info(trUi('m688'), 'success');
    } catch (err) { if (box.isConnected) feedback.innerHTML = errorBar(err); }
    finally { button.disabled = false; }
  });
  const video = $('video', box);
  if (video) { video.classList.add('final-video'); video.setAttribute('aria-label', trUi('m689')); }
  wireSafeToggle(box);
  video?.addEventListener('error', () => {
    if (!video.isConnected) return;
    $('.video-feedback', box).innerHTML = info(trUi('m690'), 'warning');
  });
  $$('.artifact-image', box).forEach(img => img.addEventListener('error', () => {
    if (!img.isConnected) return;
    $('.image-feedback', img.closest('.artifact-file')).innerHTML = info(trUi('m691'), 'warning');
  }));
  $$('.text-preview', box).forEach(details => details.addEventListener('toggle', async () => {
    if (!details.open || details.dataset.loaded) return;
    details.dataset.loaded = 'true';
    const body = $('.preview-body', details);
    body.textContent = trUi('m692');
    try {
      const preview = await api(`${fileUrl(details.closest('.artifact-file').dataset.key)}/preview`);
      if (!body.isConnected) return;
      body.innerHTML = `<pre class="artifact-text"></pre>${preview.truncated ? info(trUi('m693')) : ''}`;
      $('pre', body).textContent = preview.text;
    } catch (err) {
      if (!body.isConnected) return;
      body.innerHTML = errorBar(err);
      delete details.dataset.loaded;
    }
  }));
  const postBody = $('.post-body', box);
  const loadPost = () => postPanel(renderId, postBody);
  if (result.files.some(f => f.key === 'post' && f.available)) loadPost();
  else {
    postBody.innerHTML = `<p class="caption">${trUi('m694')}</p><fluent-button class="post-create">${trUi('m695')}</fluent-button>`;
    $('.post-create', postBody).addEventListener('click', loadPost);
  }
  const reload = document.createElement('fluent-button');
  reload.textContent = trUi('m667');
  reload.className = 'artifact-reload';
  reload.addEventListener('click', () => exportArtifacts(renderId, box));
  box.append(reload);
}

// ---------------- posting texts (YouTube / Viva Engage / chapters) ----------------
async function postPanel(renderId, box, regenerate = false) {
  box.innerHTML = `<p class="caption" role="status">${trUi('m696')}</p>`;
  let k;
  try { k = await api(`/api/renders/${renderId}/post`, regenerate ? { method: 'POST' } : {}); }
  catch (e) { if (box.isConnected) box.innerHTML = errorBar(e); return; }
  if (!box.isConnected) return;
  const block = (key, title, text, limit, hint) => `
    <div class="stack post-block" style="gap:4px">
      <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
        <h3 style="margin:0">${esc(title)}</h3>
        <div class="row"><span class="caption post-count" data-limit="${limit ?? ''}"></span>
          <fluent-button class="post-copy" data-key="${key}" size="small" appearance="primary">${trUi('m697')}</fluent-button></div></div>
      ${hint ? `<p class="caption" style="margin:0">${esc(hint)}</p>` : ''}
      <textarea class="post-text" data-key="${key}" rows="${Math.min(12, Math.max(4, text.split('\n').length + 1))}" aria-label="${esc(title)}" readonly spellcheck="false" style="width:100%;box-sizing:border-box;font:inherit;resize:vertical">${esc(text)}</textarea>
      <div class="post-feedback" role="status"></div>
    </div>`;
  box.innerHTML = `
    <p class="caption">${trUi('m707', [k.source === 'ai' ? trUi('m698') : trUi('m699')])}</p>
    ${(k.notes ?? []).length ? `<fluent-message-bar intent="warning" layout="multiline"><ul class="notes" style="margin:0">${k.notes.map(n => `<li>${esc(serverText(n))}</li>`).join('')}</ul></fluent-message-bar>` : ''}
    ${block('youtube', trUi('m700'), k.youtube, 5000, trUi('m701'))}
    ${block('engage', trUi('m702'), k.engage, 10000, trUi('m703'))}
    ${k.chapters ? block('chapters', trUi('m704'), k.chapters, null, trUi('m705')) : `<p class="caption">${trUi('m706')}</p>`}
    <div class="row"><fluent-button class="post-again" appearance="subtle">${trUi('m708')}</fluent-button>
      <a href="/api/renders/${encodeURIComponent(renderId)}/files/post" target="_blank" rel="noopener">${trUi('m709')}</a></div>`;
  const count = (ta) => {
    const c = ta.closest('.post-block').querySelector('.post-count'), lim = Number(c.dataset.limit);
    c.textContent = trUi('m710', [formatNumber(ta.value.length), lim ? ` / ${formatNumber(lim)}` : '']);
    c.style.color = lim && ta.value.length > lim ? 'var(--colorPaletteRedForeground1, #c50f1f)' : '';
  };
  $$('.post-text', box).forEach(count);
  $$('.post-copy', box).forEach(b => b.addEventListener('click', async () => {
    const ta = $(`.post-text[data-key="${b.dataset.key}"]`, box);
    await copySnippet(ta.value, b, $('.post-feedback', ta.closest('.post-block')), ta);
  }));
  $('.post-again', box).addEventListener('click', () => {
    box.innerHTML = `<p class="caption" aria-live="polite">${trUi('m711')}</p>`;
    postPanel(renderId, box, true);
  });
}

// ---------------- export ----------------
async function exportView(renderId) {
  const gone = routeGuard();
  setSteps(5);
  let timer = null, finished = false, workflowSaved = false;
  const stop = () => { finished = true; clearInterval(timer); };
  cleanup = stop;
  const poll = async () => {
    const s = await api(`/api/renders/${renderId}`);
    if (finished || gone()) return;   // an earlier poll already showed the result; re-rendering would wipe the post texts
    if (!workflowSaved && s.project_id) {
      await saveWorkflowState(s.project_id, { step: 'render', edit_plan_id: s.editplan_id, render_id: renderId });
      if (finished || gone()) return;
      workflowSaved = true;
    }
    if (s.state === 'done') {
      stop();
      const r = s.render;
      const hard = r.qa.filter(q => q.check_id.startsWith('H-'));
      const jobId = await api(`/api/editplans/${s.editplan_id}`).then(v => v.plan?.provenance?.job_id).catch(() => null);
      if (gone()) return;
      setSteps(5, 6, { 0: `#/p/${s.project_id}/settings`, 1: `#/p/${s.project_id}/settings`, 4: `#/ep/${s.editplan_id}`, ...(jobId ? { 2: `#/job/${jobId}`, 3: `#/job/${jobId}`, 5: `#/job/${jobId}/exports` } : {}) });
      main.innerHTML = `
        <div class="row" style="justify-content:space-between"><h1>${trUi('m715')}</h1><div class="row">${jobId ? `<fluent-anchor-button href="#/job/${esc(jobId)}" appearance="subtle">${trUi('m712')}</fluent-anchor-button><fluent-anchor-button href="#/job/${esc(jobId)}/exports" appearance="subtle">${trUi('m713')}</fluent-anchor-button>` : ''}</div></div>
        ${info(trUi('m714', [hard.filter(q => q.status === 'pass').length, hard.length]), 'success')}
        <section class="card stack artifact-body"></section>
        <section class="card stack"><h2>${trUi('m716')}</h2>${qaList(r.qa)}</section>`;
      exportArtifacts(renderId, $('.artifact-body'));
    } else if (s.state === 'cancelled') {
      stop();
      main.innerHTML = `<h1>${trUi('m717')}</h1>${info(serverText(s.message ?? trUi('m717')))}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}" style="margin-top:12px">${trUi('m718')}</fluent-anchor-button>`;
    } else if (s.state === 'stale') {
      stop();
      main.innerHTML = `<h1>${trUi('m720')}</h1>${info(trUi('m719'), 'warning')}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}">${trUi('m718')}</fluent-anchor-button>`;
    } else if (s.state === 'failed') {
      stop();
      main.innerHTML = `<h1>${trUi('m721')}</h1>${errorBar({ message: s.message, code: s.error_code })}${s.failures ? `<section class="card">${qaList(s.failures)}</section>` : ''}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}" style="margin-top:12px">${trUi('m718')}</fluent-anchor-button>`;
    } else {
      if (!$('#expBar')) {
        main.innerHTML = `<h1>${trUi('m722')}</h1><section class="card"><p id="expMsg" aria-live="polite"></p>
          <div class="progress-row"><span class="caption">${trUi('m404')}</span><fluent-progress-bar id="expBar" max="100" aria-label="${trUi('m723')}"></fluent-progress-bar><span id="expPct" class="caption pct"></span></div>
          <p class="caption" id="expStep"></p>
          <p class="caption">${trUi('m724')}</p></section>`;
      }
      $('#expMsg').textContent = jobMessage(s);
      const bar = $('#expBar');
      if (typeof s.progress === 'number') { bar.setAttribute('value', String(Math.round(s.progress * 100))); $('#expPct').textContent = `${Math.round(s.progress * 100)}%`; }
      else { bar.removeAttribute('value'); $('#expPct').textContent = ''; }
      $('#expStep').textContent = s.step ? trUi('m491', [serverText(s.step)]) : '';
    }
  };
  await poll();
  if (!finished && !gone()) timer = setInterval(() => poll().catch(e => {
    if (!finished && !gone()) { stop(); main.innerHTML = errorBar(e); }
  }), 1000);
}

applyShell();
$('#ui-language').value = getLanguage();
$('#ui-language').addEventListener('change', async e => {
  const selector = e.currentTarget;
  selector.disabled = true;
  const hash = location.hash;
  try {
    if (await flushLocaleWrites() === false) return;
    await workflowSaveChain.catch(() => {});
    const drafts = $$('input, textarea, select, fluent-text-input', main).filter(el => el.id || el.dataset.id || el.dataset.properField).map(el => ({
      id: el.id, dataId: el.dataset.id, field: el.dataset.properField, index: el.dataset.properIndex,
      className: el.className, value: el.value,
    }));
    const titleEditing = $('#title-edit') && !$('#title-edit').hidden;
    const expanders = $$('details[id]', main).map(el => ({ id: el.id, open: el.open }));
    const projectId = hash.match(/^#\/p\/([\w-]+)/)?.[1];
    if (projectId) {
      const properNouns = $$('#properNounRows fieldset').map(row => {
        const get = name => $(`[data-proper-field="${name}"]`, row)?.value ?? '';
        return { type: get('type'), display_text: get('display_text'), reading: get('reading'), variants: get('variants').split(/[,、，\n]/).map(s => s.trim()).filter(Boolean) };
      });
      localeDraft = { projectId, properNouns };
    }
    setLanguage(selector.value);
    applyShell();
    refreshProvider();
    if (location.hash !== hash) return;
    await router();
    if (location.hash !== hash) return;
    for (const draft of drafts) {
      const el = draft.id ? document.getElementById(draft.id) : draft.field
        ? $(`[data-proper-index="${draft.index}"][data-proper-field="${draft.field}"]`, main)
        : $$('[data-id]', main).find(el => el.dataset.id === draft.dataId && el.className === draft.className);
      if (el && draft.value !== undefined) el.value = draft.value;
    }
    if (titleEditing && $('#title-edit')) { $('#title-edit').hidden = false; $('#title-view').hidden = true; }
    for (const expander of expanders) {
      const el = document.getElementById(expander.id);
      if (el) el.open = expander.open;
    }
  } catch (err) { main.insertAdjacentHTML('afterbegin', errorBar(err)); }
  finally { selector.value = getLanguage(); selector.disabled = false; selector.focus(); }
});
refreshProvider();
router();
