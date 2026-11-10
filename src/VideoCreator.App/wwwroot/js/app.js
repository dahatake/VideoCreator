// VideoCreator UI — vanilla ES modules + Fluent 2 web components.
// Every dynamic string is escaped with esc() before reaching innerHTML (LLM/user text is untrusted).
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
// same wording as Planning.BgmPlanner.SceneWord / TrackWord
const energyWord = (e) => e < 0.35 ? '落ち着いた' : e < 0.6 ? 'ふつう' : 'にぎやか';
const main = $('#main');

const STEPS = ['素材', 'パターン', '生成', '候補', '確認・修正', '書き出し'];
const STAGES = [
  ['ingesting', '解析用プロキシ'], ['transcribing', '書き起こし'], ['analyzing', 'ショット・音量・無音の解析'],
  ['selecting', 'AI によるハイライト選定'], ['planning', '編集計画'], ['previewing', 'プレビュー作成'],
];
const ERRORS = { // UX-04: plain-language explanation + what to do next
  'E-RIGHTS-REQUIRED': '素材の権利確認が必要です。素材ごとのチェックボックスをオンにしてください。',
  'E-ING-UNSUPPORTED': 'この動画は読み込めませんでした。MP4 / MOV / MKV / WebM の動画を選んでください。',
  'E-ASR-PROVIDER': '書き起こしサービスに接続できませんでした。ネットワークと Azure へのサインイン（az login）を確認して、もう一度お試しください。',
  'E-SOL-INFEASIBLE': '条件に合う区間が見つかりませんでした。別のパターンを選ぶか、もう少し長い動画を使ってください。',
  'E-QA-HARDFAIL': '自動チェックで公開基準を満たさない項目がありました。下の一覧の項目を直してから、もう一度書き出してください。',
  'E-PRESET-UNAVAILABLE': 'このパターンはまだ使えません。',
  'E-RND-FFMPEG': '動画の書き出しに失敗しました。ディスクの空き容量を確認して、もう一度お試しください。',
  'E-PROJECT-BUSY': 'ジョブ画面で「キャンセル」するか、完了を待ってから削除してください。',
  'E-FILE-IN-USE': 'フォルダ内のファイルを開いているアプリを閉じてから、もう一度お試しください。プロジェクトはまだ削除されていません。',
};

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
  const hint = ERRORS[e.code] ?? '';
  return `<fluent-message-bar intent="error" layout="multiline"><b>${esc(e.message)}</b>${hint ? `<br>${esc(hint)}` : ''}${e.code ? `<span class="caption"> (${esc(e.code)})</span>` : ''}</fluent-message-bar>`;
}
function info(msg, intent = 'info') { return `<fluent-message-bar intent="${intent}" layout="multiline">${esc(msg)}</fluent-message-bar>`; }
function setSteps(current, done = current, links = {}) {
  $('#steps').innerHTML = current < 0 ? '' : STEPS.map((s, i) => {
    const cls = `step ${i === current ? 'current' : i < done ? 'done' : ''}`;
    const inner = `<span class="n">${i < done && i !== current ? '✓' : i + 1}</span>${s}`;
    // a finished step is a link back to it: redoing it starts a new pattern, the earlier results stay
    return links[i] && i !== current
      ? `<a class="${cls}" href="${esc(links[i])}" aria-label="${esc(s)}に戻る">${inner}</a>`
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
function safeToggle() { return field('安全領域を表示', '<fluent-switch slot="input" class="safe-toggle"></fluent-switch>'); }
function wireSafeToggle(root = main) {
  $$('.safe-toggle', root).forEach(sw => sw.addEventListener('change', () => root.classList.toggle('show-safe', sw.checked)));
}
function qaList(qa) {
  return qa.map(q => `<div class="qa-row"><b>${esc(q.check_id)}</b><span class="${q.status === 'pass' ? 'pass' : q.status === 'fail' ? 'fail' : q.status === 'not_run' ? 'muted' : 'warnc'}">${q.status === 'pass' ? '合格' : q.status === 'fail' ? '不合格' : q.status === 'not_run' ? '未実施' : '警告'}</span><span>${esc(q.message)}: ${esc(q.measured)} <span class="muted">（基準 ${esc(q.threshold)}${q.stage ? `・${esc(q.stage)}` : ''}）</span></span></div>`).join('');
}
function qaBadges(qa) {
  if (!qa?.length) return '';
  const hard = qa.filter(q => q.check_id.startsWith('H-'));
  const fails = hard.filter(q => q.status === 'fail').length, warns = qa.filter(q => q.status === 'warn').length;
  const pending = hard.filter(q => q.status === 'not_run').length;
  return `<fluent-badge appearance="tint" color="${fails ? 'danger' : pending ? 'warning' : 'success'}">QA ${fails ? `不合格 ${fails}` : `合格 ${hard.filter(q => q.status === 'pass').length}/${hard.length}`}</fluent-badge>${pending ? `<fluent-badge appearance="tint" color="warning">未実施 ${pending}（書き出し時に検査）</fluent-badge>` : ''}${warns ? `<fluent-badge appearance="tint" color="warning">警告 ${warns}</fluent-badge>` : ''}`;
}

// ---------------- settings / provider ----------------
async function refreshProvider() {
  try {
    const h = await api('/api/health');
    const names = { 'azure-openai': `Microsoft Foundry（${h.llm_model ?? ''}）`, copilot: 'GitHub Copilot（インストール済み CLI）', none: 'AI なし（音声の盛り上がりで選定）' };
    const sel = h.llm_provider !== 'none' && !h.llm_model ? 'AI なし（設定した AI が使えません）' : names[h.llm_provider] ?? h.llm_provider;
    $('#provider').textContent = `${h.local_only ? 'LLM不使用モード · ' : ''}書き起こし: ${h.asr === 'none' ? 'なし' : 'Azure AI Speech'} · 選定: ${sel}`;
    return h;
  } catch { $('#provider').textContent = ''; }
}
function llmChoice(value, title, lines, unavailable) {
  return `<fluent-field label-position="after" class="llm-choice">
    <label slot="label"><b>${esc(title)}</b>${lines.map(l => `<span class="caption">${esc(l)}</span>`).join('')}${unavailable ? `<span class="caption warnc">使えません：${esc(unavailable)}</span>` : ''}</label>
    <fluent-radio slot="input" value="${value}" ${unavailable ? 'disabled' : ''}></fluent-radio>
  </fluent-field>`;
}
function transmissionNotice(s) {
  const t = s.transmission;
  if (!t) return info('送信先を確認できません。生成前に設定を確認してください。', 'warning');
  return info(`${t.llm_disabled ? 'LLM不使用モード：LLMには送信しません。' : `LLMへの送信先：${t.llm_destination ?? '未設定'}（書き起こしの文章と音量の要約）。`}
    ${t.asr_endpoint ? `字幕用の音声は ${t.asr_endpoint} に送信します。完全オフラインではありません。認証先：${t.authentication}。` : 'ASRは未設定です。キャッシュがない音声の書き起こしはできません。'}`);
}
// Same disclosure as transmissionNotice, condensed to a caption so it stays visible above the generate button.
function transmissionCompact(s) {
  const t = s.transmission;
  if (!t) return transmissionNotice(s);
  const same = !t.llm_disabled && t.asr_endpoint && t.llm_destination && t.llm_destination.replace(/\/+$/, '') === t.asr_endpoint.replace(/\/+$/, '');
  const off = `完全オフラインではありません。認証先：${esc(t.authentication)}`;
  if (same) return `<p class="caption send-to"><b>送信先</b> 文章と音量の要約・字幕用の音声 → ${esc(t.asr_endpoint)}（${off}）</p>`;
  const llm = t.llm_disabled ? 'LLM不使用モード：LLMには送信しません' : `文章と音量の要約 → ${esc(t.llm_destination ?? '未設定')}`;
  const asr = t.asr_endpoint ? `字幕用の音声 → ${esc(t.asr_endpoint)}（${off}）` : 'ASRは未設定です。キャッシュがない音声の書き起こしはできません';
  return `<p class="caption send-to"><b>送信先</b> ${llm}／${asr}</p>`;
}
$('#settings-btn').addEventListener('click', async () => {
  const s = await api('/api/settings'); const h = await api('/api/health'); const pd = await api('/api/settings/projects-dir');
  const local = s.local_only ? 'LLM不使用モードで起動しています' : '';
  const choices = [
    ['azure-openai', `Microsoft Foundry（${s.azure_deployment}）`, [
      'おすすめ。JSON スキーマで出力を固定するため、候補の形式が崩れにくい方式です。',
      '送るもの：書き起こしの文章と音量の要約（動画・音声ファイルは送りません）。Entra ID（az login）でサインインします。',
      '料金：Azure の従量課金。生成前に見積もりを表示します。',
    ], local || (s.azure_configured ? '' : 'エンドポイント（VideoCreator:Llm:Endpoint）が未設定です。scripts/setup.ps1 を実行してください')],
    ['copilot', `GitHub Copilot（${s.copilot_model ?? '既定のモデル'}）`, [
      'PC にインストール済みの Copilot CLI を使います。ファイル操作などのツールは渡しません。',
      '送るもの：書き起こしの文章と音量の要約を GitHub に送ります。料金は Copilot のプランに含まれます。',
      '出力形式を強制できないため、まれに形式エラーになることがあります。',
    ], local || (s.copilot_cli ? '' : 'Copilot CLI が見つかりません（winget install GitHub.Copilot）')],
    ['none', 'LLM を使わない', [
      '音量のピーク（盛り上がり）の周りを切り出します。選定のために外部へ送るものはなく、AI の料金もかかりません。',
      '話の内容は考慮しません。ツッコミテロップなど、AI が必要な機能は使えません。',
    ], ''],
  ];
  const cur = choices.find(c => c[0] === s.llm_provider);
  $('#settings-body').innerHTML = `
    <p id="llm-label">ハイライトを選ぶ AI を選んでください。字幕用の書き起こし${s.asr_configured ? '（Azure AI Speech）' : ''}はどれを選んでも同じです。</p>
    ${transmissionNotice(s)}
    ${!s.local_only && cur?.[3] ? info(`設定されている「${cur[1]}」は使えないため、現在は AI を使わずに選んでいます。`, 'warning') : ''}
    <fluent-radio-group id="llm" orientation="vertical" aria-labelledby="llm-label" value="${esc(s.llm_provider)}">
      ${choices.map(([v, t, l, u]) => llmChoice(v, t, l, u)).join('')}
    </fluent-radio-group>
    <p class="caption">AI の呼び出しに失敗して候補が得られなかった場合は、自動で「AI を使わない」方式に切り替えて続けます。</p>
    <fluent-divider style="margin:16px 0"></fluent-divider>
    ${projectsDirSection(pd)}
    <fluent-divider style="margin:16px 0"></fluent-divider>
    <dl class="facts"><dt>FFmpeg</dt><dd>${esc(h.ffmpeg)}</dd><dt>不足フィルタ</dt><dd>${esc(h.missing_filters.join(', ') || 'なし')}</dd><dt>Copilot CLI</dt><dd>${esc(s.copilot_cli ?? '未検出')}</dd><dt>パターン数</dt><dd>${h.presets}</dd></dl>`;
  wireProjectsDir(pd);
  $('#llm')?.addEventListener('change', async () => {
    const radios = $$('#llm fluent-radio');
    const value = radios.find(r => r.checked)?.value;
    if (!value || value === s.llm_provider) return;
    try { await api('/api/settings', { method: 'PUT', body: { llm_provider: value } }); s.llm_provider = value; $('#settings-body fluent-message-bar[intent=warning]')?.remove(); await refreshProvider(); const current = await api('/api/settings'); if ($('#transmission')) $('#transmission').innerHTML = transmissionCompact(current); }
    catch (e) { radios.forEach(r => { r.checked = r.value === s.llm_provider; }); $('#settings-body').insertAdjacentHTML('afterbegin', errorBar(e)); }
  });
  $('#settings-dialog').show();
});
$('#settings-close').addEventListener('click', () => $('#settings-dialog').hide());

// ---------------- settings: projects folder ----------------
const bytes = (b) => b >= 2 ** 30 ? `${(b / 2 ** 30).toFixed(1)} GB` : b >= 2 ** 20 ? `${(b / 2 ** 20).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`;
function projectsDirSection(pd) {
  const source = { default: '既定の場所', config: '設定ファイル（appsettings）で指定', app: 'この画面で指定' }[pd.source] ?? '';
  return `<h3 id="pdirLabel" style="margin:0 0 4px">プロジェクトの保存先</h3>
    <p class="caption">いまの保存先: <b id="pdirNow">${esc(pd.path)}</b>（${esc(source)}）· プロジェクト ${pd.projects} 件<br>新しいプロジェクトはこのフォルダーの中に作られ、素材のアップロード・プレビュー・書き出した動画もそこに保存されます。</p>
    <div class="row" style="align-items:center"><fluent-text-input id="pdirPath" style="flex:1" placeholder="D:\\Videos\\VideoCreator" aria-label="新しい保存先のフォルダー（フルパス）"></fluent-text-input><fluent-button id="pdirBrowse" type="button">フォルダーを選ぶ…</fluent-button></div>
    <p class="caption" id="pdirMoveLabel">いまの保存先にあるフォルダーとファイルをどうしますか（どちらかを選んでください）</p>
    <fluent-radio-group id="pdirMove" orientation="vertical" aria-labelledby="pdirMoveLabel">
      ${field('すべて新しい保存先へ移動する（プロジェクトのフォルダー・アップロードした素材・書き出した動画などをすべて移し、アプリ内の参照も新しい場所に書き換えます）', '<fluent-radio slot="input" value="move"></fluent-radio>')}
      ${field('移動しない（いまのプロジェクトはいまの場所のまま使えます。新しく作るプロジェクトから新しい保存先に作ります）', '<fluent-radio slot="input" value="keep"></fluent-radio>')}
    </fluent-radio-group>
    <div><fluent-button id="pdirCheck" type="button">内容を確認する</fluent-button></div>
    <div id="pdirPlan"></div>
    <div class="progress-row" id="pdirRow" hidden><span class="caption" id="pdirStep"></span><fluent-progress-bar id="pdirBar" max="100" aria-label="保存先の移動の進み具合"></fluent-progress-bar><span class="caption pct" id="pdirPct"></span></div>
    <div id="pdirMsg" aria-live="polite"></div>`;
}
function wireProjectsDir(pd) {
  const choice = () => $$('#pdirMove fluent-radio').find(r => r.checked)?.value;
  const reset = () => { $('#pdirPlan').innerHTML = ''; };
  $('#pdirPath').addEventListener('input', reset);
  $('#pdirMove').addEventListener('change', reset);
  $('#pdirBrowse').addEventListener('click', async () => {
    const b = $('#pdirBrowse'); b.disabled = true;
    $('#pdirMsg').innerHTML = info('開いた「フォルダーの選択」画面でフォルダーを選んでください（画面が見えないときはタスク バーを確認してください）');
    try {
      const r = await api('/api/dialogs/folder', { method: 'POST', body: { initial_path: $('#pdirPath').value || pd.path } });
      if (r.path != null) { $('#pdirPath').value = r.path; reset(); }
      $('#pdirMsg').innerHTML = r.path == null ? info('選択をキャンセルしました。入力は変更していません') : '';
    } catch (e) { $('#pdirMsg').innerHTML = errorBar(e); }
    finally { b.disabled = false; }
  });
  $('#pdirCheck').addEventListener('click', async () => {
    $('#pdirMsg').innerHTML = '';
    const path = $('#pdirPath').value ?? '', how = choice();
    if (!path.trim()) { $('#pdirMsg').innerHTML = info('新しい保存先のフォルダーを入力するか、「フォルダーを選ぶ…」で選んでください', 'warning'); return; }
    if (!how) { $('#pdirMsg').innerHTML = info('いまのフォルダーとファイルを「移動する」か「移動しない」かを選んでください', 'warning'); return; }
    let plan;
    try { plan = await api('/api/settings/projects-dir/plan', { method: 'POST', body: { path, move: how === 'move' } }); }
    catch (e) { $('#pdirMsg').innerHTML = errorBar(e); return; }
    const projects = plan.items.filter(i => i.project_id), others = plan.items.filter(i => !i.project_id);
    const list = (xs) => `<ul class="notes">${xs.slice(0, 8).map(i => `<li>${esc(i.project_name ?? i.name)}${i.project_name ? `<span class="caption">（${esc(i.name)}）</span>` : ''} · ${bytes(i.bytes)}${i.is_directory ? `・${i.files} ファイル` : ''}</li>`).join('')}${xs.length > 8 ? `<li>ほか ${xs.length - 8} 件</li>` : ''}</ul>`;
    $('#pdirPlan').innerHTML = `<section class="card stack" style="margin-top:8px">
      <p><b>${esc(plan.from)}</b><br>→ <b>${esc(plan.to)}</b></p>
      ${plan.move ? `
        <p class="caption">移動するもの: プロジェクト ${projects.length} 件${others.length ? `・その他 ${others.length} 件` : ''}（合計 ${bytes(plan.total_bytes)}）。${plan.same_volume ? '同じドライブなので、フォルダーの名前を変えるだけで済みます（すぐ終わります）。' : `別のドライブへコピーして中身を照合してから、元のファイルを消します（空き ${plan.free_bytes != null ? bytes(plan.free_bytes) : '不明'}）。`}</p>
        ${projects.length ? `<details open><summary class="caption">プロジェクト</summary>${list(projects)}</details>` : ''}
        ${others.length ? `<details><summary class="caption">その他のフォルダー・ファイル（アプリが作ったものではない可能性があります。これらも移動します）</summary>${list(others)}</details>` : ''}
        ${!plan.items.length ? '<p class="caption">いまの保存先に移動するものはありません。</p>' : ''}`
      : `<p class="caption">ファイルは動かしません。いまのプロジェクトは ${esc(plan.from)} のまま使い、削除するときはこれまでどおりフォルダーごとごみ箱に移します。新しく作るプロジェクトから新しい保存先に作ります。</p>`}
      ${plan.projects_elsewhere ? `<p class="caption">ほかの場所にあるプロジェクト ${plan.projects_elsewhere} 件は動かしません。</p>` : ''}
      ${plan.problems.length ? `<fluent-message-bar intent="error" layout="multiline"><b>このままでは変更できません</b><ul class="notes">${plan.problems.map(p => `<li>${esc(p)}</li>`).join('')}</ul></fluent-message-bar>`
        : `<div><fluent-button appearance="primary" id="pdirApply">${plan.move ? 'すべて移動して保存先を変更する' : '移動せずに保存先を変更する'}</fluent-button></div>`}
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
        $('#pdirStep').textContent = `${p.done_items} / ${p.total_items} 件${p.current ? `（${p.current}）` : ''}`;
      } catch { }
    }, 500);
    try {
      const r = await api('/api/settings/projects-dir', { method: 'PUT', body: { path, move } });
      $('#pdirNow').textContent = r.projects_dir; $('#pdirPlan').innerHTML = ''; $('#pdirPath').value = '';
      const lines = [r.moved ? `${r.moved_items.length} 件を移動しました（アプリ内の参照 ${r.documents_updated} 件を書き換えました）。` : 'ファイルは動かしていません。', `新しいプロジェクトは ${r.projects_dir} に作ります。`];
      $('#pdirMsg').innerHTML = info(lines.join(''), r.failed.length ? 'warning' : 'success')
        + (r.failed.length ? `<fluent-message-bar intent="warning" layout="multiline"><b>移動できなかったため、元の場所に残したもの（そのまま使えます）</b><ul class="notes">${r.failed.map(f => `<li>${esc(f.name)}: ${esc(f.reason)}</li>`).join('')}</ul>開いているファイルを閉じてから、同じ保存先を選んでもう一度「移動する」で続きを移せます。</fluent-message-bar>` : '')
        + (r.left_behind.length ? `<fluent-message-bar intent="warning" layout="multiline">新しい場所へのコピーは済みましたが、元のファイルの一部を消せませんでした（使用中の可能性）。不要なら手で削除してください: ${r.left_behind.map(esc).join('、')}</fluent-message-bar>` : '');
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
function routeGuard() { const seq = routeSeq; return () => seq !== routeSeq; }
async function router() {
  routeSeq++;
  if (cleanup) { cleanup(); cleanup = null; }
  document.body.classList.remove('fit');
  const hash = location.hash || '#/';
  for (const [re, fn] of routes) { const m = hash.match(re); if (m) { try { await fn(...m.slice(1)); } catch (e) { main.innerHTML = errorBar(e); } main.focus(); return; } }
  main.innerHTML = info('ページが見つかりません');
}
window.addEventListener('hashchange', router);

// ---------------- home ----------------
let flash = '';   // one-shot message shown on the next home render (e.g. after a delete)
async function home() {
  setSteps(-1);
  const projects = await api('/api/projects');
  const notice = flash; flash = '';
  main.innerHTML = `
    <div class="row" style="justify-content:space-between;margin-bottom:16px">
      <div><h1>プロジェクト</h1><p class="muted">長い動画を選んで、パターンを選ぶだけ。AI が見どころを探して、字幕付きの縦型ショートを作ります。</p></div>
      <form id="new" class="row"><fluent-text-input id="name" placeholder="プロジェクト名" aria-label="プロジェクト名"></fluent-text-input><fluent-button appearance="primary" type="submit">新しいプロジェクト</fluent-button></form>
    </div>
    <div id="homeMsg" role="status">${notice}</div>
    ${projects.length === 0 ? `<div class="card empty"><img src="/img/icon.svg" alt=""><h2>はじめましょう</h2><p class="muted">「新しいプロジェクト」から動画を追加してください。</p></div>` :
      `<div class="grid cols-auto">${projects.map(p => `
        <article class="card selectable project-card">
          <div class="card-head"><h3><a class="stretch" href="#/p/${esc(p.id)}">${esc(p.name)}</a></h3>
            <fluent-button appearance="subtle" size="small" class="del" data-id="${esc(p.id)}" aria-label="「${esc(p.name)}」を削除">削除</fluent-button></div>
          <dl class="facts"><dt>作成</dt><dd>${esc(new Date(p.created_at).toLocaleString('ja-JP'))}</dd><dt>素材</dt><dd>${p.assets} 本</dd>
          <dt>最新の生成</dt><dd>${p.last_job ? esc(p.last_job.message || p.last_job.status) : '—'}</dd></dl>
        </article>`).join('')}</div>`}`;
  $('#new').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const p = await api('/api/projects', { method: 'POST', body: { name: $('#name').value || '' } });
    location.hash = `#/p/${p.id}`;
  });
  $$('.project-card .del').forEach(b => b.addEventListener('click', () => confirmDelete(projects.find(p => p.id === b.dataset.id), b)));
}

const RUNNING = ['queued', 'ingesting', 'transcribing', 'analyzing', 'selecting', 'planning', 'previewing'];
function confirmDelete(p, opener) {
  const dlg = $('#delete-dialog'), ok = $('#delete-confirm'), cancel = $('#delete-cancel');
  const busy = p.last_job && RUNNING.includes(p.last_job.status);
  $('#delete-body').innerHTML = `
    <p><b>${esc(p.name)}</b> を削除します。</p>
    <ul class="del-list">
      <li>プロジェクトのフォルダ（書き出した動画・プレビュー・アップロードした素材）は<b>ごみ箱</b>に移動します。</li>
      <li>候補・編集内容・書き起こしなどの解析データは削除され、元に戻せません。</li>
      <li>パスを指定して追加した<b>元の動画ファイルは削除しません</b>。</li>
    </ul>
    <p class="caption">フォルダ: ${esc(p.directory)}</p>
    ${busy ? info('生成の途中です。完了するか、キャンセルしてから削除してください。', 'warning') : ''}
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
      const where = r.folder === 'removed' ? 'フォルダはごみ箱に移動しました。' : r.folder === 'kept' ? `フォルダはアプリの管理外のため残しています（${r.folder_path}）。` : '';
      flash = info(`「${r.name}」を削除しました。${where}`, 'success');
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
  const [d, presets, bgmList, kitView, settings] = await Promise.all([api(`/api/projects/${id}`), api('/api/presets'), api(`/api/bgm?project_id=${encodeURIComponent(id)}`), api('/api/brandkit'), api('/api/settings')]);
  const resumeRoute = workflowRoute(id, d.resume_state ?? { step: 'project' });
  const userBgm = bgmList.filter(b => b.source === 'user');
  if (!showSettings && resumeRoute && resumeRoute !== `#/p/${id}`) { location.hash = resumeRoute; return; }
  await saveWorkflowState(id, { step: showSettings ? 'settings' : 'project' });
  const assets = d.assets, rights = new Set(d.rights.map(r => r.asset_id));
  const allRights = assets.length > 0 && assets.every(a => rights.has(a.id));
  // the generate button stays enabled (a disabled button is unreadable and unfocusable); clicking it early explains what is missing
  const notReady = !assets.length ? '素材を追加してください' : !allRights ? '権利確認にチェックしてください' : '';
  const saved = d.project.generation_settings ?? {};
  let selected = saved.preset_id ?? 'P4';
  const opts = { scope: saved.scope ?? 'balanced', bgm: saved.bgm ?? 'auto', sfx: saved.sfx ?? true, tsukkomi: saved.tsukkomi ?? false, tsukkomi_style: ['auto', 'telop', 'card'].includes(saved.tsukkomi_style) ? saved.tsukkomi_style : 'auto', series: saved.series ?? false, series_by: saved.series_by ?? 'parts', parts: saved.parts ?? 2, part_minutes: saved.part_minutes ?? 15,
    opening: (saved.opening ?? true) && !!kitView.opening, closing: (saved.closing ?? true) && !!kitView.closing, scene_cards: saved.scene_cards ?? false, mask_words: Array.isArray(saved.mask_words) ? saved.mask_words : [] };
  setSteps(assets.length && allRights ? 1 : 0);
  let estimate = null;
  if (assets.length) try { estimate = await api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`); } catch { }
  const lastJob = d.jobs[0];
  const roleLabel = { main: '基準（音声の元）', cam_a: 'カメラA', cam_b: 'カメラB', wide: '引き（全体）', audio_master: '音声のみの基準', screen: '画面（PC の画面収録）' };
  const syncText = (a) => {
    if (a.role === 'main') return '基準（ほかの素材をこの音声に合わせます）';
    if (a.sync_method === 'manual' || (a.sync_method == null && a.sync_confidence === 9999)) return `手入力のずれ ${(a.sync_offset * 1000).toFixed(0)} ms`;
    if (a.sync_method === 'audio') return `音声で同期: ${a.sync_offset >= 0 ? '+' : ''}${(a.sync_offset * 1000).toFixed(1)} ms` +
      (a.sync_drift_ppm ? `・時計のずれ ${a.sync_drift_ppm > 0 ? '+' : ''}${a.sync_drift_ppm.toFixed(1)} ppm（1時間で ${(Math.abs(a.sync_drift_ppm) * 3.6).toFixed(0)} ms を補正）` : '') +
      (a.sync_windows ? `・${a.sync_windows} 区間が一致（残差 ${a.sync_residual_ms} ms）` : '');
    if (a.sync_method === 'metadata') return `撮影日時で同期: ${a.sync_offset.toFixed(1)} 秒（誤差 ±1 秒程度・要確認）`;
    if (a.sync_method === 'none') return '同期できませんでした（「ずれ(秒)」で指定できます）';
    return a.sync_confidence != null ? `同期の信頼度 ${a.sync_confidence}（次の生成で測り直します）` : '生成時に音声で自動同期';
  };
  const scopeLabel = { economy: ['節約', '盛り上がりの大きい区間だけを AI に送ります（速い・安い）'], balanced: ['標準', '半分程度に絞って AI に送ります'], full: ['全文', '書き起こし全体を AI に送ります（いちばん見落としが少ない）'] };
  const jpy = (c) => `¥${Number(c.estimated_jpy).toFixed(1)}`;
  const long = estimate && estimate.duration > 1800;

  // Windows Settings-style expanders: the header always shows the current value, so a collapsed group still reads at a glance
  const groupState = JSON.parse(localStorage.getItem('vc.genGroups') ?? '{}');
  const group = (key, title, body, { open = true, id = '', titleId = '' } = {}) => `
        <details class="setgroup" data-group="${key}" ${id ? `id="${id}"` : ''} ${(groupState[key] ?? open) ? 'open' : ''}>
          <summary><h3 ${titleId ? `id="${titleId}"` : ''}>${title}</h3><span class="caption group-sum" id="sum-${key}"></span></summary>
          <div class="stack setgroup-body">${body}</div>
        </details>`;

  main.innerHTML = `
    <div class="fit-top">
    <div class="row" style="justify-content:space-between">
      <div class="row title-row" id="title-view"><h1 id="pname">${esc(d.project.name)}</h1><fluent-button id="rename" appearance="subtle" size="small">名前を変更</fluent-button></div>
      <form class="row title-row" id="title-edit" hidden>
        <fluent-text-input id="rename-input" maxlength="80" aria-label="プロジェクト名" aria-describedby="rename-note"></fluent-text-input>
        <fluent-button appearance="primary" type="submit">保存</fluent-button><fluent-button id="rename-cancel" type="button">キャンセル</fluent-button>
        <span id="rename-note" class="caption">保存先フォルダの名前は変わりません。これから書き出す動画のファイル名に使われます。</span>
      </form>
      ${lastJob ? `<fluent-anchor-button href="#/job/${esc(lastJob.id)}" appearance="subtle">前回の生成結果を見る</fluent-anchor-button>` : ''}</div>
    <div id="renameMsg"></div>
    <p class="caption">保存先: ${esc(d.project.directory)}</p>
    <div class="grid cols-steps">
      <section class="card step-col" aria-labelledby="h-src">
        <h2 id="h-src">1. 素材を選ぶ</h2>
        <div class="col-body">
        <div class="drop" id="drop">
          <p><b>動画ファイルをここにドラッグ＆ドロップ</b></p>
          <p class="caption">MP4 / MOV / MKV / WebM、10秒〜3時間。複数カメラの切り替え（P7）や画面解説（P10: PC の画面収録＋話し手のカメラ）では、同じ場面を撮った動画を複数追加します。開始時刻のずれは音声から自動で合わせます。</p>
          <input type="file" id="file" accept="video/*,.mkv" hidden>
          <fluent-button id="pick">ファイルを選ぶ</fluent-button>
          <div id="upload" class="stack" style="margin-top:12px"></div>
        </div>
        <details style="margin-top:12px"><summary class="caption">フルパスで指定する</summary>
          <form id="byPath" class="row" style="margin-top:8px"><fluent-text-input id="path" placeholder="C:\\Videos\\stream.mp4" style="flex:1" aria-label="動画ファイルのフルパス"></fluent-text-input><fluent-button type="submit">追加</fluent-button></form>
        </details>
        <div id="assetMsg"></div>
        ${assets.map(a => `
          <div class="asset">
            <div>
              <b>${esc(a.path.split(/[\\/]/).pop())}</b>
              <div class="caption">${fmt(a.media.duration)} · ${a.media.width}×${a.media.height} · ${a.media.fps.toFixed(2)}fps${a.media.vfr ? '（VFR）' : ''} · ${a.media.audio_channels ? '音声あり' : '音声なし'} · ${esc(a.media.video_codec)}</div>
              ${a.warnings.map(w => `<div class="caption warn">${esc(w)}</div>`).join('')}
              ${assets.length > 1 ? `<div class="row" style="margin:8px 0"><label class="caption">役割 <select class="native role" data-asset="${esc(a.id)}">${Object.entries(roleLabel).map(([k, v]) => `<option value="${k}" ${a.role === k ? 'selected' : ''}>${v}</option>`).join('')}</select></label>
                <label class="caption">話者 <input class="native spk" type="number" min="1" max="8" style="width:64px" data-asset="${esc(a.id)}" value="${a.speaker ?? ''}" placeholder="自動"></label>
                <label class="caption">ずれ(秒) <input class="native off" type="number" step="0.001" style="width:96px" data-asset="${esc(a.id)}" value="${a.sync_offset ?? 0}"></label>
                <span class="caption">${esc(syncText(a))}</span></div>` : ''}
              <div style="margin-top:8px">${field(esc(d.rights_statement), `<fluent-checkbox slot="input" class="rights" data-asset="${esc(a.id)}" ${rights.has(a.id) ? 'checked disabled' : ''}></fluent-checkbox>`)}</div>
            </div>
            <fluent-badge appearance="tint" color="${rights.has(a.id) ? 'success' : 'warning'}">${rights.has(a.id) ? '確認済み' : '要確認'}</fluent-badge>
          </div>`).join('')}
        </div>
      </section>
      <section class="card step-col" aria-labelledby="h-pat">
        <h2 id="h-pat">2. パターンを選ぶ</h2>
        <div class="grid col-body" id="presets" role="radiogroup" aria-label="編集パターン">
          ${presets.map(p => `
            <div class="card selectable preset ${p.available ? '' : 'disabled'} ${p.id === selected ? 'selected' : ''}" style="padding:16px">
              <div class="preset-radio" role="radio" aria-checked="${p.id === selected}" aria-disabled="${!p.available}" tabindex="-1" data-id="${esc(p.id)}">
              <div class="card-head"><span><span class="pid">${esc(p.id)}</span> <b>${esc(p.name)}</b></span>
                <span class="badges"><fluent-badge appearance="tint" color="informative">${esc(p.genre)}</fluent-badge>${p.available ? `<fluent-badge appearance="tint" color="brand">${p.target.duration_sec[0]}〜${p.target.duration_sec[1]}秒</fluent-badge>` : '<fluent-badge appearance="tint" color="subtle">準備中</fluent-badge>'}</span></div>
              ${p.card ? `<dl class="facts"><dt>冒頭</dt><dd>${esc(p.card.opening)}</dd><dt>テンポ</dt><dd>${esc(p.card.tempo)}</dd><dt>テロップ</dt><dd>${esc(p.card.telop)}</dd><dt>音</dt><dd>${esc(p.card.sound)}</dd></dl>` : ''}
              ${p.available ? '' : `<div class="caption">${esc(p.unavailable_reason)}</div>`}
              </div>
              <div class="caption">例: ${p.examples.map(x => `<a href="${esc(x.url)}" target="_blank" rel="noopener noreferrer">${esc(x.title)}</a>`).join(' / ')}</div>
            </div>`).join('')}
        </div>
      </section>
      <section class="card step-col" aria-labelledby="h-gen">
        <h2 id="h-gen">3. 生成する</h2>
        <div id="settingsSaveMsg" aria-live="polite"></div>
        <div class="col-body stack" style="gap:var(--s8)">
        ${group('series', 'シリーズに分ける（前編・中編・後編…）', `
          ${field('長尺を、前編・後編のように続けて見るシリーズに分ける', `<fluent-switch slot="input" id="seriesSw" ${opts.series ? 'checked' : ''}></fluent-switch>`)}
          <fieldset class="series-by" aria-describedby="seriesInfo"><legend class="caption">分け方</legend>
            <label class="caption"><input type="radio" name="seriesBy" value="parts" ${opts.series_by === 'parts' ? 'checked' : ''}> 本数で指定 <input class="native" type="number" id="partCount" min="2" max="20" step="1" value="${esc(opts.parts)}" style="width:64px" aria-label="本数"> 本</label>
            <label class="caption"><input type="radio" name="seriesBy" value="minutes" ${opts.series_by !== 'parts' ? 'checked' : ''}> 1本の長さで指定 <input class="native" type="number" id="partMin" min="1" max="60" step="0.5" value="${esc(opts.part_minutes)}" style="width:72px" aria-label="1本あたりの分数"> 分</label>
          </fieldset>
          <p class="caption" id="seriesInfo" aria-live="polite"></p>
          <details class="help" id="seriesHow"><summary class="caption">分け方のしくみ</summary>
            <p class="caption">分け目は時間ではなく話題の切れ目で決まるため、各パートの長さは前後します。各パートの冒頭に見どころ、2本目以降に前のパートのおさらい、最後に「〜につづく」と次の予告が入ります。AI には書き起こし全体を送ります。</p></details>`)}
        ${group('scope', '長い動画の絞り込み', `
          <div class="grid" id="scopes" role="radiogroup" aria-label="長尺の絞り込み（速さ・費用と見落としの少なさ）">
            ${(estimate?.scopes ?? [{ scope: 'economy' }, { scope: 'balanced' }, { scope: 'full' }]).map(s => `
              <div class="card selectable scope ${s.scope === opts.scope ? 'selected' : ''}" role="radio" aria-checked="${s.scope === opts.scope}" tabindex="-1" data-scope="${s.scope}">
                <b>${scopeLabel[s.scope][0]}</b>${s.cost ? `<span class="caption">AI に送る量 約${Math.round(s.share * 100)}%</span><fluent-badge appearance="tint" color="brand" class="scope-cost">約 ${jpy(s.cost)}</fluent-badge>` : ''}
                <span class="sr-only">${scopeLabel[s.scope][1]}</span>
              </div>`).join('')}
          </div>
          <p class="caption" id="scopeDesc" aria-hidden="true">${scopeLabel[opts.scope]?.[1] ?? ''}</p>
          <p class="caption">${estimate ? (long ? '30分を超える動画では、選んだ項目によって AI に送る量が変わります。' : 'この長さの動画は、どの項目でも全文を AI に送るため結果は同じです。') : '素材を追加すると、費用の目安が表示されます。'}</p>`, { id: 'scopeBox' })}
        ${group('sound', '音', `
          <label class="caption">BGM <select class="native" id="bgm">
            <option value="auto" ${opts.bgm === 'auto' ? 'selected' : ''}>パターンに任せる</option><option value="none" ${opts.bgm === 'none' ? 'selected' : ''}>なし</option>
            <option value="scene" ${opts.bgm === 'scene' ? 'selected' : ''}>場面に合わせて自動で選ぶ（${userBgm.length ? `登録した ${userBgm.length} 曲から` : '内蔵の 3 曲から'}）</option>
            ${bgmList.map(b => `<option value="${esc(b.id)}" ${opts.bgm === b.id ? 'selected' : ''}>全編この曲: ${esc(b.name)}${b.source === 'user' ? '（自分の曲）' : ''}${b.bpm ? ` ${Math.round(b.bpm)}BPM` : ''}</option>`).join('')}
          </select></label>
          <p class="caption">BGM は話している間だけ自動で小さくなります（声との差はパターンごとの目安で、標準は 14 LU 以上）。曲・音量は書き出し前の確認画面で場面ごとに直せます。</p>
          ${field('効果音（ポン・ピンポン・ドーン など）', `<fluent-switch slot="input" id="sfx" ${opts.sfx ? 'checked' : ''}></fluent-switch>`)}
          ${field('ツッコミを AI に提案させる（P3 のみ。承認するまで書き出せません）', `<fluent-switch slot="input" id="tsukkomi" ${opts.tsukkomi ? 'checked' : ''}></fluent-switch>`)}
          <div class="stack" id="tsukkomiStyleBox" style="margin-left:16px" ${opts.tsukkomi ? '' : 'hidden'}>
            <span class="caption" id="tsukkomiStyleLabel">ツッコミの見せ方</span>
            <fluent-radio-group id="tsukkomiStyle" orientation="vertical" aria-labelledby="tsukkomiStyleLabel" value="${esc(opts.tsukkomi_style)}" ${opts.tsukkomi ? '' : 'disabled'}>
              ${field('自動（おすすめ）: 発言の切れ目だけ停止カード、それ以外は重ねテロップ', '<fluent-radio slot="input" value="auto"></fluent-radio>')}
              ${field('ツッコミテロップ: 映像を止めずに文字を重ねる', '<fluent-radio slot="input" value="telop"></fluent-radio>')}
              ${field('カード: 映像を2〜3秒止めて、画像＋文字＋BGM', '<fluent-radio slot="input" value="card"></fluent-radio>')}
            </fluent-radio-group>
          </div>
          <label class="caption">伏せる語（読点・カンマ区切り。字幕・テロップでは ● になり、音声はピー音になります）<input type="text" class="native" id="maskWords" maxlength="400" style="display:block;width:100%;margin-top:4px" value="${esc(opts.mask_words.join('、'))}" placeholder="例: 店の名前、人の名前"></label>
          <details ${userBgm.length ? 'open' : ''}><summary class="caption">自分の BGM（${userBgm.length ? `${userBgm.length} 曲登録済み` : '複数まとめて追加できます'}）</summary>
            ${userBgm.length ? `<ul class="notes bgm-list">${userBgm.map(b => `<li><b>${esc(b.name)}</b> <span class="caption">${b.analysis ? `${Number(b.analysis.lufs).toFixed(1)} LUFS・${b.analysis.bpm ? `${Math.round(b.analysis.bpm)} BPM・` : ''}勢い ${energyWord(b.analysis.energy)}（${Number(b.analysis.energy).toFixed(2)}）・使う部分 ${fmt(b.analysis.start)}–${fmt(b.analysis.end)}` : '未測定（次の生成で測ります）'}</span>
              <fluent-button size="small" appearance="subtle" data-del-bgm="${esc(b.id)}" aria-label="${esc(b.name)} を外す">外す</fluent-button></li>`).join('')}</ul>` : ''}
            <form id="bgmForm" class="stack" style="margin-top:8px">
              <input type="file" id="bgmFile" accept="audio/*,.mp3,.wav,.m4a,.aac,.flac,.ogg" class="native" multiple aria-label="BGM ファイル（複数選べます）">
              ${field('この曲を使う権利（著作権・利用許諾）を自分が持っています', '<fluent-checkbox slot="input" id="bgmRights"></fluent-checkbox>')}
              <div><fluent-button type="submit" size="small">BGM を追加</fluent-button></div>
              <p class="caption">追加すると、曲ごとに音量（ラウドネス）・テンポ・音の立ち上がり・明るさを測り、場面の勢いに合う曲を選ぶのに使います。</p>
              <div id="bgmMsg" aria-live="polite"></div>
            </form></details>`)}
        ${group('pack', 'オープニング・クロージング・場面転換', `
          ${(() => {
            const sign = (v, d) => v == null || Number.isNaN(v) ? '—' : `${v >= 0 ? '+' : ''}${Number(v).toFixed(d)}`;
            const enh = (x) => {
              const e = x?.enhancement; if (!e) return '';
              const g = e.gain ? `この動画での実測: VMAF ${sign(e.gain.vmaf, 1)}・XPSNR ${sign(e.gain.xpsnr, 2)} dB` : '';
              return e.path ? `<br><span class="caption pass">→ ${e.width}×${e.height} に高画質化済み（${esc(e.device)}・${Math.round(e.seconds)}秒。${esc(g)}）</span>` : `<br><span class="caption">→ ${esc(e.note)}（${esc(g)}）</span>`;
            };
            const f = (x) => x ? `${esc(x.path.split(/[\\/]/).pop())}（${x.width}×${x.height}・${Number(x.duration).toFixed(1)}秒${x.upscale > 1.05 && !x.enhancement?.path ? `・${x.upscale}倍に拡大` : ''}）${enh(x)}` : '未登録';
            const pending = [kitView.opening, kitView.closing].filter(x => x?.needs && !x.enhancement?.path);
            return `<p class="caption" id="kitInfo">オープニング: ${f(kitView.opening)}<br>クロージング: ${f(kitView.closing)}<br>カード画像: ${kitView.card_images ? `${kitView.card_images} 枚` : 'なし（ツッコミの文字カード）'}</p>
              <div id="enhStatus" aria-live="polite"></div>
              ${pending.length && kitView.enhance?.state !== 'running' && !pending.every(x => x.enhancement) ? `<p class="caption warnc">1080×1920 より小さい動画は書き出し時に拡大されて少しぼやけます。<fluent-button size="small" id="enhanceBtn">いま高画質化する</fluent-button></p>` : ''}`;
          })()}
          ${field('オープニングを入れる（先出しの見どころのあと）', `<fluent-switch slot="input" id="openingSw" ${opts.opening ? 'checked' : ''} ${kitView.opening ? '' : 'disabled'}></fluent-switch>`)}
          ${field('クロージングを入れる（最後）', `<fluent-switch slot="input" id="closingSw" ${opts.closing ? 'checked' : ''} ${kitView.closing ? '' : 'disabled'}></fluent-switch>`)}
          ${field('場面が変わる所に2〜3秒の「ツッコミ」カードを入れる（画像＋文字＋BGM。AI の文は承認が必要）', `<fluent-switch slot="input" id="cardsSw" ${opts.scene_cards ? 'checked' : ''}></fluent-switch>`)}
          <details><summary class="caption">${kitView.registered ? '登録し直す・解除する' : 'オープニング・クロージングを登録する'}</summary>
            <form id="kitForm" class="stack" style="margin-top:8px">
              <p class="caption">フォルダーのフルパスを指定すると、ファイル名に Opening / Closing を含む動画と、PNG / JPG 画像（カード用）を読み込みます。縦型（9:16）と横型（16:9）の両方があれば、出力に合う方を自動で使います。登録はすべてのプロジェクトで共通です。</p>
              <fluent-text-input id="kitPath" placeholder="C:\\Work\\HubRadio\\OpeningClosing" value="${esc(kitView.kit?.sources?.[0] ?? '')}" aria-label="オープニング・クロージングのフォルダー"></fluent-text-input>
              <fluent-button id="kitBrowse" type="button" size="small">フォルダーを選ぶ…</fluent-button>
              <label class="caption">カードの長さ（秒） <input class="native" type="number" id="kitCardSec" min="2" max="3" step="0.1" value="${esc(Math.max(2, Math.min(3, kitView.kit?.card_sec ?? 2.5)))}" style="width:72px"></label>
              ${field('1080×1920 より小さい動画を高画質化する（登録時に一度だけ。AI の超解像で細部を補い、この動画で実測して元に忠実になる場合だけ使います。GPU で 1 本 1〜2 分）', `<fluent-checkbox slot="input" id="kitUpscale" ${kitView.kit?.upscale === false ? '' : 'checked'}></fluent-checkbox>`)}
              ${field('これらの動画・画像（ロゴ・音楽・イラストを含む）を使う権利を自分が持っている、または許諾を得ています', '<fluent-checkbox slot="input" id="kitRights"></fluent-checkbox>')}
              <div class="row"><fluent-button type="submit" size="small">登録</fluent-button>${kitView.registered ? '<fluent-button size="small" id="kitClear" type="button">登録を解除</fluent-button>' : ''}</div><div id="kitMsg"></div>
            </form></details>
          <div class="stack" style="margin-top:4px">
            <b id="cardMakeLabel">動画から場面転換の画像を作る</b>
            <details class="help"><summary class="caption">どんな画像ができるか</summary>
            <p class="caption">登録したカード画像がない場合に使います。元動画かオープニング・クロージングから、映像の変化・明るさ・時間分布で代表画像を選び、黒画面や似た画像を除きます。AI に動画を送らず、このプロジェクトだけに保存します。意味上の見どころを保証するものではありません。作成後に候補を生成すると反映されます。</p></details>
            ${(() => {
              if (kitView.card_images) return '<p class="caption">登録済みのカード画像があるため、その画像を使用します。</p>';
              const made = d.project.scene_card_images ?? [];
              const make = d.project.make_scene_card_images ?? made.length > 0;
              const name = (p) => esc(p.split(/[\\/]/).pop());
              const bumpers = (kitView.kit?.files ?? []).filter(f => f.kind === 'opening' || f.kind === 'closing');
              const videos = assets.filter(a => a.media.duration > 0);
              const at = (t) => t < 60 ? `${t.toFixed(1)}秒` : fmt(t);
              const origin = (img) => (img.brand_path ?? assets.find(a => a.id === img.asset_id)?.path ?? '元動画').split(/[\\/]/).pop();
              return `<fluent-radio-group id="cardMake" orientation="horizontal" aria-labelledby="cardMakeLabel" value="${make ? 'make' : 'skip'}">
                  ${field('作る', '<fluent-radio slot="input" value="make"></fluent-radio>')}
                  ${field('作らない', '<fluent-radio slot="input" value="skip"></fluent-radio>')}
                </fluent-radio-group>
                <p class="caption" id="cardSkipNote" ${make ? 'hidden' : ''}>${made.length ? `作成済みの ${made.length} 枚は消さずに残し、候補には使いません。` : ''}場面転換は文字カード（ツッコミ）になります。</p>
                <div class="stack" id="cardMakeBody" ${make ? '' : 'hidden'}>
                ${made.length ? `<div class="row" style="flex-wrap:wrap">${made.map((img, i) => `<figure style="margin:0;width:120px"><img src="/api/projects/${encodeURIComponent(id)}/scene-cards/${i}" alt="場面転換の候補 ${i + 1}、元動画の ${esc(at(img.source_t))}" style="width:120px;height:90px;object-fit:contain"><figcaption class="caption">${esc(origin(img))} · ${esc(at(img.source_t))}</figcaption></figure>`).join('')}</div><p class="caption">${made.length} 枚を作成済み。場面転換で順番に使います。既存の画像は上書きしません。</p>` : `
                  <label class="caption">画像を取り出す元動画
                    <select class="native" id="cardSource" aria-label="カード画像の元動画">
                      ${videos.length ? `<optgroup label="このプロジェクトの元動画">${videos.map(a => `<option value="asset:${esc(a.id)}">${name(a.path)}${rights.has(a.id) ? '' : '（権利未確認）'}</option>`).join('')}</optgroup>` : ''}
                      ${bumpers.length ? `<optgroup label="登録済みのオープニング・クロージング">${bumpers.map(f => `<option value="brand:${esc(f.path)}">${f.kind === 'opening' ? 'オープニング' : 'クロージング'}: ${name(f.path)}（${f.width}×${f.height}・${Number(f.duration).toFixed(1)}秒）</option>`).join('')}</optgroup>` : ''}
                    </select>
                  </label>
                  <label class="caption">画像の枚数（最大） <input class="native" type="number" id="cardCount" min="2" max="6" step="1" value="4" style="width:72px"></label>
                  <fluent-button id="makeCards" size="small" ${videos.length + bumpers.length ? '' : 'disabled'}>動画からカード画像を作る</fluent-button>
                  ${videos.length + bumpers.length ? '' : '<p class="caption">先に元動画を追加するか、オープニング・クロージングを登録してください。</p>'}
                `}
                </div>`;
            })()}
            <div id="cardImageMsg" aria-live="polite"></div>
          </div>`, { open: false })}
        ${group('analysis', '前回の解析結果（他のパターンで作ったもの）', `
          <fluent-radio-group id="analysisReuse" orientation="vertical" aria-labelledby="analysisLabel" aria-describedby="analysisInfo" value="reuse">
            ${field('流用する（おすすめ・速い）', '<fluent-radio slot="input" value="reuse"></fluent-radio>')}
            ${field('流用せず、最初の解析からやり直す（アプリを更新したときなど）', '<fluent-radio slot="input" value="redo"></fluent-radio>')}
          </fluent-radio-group>
          <p class="caption" id="analysisInfo" aria-live="polite"></p>`, { id: 'analysisBox', titleId: 'analysisLabel' })}
        </div>
        <div class="col-foot">
          <div id="transmission">${transmissionCompact(settings)}</div>
          ${estimate ? `<details class="help" id="costInfo"><summary class="caption">費用の内訳</summary><p class="caption" id="costLine"></p></details>` : ''}
          <div id="genMsg"></div>
          <div class="row">
            <fluent-button appearance="primary" id="gen" ${notReady ? 'aria-describedby="genHint"' : ''}>候補を3本つくる</fluent-button>
            ${estimate ? '<span class="caption" id="costShort"></span>' : ''}
            ${notReady ? `<span class="caption warnc" id="genHint">${notReady}</span>` : ''}
          </div>
        </div>
      </section>
    </div>
    </div>
    <section class="card" id="patterns" aria-labelledby="patterns-h" style="margin-top:16px">
      <h2 id="patterns-h">作ったパターン <span class="caption">（<span id="patternCount">0</span>件）</span></h2>
      <p class="caption">上の設定を変えて「候補をつくる」を押すたびに、新しいパターンが増えます。前に作ったパターンは、ここに残ります。</p>
      <div id="patternList"></div>
      <div id="patternMsg" role="status" aria-live="polite"></div>
    </section>
    <section class="card" aria-labelledby="proper-nouns-heading" style="margin-top:16px">
      <details id="properNounsDetails">
        <summary id="proper-nouns-heading"><b>字幕・テロップの固有名詞</b> <span class="caption">（<span id="properNounCount">${d.project.proper_nouns?.length ?? 0}</span>件）</span></summary>
        <p class="caption" style="margin-top:8px">人名・地名・会社名などの表示表記、読み、音声認識で出やすい別表記を登録できます。表記・読み・別表記は次回以降の書き起こしで認識ヒントとして使い、表示表記はテロップの語句照合にも使います。既存の書き起こしや時刻は変更しません。音声認識結果は確認画面で確認・修正してください。</p>
        <div id="properNounRows" class="stack"></div>
        <div class="row" style="margin-top:8px"><fluent-button id="addProperNoun" type="button" size="small">固有名詞を追加</fluent-button><fluent-button id="saveProperNouns" type="button" appearance="primary" size="small">保存</fluent-button></div>
        <div id="properNounsMsg" role="status" aria-live="polite"></div>
      </details>
    </section>`;
  document.body.classList.add('fit');
  loadPatterns(id, presets);

  let properNouns = (d.project.proper_nouns ?? []).map(n => ({ ...n, variants: n.variants ?? [] }));
  const properNounTypes = { person: '人名', place: '地名', organization: '企業・団体名', product: '商品・作品名', other: 'その他' };
  const renderProperNouns = () => {
    $('#properNounCount').textContent = String(properNouns.length);
    $('#properNounRows').innerHTML = properNouns.length ? properNouns.map((n, i) => `
      <fieldset class="proper-noun-row">
        <legend class="caption">固有名詞 ${i + 1}</legend>
        <div class="row">
          <label class="caption">種類 <select class="native" data-proper-index="${i}" data-proper-field="type">${Object.entries(properNounTypes).map(([v, label]) => `<option value="${v}" ${n.type === v ? 'selected' : ''}>${label}</option>`).join('')}</select></label>
          <label class="caption">字幕・テロップの表記 <input class="native" data-proper-index="${i}" data-proper-field="display_text" maxlength="100" value="${esc(n.display_text)}" placeholder="例: 東京太郎"></label>
          <label class="caption">読み（任意） <input class="native" data-proper-index="${i}" data-proper-field="reading" maxlength="100" value="${esc(n.reading)}" placeholder="例: とうきょうたろう"></label>
          <label class="caption">音声認識の別表記（任意・読点区切り） <input class="native proper-variants" data-proper-index="${i}" data-proper-field="variants" maxlength="1000" value="${esc(n.variants.join('、'))}" placeholder="例: 東京たろう、とうきょう太郎"></label>
          <fluent-button type="button" size="small" appearance="subtle" class="removeProperNoun" data-proper-index="${i}" aria-label="固有名詞 ${i + 1} を削除">削除</fluent-button>
        </div>
      </fieldset>`).join('') : '<p class="caption">まだ登録されていません。</p>';
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
      msg.innerHTML = info('固有名詞を保存しました。ASRのヒントは次回の候補生成時の書き起こしから反映されます。', 'success');
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
  const saveOpts = (delay = 0) => {
    updateSums();
    const revision = ++saveRevision;
    const settings = { ...opts, preset_id: selected };
    const status = $('#settingsSaveMsg');
    status.textContent = '設定を保存しています…';
    const save = async () => {
      if (delay) await new Promise(resolve => setTimeout(resolve, delay));
      try {
        await api(`/api/projects/${id}/generation-settings`, { method: 'PUT', body: settings });
        if (revision === saveRevision) status.textContent = '設定を保存しました';
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
    set('series', !opts.series ? 'オフ（ショート候補を3本）' : opts.series_by === 'parts' ? `オン・${opts.parts}本に分ける` : `オン・1本 約${opts.part_minutes}分で分ける`);
    set('scope', opts.series ? 'シリーズでは全文を送ります' : `${scopeLabel[opts.scope]?.[0] ?? ''}${$('#scopes .selected .scope-cost')?.textContent ? `・${$('#scopes .selected .scope-cost').textContent}` : ''}`);
    const bgm = $('#bgm'), bgmName = opts.bgm === 'auto' ? '自動' : opts.bgm === 'none' ? 'なし' : opts.bgm === 'scene' ? '場面ごとに自動' : bgm?.selectedOptions[0]?.textContent.trim() ?? '指定';
    set('sound', [`BGM ${bgmName}`, `効果音 ${opts.sfx ? 'オン' : 'オフ'}`, opts.tsukkomi ? `ツッコミ ${{ telop: 'テロップ', card: 'カード' }[opts.tsukkomi_style] ?? '自動'}` : 'ツッコミ オフ', ...(opts.mask_words.length ? [`伏せ字 ${opts.mask_words.length}語`] : [])].join('・'));
    set('pack', [`オープニング ${!kitView.opening ? '未登録' : opts.opening ? 'あり' : 'なし'}`, `クロージング ${!kitView.closing ? '未登録' : opts.closing ? 'あり' : 'なし'}`, `場面転換カード ${opts.scene_cards ? 'あり' : 'なし'}`].join('・'));
    set('analysis', reuseAnalysis ? '流用する' : '最初からやり直す');
  };
  $$('.setgroup').forEach(g => g.addEventListener('toggle', () => {
    groupState[g.dataset.group] = g.open;
    localStorage.setItem('vc.genGroups', JSON.stringify(groupState));
  }));
  const scopeCost = (list) => (list.find(s => s.scope === (opts.series ? 'full' : opts.scope)) ?? list[1]).cost;
  const showAnalysis = () => {
    const el = $('#analysisInfo'); if (!el) return;
    const a = estimate?.analysis;
    if (!a?.cached) { el.textContent = '素材をまだ解析していないため、どちらを選んでも最初から解析します。'; return; }
    const when = new Date(a.updated_at).toLocaleString('ja-JP');
    const extra = scopeCost(a.redo_scopes).estimated_jpy - scopeCost(estimate.scopes).estimated_jpy;
    el.textContent = reuseAnalysis
      ? `この素材は ${when} に解析済みです。解析用プロキシ・書き起こし・ショット/音量/無音・笑い・顔・画面の判定・カメラの自動同期をそのまま使います。AI のハイライト選定・編集計画・プレビューは毎回行います。`
      : `${when} の解析結果を使わず、すべて作り直します（時間がかかります${extra > 0.05 ? `。書き起こしの費用 約 ¥${extra.toFixed(1)} が増えます` : ''}）。${a.transcript_cached ? '確認画面で直した書き起こしは、新しい書き起こしで置き換わります。' : ''}手入力した「ずれ(秒)」と「話者」はそのまま使います。`;
  };
  const showCost = () => {
    showAnalysis(); updateSums();
    const el = $('#costLine'); if (!el || !estimate) return;
    const list = reuseAnalysis || !estimate.analysis ? estimate.scopes : estimate.analysis.redo_scopes;
    $$('#scopes [data-scope]').forEach(card => { const b = $('.scope-cost', card), sc = list.find(s => s.scope === card.dataset.scope); if (b && sc) b.textContent = `約 ${jpy(sc.cost)}`; });
    const c = scopeCost(list);
    $('#costShort').textContent = `推定コスト 約 ${jpy(c)}`;
    el.textContent = `推定コスト: 約 ${jpy(c)}（書き起こし ${c.asr_minutes} 分${reuseAnalysis && estimate.analysis?.transcript_cached ? '・前回の結果を流用' : ''}、AI 入力 約 ${c.llm_input_tokens.toLocaleString()} トークン）— ${c.basis}`;
    updateSums();
  };
  showCost();
  $('#analysisReuse').addEventListener('change', () => {
    reuseAnalysis = $$('#analysisReuse fluent-radio').find(r => r.checked)?.value !== 'redo';
    showCost();
  });
  const seriesLabels = (n) => n === 2 ? ['前編', '後編'] : n === 3 ? ['前編', '中編', '後編'] : Array.from({ length: n }, (_, i) => `第${i + 1}話`);
  const showSeries = () => {
    const on = opts.series, byParts = opts.series_by === 'parts', min = Number(opts.part_minutes), parts = Number(opts.parts);
    $('#partMin').disabled = !on || byParts; $('#partCount').disabled = !on || !byParts;
    $$('input[name=seriesBy]').forEach(r => { r.disabled = !on; });
    $('#scopeBox').classList.toggle('muted-box', on); $('#scopeBox').inert = on; $('#scopeBox > .setgroup-body').hidden = on;
    $('#seriesHow').hidden = !on; updateSums();
    const el = $('#seriesInfo');
    if (!on) { $('#gen').textContent = '候補を3本つくる'; el.textContent = 'オフのときは、見どころを切り出したショート候補を3本つくります。'; return; }
    $('#gen').textContent = byParts && parts >= 2 ? `${seriesLabels(parts).length}本（${seriesLabels(parts).slice(0, 3).join('・')}${parts > 3 ? '…' : ''}）をつくる` : '前編・後編をつくる';
    if (!estimate) { el.textContent = '素材を追加すると、1本あたりの長さの目安が表示されます。'; return; }
    // the length after jump cuts is known once the video has been transcribed (any earlier generation); otherwise the raw length is used
    const se = estimate.series ?? {}, edited = se.edited_sec, base = edited ?? estimate.duration;
    const basis = edited != null ? `無音カット後の推定 ${fmt(edited)}` : `元の長さ ${fmt(estimate.duration)}（無音のカットで短くなります）`;
    const max = se.max_parts ?? 0;
    $('#partCount').max = String(Math.max(2, max));
    if (byParts) {
      if (!(Number.isInteger(parts) && parts >= 2 && parts <= 20)) { el.textContent = '本数は 2〜20 本の整数で指定してください。'; return; }
      if (max < 2) { el.textContent = `この動画（${basis}）は短いため分けられません（1本あたり1分以上が必要です）。`; return; }
      if (parts > max) { el.textContent = `${parts}本では1本あたり1分未満になります。この動画（${basis}）は最大 ${max} 本まで分けられます。`; return; }
      el.textContent = `${parts}本（${seriesLabels(parts).join('・')}）、1本あたり 約${fmt(base / parts)}（${basis}を${parts}等分した目安）。`;
      return;
    }
    if (!(min >= 1 && min <= 60)) { el.textContent = '1本あたりの長さは 1〜60 分で指定してください。'; return; }
    const n = Math.round(base / (min * 60));
    el.textContent = n < 2
      ? `この動画（${basis}）は ${min} 分の2本分に足りません。1本あたりの長さを短くするか、本数で指定してください。`
      : `約 ${n} 本（${seriesLabels(n).join('・')}）、1本あたり 約${fmt(base / n)}（${basis}から）。`;
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
    msg.innerHTML = info('開いた「フォルダーの選択」画面でフォルダーを選んでください（画面が見えないときはタスク バーを確認してください）');
    try {
      const r = await api('/api/dialogs/folder', { method: 'POST', body: { initial_path: input.value ?? '' }, signal: cardRequests.signal });
      if (r.path != null) input.value = r.path;
      msg.innerHTML = r.path == null ? info('選択をキャンセルしました。入力は変更していません') : info('フォルダーを選択しました。権利を確認して「登録」を押してください');
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
      $('#cardImageMsg').innerHTML = info(make ? '作る: 動画から作った画像を場面転換で使います' : '作らない: 次に候補を生成するときから、場面転換は文字カードになります');
    } catch (e) {
      if (e.name === 'AbortError') return;
      group.value = saved ? 'make' : 'skip';
      $('#cardImageMsg').innerHTML = errorBar(e);
    } finally { radios.forEach(r => { r.disabled = false; }); }
  });
  $('#makeCards')?.addEventListener('click', async () => {
    const button = $('#makeCards'), msg = $('#cardImageMsg'), [kind, ...rest] = $('#cardSource').value.split(':'), ref = rest.join(':');
    if (kind === 'asset' && !rights.has(ref)) { msg.innerHTML = info('元動画の権利確認にチェックしてください', 'warning'); return; }
    const count = Number($('#cardCount').value);
    if (!Number.isInteger(count) || count < 2 || count > 6) { msg.innerHTML = info('画像の枚数は 2〜6 枚の整数で指定してください', 'warning'); return; }
    button.disabled = true; $('#gen').disabled = true; $$('#cardMake fluent-radio').forEach(r => { r.disabled = true; });
    msg.innerHTML = `<fluent-spinner size="small"></fluent-spinner> ${kind === 'brand' ? 'オープニング・クロージング' : '元動画'}から代表画像を抽出しています…`;
    try {
      const r = await api(`/api/projects/${id}/scene-cards`, { method: 'POST', body: kind === 'brand' ? { brand_path: ref, count } : { asset_id: ref, count }, signal: cardRequests.signal });
      opts.scene_cards = true; saveOpts();
      sessionStorage.setItem(`cardImageMessage:${id}`, r.message);
      router();
    } catch (e) { if (e.name !== 'AbortError') msg.innerHTML = errorBar(e); }
    finally { button.disabled = false; const gen = $('#gen'); if (gen) gen.disabled = false; $$('#cardMake fluent-radio').forEach(r => { r.disabled = false; }); }
  });
  { const message = sessionStorage.getItem(`cardImageMessage:${id}`); sessionStorage.removeItem(`cardImageMessage:${id}`); if (message) $('#cardImageMsg').innerHTML = info(message, 'success'); }
  $('#kitForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    if (!$('#kitRights').checked) { $('#kitMsg').innerHTML = info('使う権利の確認にチェックしてください', 'warning'); $('#kitRights').focus(); return; }
    $('#kitMsg').innerHTML = '<fluent-spinner size="small"></fluent-spinner> 読み込んで音量を測っています…';
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
  { const probs = JSON.parse(sessionStorage.getItem('kitProblems') ?? '[]'); sessionStorage.removeItem('kitProblems'); if (probs.length) { $('#kitForm').closest('details').open = true; $('#kitMsg').innerHTML = probs.map(p => info(p, 'warning')).join(''); } }
  $('#enhanceBtn')?.addEventListener('click', async () => { try { await api('/api/brandkit/enhance', { method: 'POST' }); router(); } catch (e) { $('#kitMsg').innerHTML = errorBar(e); } });
  // registration-time upscale runs in the background: show its progress, then redraw with the measured result
  const showEnh = (s) => {
    const el = $('#enhStatus'); if (!el || !s) return;
    if (s.state === 'running') el.innerHTML = `<p class="caption">高画質化しています: ${esc(s.message)}</p><fluent-progress-bar max="100" value="${Math.round((s.progress ?? 0) * 100)}" aria-label="高画質化の進み具合"></fluent-progress-bar>`;
    else if (s.state === 'failed') el.innerHTML = info(s.message, 'warning');
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
    if (!files.length) { $('#bgmMsg').innerHTML = info('音声ファイルを選んでください', 'warning'); return; }
    const fd = new FormData(); files.forEach(f => fd.append('file', f)); fd.append('rights_confirmed', $('#bgmRights').checked ? 'true' : 'false');
    $('#bgmMsg').innerHTML = info(`${files.length} 曲を追加して、音量と曲の勢いを測っています…`);
    try {
      const r = await fetch(`/api/projects/${id}/bgm`, { method: 'POST', body: fd, headers: { 'X-VideoCreator': '1' } });
      const data = await r.json(); if (!r.ok) throw Object.assign(new Error(data?.error?.message ?? r.statusText), { code: data?.error?.code });
      // registering tracks means choosing per scene from them
      opts.bgm = 'scene'; await saveOpts();
      sessionStorage.setItem('vc.bgmFlash', `${data.tracks.length} 曲を追加しました（BGM は「場面に合わせて自動で選ぶ」にしました）${data.skipped.length ? `。追加できなかった曲: ${data.skipped.map(s => `${s.name}（${s.reason}）`).join('、')}` : ''}`);
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
    $('#upload').innerHTML = `<span class="caption">${esc(file.name)} を読み込み中…</span><fluent-progress-bar id="up" max="100" value="0"></fluent-progress-bar>`;
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) $('#up')?.setAttribute('value', String(Math.round(e.loaded / e.total * 100))); };
    xhr.onload = () => { const data = xhr.responseText ? JSON.parse(xhr.responseText) : {}; xhr.status < 300 ? resolve(data) : reject(Object.assign(new Error(data?.error?.message ?? xhr.statusText), { code: data?.error?.code })); };
    xhr.onerror = () => reject(new Error('アップロードに失敗しました'));
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
    if (assets.length) api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`).then(e => { estimate = e; showSeries(); }).catch(() => { });
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
const JOB_STATE = { ready: '完了', done: '完了', failed: '失敗', cancelled: '中止' };
function patternSummary(j, presets) {
  const o = j.options ?? {};
  const scope = { economy: '節約', balanced: '標準', full: '全文' }[o.scope] ?? o.scope;
  const bits = [`絞り込み: ${scope}`];
  if (o.parts) bits.push(`${o.parts}本に分割`); else if (o.part_minutes) bits.push(`約${o.part_minutes}分ずつに分割`);
  bits.push(`BGM: ${o.bgm === 'auto' ? '自動' : o.bgm === 'none' ? 'なし' : '指定'}`);
  if (o.opening) bits.push('オープニング'); if (o.closing) bits.push('クロージング');
  if (o.scene_cards) bits.push('場面転換カード'); if (o.tsukkomi) bits.push(`ツッコミ（${{ telop: 'テロップ', card: 'カード' }[o.tsukkomi_style] ?? '自動'}）`);
  if (o.mask_words?.length) bits.push(`伏せ字 ${o.mask_words.length} 語`);
  if (j.instruction) bits.push(`追加指示: ${j.instruction}`);
  if (o.reuse_analysis === false) bits.push('解析を最初からやり直し');
  return bits.join(' · ');
}
async function loadPatterns(projectId, presets) {
  const box = $('#patternList'); if (!box) return;
  const items = await api(`/api/projects/${projectId}/patterns`);
  if (!$('#patternList')) return;   // navigated away meanwhile
  $('#patternCount').textContent = String(items.length);
  const ids = new Set(items.map(x => x.job.id));
  box.innerHTML = items.length ? items.map(x => {
    const j = x.job, finished = j.status in JOB_STATE;
    const state = JOB_STATE[j.status] ?? '生成中';
    const parent = j.derived_from_job_id && ids.has(j.derived_from_job_id) ? `<span class="caption">（${esc(items.find(y => y.job.id === j.derived_from_job_id).preset_names.join('・'))} のパターンから）</span>` : '';
    return `<div class="pattern" data-job="${esc(j.id)}">
      <div>
        <div><b>${esc(x.preset_names.join('・'))}</b> <fluent-badge appearance="tint" color="${j.status === 'failed' ? 'danger' : finished ? 'success' : 'informative'}">${state}</fluent-badge>
          <span class="caption">${esc(new Date(j.created_at).toLocaleString('ja-JP'))}</span> ${parent}</div>
        <div class="caption">${esc(patternSummary(j, presets))}</div>
        <div class="caption">候補 ${x.candidates} 件 · 承認 ${x.approved} 件 · 書き出し済み ${x.exported} 件</div>
      </div>
      <div class="row">
        <fluent-anchor-button size="small" href="#/job/${esc(j.id)}">${finished ? '候補を見る' : '進み具合を見る'}</fluent-anchor-button>
        <fluent-button size="small" class="reuse" data-job="${esc(j.id)}" ${finished ? '' : 'disabled'} aria-label="${esc(x.preset_names.join('・'))}の設定を引き継いで別のパターンを作る">この設定から作り直す</fluent-button>
        <fluent-button size="small" appearance="subtle" class="rm" data-job="${esc(j.id)}" ${x.busy ? 'disabled' : ''} aria-label="${esc(x.preset_names.join('・'))}のパターンを削除">削除</fluent-button>
      </div>
      <div class="rm-confirm" hidden style="grid-column:1/-1"></div>
    </div>`;
  }).join('') : '<p class="caption">まだありません。上で設定して「候補をつくる」を押してください。</p>';
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
      $('#genMsg').innerHTML = info('このパターンの設定を読み込みました。変えたい所を変えて「候補をつくる」を押すと、別のパターンとして増えます（今のパターンは残ります）。', 'success');
    } catch (e) { $('#patternMsg').innerHTML = errorBar(e); }
  }));
  $$('#patternList .rm').forEach(b => b.addEventListener('click', () => {
    const row = b.closest('.pattern'), box = $('.rm-confirm', row), x = items.find(y => y.job.id === b.dataset.job);
    box.hidden = false;
    box.innerHTML = `<fluent-message-bar intent="warning" layout="multiline">「${esc(x.preset_names.join('・'))}」のパターンを削除します。このパターンの候補・編集内容・書き出した動画は<b>削除</b>され（動画ファイルはごみ箱へ）、元に戻せません。ほかのパターンと元の素材は残ります。</fluent-message-bar>
      <div class="row"><fluent-button size="small" appearance="primary" class="rm-ok">削除する</fluent-button><fluent-button size="small" class="rm-cancel">やめる</fluent-button></div>`;
    $('.rm-cancel', box).addEventListener('click', () => { box.hidden = true; box.innerHTML = ''; b.focus(); });
    $('.rm-ok', box).addEventListener('click', async () => {
      $('.rm-ok', box).disabled = true;
      try {
        await api(`/api/jobs/${encodeURIComponent(b.dataset.job)}`, { method: 'DELETE' });
        if (derivedFrom?.job === b.dataset.job) derivedFrom = null;
        await router();
        $('#patternMsg').innerHTML = info(`「${x.preset_names.join('・')}」のパターンを削除しました。`, 'success');
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
      <h1>生成しています</h1>
      <div class="grid cols-2" style="align-items:start">
        <section class="card">
          ${failed ? errorBar(failed) : `<p id="jobMsg" aria-live="polite">${esc(message)}</p>`}
          ${failed ? '' : `<div class="progress-row"><span class="caption">全体</span><fluent-progress-bar id="jobBar" max="100" value="0" aria-label="全体の進み具合"></fluent-progress-bar><span id="jobPct" class="caption pct">0%</span></div>
          <div id="stepBox" class="progress-row" hidden><span id="stepLabel" class="caption"></span><fluent-progress-bar id="stepBar" max="100" aria-label="実行中の処理の進み具合"></fluent-progress-bar><span id="stepPct" class="caption pct"></span></div>`}
          <p class="caption" style="margin-top:8px">経過 <span id="elapsed">0:00</span><span id="eta"></span>${j.cost ? ` · 推定コスト 約 ¥${j.cost.estimated_jpy.toFixed(1)}` : ''} · ${j.options?.reuse_analysis === false ? '解析を最初からやり直しています' : '前回の解析結果があれば流用します'}</p>
          <div id="stages">${STAGES.map(([k, label]) => `<div class="stage" data-stage="${k}"><span class="dot"></span><span>${label}</span><span class="caption"></span></div>`).join('')}</div>
          <div class="row" style="margin-top:12px">${failed ? `<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings">プロジェクトに戻る</fluent-anchor-button>` : '<fluent-button id="cancel">キャンセル</fluent-button>'}</div>
        </section>
        <section class="card"><h2>この間に行っていること</h2>
          <ul class="caption" style="line-height:20px">
            <li>Azure AI Speech で単語ごとの時刻付きで書き起こし、話者を分けます</li>
            <li>FFmpeg でショットの切り替わり・音量・無音を測ります</li>
            <li>AI が「冒頭の引き・単体で通じるか・オチ・感情」を採点し、音声の盛り上がりと合わせて順位を付けます</li>
            <li>文の切れ目に合わせて区間を確定し、間を詰め、字幕と強調テロップを付けます</li>
            <li>複数カメラは音声で時間を合わせ、話している人の映像に切り替えます。顔を検出して縦型に追いかけます</li>
            <li>BGM（話している間は自動で小さく）と効果音を付けます</li>
            <li>全候補を自動チェック（尺・解像度・字幕の安全領域・発話の途中切り・テロップの事実確認）します</li>
          </ul></section>
      </div>`;
    $('#cancel')?.addEventListener('click', async () => { try { await api(`/api/jobs/${jobId}/cancel`, { method: 'POST' }); } catch { } });
  };
  // update values in place: rebuilding the DOM on every SSE message would restart the bars' animation and reset screen readers
  let stepKey = null, stepT0 = 0;
  const update = (u) => {
    const idx = STAGES.findIndex(s => s[0] === u.stage);
    if ($('#jobMsg') && $('#jobMsg').textContent !== u.message) $('#jobMsg').textContent = u.message ?? '';
    const pct = Math.round((u.progress ?? 0) * 100);
    $('#jobBar')?.setAttribute('value', String(pct));
    if ($('#jobPct')) $('#jobPct').textContent = `${pct}%`;
    const step = u.step_progress;
    const box = $('#stepBox');
    if (box) {
      box.hidden = idx < 0;
      $('#stepLabel').textContent = u.step ?? (idx >= 0 ? STAGES[idx][1] : '');
      const bar = $('#stepBar');
      if (typeof step === 'number') { bar.setAttribute('value', String(Math.round(step * 100))); $('#stepPct').textContent = `${Math.round(step * 100)}%`; }
      else { bar.removeAttribute('value'); $('#stepPct').textContent = ''; }
    }
    // ETA only for a measured sub-task, from its own elapsed time
    const key = `${u.stage}|${(u.step ?? '').split(':')[0]}`;
    if (key !== stepKey) { stepKey = key; stepT0 = Date.now(); }
    const eta = $('#eta');
    if (eta) eta.textContent = typeof step === 'number' && step >= 0.05 && step < 1 && Date.now() - stepT0 > 3000 ? ` · この処理の残り 約 ${fmt((Date.now() - stepT0) / 1000 * (1 - step) / step)}` : '';
    $$('#stages .stage').forEach((el, i) => {
      const done = i < idx || u.stage === 'ready', active = i === idx;
      el.classList.toggle('done', done); el.classList.toggle('active', active);
      el.lastElementChild.textContent = done ? '完了' : active ? (typeof step === 'number' ? `実行中 ${Math.round(step * 100)}%` : '実行中') : '';
    });
  };
  let last = { stage: j.status, progress: j.progress, message: j.message };
  skeleton(null, j.message);
  update(last);
  const timer = setInterval(() => { const el = $('#elapsed'); if (el) el.textContent = fmt((Date.now() - started) / 1000); }, 1000);
  const es = new EventSource(`/api/jobs/${jobId}/events`);
  es.onmessage = async (ev) => {
    const u = JSON.parse(ev.data);
    if (u.stage === 'ready') { es.close(); clearInterval(timer); candidates(await api(`/api/jobs/${jobId}`)); return; }
    if (u.stage === 'failed' || u.stage === 'cancelled') {
      es.close(); clearInterval(timer);
      const cur = await api(`/api/jobs/${jobId}`);
      skeleton({ message: cur.message, code: cur.error_code ?? (u.stage === 'cancelled' ? 'CANCELLED' : '') });
      update({ ...last, step_progress: null });
      $$('#stages .stage.active').forEach(el => { el.lastElementChild.textContent = u.stage === 'cancelled' ? '中止' : '失敗'; });
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
  setSteps(3, 3, jobStepLinks(j, list));
  if (list.length && list[0].candidate.series) return seriesView(j, list);
  const bar = (label, v) => `<span>${label}</span><span class="bar"><span style="width:${v * 10}%"></span></span><span>${Number(v).toFixed(v % 1 ? 1 : 0)}</span>`;
  main.innerHTML = `
    <div class="row" style="justify-content:space-between"><h1>候補（${list.length}件）</h1>
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings" appearance="subtle">素材・パターンに戻る</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(w, 'warning')).join('')}
    <p class="caption">選定: ${esc(j.llm_model ?? '')} · プロンプト ${esc(j.prompt_version ?? '')} · 実績コスト 約 ¥${(j.cost?.estimated_jpy ?? 0).toFixed(2)} · 所要 ${Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)} 秒</p>
    ${workProgress(j, list)}
    <div class="grid cols-cands">
      ${list.map(({ candidate: c, preview_url, preview, preview_stale }) => `
        <article class="card stack" aria-label="候補 ${c.rank}">
          <div class="card-head"><b>#${c.rank}</b><span class="badges">${preview_stale ? '<fluent-badge appearance="tint" color="warning">プレビュー・QAは編集前の結果</fluent-badge>' : qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${(c.segments.reduce((a, s) => a + s.t1 - s.t0, 0)).toFixed(0)}秒 素材</fluent-badge>${c.source === 'fallback' ? '<fluent-badge appearance="tint" color="warning">信号ベース</fluent-badge>' : ''}</span></div>
          ${preview_url ? phone(preview_url) : '<p class="muted">プレビューなし</p>'}
          <div class="headline">${esc(c.headline || c.hook_text || '（見出しなし）')}</div>
          <div class="caption">${c.segments.map(s => `${fmt(s.t0)}–${fmt(s.t1)}`).join(' + ')}</div>
          <p>${esc(c.reason)}</p>
          <div class="scores">${bar('冒頭の引き', c.scores.hook)}${bar('単体で通じる', c.scores.standalone)}${bar('オチ', c.scores.payoff)}${bar('感情', c.scores.emotion)}${bar('音声の勢い', c.scores.signal)}${bar('総合', c.scores.final)}</div>
          ${c.notes?.length ? `<details><summary class="caption">調整の記録（${c.notes.length}）</summary><ul class="notes">${c.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button href="#/ep/${esc(c.edit_plan_id)}">この候補を確認・修正</fluent-anchor-button>
          ${approvalBlock(list.find(x => x.candidate.id === c.id), `候補 #${c.rank}`)}
        </article>`).join('')}
    </div>
    ${batchBox()}
    <section class="card" style="margin-top:16px">
      <h2>ほかの候補を出す</h2>
      <div class="chips" id="chips">${['もっと短く', '別の場面', 'もっと笑える所', 'オチがはっきりした所'].map(t => `<fluent-button size="small" data-t="${esc(t)}">${esc(t)}</fluent-button>`).join('')}</div>
      <div class="row" style="margin-top:8px"><fluent-text-input id="instr" maxlength="200" placeholder="追加の指示（任意）" style="flex:1" aria-label="追加の指示"></fluent-text-input><fluent-button id="regen">再生成</fluent-button></div>
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
    <div class="row" style="justify-content:space-between"><h1>シリーズ（${list.length}本）${first.series_title ? `: ${esc(first.series_title)}` : ''}</h1>
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}/settings" appearance="subtle">素材・パターンに戻る</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(w, 'warning')).join('')}
    <p class="caption">${first.requested_parts ? `${first.requested_parts}本に指定（1本あたり 約${(first.target_sec / 60).toFixed(1)}分）` : `1本あたり 約${(first.target_sec / 60).toFixed(1)}分の指定`} · 合計 ${fmt(total)} · 章分け: ${esc(j.llm_model && j.llm_model !== 'none' ? j.llm_model : '書き起こしの語彙の変化（AI なし）')} · 実績コスト 約 ¥${(j.cost?.estimated_jpy ?? 0).toFixed(2)} · 所要 ${Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)} 秒</p>
        ${workProgress(j, list)}
        <div class="grid cols-cands">
          ${list.map(({ candidate: c, preview_url, preview, preview_stale }) => {
            const se = c.series;
        return `
        <article class="card stack" aria-label="${esc(se.label)}">
          <div class="card-head"><b>${esc(se.label)}</b><span class="badges">${preview_stale ? '<fluent-badge appearance="tint" color="warning">プレビュー・QAは編集前の結果</fluent-badge>' : qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${preview ? fmt(preview.duration) : '—'}</fluent-badge></span></div>
          ${preview_url ? phone(preview_url) : '<p class="muted">プレビューなし</p>'}
          <div class="headline">${esc(c.hook_text)}</div>
          <div class="caption">元動画の ${fmt(se.body.t0)}–${fmt(se.body.t1)}（本編）</div>
          <p class="caption">${se.part < se.parts ? `分け目: ${esc(se.boundary)}` : '最後のパート'}</p>
          ${se.chapters?.length ? `<details open><summary class="caption">構成（${se.chapters.length}）</summary><ul class="notes">${se.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          ${c.notes?.length ? `<details><summary class="caption">編集の記録（${c.notes.length}）</summary><ul class="notes">${c.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button href="#/ep/${esc(c.edit_plan_id)}">${esc(se.label)}を確認・修正</fluent-anchor-button>
          ${approvalBlock(list.find(x => x.candidate.id === c.id), se.label)}
        </article>`;
      }).join('')}
    </div>
    ${batchBox()}
    <p class="caption" style="margin-top:16px">分け目を変えたいときは、プロジェクトに戻って本数か1本あたりの長さを変えて作り直してください。パートごとに承認し、承認したパートをまとめて書き出せます（投稿用メタデータにチャプターとシリーズのリンク欄が入ります）。</p>`;
  wireSafeToggle();
  wireApprovals(j, list);
}

// ---------------- approval & export of one or more candidates (candidates / series / export screens) ----------------
// One job can yield several videos: each is reviewed and approved on its own (step 5), then the approved ones are exported together (step 6).
function videoName(x) { return x.candidate.series?.label ?? `候補 #${x.candidate.rank}`; }
function videoStatus(x) {
  const e = x.export;
  if (e && (e.state === 'queued' || e.state === 'rendering')) return { key: 'exporting', label: '書き出し中', color: 'informative' };
  if (x.approval?.stale) return { key: 'stale', label: '要再承認', color: 'warning' };
  if (x.approval && x.final_render_id) return { key: 'exported', label: '書き出し済み', color: 'success' };
  if (x.approval) return { key: 'approved', label: '承認済み', color: 'brand' };
  return { key: 'todo', label: '未確認', color: 'informative' };
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
  return `<section class="card work-progress" id="workProgress" aria-label="確認と書き出しの進み具合">
    <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
      <div class="stack" style="gap:4px"><b>確認の進み具合: 承認 ${reviewed} / ${list.length} 本 · 書き出し済み ${exported} / ${list.length} 本</b>
        <span class="caption">1本ずつ「確認・修正」で内容を確かめて承認し、承認した動画を「書き出し」でまとめて書き出します。</span></div>
      <div class="row">${next ? `<fluent-anchor-button id="nextReview" href="#/ep/${esc(next.candidate.edit_plan_id)}">次の未承認（${esc(videoName(next))}）を確認</fluent-anchor-button>` : ''}
        ${onExports ? '' : `<fluent-anchor-button id="toExports" href="#/job/${esc(j.id)}/exports" ${reviewed ? 'appearance="primary"' : ''}>書き出しへ進む${reviewed ? `（承認 ${reviewed} 本）` : ''}</fluent-anchor-button>`}</div>
    </div>
    <ol class="video-status">${list.map(x => { const s = videoStatus(x); return `<li><a href="#/ep/${esc(x.candidate.edit_plan_id)}">${esc(videoName(x))}</a> <fluent-badge appearance="tint" color="${s.color}">${s.label}</fluent-badge></li>`; }).join('')}</ol>
  </section>`;
}
function approvalBadge(a) {
  if (!a) return '<fluent-badge appearance="tint" color="informative">未承認</fluent-badge>';
  if (a.stale) return '<fluent-badge appearance="tint" color="warning">承認後に修正あり（再承認が必要）</fluent-badge>';
  return `<fluent-badge appearance="tint" color="success">承認済み</fluent-badge><span class="caption">${esc(new Date(a.at).toLocaleString('ja-JP', { dateStyle: 'short', timeStyle: 'short' }))}${a.by ? ` · ${esc(a.by)}` : ''}</span>`;
}
function approvalBlock(x, name) {
  const id = esc(x.candidate.id);
  return `<div class="stack approval" data-cand="${id}" data-name="${esc(name)}">
    <div class="row" style="align-items:center;gap:8px;flex-wrap:wrap"><span class="row appr-badge" style="gap:8px;align-items:center">${approvalBadge(x.approval)}</span></div>
    <div class="row" style="align-items:center;gap:8px;flex-wrap:wrap">
      <fluent-button size="small" class="appr-btn"></fluent-button>
      ${field('書き出す', `<fluent-checkbox slot="input" class="exp-pick" aria-label="${esc(name)}を書き出す"></fluent-checkbox>`)}
    </div>
    <div class="appr-msg" aria-live="polite"></div>
    <div class="progress-row exp-row" hidden><span class="caption exp-label"></span><fluent-progress-bar max="100" aria-label="${esc(name)}の書き出しの進み具合"></fluent-progress-bar><span class="caption pct exp-pct"></span></div>
    <p class="caption exp-text" aria-live="polite"></p>
  </div>`;
}
function batchBox(showHeading = true) {
  return `<section class="card stack" id="batchBox" style="margin-top:16px">
    ${showHeading ? '<h2>承認した動画を書き出す</h2>' : ''}
    <p class="caption">内容を確かめて「この内容で承認する」を押し、書き出す動画に印を付けてください（1本でも複数でも書き出せます）。承認したあとに「確認・修正」で直すと、承認はやり直しになります。書き出せない動画が1本でもあるときは、どれも書き出しません。書き出しは1本ずつ順番に行います（1080×1920）。</p>
    <div class="row"><fluent-button id="pickAll">承認済みをすべて選ぶ</fluent-button><fluent-button appearance="primary" id="batchExport" disabled>選んだ動画を書き出す</fluent-button></div>
    <div class="progress-row" id="batchRow" hidden><span class="caption" id="batchLabel"></span><fluent-progress-bar id="batchBar" max="100" aria-label="まとめて書き出しの全体の進み具合"></fluent-progress-bar><span class="caption pct" id="batchPct"></span></div>
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
    b.textContent = n ? `選んだ ${n} 本を書き出す` : '選んだ動画を書き出す';
  };
  const show = (x) => {
    const el = block(x.candidate.id); if (!el) return;
    setHtml(el.querySelector('.appr-badge'), approvalBadge(x.approval));
    const btn = el.querySelector('.appr-btn');
    btn.textContent = approved(x) ? '承認を取り消す' : x.approval?.stale ? 'もう一度承認する' : 'この内容で承認する';
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
      el.querySelector('.exp-label').textContent = e.state === 'queued' ? '書き出し待ち' : '書き出し中';
      el.querySelector('.exp-pct').textContent = `${pct}%`;
    }
    const last = x.final_render_id ? `<a href="#/render/${esc(x.final_render_id)}">書き出した動画を開く</a>` : '';
    setHtml(el.querySelector('.exp-text'),
      e?.state === 'queued' ? `順番を待っています。<fluent-button size="small" class="exp-cancel" data-render="${esc(e.render_id)}">取り消す</fluent-button>`
      : e?.state === 'rendering' ? esc(e.step ? `実行中: ${e.step}` : e.message ?? '')
      : e?.state === 'failed' ? `<span class="fail">書き出しできませんでした。</span> <a href="#/render/${esc(e.render_id)}">理由を見る</a>${last ? ` · 前回の ${last}` : ''}`
      : e?.state === 'cancelled' ? `書き出しを取り消しました。${last ? ` 前回の ${last}` : ''}`
      : x.final_render_stale ? '<span class="warnc">編集後のため、本番QA・書き出し結果は失効しています。</span>'
      : last ? `書き出し済み: ${last}` : '');
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
    const tail = [n('failed') ? `失敗 ${n('failed')}` : '', n('cancelled') ? `取り消し ${n('cancelled')}` : ''].filter(Boolean).join('・');
    $('#batchLabel').textContent = `全体: ${n('done')} / ${work.length} 本 完了${tail ? `（${tail}）` : ''}`;
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
      $('#batchMsg').innerHTML = info(`${batch.length} 本の書き出しを始めました。この画面で進み具合を確認できます（ほかの画面に移っても書き出しは続きます）。`, 'success');
      await refresh();
    } catch (e) {
      // each item: video name and reason; for a hard QA failure also the failed checks (as in single export)
      const reasons = Array.isArray(e.detail) ? `<ul class="notes">${e.detail.map(d => `<li>${esc(d.name)}: ${esc(d.reason)}${d.code === 'E-QA-HARDFAIL' && Array.isArray(d.detail) ? qaList(d.detail) : ''}</li>`).join('')}</ul>` : '';
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
  const bgmList = await api(`/api/bgm?project_id=${encodeURIComponent(plan.project_id)}`);
  const projAssets = (await api(`/api/projects/${plan.project_id}`)).assets;
  const assetName = (aid) => (projAssets.find(a => a.id === aid)?.path ?? aid).split(/[\\/]/).pop();
  let tr = await api(`/api/assets/${plan.audio.master}/transcript`);
  const capStyle = (await api('/api/presets')).find(x => x.id === plan.preset.id)?.telop?.caption ?? {};
  const roleName = { teaser: '先出し', reaction: 'リアクション', wide: '引き', recap: 'おさらい', next: '次回予告' };
  let previewUrl = me?.preview_url, stale = me?.preview_stale ?? false;
  const typeLabel = { caption: '字幕', emphasis: '強調', hook: 'フック', headline: '見出し', tsukkomi: 'ツッコミ', sfx: '擬音', counter: 'カウンター' };
  const width = (s) => [...s].reduce((a, c) => a + (c.charCodeAt(0) < 0x2e80 || (c.charCodeAt(0) >= 0xff61 && c.charCodeAt(0) <= 0xffdc) ? 0.5 : 1), 0);
  // body time → time in the rendered video (mirrors Planning.InsertMap: inserts push later content back)
  const kindOrder = { opening: 0, card: 1, closing: 2 };
  const active = () => (plan.inserts ?? []).filter(i => i.enabled && i.duration > 0).sort((a, b) => a.at - b.at || kindOrder[a.kind] - kindOrder[b.kind]);
  const finalT = (t, end = false) => t + active().filter(i => i.at < t - 1e-3 || (!end && i.at <= t + 1e-3)).reduce((a, i) => a + i.duration, 0);
  const insertStart = (ins) => { let off = 0; for (const i of active()) { if (i.id === ins.id) break; off += i.duration; } return ins.at + off; };
  const insertName = { opening: 'オープニング', closing: 'クロージング', card: '場面転換カード' };
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
    const start = insertStart(i), end = start + Number(i.duration), name = isTsukkomiCard(i) ? 'ツッコミ' : insertName[i.kind] ?? i.kind;
    const file = (i.path ?? '').split(/[\\/]/).pop();
    const notes = i.kind === 'card'
      ? [isTsukkomiCard(i) ? '停止カード' : '', i.path ? `全画面の画像 ${file}` : '', `映像を止めて ${Number(i.duration).toFixed(1)} 秒`, i.text_source === 'ai' ? 'AI の提案' : ''].filter(Boolean)
      : [file, `${Number(i.duration).toFixed(1)} 秒`];
    return `<tr class="insert ${i.enabled ? '' : 'off'}" data-insert="${esc(i.id)}"><td><span class="tag ${esc(groupOf(i))}">${esc(name)}</span></td>
      <td>${i.enabled ? `<button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${start.toFixed(1)}秒から${end.toFixed(1)}秒の${esc(name)}を確認（開始位置へ移動）">${start.toFixed(1)}–${end.toFixed(1)}</button>` : '<span class="caption">（使わない）</span>'}</td>
      <td>${i.kind === 'card' ? `<input class="card-text" data-id="${esc(i.id)}" value="${esc(i.text)}" maxlength="20" aria-label="${esc(name)} ${esc(i.id)} の文字" placeholder="${i.path ? '（文字なし）' : ''}">` : ''}
        <p class="caption insert-notes">${esc(notes.join(' · '))}</p></td>
      <td>${i.kind === 'card' ? [...(i.text ?? '')].length : ''}</td><td></td>
      <td><fluent-switch class="ins-en" data-id="${esc(i.id)}" ${i.enabled ? 'checked' : ''} aria-label="${esc(name)} ${esc(i.id)} を使う"></fluent-switch>${i.approved || !i.text ? '' : `<fluent-button size="small" data-approve-card="${esc(i.id)}">承認</fluent-button>`}</td></tr>`;
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
    const sfxSw = field('効果音', `<fluent-switch slot="input" id="sfxSw" ${a.sfx_enabled ? 'checked' : ''}></fluent-switch>`);
    if (!cues.length) return `<h3>音</h3>
          <div class="row"><label class="caption">BGM <select class="native" id="bgmSel">
            <option value="none" ${!a.bgm ? 'selected' : ''}>なし</option>${bgmList.map(b => `<option value="${esc(b.id)}" ${a.bgm === b.id ? 'selected' : ''}>${esc(b.name)}</option>`).join('')}</select></label>
            ${sfxSw}</div>
          <p class="caption">${a.sfx?.length ?? 0} 個の効果音${a.bgm ? ` · BGM ${a.duck ? 'は話している間だけ自動で小さくなります' : 'あり'}` : ''}。この動画は以前の形式のため、場面ごとの選曲と音量の調整は作り直すと使えます。</p>`;
    const beat = a.bgm_mode === 'beat', on = !!a.bgm, spans = cueSpans(), total = Math.max(0.1, totalDur());
    const tracks = [...new Set(cues.map(c => c.track).filter(Boolean))];
    const color = (id) => cueColors[Math.max(0, tracks.indexOf(id)) % cueColors.length];
    const single = cues.every(c => c.track === cues[0].track) && a.bgm_mode !== 'scene';
    const usable = bgmList.filter(b => b.analysis || b.source === 'library');
    const s15 = qa.find(q => q.check_id === 'S-15');
    const depths = (a.duck_spans ?? []).map(d => d.db);
    const ducks = (a.duck_spans ?? []).map(d => { const s = finalT(d.t0), e = finalT(d.t1, true); return `<span class="bgm-duck" style="left:${(s / total * 100).toFixed(2)}%;width:${Math.max(0.2, (e - s) / total * 100).toFixed(2)}%;height:${Math.min(100, 20 + d.db * 4)}%" title="${s.toFixed(1)}–${e.toFixed(1)}秒 ${d.db.toFixed(1)} dB 下げる"></span>`; }).join('');
    return `<h3 id="h-bgm">音・BGM</h3>
      <div class="row"><label class="caption">BGM <select class="native" id="bgmSel" ${beat ? 'disabled' : ''}>
        <option value="none" ${!on ? 'selected' : ''}>なし</option>
        <option value="scene" ${on && !single ? 'selected' : ''}>場面ごとに選ぶ（自動選曲）</option>
        ${usable.map(b => `<option value="${esc(b.id)}" ${on && single && cues[0].track === b.id ? 'selected' : ''}>全編この曲: ${esc(b.name)}</option>`).join('')}</select></label>
        ${sfxSw}</div>
      ${beat ? '<p class="caption">ビート同期のパターンは、曲の拍に合わせてカットしているため曲を変えられません（音量は調整できます）。</p>' : ''}
      ${on ? `<div class="bgm-strip" role="img" aria-label="BGM の流れ: ${spans.map(x => `${x.start.toFixed(0)}秒から${x.c.track ? esc(trackLabel(x.c.track)) : 'BGM なし'}`).join('、')}。下の帯は話していて BGM を下げる所">
          ${spans.map(x => `<span class="bgm-seg" style="left:${(x.start / total * 100).toFixed(2)}%;width:${((x.end - x.start) / total * 100).toFixed(2)}%;background:${x.c.track ? color(x.c.track) : 'transparent'}">${x.c.track ? esc(trackLabel(x.c.track)) : 'なし'}</span>`).join('')}
          <span class="bgm-ducks">${ducks}</span><span class="bgm-playhead"></span></div>
        <div class="row">
          <label class="caption">話している間（声との差） <select class="native" id="bgmLd">${lvlOpts([10, 12, 14, 16, 18, 20, 24], a.bgm_speech_ld, v => `声より ${v} LU 小さく${v === 14 ? '（おすすめ）' : v === 10 ? '（下限）' : ''}`)}</select></label>
          <label class="caption">声のない所 <select class="native" id="bgmGap">${lvlOpts([0, -4, -6, -8, -10, -12, -16, -20], a.bgm_gap_lu, v => v === 0 ? '声と同じ大きさ' : `声より ${-v} LU 小さく`)}</select></label>
        </div>
        <p class="caption">${depths.length} か所の発話に合わせて、単語の 0.25 秒前から BGM を${depths.length ? ` ${Math.min(...depths).toFixed(0)}〜${Math.max(...depths).toFixed(0)} dB` : ''}下げ、0.8 秒未満の言葉の間は下げたままにします（声が小さい所・曲が盛り上がる所はより深く）。差は 10 LU 以上が聞き取りやすさの目安です（Torcoli ほか, JAES 2019）。</p>
        <p class="caption ${s15 ? (s15.status === 'pass' ? 'pass' : 'warnc') : ''}" id="bgmMeasured">${s15 ? `実測（${s15.stage === 'final' ? '書き出し' : 'プレビュー'}の音を ebur128 で測定）: 話している間 ${esc(s15.measured)}` : '「プレビューを更新」すると、作った音で声と BGM の差を実測します。'}</p>
        <div class="bgm-cues stack" role="list" aria-label="場面ごとの BGM">
          ${spans.map(({ c, start, end }) => `<div class="bgm-cue" role="listitem" data-cue="${esc(c.id)}">
            <div class="row"><span class="bgm-swatch" style="background:${c.track ? color(c.track) : 'transparent'}"></span>
              <button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${start.toFixed(1)}秒からの場面を再生">${tc(start)}–${tc(end)}</button>
              <b>${energyWord(c.scene_energy)}場面</b><span class="caption">${c.auto ? '自動で選曲' : 'あなたが選択'}</span></div>
            <p class="caption">${esc(c.scene)}</p>
            <div class="row">
              <label class="caption">曲 <select class="native bgm-cue-track" data-id="${esc(c.id)}" aria-label="${tc(start)}からの曲" ${beat ? 'disabled' : ''}>
                ${c.auto ? '' : '<option value="auto">自動の選曲に戻す</option>'}
                ${usable.map(b => `<option value="${esc(b.id)}" ${c.track === b.id ? 'selected' : ''}>${esc(b.name)}${b.analysis ? `（${energyWord(b.analysis.energy)}）` : ''}</option>`).join('')}
                <option value="none" ${!c.track ? 'selected' : ''}>この場面は BGM なし</option></select></label>
              <label class="caption">音量 <select class="native bgm-trim" data-id="${esc(c.id)}" aria-label="${tc(start)}からの曲の音量">${lvlOpts([6, 3, 0, -3, -6, -9, -12], c.trim_db, v => v === 0 ? '±0 dB' : `${v > 0 ? '+' : ''}${v} dB`)}</select></label>
            </div>
            <p class="caption">理由: ${esc(c.reason)}</p></div>`).join('')}
        </div>` : ''}
      <p class="caption">${a.sfx?.length ?? 0} 個の効果音。変更後は「プレビューを更新」で聞けます。</p>`;
  };

  // ---- 自動チェック panel (right column): findings on the rendered-file timeline, recommended fix, Copilot auto-repair ----
  const totalDur = () => plan.timeline.reduce((a, c) => a + (c.src_out - c.src_in) / (c.speed || 1), 0) + active().reduce((a, i) => a + i.duration, 0);
  const tc = (x) => `${Math.floor(x / 60)}:${(x % 60).toFixed(1).padStart(4, '0')}`;
  const statusName = { pass: '合格', fail: '不合格', warn: '警告', not_run: '未実施' };
  const repairName = { rule: '自動で修復できます', ai: 'AI で修復できます', manual: '手動で修正', none: '自動では直せません' };
  // findings 「Copilotで自動修復」 will fix (advice.fixable from the server; every item when not assessed)
  const fixableOf = (q) => !q.advice || q.advice.repair === 'manual' || q.advice.repair === 'none' ? 0 : q.advice.fixable ?? (q.items ?? []).length;
  const targetText = (it) => {
    if (it.kind === 'telop') { const t = plan.telops.find(x => x.id === it.target); return t ? `${typeLabel[t.type] ?? t.type}「${t.text}」` : it.target; }
    if (it.kind === 'insert') { const i = (plan.inserts ?? []).find(x => x.id === it.target); return i ? `${isTsukkomiCard(i) ? 'ツッコミ' : insertName[i.kind] ?? i.kind}${i.text ? `「${i.text}」` : ''}` : it.target; }
    if (it.kind === 'clip') return `カット ${it.target}`;
    if (it.kind === 'asset') return assetName(it.target);
    return it.target;
  };
  const seekBtn = (it, check) => Number.isFinite(it.start)
    ? `<button class="qa-seek" type="button" data-time="${it.start.toFixed(3)}" data-target="${esc(it.target)}" data-kind="${esc(it.kind)}" aria-label="${esc(check)} ${tc(it.start)} の位置へ移動">${tc(it.start)}</button>`
    : '<span class="qa-seek none" aria-hidden="true">—</span>';
  const SPARKLE = '<svg slot="start" class="ico" viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" fill="currentColor"><path d="M9 2.2l1.7 4.4c.2.6.7 1.1 1.3 1.3L16.4 9.6l-4.4 1.7c-.6.2-1.1.7-1.3 1.3L9 17l-1.7-4.4c-.2-.6-.7-1.1-1.3-1.3L1.6 9.6 6 7.9c.6-.2 1.1-.7 1.3-1.3z"/><path d="M15.5 1.5l.6 1.6c.1.2.2.3.4.4l1.6.6-1.6.6c-.2.1-.3.2-.4.4l-.6 1.6-.6-1.6c-.1-.2-.2-.3-.4-.4l-1.6-.6 1.6-.6c.2-.1.3-.2.4-.4z"/></svg>';
  const BULB = '<svg class="ico" viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" fill="currentColor"><path d="M8 1a5 5 0 0 0-3 9v1.5c0 .3.2.5.5.5h5c.3 0 .5-.2.5-.5V10a5 5 0 0 0-3-9zm2 8.4V11H6V9.4l-.2-.2A4 4 0 1 1 10.2 9.2zM6 13h4v1a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1z"/></svg>';
  const resultBlock = (r) => {
    // the reasons, most frequent first, so a click that fixes little still says why
    const reasons = [...r.skipped.reduce((m, s) => m.set(s.reason, (m.get(s.reason) ?? 0) + 1), new Map())].sort((a, b) => b[1] - a[1]);
    return `<div class="qa-result" role="status" tabindex="-1">
      <div class="row" style="justify-content:space-between"><b>${r.scope ? `${esc(r.scope)}: ` : ''}${r.fixed_count ? `${r.fixed_count} 件を修復しました` : '修復できた項目はありませんでした'}${r.skipped.length ? `（直せなかった ${r.skipped.length} 件）` : ''}</b>
        ${r.repair_id ? `<fluent-button size="small" id="qaUndo">元に戻す</fluent-button>` : ''}</div>
      ${r.changes.length ? `<ul class="qa-changes">${r.changes.map(c => `<li>${seekBtn(c, c.check_id)}<span><b>${esc(c.check_id)}</b> <span class="repair-tag ${esc(c.method)}">${c.method === 'ai' ? 'AI' : 'ルール'}</span>${c.needs_approval ? ' <span class="repair-tag manual">要承認</span>' : ''}
        <span class="qa-diff"><del>${esc(c.before ?? '')}</del> → <ins>${esc(c.after ?? '')}</ins></span></span></li>`).join('')}</ul>` : ''}
      ${r.changes.some(c => c.needs_approval) ? '<p class="caption">AI が書き直した文は未承認です。内容を確認して「承認」してください。</p>' : ''}
      ${reasons.length ? `<p class="caption"><b>直せなかった理由</b></p><ul class="qa-reasons">${reasons.slice(0, 4).map(([why, n]) => `<li><b>${n} 件</b> ${esc(why)}</li>`).join('')}</ul>
        <details><summary class="caption">直せなかった項目 ${r.skipped.length} 件</summary><ul class="qa-changes">${r.skipped.map(s => `<li>${seekBtn(s, s.check_id)}<span><b>${esc(s.check_id)}</b> ${esc(s.target)} <span class="caption">${esc(s.reason)}</span></span></li>`).join('')}</ul></details>` : ''}
      <p class="caption">字幕は話した言葉どおりに、言いよどみ・語尾・繰り返しも残し、表示時間・区切り・結合だけで直します（設定済みの伏せ字を除く）。演出テロップ・カードは見た目を優先して AI で修正し、要承認にします。変更ごとに自動チェックをやり直し、ほかの項目が悪くならないものだけを残しています。</p></div>`;
  };
  const qaPanel = () => {
    const reviewed = qa.filter(q => q.status !== 'not_run' && q.review_hash);
    const pending = reviewed.filter(q => !q.review_approval);
    const approvalControl = (q) => q.status === 'not_run' || !q.review_hash ? '' : `<div class="row qa-approval">
      <span class="caption">${q.review_approval ? `承認済み · ${esc(new Date(q.review_approval.at).toLocaleString('ja-JP'))}` : '未承認'}</span>
      <fluent-button size="small" data-qa-approve="${esc(q.check_id)}" aria-label="${esc(q.check_id)} ${q.review_approval ? '承認を取り消す' : '承認'}" ${approvingQa || repairing ? 'disabled' : ''}>${q.review_approval ? '承認を取り消す' : '承認'}</fluent-button></div>`;
    const issues = qa.filter(q => q.status === 'fail' || q.status === 'warn').sort((a, b) => (a.status === 'fail' ? 0 : 1) - (b.status === 'fail' ? 0 : 1));
    const passed = qa.filter(q => q.status === 'pass' || q.status === 'not_run');
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
        <p class="caption qa-measure">${esc(q.measured)}（基準 ${esc(q.threshold)}${q.stage ? `・${esc(q.stage)}` : ''}）</p>
        ${approvalControl(q)}
        ${q.advice ? `<div class="qa-advice">${BULB}<div><b>推奨する修正</b> <span class="repair-tag ${esc(q.advice.repair)}">${repairName[q.advice.repair] ?? ''}</span><p>${esc(q.advice.recommendation)}</p>
          ${n > 0 ? `<fluent-button size="small" appearance="subtle" data-repair-one="${esc(q.check_id)}" ${repairing ? 'disabled' : ''}>${SPARKLE}${repairing === q.check_id ? '修復しています…' : assessed && all.length > 1 ? `この項目を修復（${n} 件が対象）` : 'この項目を修復'}</fluent-button>
            ${repairing === q.check_id ? '<fluent-progress-bar aria-label="この項目の修復の進み具合"></fluent-progress-bar>' : ''}` : ''}
          ${q.advice.remaining ? `<p class="caption">${esc(q.advice.remaining)}</p>` : ''}</div></div>` : ''}
        ${lastRepair && lastRepair.scope === q.check_id ? resultBlock(lastRepair) : ''}
        ${items.length ? `<ul class="qa-items">${items.slice(0, 12).map(row).join('')}</ul>${items.length > 12 ? `<details><summary class="caption">ほか ${items.length - 12} 件</summary><ul class="qa-items">${items.slice(12).map(row).join('')}</ul></details>` : ''}` : ''}
        ${stays.length ? `<details class="qa-stays"><summary class="caption">自動では直らない ${stays.length} 件（${/^S-0[128]$/.test(q.check_id) ? '言葉を変えないため' : ''}警告のまま残ります）</summary><ul class="qa-items">${stays.map(row).join('')}</ul></details>` : ''}
      </li>`;
    };
    const r = lastRepair;
    const result = !r || issues.some(q => q.check_id === r.scope) ? '' : resultBlock(r);
    return `<section class="card qa-panel" aria-labelledby="qa-title">
      <div class="row" style="justify-content:space-between"><h2 id="qa-title">自動チェック</h2>
        <div class="row"><span class="qa-count fail">不合格 ${fails}</span><span class="qa-count warnc">警告 ${warns}</span><span class="qa-count pass">合格 ${passed.filter(q => q.status === 'pass').length}</span></div></div>
      <div class="qa-review">
        <div class="row"><fluent-button id="qaApproveAll" ${pending.length && !approvingQa && !repairing ? '' : 'disabled'}>一括承認${pending.length ? `（${pending.length} 項目）` : ''}</fluent-button>
          <span class="caption" id="qaApprovalCount">承認済み ${reviewed.length - pending.length} / ${reviewed.length} 項目</span></div>
        <p class="caption">承認はチェック結果を確認した記録です。不合格・警告は残り、書き出し条件やテロップ・カードの承認は変わりません。編集・判定結果の変更後は再承認が必要です。未実施は対象外です。</p>
        <div id="qaApprovalMsg" role="status"></div>
      </div>
      <div class="qa-timeline" role="group" aria-label="指摘の位置（動画の時間）">${markers}<div class="qa-playhead" aria-hidden="true"></div></div>
      <div class="row caption" style="justify-content:space-between"><span>0:00.0</span><span id="qaNow" aria-live="off">0:00.0</span><span>${tc(total)}</span></div>
      ${issues.length ? `<div class="qa-repair">
        <fluent-button appearance="primary" id="qaRepair" ${repairable.length && !repairing ? '' : 'disabled'} aria-describedby="qaRepairNote">${SPARKLE}${repairing === 'all' ? '修復しています…' : 'Copilotで自動修復'}</fluent-button>
        ${repairing === 'all' ? '<fluent-progress-bar aria-label="自動修復の進み具合"></fluent-progress-bar>' : ''}
        <p class="caption" id="qaRepairNote">${repairable.length ? `${repairable.length} 項目・${fixTotal} 件が対象。` : '自動で直せる項目はありません。'}${llmDest ? `AI の修正にはテロップの文字と前後の書き起こしを ${esc(llmDest)} に送ります。` : 'LLM が未設定のため、ルールで直せる項目だけを修復します。'}</p></div>` : '<p class="pass">すべての自動チェックに合格しています。</p>'}
      <div id="qaRepairMsg">${result}</div>
      ${issues.length ? `<ul class="qa-issues">${issues.map(issueCard).join('')}</ul>` : ''}
      ${passed.length ? `<details class="qa-passed"><summary class="caption">合格・未実施 ${passed.length} 件</summary>${passed.map(q => `<div data-check="${esc(q.check_id)}">${qaList([q])}${approvalControl(q)}</div>`).join('')}</details>` : ''}
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
        $('#qaApprovalMsg').textContent = `${checks.length} 項目の${approved ? '承認を保存しました' : '承認を取り消しました'}。`;
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
      <div class="row" style="justify-content:space-between"><h1>確認・修正</h1>
        ${multi ? `<nav class="row video-nav" aria-label="動画の切り替え">
          ${meIdx > 0 ? `<fluent-anchor-button size="small" appearance="subtle" id="prevVideo" href="#/ep/${esc(cands[meIdx - 1].candidate.edit_plan_id)}">‹ 前の動画</fluent-anchor-button>` : ''}
          <label class="caption">動画 ${meIdx + 1} / ${cands.length} <select class="native" id="videoSel" aria-label="確認する動画を選ぶ">${cands.map((x, i) => `<option value="${esc(x.candidate.edit_plan_id)}" ${i === meIdx ? 'selected' : ''}>${i + 1}. ${esc(videoName(x))} — ${videoStatus(navItem(i)).label}</option>`).join('')}</select></label>
          ${meIdx < cands.length - 1 ? `<fluent-anchor-button size="small" appearance="subtle" id="nextVideo" href="#/ep/${esc(cands[meIdx + 1].candidate.edit_plan_id)}">次の動画 ›</fluent-anchor-button>` : ''}
        </nav>` : ''}
        <div class="row">${safeToggle()}<fluent-anchor-button href="#/job/${esc(job.id)}" appearance="subtle">候補に戻る</fluent-anchor-button></div></div>
      <div class="grid cols-review with-qa" style="align-items:start">
        <section class="card stack">
          ${previewUrl ? phone(previewUrl + (stale ? '' : `?v=${Date.now()}`), 'pv') : ''}
          ${stale ? info('変更があったため、プレビューが古くなっています。', 'warning') : ''}
          <fluent-button id="repreview" ${stale ? 'appearance="primary"' : ''}>プレビューを更新</fluent-button>
          <p class="caption">${plan.series ? `${esc(plan.series.label)}（${plan.series.part}/${plan.series.parts}）· ` : ''}長さ ${(() => { const d = plan.timeline.reduce((a, c) => a + (c.src_out - c.src_in) / (c.speed || 1), 0); const all = d + active().reduce((a, i) => a + i.duration, 0); const f = (x) => x >= 180 ? fmt(x) : `${x.toFixed(1)} 秒`; return all - d > 0.01 ? `本編 ${f(d)}・全体 ${f(all)}` : f(d); })()} · カット ${plan.timeline.length} · テロップ ${plan.telops.filter(t => t.enabled).length}</p>
          ${plan.series?.chapters?.length ? `<details><summary class="caption">構成・チャプター（${plan.series.chapters.length}）</summary><ul class="notes">${plan.series.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          <details><summary class="caption">カット一覧（映像・速度）</summary>
            <ul class="notes">${plan.timeline.map(c => `<li>${esc(c.clip_id)} ${esc(assetName(c.asset_id))}${c.role !== 'body' ? `（${esc(roleName[c.role] ?? c.role)}）` : ''} ${((c.src_out - c.src_in) / (c.speed || 1)).toFixed(1)}秒${c.speed && c.speed !== 1 ? ` ×${c.speed}倍速` : ''}${c.reframe?.mode === 'face' ? ' · 顔を追う' : ''}${c.reframe?.mode === 'screen' ? (c.reframe.crop_w ? ' · 画面（操作箇所へズーム）' : ' · 画面（全体）') : ''}${c.pip ? ` · ワイプ: ${esc(assetName(c.pip.asset_id))}` : ''}</li>`).join('')}</ul></details>
          ${bgmPanel()}
          <details><summary class="caption">書き起こしを直す（誤認識の修正）</summary>
            <p class="caption">この動画に使われている範囲の単語です。直すと字幕が作り直されます（時刻はそのまま。字幕を手で直していた場合はやり直しになります）。</p>
            <div class="words">${tr.words.filter(w => plan.timeline.some(c => (w.t0 + w.t1) / 2 >= (c.audio_in ?? c.src_in) && (w.t0 + w.t1) / 2 <= (c.audio_in ?? c.src_in) + (c.src_out - c.src_in))).map(w => `<input class="native wd" data-id="${esc(w.id)}" value="${esc(w.text)}" size="${Math.max(2, [...w.text].length + 1)}" maxlength="30" aria-label="単語 ${esc(w.text)}">`).join('')}</div></details>
          ${plan.series ? '<p class="caption">前編・後編の区切りは前後のパートとつながっているため、ここでは動かせません（本数か1本あたりの長さを変えて作り直します）。</p>' : `<h3>区間の調整（文単位）</h3>
          <div class="row"><fluent-button size="small" data-adj="start:1">開始を1文前へ</fluent-button><fluent-button size="small" data-adj="start:-1">開始を1文後へ</fluent-button></div>
          <div class="row"><fluent-button size="small" data-adj="end:-1">終了を1文前へ</fluent-button><fluent-button size="small" data-adj="end:1">終了を1文後へ</fluent-button></div>`}
          <div id="review-approval">${approvalBadge(approval)}</div>
          ${finalStale ? info('編集後のため、本番QA・書き出し結果は失効しています。現在の内容で再承認・再書き出ししてください。', 'warning') : ''}
          ${multi ? `<fluent-button appearance="primary" id="approveNext" ${dis}>${nextTodo ? `承認して次の動画へ（${esc(videoName(nextTodo))}）` : '承認して書き出しへ進む'}</fluent-button>
          <fluent-button id="approveBack" ${dis}>この内容で承認して候補に戻る</fluent-button>
          <fluent-button appearance="subtle" id="export" ${dis}>この1本だけ今すぐ書き出す（1080×1920）</fluent-button>
          <p class="caption">承認した動画は「6 書き出し」でほかの動画とまとめて書き出せます。承認したあとに直すと、承認はやり直しになります。</p>`
          : `<fluent-button appearance="primary" id="export" ${dis}>この内容を承認して書き出す（1080×1920）</fluent-button>
          ${me ? `<fluent-button id="approveBack" ${dis}>承認だけして候補に戻る</fluent-button>` : ''}`}
          ${hardFail ? '<span class="caption fail">不合格の項目を直すと書き出せます（右の「自動チェック」）</span>' : ''}
          <div id="msg"></div>
        </section>
        <section class="card">
          <div class="row" style="justify-content:space-between"><h2>テロップ・場面転換</h2>
            <div class="row">${filterTypes.filter(t => itemsOf(t).length || ['caption', 'emphasis', 'headline'].includes(t)).map(t => field(typeLabel[t], `<fluent-switch slot="input" data-type="${t}" ${itemsOf(t).some(x => x.enabled) || !itemsOf(t).length ? 'checked' : ''}></fluent-switch>`)).join('')}
              ${plan.telops.some(t => t.type === 'tsukkomi' && !t.approved) || (plan.inserts ?? []).some(i => isTsukkomiCard(i) && i.text && !i.approved) ? '<fluent-button size="small" id="approveAll">ツッコミをすべて承認</fluent-button>' : ''}
              ${(plan.inserts ?? []).some(i => i.kind === 'card' && !isTsukkomiCard(i) && i.text && !i.approved) ? '<fluent-button size="small" id="approveCards">カードをすべて承認</fluent-button>' : ''}</div></div>
          <p class="caption">文字を直すと、文字数・表示速度・行頭や行末の禁則・安全領域の自動チェックがすぐにやり直されます。1行は全角${maxW}字${maxLines > 1 ? `（字幕は${maxLines}行）` : ''}まで、字幕は毎秒${maxCps}字までが目安です。カードは映像を止めて全画面で 2〜3 秒挟み、文字は 1 秒で読める短さ（20 字まで）にします。AI が考えた文は「承認」するまで書き出せません。カードの変更は「プレビューを更新」で確かめられます。</p>
          <div class="scroll"><table class="list"><thead><tr><th>種類</th><th>時刻</th><th style="width:55%">テキスト</th><th>字数</th><th>速さ</th><th>表示</th></tr></thead><tbody>
            ${[...plan.telops.map(t => ({ at: finalT(t.start), t })), ...(plan.inserts ?? []).map(i => ({ at: i.enabled && i.duration > 0 ? insertStart(i) : finalT(i.at), i }))]
              .sort((a, b) => a.at - b.at || (a.i ? 0 : 1) - (b.i ? 0 : 1)).map(({ t, i }) => i ? insertRow(i) : (() => {
              const w = width(t.text), cps = w / Math.max(0.05, t.end - t.start);
              const start = finalT(t.start), end = finalT(t.end, true);
              return `<tr class="${t.enabled ? '' : 'off'}" data-telop="${esc(t.id)}"><td><span class="tag ${esc(t.type)}">${esc(typeLabel[t.type] ?? t.type)}</span></td>
                <td><button class="timestamp-seek" type="button" data-time="${start.toFixed(3)}" aria-label="${start.toFixed(1)}秒から${end.toFixed(1)}秒の範囲を確認（開始位置へ移動）">${start.toFixed(1)}–${end.toFixed(1)}</button></td>
                <td><input class="telop" data-id="${esc(t.id)}" value="${esc(t.text)}" maxlength="40" aria-label="テロップ ${esc(t.id)}">
                  ${['emphasis', 'headline', 'hook', 'counter'].includes(t.type) ? (() => {
                    const words = t.source_word_ids.map(id => tr.words.find(w => w.id === id)).filter(Boolean);
                    const evidence = words.length ? `話者${[...new Set(words.map(w => w.speaker))].join('/')}・${words[0].t0.toFixed(1)}–${words.at(-1).t1.toFixed(1)}秒「${words.map(w => w.text).join('')}」` : '根拠発話が未指定です';
                    return `<p class="caption evidence" data-evidence="${esc(t.id)}">${esc(evidence)}</p>
                      <label class="caption">根拠発話 <select class="native evidence-select" data-id="${esc(t.id)}" aria-label="根拠発話 ${esc(t.id)}"><option value="">選択してください</option>
                      ${tr.sentences.filter(s => plan.timeline.some(c => s.t1 > (c.audio_in ?? c.src_in) && s.t0 < (c.audio_in ?? c.src_in) + c.src_out - c.src_in)).map(s => {
                        const ids = tr.words.slice(s.first_word, s.last_word + 1).filter(w => plan.timeline.some(c => w.t0 >= (c.audio_in ?? c.src_in) - 0.15 && w.t1 <= (c.audio_in ?? c.src_in) + c.src_out - c.src_in + 0.15)).map(w => w.id);
                        return `<option value="${esc(JSON.stringify(ids))}" ${JSON.stringify(ids) === JSON.stringify(t.source_word_ids) ? 'selected' : ''}>話者${s.speaker}・${s.t0.toFixed(1)}秒 ${esc(s.text)}</option>`;
                      }).join('')}</select></label>`;
                  })() : ''}</td>
                <td class="${w > maxW * (t.type === 'caption' ? maxLines : 1) && t.type !== 'headline' ? 'warn' : ''}">${w}</td>
                <td class="${t.type === 'caption' && cps > maxCps ? 'warn' : 'caption'}">${t.type === 'caption' ? cps.toFixed(1) : ''}</td>
                <td><fluent-switch data-id="${esc(t.id)}" class="en" ${t.enabled ? 'checked' : ''} aria-label="表示"></fluent-switch>${t.approved ? '' : `<fluent-button size="small" data-approve="${esc(t.id)}">承認</fluent-button>`}</td></tr>`;
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
    const patch = async (ops) => {
      try { const r = await api(`/api/editplans/${epId}`, { method: 'PATCH', body: { ops } }); plan = r.plan; qa = r.qa; planHash = r.plan_hash; approval = r.approval; finalStale = r.final_render_stale; stale = stale || r.preview_stale; lastRepair = null; draw(); }
      catch (e) { $('#msg').innerHTML = errorBar(e); }
    };
    $$('input.wd').forEach(inp => inp.addEventListener('change', async () => {
      try { tr = await api(`/api/assets/${plan.audio.master}/transcript`, { method: 'PATCH', body: { edits: [{ word_id: inp.dataset.id, text: inp.value }] } }); await patch([{ op: 'replan' }]); }
      catch (e) { $('#msg').innerHTML = errorBar(e); }
    }));
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
      $('#msg').innerHTML = `<div class="progress-row"><span class="caption">プレビューを作成しています</span><fluent-progress-bar id="rpBar" max="100" value="0" aria-label="プレビュー作成の進み具合"></fluent-progress-bar><span id="rpPct" class="caption pct">0%</span></div><p class="caption" id="rpStep"></p>`;
      const tick = setInterval(async () => {
        try {
          const p = await api(`/api/editplans/${epId}/preview/progress`);
          if (!p.running || !$('#rpBar')) return;
          $('#rpBar').setAttribute('value', String(Math.round(p.progress * 100)));
          $('#rpPct').textContent = `${Math.round(p.progress * 100)}%`;
          $('#rpStep').textContent = p.step ?? '';
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
    <div class="row" style="justify-content:space-between"><h1>書き出し（${list.length}本）</h1>
      <div class="row"><fluent-anchor-button href="#/job/${esc(j.id)}" appearance="subtle">候補に戻る</fluent-anchor-button></div></div>
    <p class="caption">承認した動画に印を付けて、まとめて書き出します。まだ承認していない動画は「確認・修正」で内容を確かめて承認してください。</p>
    ${workProgress(j, list, true)}
    <div class="stack export-list">
      ${list.map(x => `
        <article class="card export-item" aria-label="${esc(videoName(x))}">
          <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
            <div class="stack" style="gap:2px"><b>${esc(videoName(x))}</b>
              <span class="headline">${esc(x.candidate.headline || x.candidate.hook_text || '（見出しなし）')}</span>
              <span class="caption">${fmt(dur(x))}${x.preview_stale ? ' · プレビュー・QAは編集前の結果' : ''}</span></div>
            <div class="row"><span class="badges">${x.preview_stale ? '' : qaBadges(x.preview?.qa)}</span>
              <fluent-anchor-button size="small" href="#/ep/${esc(x.candidate.edit_plan_id)}">確認・修正</fluent-anchor-button></div>
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
    feedback.innerHTML = info('コピーしました。貼り付け先で Ctrl+V を押してください。', 'success');
  } catch {
    if (field) { field.focus(); field.select(); }
    feedback.innerHTML = info('クリップボードにコピーできませんでした。テキストを選択して Ctrl+C でコピーしてください。', 'warning');
  } finally { button.disabled = false; }
}

async function exportArtifacts(renderId, box) {
  box.innerHTML = '<p class="caption" role="status">書き出したファイルを確認しています…</p>';
  const base = `/api/renders/${encodeURIComponent(renderId)}`;
  let result;
  try { result = await api(`${base}/artifacts`); }
  catch (e) {
    if (!box.isConnected) return;
    box.innerHTML = `${errorBar(e)}<fluent-button class="artifact-retry">成果物を再読み込み</fluent-button>`;
    $('.artifact-retry', box).addEventListener('click', () => exportArtifacts(renderId, box));
    return;
  }
  if (!box.isConnected) return;
  const names = { mp4: '完成動画', srt: '字幕（SRT）', ass: '字幕・演出（ASS）', editplan: '編集計画（JSON）', metadata: '投稿用メタデータ', post: '投稿文データ（JSON）' };
  const fileUrl = key => `${base}/files/${encodeURIComponent(key)}`;
  box.innerHTML = `
    <div class="grid artifact-layout">
      <section class="stack artifact-video">
        <div class="row"><h3>書き出した動画のプレビュー</h3>${safeToggle()}</div>
        <p class="caption">編集用の低解像度プレビューではなく、保存済みの完成動画です。</p>
        ${result.available ? phone(`/api/media/${encodeURIComponent(renderId)}`) : info('動画ファイルが見つかりません。保存先を確認してください。', 'warning')}
        <div class="video-feedback" role="status"></div>
        <label class="stack">動画ファイルの保存先
          <textarea class="artifact-path" rows="3" readonly aria-label="動画ファイルの保存先">${esc(result.path)}</textarea></label>
        <div class="row"><fluent-button class="path-copy">パスをコピー</fluent-button>
          <fluent-button class="folder-open" appearance="primary" ${result.available ? '' : 'disabled'}>フォルダーを開く</fluent-button></div>
        <div class="artifact-feedback" role="status"></div>
      </section>
      <section class="stack artifact-post"><h3>投稿用の文章（コピーして貼り付け）</h3>
        <div class="post-body stack"></div>
      </section>
    </div>
    <details class="artifact-files"><summary>その他の成果物（${result.files.length} 件）・プレビュー</summary>
      <div class="stack">${result.files.map(f => `
        <section class="artifact-file stack" data-key="${esc(f.key)}">
          <div class="row"><b>${esc(names[f.key] ?? (f.key.startsWith('thumb') ? `サムネイル候補 ${f.key.slice(5)}` : f.key))}</b>
            ${f.available ? `<a href="${fileUrl(f.key)}" target="_blank" rel="noopener">開く</a><a href="${fileUrl(f.key)}" download="${esc(f.path.split(/[\\/]/).pop())}">保存</a>` : '<span class="fail">ファイルが見つかりません</span>'}</div>
          <code class="artifact-file-path">${esc(f.path)}</code>
          ${!f.available ? '' : f.preview_type === 'image' ? `<a href="${fileUrl(f.key)}" target="_blank" rel="noopener"><img class="artifact-image" src="${fileUrl(f.key)}" loading="lazy" alt="${esc(names[f.key] ?? f.key)}"></a><div class="image-feedback" role="status"></div>`
            : f.preview_type === 'text' ? '<details class="text-preview"><summary>内容をプレビュー</summary><div class="preview-body"></div></details>'
            : f.preview_type === 'video' ? '<p class="caption">上の動画プレイヤーで確認できます。</p>' : '<p class="caption">この形式のプレビューはありません。「開く」または「保存」を使ってください。</p>'}
        </section>`).join('')}</div>
    </details>`;
  const feedback = $('.artifact-feedback', box), path = $('.artifact-path', box);
  $('.path-copy', box).addEventListener('click', e => copySnippet(path.value, e.currentTarget, feedback, path));
  $('.folder-open', box).addEventListener('click', async e => {
    const button = e.currentTarget;
    button.disabled = true;
    try {
      await api(`${base}/open-folder`, { method: 'POST' });
      if (box.isConnected) feedback.innerHTML = info('エクスプローラーに保存先を開くよう依頼しました。', 'success');
    } catch (err) { if (box.isConnected) feedback.innerHTML = errorBar(err); }
    finally { button.disabled = false; }
  });
  const video = $('video', box);
  if (video) { video.classList.add('final-video'); video.setAttribute('aria-label', '書き出した完成動画'); }
  wireSafeToggle(box);
  video?.addEventListener('error', () => {
    if (!video.isConnected) return;
    $('.video-feedback', box).innerHTML = info('動画を再生できませんでした。ファイルの有無・対応形式を確認し、成果物を再読み込みしてください。', 'warning');
  });
  $$('.artifact-image', box).forEach(img => img.addEventListener('error', () => {
    if (!img.isConnected) return;
    $('.image-feedback', img.closest('.artifact-file')).innerHTML = info('画像を読み込めませんでした。「開く」で保存済みファイルを確認してください。', 'warning');
  }));
  $$('.text-preview', box).forEach(details => details.addEventListener('toggle', async () => {
    if (!details.open || details.dataset.loaded) return;
    details.dataset.loaded = 'true';
    const body = $('.preview-body', details);
    body.textContent = '読み込んでいます…';
    try {
      const preview = await api(`${fileUrl(details.closest('.artifact-file').dataset.key)}/preview`);
      if (!body.isConnected) return;
      body.innerHTML = `<pre class="artifact-text"></pre>${preview.truncated ? info('先頭65,536文字のみ表示しています。全文は「開く」または「保存」で確認してください。') : ''}`;
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
    postBody.innerHTML = `<p class="caption">投稿文はまだ生成されていないか、ファイルが見つかりません。</p><fluent-button class="post-create">投稿用の文章を作る</fluent-button>`;
    $('.post-create', postBody).addEventListener('click', loadPost);
  }
  const reload = document.createElement('fluent-button');
  reload.textContent = '成果物を再読み込み';
  reload.className = 'artifact-reload';
  reload.addEventListener('click', () => exportArtifacts(renderId, box));
  box.append(reload);
}

// ---------------- posting texts (YouTube / Viva Engage / chapters) ----------------
async function postPanel(renderId, box, regenerate = false) {
  box.innerHTML = '<p class="caption" role="status">投稿用の文章を読み込んでいます…</p>';
  let k;
  try { k = await api(`/api/renders/${renderId}/post`, regenerate ? { method: 'POST' } : {}); }
  catch (e) { if (box.isConnected) box.innerHTML = errorBar(e); return; }
  if (!box.isConnected) return;
  const block = (key, title, text, limit, hint) => `
    <div class="stack post-block" style="gap:4px">
      <div class="row" style="justify-content:space-between;flex-wrap:wrap;gap:8px">
        <h3 style="margin:0">${esc(title)}</h3>
        <div class="row"><span class="caption post-count" data-limit="${limit ?? ''}"></span>
          <fluent-button class="post-copy" data-key="${key}" size="small" appearance="primary">コピー</fluent-button></div></div>
      ${hint ? `<p class="caption" style="margin:0">${esc(hint)}</p>` : ''}
      <textarea class="post-text" data-key="${key}" rows="${Math.min(12, Math.max(4, text.split('\n').length + 1))}" aria-label="${esc(title)}" readonly spellcheck="false" style="width:100%;box-sizing:border-box;font:inherit;resize:vertical">${esc(text)}</textarea>
      <div class="post-feedback" role="status"></div>
    </div>`;
  box.innerHTML = `
    <p class="caption">${k.source === 'ai' ? 'AI が書き起こしをもとに作った案です（発話にない数字・名前は自動で除いています）。' : 'ひな形から作った案です。'}保存済みの文章を表示しています。投稿前に内容を確認し、修正は貼り付け先で行ってください。</p>
    ${(k.notes ?? []).length ? `<fluent-message-bar intent="warning" layout="multiline"><ul class="notes" style="margin:0">${k.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></fluent-message-bar>` : ''}
    ${block('youtube', 'YouTube の説明文', k.youtube, 5000, '説明欄にそのまま貼り付けます。最初の1〜2行が「もっと見る」の前に表示されます。')}
    ${block('engage', 'Viva Engage の投稿', k.engage, 10000, '社内向けの投稿文です。動画を添付して貼り付けます。')}
    ${k.chapters ? block('chapters', 'チャプター（タイムコード）', k.chapters, null, '書き出した動画の実際の時刻です（オープニング・カードの分も反映）。YouTube の説明欄に入れるとチャプターになります。') : `<p class="caption">チャプター: YouTube の条件（00:00 から・3つ以上・各10秒以上）を満たさないため付けません。</p>`}
    <div class="row"><fluent-button class="post-again" appearance="subtle">別の案を作る</fluent-button>
      <a href="/api/renders/${encodeURIComponent(renderId)}/files/post" target="_blank" rel="noopener">投稿文データ（JSON）を開く</a></div>`;
  const count = (ta) => {
    const c = ta.closest('.post-block').querySelector('.post-count'), lim = Number(c.dataset.limit);
    c.textContent = `${ta.value.length.toLocaleString()} 文字${lim ? ` / ${lim.toLocaleString()}` : ''}`;
    c.style.color = lim && ta.value.length > lim ? 'var(--colorPaletteRedForeground1, #c50f1f)' : '';
  };
  $$('.post-text', box).forEach(count);
  $$('.post-copy', box).forEach(b => b.addEventListener('click', async () => {
    const ta = $(`.post-text[data-key="${b.dataset.key}"]`, box);
    await copySnippet(ta.value, b, $('.post-feedback', ta.closest('.post-block')), ta);
  }));
  $('.post-again', box).addEventListener('click', () => {
    box.innerHTML = '<p class="caption" aria-live="polite">別の案を作っています…</p>';
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
        <div class="row" style="justify-content:space-between"><h1>書き出しが完了しました</h1><div class="row">${jobId ? `<fluent-anchor-button href="#/job/${esc(jobId)}" appearance="subtle">候補に戻る（ほかの動画を確認）</fluent-anchor-button><fluent-anchor-button href="#/job/${esc(jobId)}/exports" appearance="subtle">書き出し一覧</fluent-anchor-button>` : ''}</div></div>
        ${info(`自動チェック: ハード制約 ${hard.filter(q => q.status === 'pass').length}/${hard.length} 合格（ffprobe / loudnorm / 実測による検証）`, 'success')}
        <section class="card stack artifact-body"></section>
        <section class="card stack"><h2>自動チェック（QA）</h2>${qaList(r.qa)}</section>`;
      exportArtifacts(renderId, $('.artifact-body'));
    } else if (s.state === 'cancelled') {
      stop();
      main.innerHTML = `<h1>書き出しを取り消しました</h1>${info(s.message ?? '書き出しを取り消しました')}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}" style="margin-top:12px">確認・修正に戻る</fluent-anchor-button>`;
    } else if (s.state === 'stale') {
      stop();
      main.innerHTML = `<h1>書き出し結果は失効しています</h1>${info('編集計画が変更されたため、承認・本番QA・書き出し結果は現在の内容に使えません。再承認して書き出してください。', 'warning')}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}">確認・修正に戻る</fluent-anchor-button>`;
    } else if (s.state === 'failed') {
      stop();
      main.innerHTML = `<h1>書き出しできませんでした</h1>${errorBar({ message: s.message, code: s.error_code })}${s.failures ? `<section class="card">${qaList(s.failures)}</section>` : ''}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}" style="margin-top:12px">確認・修正に戻る</fluent-anchor-button>`;
    } else {
      if (!$('#expBar')) {
        main.innerHTML = `<h1>書き出しています</h1><section class="card"><p id="expMsg" aria-live="polite"></p>
          <div class="progress-row"><span class="caption">全体</span><fluent-progress-bar id="expBar" max="100" aria-label="書き出しの進み具合"></fluent-progress-bar><span id="expPct" class="caption pct"></span></div>
          <p class="caption" id="expStep"></p>
          <p class="caption">1080×1920 / H.264 / AAC 48kHz。音量は 2 パスで −14 LUFS に合わせ、書き出し後に自動チェックを行います。</p></section>`;
      }
      if ($('#expMsg').textContent !== (s.message ?? '')) $('#expMsg').textContent = s.message ?? '';
      const bar = $('#expBar');
      if (typeof s.progress === 'number') { bar.setAttribute('value', String(Math.round(s.progress * 100))); $('#expPct').textContent = `${Math.round(s.progress * 100)}%`; }
      else { bar.removeAttribute('value'); $('#expPct').textContent = ''; }
      $('#expStep').textContent = s.step ? `実行中: ${s.step}` : '';
    }
  };
  await poll();
  if (!finished && !gone()) timer = setInterval(() => poll().catch(e => {
    if (!finished && !gone()) { stop(); main.innerHTML = errorBar(e); }
  }), 1000);
}

refreshProvider();
router();
