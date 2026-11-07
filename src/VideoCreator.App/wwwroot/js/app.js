// VideoCreator UI — vanilla ES modules + Fluent 2 web components.
// Every dynamic string is escaped with esc() before reaching innerHTML (LLM/user text is untrusted).
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];
const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`;
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
  const init = { method: opts.method ?? 'GET', headers: { 'X-VideoCreator': '1' } };
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
function setSteps(current, done = current) {
  $('#steps').innerHTML = current < 0 ? '' : STEPS.map((s, i) => `<span class="step ${i === current ? 'current' : i < done ? 'done' : ''}" ${i === current ? 'aria-current="step"' : ''}><span class="n">${i < done && i !== current ? '✓' : i + 1}</span>${s}</span>`).join('');
}
function phone(src, id = '') {
  return `<div class="phone" ${id ? `id="${id}"` : ''}><video src="${esc(src)}" controls playsinline preload="metadata"></video><div class="safezone" aria-hidden="true"><i class="t"></i><i class="b"></i><i class="l"></i><i class="r"></i></div></div>`;
}
function field(label, control) { return `<fluent-field label-position="after"><label slot="label">${label}</label>${control}</fluent-field>`; }
function safeToggle() { return field('安全領域を表示', '<fluent-switch slot="input" class="safe-toggle"></fluent-switch>'); }
function wireSafeToggle(root = main) {
  $$('.safe-toggle', root).forEach(sw => sw.addEventListener('change', () => root.classList.toggle('show-safe', sw.checked)));
}
function qaList(qa) {
  return qa.map(q => `<div class="qa-row"><b>${esc(q.check_id)}</b><span class="${q.status === 'pass' ? 'pass' : q.status === 'fail' ? 'fail' : 'warnc'}">${q.status === 'pass' ? '合格' : q.status === 'fail' ? '不合格' : '警告'}</span><span>${esc(q.message)}: ${esc(q.measured)} <span class="muted">（基準 ${esc(q.threshold)}）</span></span></div>`).join('');
}
function qaBadges(qa) {
  if (!qa?.length) return '';
  const hard = qa.filter(q => q.check_id.startsWith('H-'));
  const fails = hard.filter(q => q.status === 'fail').length, warns = qa.filter(q => q.status === 'warn').length;
  return `<fluent-badge appearance="tint" color="${fails ? 'danger' : 'success'}">QA ${fails ? `不合格 ${fails}` : `合格 ${hard.filter(q => q.status === 'pass').length}/${hard.length}`}</fluent-badge>${warns ? `<fluent-badge appearance="tint" color="warning">警告 ${warns}</fluent-badge>` : ''}`;
}

// ---------------- settings / provider ----------------
async function refreshProvider() {
  try {
    const h = await api('/api/health');
    const names = { 'azure-openai': `Azure OpenAI（${h.llm_model ?? ''}）`, copilot: 'GitHub Copilot（インストール済み CLI）', none: 'AI なし（音声の盛り上がりで選定）' };
    const sel = h.llm_provider !== 'none' && !h.llm_model ? 'AI なし（設定した AI が使えません）' : names[h.llm_provider] ?? h.llm_provider;
    $('#provider').textContent = `${h.local_only ? 'ローカルのみ · ' : ''}書き起こし: ${h.asr === 'none' ? 'なし' : 'Azure AI Speech'} · 選定: ${sel}`;
    return h;
  } catch { $('#provider').textContent = ''; }
}
function llmChoice(value, title, lines, unavailable) {
  return `<fluent-field label-position="after" class="llm-choice">
    <label slot="label"><b>${esc(title)}</b>${lines.map(l => `<span class="caption">${esc(l)}</span>`).join('')}${unavailable ? `<span class="caption warnc">使えません：${esc(unavailable)}</span>` : ''}</label>
    <fluent-radio slot="input" value="${value}" ${unavailable ? 'disabled' : ''}></fluent-radio>
  </fluent-field>`;
}
$('#settings-btn').addEventListener('click', async () => {
  const s = await api('/api/settings'); const h = await api('/api/health');
  const local = s.local_only ? 'ローカルのみモードで起動しています' : '';
  const choices = [
    ['azure-openai', `Azure OpenAI（${s.azure_deployment}）`, [
      'おすすめ。JSON スキーマで出力を固定するため、候補の形式が崩れにくい方式です。',
      '送るもの：書き起こしの文章と音量の要約（動画・音声ファイルは送りません）。Entra ID（az login）でサインインします。',
      '料金：Azure の従量課金。生成前に見積もりを表示します。',
    ], local || (s.azure_configured ? '' : 'エンドポイント（VideoCreator:Llm:Endpoint）が未設定です。scripts/setup.ps1 を実行してください')],
    ['copilot', `GitHub Copilot（${s.copilot_model ?? '既定のモデル'}）`, [
      'PC にインストール済みの Copilot CLI を使います。ファイル操作などのツールは渡しません。',
      '送るもの：書き起こしの文章と音量の要約を GitHub に送ります。料金は Copilot のプランに含まれます。',
      '出力形式を強制できないため、まれに形式エラーになることがあります。',
    ], local || (s.copilot_cli ? '' : 'Copilot CLI が見つかりません（winget install GitHub.Copilot）')],
    ['none', 'AI を使わない', [
      '音量のピーク（盛り上がり）の周りを切り出します。選定のために外部へ送るものはなく、AI の料金もかかりません。',
      '話の内容は考慮しません。ツッコミテロップなど、AI が必要な機能は使えません。',
    ], ''],
  ];
  const cur = choices.find(c => c[0] === s.llm_provider);
  $('#settings-body').innerHTML = `
    <p id="llm-label">ハイライトを選ぶ AI を選んでください。字幕用の書き起こし${s.asr_configured ? '（Azure AI Speech）' : ''}はどれを選んでも同じです。</p>
    ${s.local_only ? info('ローカルのみモードのため、ハイライト選定には AI を使いません（「AI を使わない」に固定）。字幕用の書き起こしは引き続き使います。') : ''}
    ${!s.local_only && cur?.[3] ? info(`設定されている「${cur[1]}」は使えないため、現在は AI を使わずに選んでいます。`, 'warning') : ''}
    <fluent-radio-group id="llm" orientation="vertical" aria-labelledby="llm-label" value="${esc(s.llm_provider)}">
      ${choices.map(([v, t, l, u]) => llmChoice(v, t, l, u)).join('')}
    </fluent-radio-group>
    <p class="caption">AI の呼び出しに失敗して候補が得られなかった場合は、自動で「AI を使わない」方式に切り替えて続けます。</p>
    <fluent-divider style="margin:16px 0"></fluent-divider>
    <dl class="facts"><dt>FFmpeg</dt><dd>${esc(h.ffmpeg)}</dd><dt>不足フィルタ</dt><dd>${esc(h.missing_filters.join(', ') || 'なし')}</dd><dt>Copilot CLI</dt><dd>${esc(s.copilot_cli ?? '未検出')}</dd><dt>パターン数</dt><dd>${h.presets}</dd></dl>`;
  $('#llm')?.addEventListener('change', async () => {
    const radios = $$('#llm fluent-radio');
    const value = radios.find(r => r.checked)?.value;
    if (!value || value === s.llm_provider) return;
    try { await api('/api/settings', { method: 'PUT', body: { llm_provider: value } }); s.llm_provider = value; $('#settings-body fluent-message-bar[intent=warning]')?.remove(); await refreshProvider(); }
    catch (e) { radios.forEach(r => { r.checked = r.value === s.llm_provider; }); $('#settings-body').insertAdjacentHTML('afterbegin', errorBar(e)); }
  });
  $('#settings-dialog').show();
});
$('#settings-close').addEventListener('click', () => $('#settings-dialog').hide());

// ---------------- routes ----------------
const routes = [
  [/^#?\/?$/, home],
  [/^#\/p\/([\w-]+)$/, project],
  [/^#\/job\/([\w-]+)$/, job],
  [/^#\/ep\/([\w-]+)$/, review],
  [/^#\/render\/([\w-]+)$/, exportView],
];
let cleanup = null;
async function router() {
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
async function project(id) {
  const [d, presets, bgmList] = await Promise.all([api(`/api/projects/${id}`), api('/api/presets'), api(`/api/bgm?project_id=${encodeURIComponent(id)}`)]);
  const assets = d.assets, rights = new Set(d.rights.map(r => r.asset_id));
  const allRights = assets.length > 0 && assets.every(a => rights.has(a.id));
  let selected = sessionStorage.getItem(`preset:${id}`) ?? 'P4';
  const saved = JSON.parse(sessionStorage.getItem(`opts:${id}`) ?? '{}');
  const opts = { scope: saved.scope ?? 'balanced', bgm: saved.bgm ?? 'auto', sfx: saved.sfx ?? true, tsukkomi: saved.tsukkomi ?? false, series: saved.series ?? false, series_by: saved.series_by ?? 'parts', parts: saved.parts ?? 2, part_minutes: saved.part_minutes ?? 15 };
  setSteps(assets.length && allRights ? 1 : 0);
  let estimate = null;
  if (assets.length) try { estimate = await api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`); } catch { }
  const lastJob = d.jobs[0];
  const roleLabel = { main: '基準（音声の元）', cam_a: 'カメラA', cam_b: 'カメラB', wide: '引き（全体）', audio_master: '音声のみの基準' };
  const scopeLabel = { economy: ['節約', '盛り上がりの大きい区間だけを AI に送ります（速い・安い）'], balanced: ['標準', '半分程度に絞って AI に送ります'], full: ['全文', '書き起こし全体を AI に送ります（いちばん見落としが少ない）'] };
  const jpy = (c) => `¥${Number(c.estimated_jpy).toFixed(1)}`;
  const long = estimate && estimate.duration > 1800;

  main.innerHTML = `
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
          <p class="caption">MP4 / MOV / MKV / WebM、10秒〜3時間。複数カメラの切り替え（P7）では同じ場面を撮った動画を複数追加します。</p>
          <input type="file" id="file" accept="video/*,.mkv" class="sr-only">
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
                <span class="caption">${a.sync_confidence != null ? `同期の信頼度 ${a.sync_confidence}` : '生成時に音声で自動同期'}</span></div>` : ''}
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
            <div class="card selectable ${p.available ? '' : 'disabled'} ${p.id === selected ? 'selected' : ''}" role="radio" aria-checked="${p.id === selected}" aria-disabled="${!p.available}" tabindex="${p.available ? 0 : -1}" data-id="${esc(p.id)}" style="padding:16px">
              <div class="card-head"><span><span class="pid">${esc(p.id)}</span> <b>${esc(p.name)}</b></span>
                <span class="badges"><fluent-badge appearance="tint" color="informative">${esc(p.genre)}</fluent-badge>${p.available ? `<fluent-badge appearance="tint" color="brand">${p.target.duration_sec[0]}〜${p.target.duration_sec[1]}秒</fluent-badge>` : '<fluent-badge appearance="tint" color="subtle">準備中</fluent-badge>'}</span></div>
              ${p.card ? `<dl class="facts"><dt>冒頭</dt><dd>${esc(p.card.opening)}</dd><dt>テンポ</dt><dd>${esc(p.card.tempo)}</dd><dt>テロップ</dt><dd>${esc(p.card.telop)}</dd><dt>音</dt><dd>${esc(p.card.sound)}</dd></dl>` : ''}
              ${p.available ? '' : `<div class="caption">${esc(p.unavailable_reason)}</div>`}
              <div class="caption">例: ${p.examples.map(x => `<a href="${esc(x.url)}" target="_blank" rel="noopener noreferrer">${esc(x.title)}</a>`).join(' / ')}</div>
            </div>`).join('')}
        </div>
      </section>
      <section class="card step-col" aria-labelledby="h-gen">
        <h2 id="h-gen">3. 生成する</h2>
        <div class="col-body stack" style="gap:16px">
        <div class="stack">
          <h3>シリーズに分ける（前編・中編・後編…）</h3>
          ${field('長尺を、前編・後編のように続けて見るシリーズに分ける', `<fluent-switch slot="input" id="seriesSw" ${opts.series ? 'checked' : ''}></fluent-switch>`)}
          <fieldset class="series-by" aria-describedby="seriesInfo"><legend class="caption">分け方</legend>
            <label class="caption"><input type="radio" name="seriesBy" value="parts" ${opts.series_by === 'parts' ? 'checked' : ''}> 本数で指定 <input class="native" type="number" id="partCount" min="2" max="20" step="1" value="${esc(opts.parts)}" style="width:64px" aria-label="本数"> 本</label>
            <label class="caption"><input type="radio" name="seriesBy" value="minutes" ${opts.series_by !== 'parts' ? 'checked' : ''}> 1本の長さで指定 <input class="native" type="number" id="partMin" min="1" max="60" step="0.5" value="${esc(opts.part_minutes)}" style="width:72px" aria-label="1本あたりの分数"> 分</label>
          </fieldset>
          <p class="caption" id="seriesInfo" aria-live="polite"></p>
        </div>
        <div id="scopeBox">
          <h3>長い動画の絞り込み（速さ・費用と見落としの少なさ）</h3>
          <div class="grid" id="scopes" role="radiogroup" aria-label="長尺の絞り込み">
            ${(estimate?.scopes ?? [{ scope: 'economy' }, { scope: 'balanced' }, { scope: 'full' }]).map(s => `
              <div class="card selectable ${s.scope === opts.scope ? 'selected' : ''}" role="radio" aria-checked="${s.scope === opts.scope}" tabindex="0" data-scope="${s.scope}" style="padding:12px">
                <div class="card-head"><b>${scopeLabel[s.scope][0]}</b>${s.cost ? `<span class="badges"><fluent-badge appearance="tint" color="informative">AI に送る量 約${Math.round(s.share * 100)}%</fluent-badge><fluent-badge appearance="tint" color="brand">約 ${jpy(s.cost)}</fluent-badge></span>` : ''}</div>
                <div class="caption">${scopeLabel[s.scope][1]}</div>
              </div>`).join('')}
          </div>
          <p class="caption" style="margin-top:8px">${estimate ? (long ? '30分を超える動画では、選んだ項目によって AI に送る量が変わります。' : 'この長さの動画は、どの項目でも全文を AI に送るため結果は同じです。') : '素材を追加すると、費用の目安が表示されます。'}</p>
        </div>
        <div class="stack">
          <h3>音</h3>
          <label class="caption">BGM <select class="native" id="bgm">
            <option value="auto" ${opts.bgm === 'auto' ? 'selected' : ''}>パターンに任せる</option><option value="none" ${opts.bgm === 'none' ? 'selected' : ''}>なし</option>
            ${bgmList.map(b => `<option value="${esc(b.id)}" ${opts.bgm === b.id ? 'selected' : ''}>${esc(b.name)}${b.source === 'user' ? '（自分の曲）' : ''}${b.bpm ? ` ${Math.round(b.bpm)}BPM` : ''}</option>`).join('')}
          </select></label>
          ${field('効果音（ポン・ピンポン・ドーン など）', `<fluent-switch slot="input" id="sfx" ${opts.sfx ? 'checked' : ''}></fluent-switch>`)}
          ${field('ツッコミテロップを AI に提案させる（P3 のみ。承認するまで書き出せません）', `<fluent-switch slot="input" id="tsukkomi" ${opts.tsukkomi ? 'checked' : ''}></fluent-switch>`)}
          <details><summary class="caption">自分の BGM を追加する</summary>
            <form id="bgmForm" class="stack" style="margin-top:8px">
              <input type="file" id="bgmFile" accept="audio/*" class="native" aria-label="BGM ファイル">
              ${field('この曲を使う権利（著作権・利用許諾）を自分が持っています', '<fluent-checkbox slot="input" id="bgmRights"></fluent-checkbox>')}
              <div><fluent-button type="submit" size="small">BGM を追加</fluent-button></div><div id="bgmMsg"></div>
            </form></details>
        </div>
        </div>
        <div class="col-foot">
          ${estimate ? `<p class="caption" id="costLine"></p>` : ''}
          <div id="genMsg"></div>
          <fluent-button appearance="primary" id="gen" ${assets.length && allRights ? '' : 'disabled'}>候補を3本つくる</fluent-button>
          ${!assets.length ? '<span class="caption"> 素材を追加してください</span>' : !allRights ? '<span class="caption"> 権利確認にチェックしてください</span>' : ''}
        </div>
      </section>
    </div>`;
  document.body.classList.add('fit');

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

  const saveOpts = () => sessionStorage.setItem(`opts:${id}`, JSON.stringify(opts));
  const showCost = () => {
    const el = $('#costLine'); if (!el || !estimate) return;
    const c = (estimate.scopes.find(s => s.scope === (opts.series ? 'full' : opts.scope)) ?? estimate.scopes[1]).cost;
    el.textContent = `推定コスト: 約 ${jpy(c)}（書き起こし ${c.asr_minutes} 分、AI 入力 約 ${c.llm_input_tokens.toLocaleString()} トークン）— ${c.basis}`;
  };
  showCost();
  const seriesLabels = (n) => n === 2 ? ['前編', '後編'] : n === 3 ? ['前編', '中編', '後編'] : Array.from({ length: n }, (_, i) => `第${i + 1}話`);
  const showSeries = () => {
    const on = opts.series, byParts = opts.series_by === 'parts', min = Number(opts.part_minutes), parts = Number(opts.parts);
    $('#partMin').disabled = !on || byParts; $('#partCount').disabled = !on || !byParts;
    $$('input[name=seriesBy]').forEach(r => { r.disabled = !on; });
    $('#scopeBox').classList.toggle('muted-box', on); $('#scopeBox').inert = on;
    const el = $('#seriesInfo');
    if (!on) { $('#gen').textContent = '候補を3本つくる'; el.textContent = 'オフのときは、見どころを切り出したショート候補を3本つくります。'; return; }
    $('#gen').textContent = byParts && parts >= 2 ? `${seriesLabels(parts).length}本（${seriesLabels(parts).slice(0, 3).join('・')}${parts > 3 ? '…' : ''}）をつくる` : '前編・後編をつくる';
    if (!estimate) { el.textContent = '素材を追加すると、1本あたりの長さの目安が表示されます。'; return; }
    // the length after jump cuts is known once the video has been transcribed (any earlier generation); otherwise the raw length is used
    const se = estimate.series ?? {}, edited = se.edited_sec, base = edited ?? estimate.duration;
    const basis = edited != null ? `無音カット後の推定 ${fmt(edited)}` : `元の長さ ${fmt(estimate.duration)}（無音のカットで短くなります）`;
    const max = se.max_parts ?? 0;
    $('#partCount').max = String(Math.max(2, max));
    const tail = '分け目は時間ではなく話題の切れ目で決まるため、各パートの長さは前後します。各パートの冒頭に見どころ、2本目以降に前のパートのおさらい、最後に「〜につづく」と次の予告が入ります。AI には書き起こし全体を送ります。';
    if (byParts) {
      if (!(Number.isInteger(parts) && parts >= 2 && parts <= 20)) { el.textContent = '本数は 2〜20 本の整数で指定してください。'; return; }
      if (max < 2) { el.textContent = `この動画（${basis}）は短いため分けられません（1本あたり1分以上が必要です）。`; return; }
      if (parts > max) { el.textContent = `${parts}本では1本あたり1分未満になります。この動画（${basis}）は最大 ${max} 本まで分けられます。`; return; }
      el.textContent = `${parts}本（${seriesLabels(parts).join('・')}）、1本あたり 約${fmt(base / parts)}（${basis}を${parts}等分した目安）。${tail}`;
      return;
    }
    if (!(min >= 1 && min <= 60)) { el.textContent = '1本あたりの長さは 1〜60 分で指定してください。'; return; }
    const n = Math.round(base / (min * 60));
    el.textContent = n < 2
      ? `この動画（${basis}）は ${min} 分の2本分に足りません。1本あたりの長さを短くするか、本数で指定してください。`
      : `約 ${n} 本（${seriesLabels(n).join('・')}）、1本あたり 約${fmt(base / n)}（${basis}から）。${tail}`;
  };
  showSeries();
  $('#seriesSw').addEventListener('change', () => { opts.series = $('#seriesSw').checked; saveOpts(); showSeries(); showCost(); });
  $('#partMin').addEventListener('input', () => { opts.part_minutes = Number($('#partMin').value); saveOpts(); showSeries(); });
  $('#partCount').addEventListener('input', () => { opts.parts = Number($('#partCount').value); saveOpts(); showSeries(); });
  $$('input[name=seriesBy]').forEach(r => r.addEventListener('change', () => { if (r.checked) { opts.series_by = r.value; saveOpts(); showSeries(); } }));
  $$('#scopes [role=radio]').forEach(el => {
    const pick = () => { opts.scope = el.dataset.scope; saveOpts(); $$('#scopes [role=radio]').forEach(c => { c.classList.toggle('selected', c === el); c.setAttribute('aria-checked', String(c === el)); }); showCost(); };
    el.addEventListener('click', pick);
    el.addEventListener('keydown', (e) => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); pick(); } });
  });
  $('#bgm').addEventListener('change', () => { opts.bgm = $('#bgm').value; saveOpts(); });
  $('#sfx').addEventListener('change', () => { opts.sfx = $('#sfx').checked; saveOpts(); });
  $('#tsukkomi').addEventListener('change', () => { opts.tsukkomi = $('#tsukkomi').checked; saveOpts(); });
  $('#bgmForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const f = $('#bgmFile').files[0];
    if (!f) { $('#bgmMsg').innerHTML = info('音声ファイルを選んでください', 'warning'); return; }
    const fd = new FormData(); fd.append('file', f); fd.append('rights_confirmed', $('#bgmRights').checked ? 'true' : 'false');
    try {
      const r = await fetch(`/api/projects/${id}/bgm`, { method: 'POST', body: fd, headers: { 'X-VideoCreator': '1' } });
      const data = await r.json(); if (!r.ok) throw Object.assign(new Error(data?.error?.message ?? r.statusText), { code: data?.error?.code });
      opts.bgm = data.id; saveOpts(); router();
    } catch (e) { $('#bgmMsg').innerHTML = errorBar(e); }
  });
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
  const choose = (el) => {
    if (el.classList.contains('disabled')) return;
    selected = el.dataset.id; sessionStorage.setItem(`preset:${id}`, selected);
    $$('#presets [role=radio]').forEach(c => { c.classList.toggle('selected', c === el); c.setAttribute('aria-checked', String(c === el)); });
    // the pattern's pause threshold changes the length after jump cuts, so refresh the series preview
    if (assets.length) api(`/api/projects/${id}/estimate?preset=${encodeURIComponent(selected)}`).then(e => { estimate = e; showSeries(); }).catch(() => { });
  };
  $$('#presets [role=radio]').forEach(el => {
    el.addEventListener('click', (e) => { if (e.target.closest('a')) return; choose(el); });
    el.addEventListener('keydown', (e) => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); choose(el); } });
  });
  $('#gen').addEventListener('click', async () => {
    const { series, series_by, parts, part_minutes, ...rest } = opts;
    const options = { ...rest, parts: series && series_by === 'parts' ? Number(parts) : null, part_minutes: series && series_by !== 'parts' ? Number(part_minutes) : null };
    try { const r = await api(`/api/projects/${id}/jobs`, { method: 'POST', body: { preset_ids: [selected], options } }); location.hash = `#/job/${r.job_id}`; }
    catch (e) { $('#genMsg').innerHTML = errorBar(e); }
  });
}

// ---------------- job: progress (SSE) + candidates ----------------
async function job(jobId) {
  const j = await api(`/api/jobs/${jobId}`);
  if (j.status === 'ready' || j.status === 'done') return candidates(j);
  setSteps(2);
  const started = Date.now();
  const render = (stage, progress, message, failed) => {
    const idx = STAGES.findIndex(s => s[0] === stage);
    main.innerHTML = `
      <h1>生成しています</h1>
      <div class="grid cols-2" style="align-items:start">
        <section class="card">
          ${failed ? errorBar(failed) : `<p aria-live="polite">${esc(message)}</p>`}
          <fluent-progress-bar max="100" value="${Math.round((progress ?? 0) * 100)}" aria-label="進捗"></fluent-progress-bar>
          <p class="caption" style="margin-top:8px">経過 <span id="elapsed">0:00</span>${j.cost ? ` · 推定コスト 約 ¥${j.cost.estimated_jpy.toFixed(1)}` : ''}</p>
          <div>${STAGES.map(([k, label], i) => `<div class="stage ${i < idx || stage === 'ready' ? 'done' : i === idx ? 'active' : ''}"><span class="dot"></span><span>${label}</span><span class="caption">${i < idx ? '完了' : i === idx ? '実行中' : ''}</span></div>`).join('')}</div>
          <div class="row" style="margin-top:12px">${failed ? `<fluent-anchor-button href="#/p/${esc(j.project_id)}">プロジェクトに戻る</fluent-anchor-button>` : '<fluent-button id="cancel">キャンセル</fluent-button>'}</div>
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
  render(j.status, j.progress, j.message);
  const timer = setInterval(() => { const el = $('#elapsed'); if (el) el.textContent = fmt((Date.now() - started) / 1000); }, 1000);
  const es = new EventSource(`/api/jobs/${jobId}/events`);
  es.onmessage = async (ev) => {
    const u = JSON.parse(ev.data);
    if (u.stage === 'ready') { es.close(); clearInterval(timer); candidates(await api(`/api/jobs/${jobId}`)); return; }
    if (u.stage === 'failed' || u.stage === 'cancelled') {
      es.close(); clearInterval(timer);
      const cur = await api(`/api/jobs/${jobId}`);
      render(u.stage, 0, u.message, { message: cur.message, code: cur.error_code ?? (u.stage === 'cancelled' ? 'CANCELLED' : '') });
      return;
    }
    render(u.stage, u.progress, u.message);
  };
  cleanup = () => { es.close(); clearInterval(timer); };
}

async function candidates(j) {
  setSteps(3);
  const list = await api(`/api/jobs/${j.id}/candidates`);
  if (list.length && list[0].candidate.series) return seriesView(j, list);
  const bar = (label, v) => `<span>${label}</span><span class="bar"><span style="width:${v * 10}%"></span></span><span>${Number(v).toFixed(v % 1 ? 1 : 0)}</span>`;
  main.innerHTML = `
    <div class="row" style="justify-content:space-between"><h1>候補（${list.length}件）</h1>
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}" appearance="subtle">素材・パターンに戻る</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(w, 'warning')).join('')}
    <p class="caption">選定: ${esc(j.llm_model ?? '')} · プロンプト ${esc(j.prompt_version ?? '')} · 実績コスト 約 ¥${(j.cost?.estimated_jpy ?? 0).toFixed(2)} · 所要 ${Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)} 秒</p>
    <div class="grid cols-cands">
      ${list.map(({ candidate: c, preview_url, preview }) => `
        <article class="card stack" aria-label="候補 ${c.rank}">
          <div class="card-head"><b>#${c.rank}</b><span class="badges">${qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${(c.segments.reduce((a, s) => a + s.t1 - s.t0, 0)).toFixed(0)}秒 素材</fluent-badge>${c.source === 'fallback' ? '<fluent-badge appearance="tint" color="warning">信号ベース</fluent-badge>' : ''}</span></div>
          ${preview_url ? phone(preview_url) : '<p class="muted">プレビューなし</p>'}
          <div class="headline">${esc(c.headline || c.hook_text || '（見出しなし）')}</div>
          <div class="caption">${c.segments.map(s => `${fmt(s.t0)}–${fmt(s.t1)}`).join(' + ')}</div>
          <p>${esc(c.reason)}</p>
          <div class="scores">${bar('冒頭の引き', c.scores.hook)}${bar('単体で通じる', c.scores.standalone)}${bar('オチ', c.scores.payoff)}${bar('感情', c.scores.emotion)}${bar('音声の勢い', c.scores.signal)}${bar('総合', c.scores.final)}</div>
          ${c.notes?.length ? `<details><summary class="caption">調整の記録（${c.notes.length}）</summary><ul class="notes">${c.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button appearance="primary" href="#/ep/${esc(c.edit_plan_id)}">この候補を確認・修正</fluent-anchor-button>
        </article>`).join('')}
    </div>
    <section class="card" style="margin-top:16px">
      <h2>ほかの候補を出す</h2>
      <div class="chips" id="chips">${['もっと短く', '別の場面', 'もっと笑える所', 'オチがはっきりした所'].map(t => `<fluent-button size="small" data-t="${esc(t)}">${esc(t)}</fluent-button>`).join('')}</div>
      <div class="row" style="margin-top:8px"><fluent-text-input id="instr" maxlength="200" placeholder="追加の指示（任意）" style="flex:1" aria-label="追加の指示"></fluent-text-input><fluent-button id="regen">再生成</fluent-button></div>
      <div id="regenMsg"></div>
    </section>`;
  wireSafeToggle();
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
      <div class="row">${safeToggle()}<fluent-anchor-button href="#/p/${esc(j.project_id)}" appearance="subtle">素材・パターンに戻る</fluent-anchor-button></div></div>
    ${(j.warnings ?? []).map(w => info(w, 'warning')).join('')}
    <p class="caption">${first.requested_parts ? `${first.requested_parts}本に指定（1本あたり 約${(first.target_sec / 60).toFixed(1)}分）` : `1本あたり 約${(first.target_sec / 60).toFixed(1)}分の指定`} · 合計 ${fmt(total)} · 章分け: ${esc(j.llm_model && j.llm_model !== 'none' ? j.llm_model : '書き起こしの語彙の変化（AI なし）')} · 実績コスト 約 ¥${(j.cost?.estimated_jpy ?? 0).toFixed(2)} · 所要 ${Object.values(j.stage_seconds ?? {}).reduce((a, b) => a + b, 0).toFixed(0)} 秒</p>
    <div class="grid cols-cands">
      ${list.map(({ candidate: c, preview_url, preview }) => {
        const se = c.series;
        return `
        <article class="card stack" aria-label="${esc(se.label)}">
          <div class="card-head"><b>${esc(se.label)}</b><span class="badges">${qaBadges(preview?.qa)}<fluent-badge appearance="tint" color="brand">${preview ? fmt(preview.duration) : '—'}</fluent-badge></span></div>
          ${preview_url ? phone(preview_url) : '<p class="muted">プレビューなし</p>'}
          <div class="headline">${esc(c.hook_text)}</div>
          <div class="caption">元動画の ${fmt(se.body.t0)}–${fmt(se.body.t1)}（本編）</div>
          <p class="caption">${se.part < se.parts ? `分け目: ${esc(se.boundary)}` : '最後のパート'}</p>
          ${se.chapters?.length ? `<details open><summary class="caption">構成（${se.chapters.length}）</summary><ul class="notes">${se.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          ${c.notes?.length ? `<details><summary class="caption">編集の記録（${c.notes.length}）</summary><ul class="notes">${c.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></details>` : ''}
          <fluent-anchor-button appearance="primary" href="#/ep/${esc(c.edit_plan_id)}">${esc(se.label)}を確認・書き出し</fluent-anchor-button>
        </article>`;
      }).join('')}
    </div>
    <p class="caption" style="margin-top:16px">分け目を変えたいときは、プロジェクトに戻って本数か1本あたりの長さを変えて作り直してください。書き出しはパートごとに行います（投稿用メタデータにチャプターとシリーズのリンク欄が入ります）。</p>`;
  wireSafeToggle();
}

// ---------------- review & edit ----------------
async function review(epId) {
  setSteps(4);
  let { plan, qa } = await api(`/api/editplans/${epId}`);
  const job = await api(`/api/jobs/${plan.provenance.job_id}`);
  const cands = await api(`/api/jobs/${job.id}/candidates`);
  const me = cands.find(x => x.candidate.edit_plan_id === epId);
  const bgmList = await api(`/api/bgm?project_id=${encodeURIComponent(plan.project_id)}`);
  const projAssets = (await api(`/api/projects/${plan.project_id}`)).assets;
  const assetName = (aid) => (projAssets.find(a => a.id === aid)?.path ?? aid).split(/[\\/]/).pop();
  let tr = await api(`/api/assets/${plan.audio.master}/transcript`);
  const roleName = { teaser: '先出し', reaction: 'リアクション', wide: '引き', recap: 'おさらい', next: '次回予告' };
  let previewUrl = me?.preview_url, stale = false;
  const typeLabel = { caption: '字幕', emphasis: '強調', hook: 'フック', headline: '見出し', tsukkomi: 'ツッコミ', sfx: '擬音', counter: 'カウンター' };
  const width = (s) => [...s].reduce((a, c) => a + (c.charCodeAt(0) < 0x2e80 || (c.charCodeAt(0) >= 0xff61 && c.charCodeAt(0) <= 0xffdc) ? 0.5 : 1), 0);

  const draw = () => {
    const hardFail = qa.some(q => q.check_id.startsWith('H-') && q.status === 'fail');
    const maxW = 14, maxCps = 6;
    main.innerHTML = `
      <div class="row" style="justify-content:space-between"><h1>確認・修正</h1>
        <div class="row">${safeToggle()}<fluent-anchor-button href="#/job/${esc(job.id)}" appearance="subtle">候補に戻る</fluent-anchor-button></div></div>
      <div class="grid cols-review" style="align-items:start">
        <section class="card stack">
          ${previewUrl ? phone(previewUrl + (stale ? '' : `?v=${Date.now()}`), 'pv') : ''}
          ${stale ? info('区間を変えたため、プレビューが古くなっています。', 'warning') : ''}
          <fluent-button id="repreview" ${stale ? 'appearance="primary"' : ''}>プレビューを更新</fluent-button>
          <p class="caption">${plan.series ? `${esc(plan.series.label)}（${plan.series.part}/${plan.series.parts}）· ` : ''}長さ ${(() => { const d = plan.timeline.reduce((a, c) => a + (c.src_out - c.src_in) / (c.speed || 1), 0); return d >= 180 ? fmt(d) : `${d.toFixed(1)} 秒`; })()} · カット ${plan.timeline.length} · テロップ ${plan.telops.filter(t => t.enabled).length}</p>
          ${plan.series?.chapters?.length ? `<details><summary class="caption">構成・チャプター（${plan.series.chapters.length}）</summary><ul class="notes">${plan.series.chapters.map(ch => `<li>${fmt(ch.start)} ${esc(ch.title)}</li>`).join('')}</ul></details>` : ''}
          <details><summary class="caption">カット一覧（映像・速度）</summary>
            <ul class="notes">${plan.timeline.map(c => `<li>${esc(c.clip_id)} ${esc(assetName(c.asset_id))}${c.role !== 'body' ? `（${esc(roleName[c.role] ?? c.role)}）` : ''} ${((c.src_out - c.src_in) / (c.speed || 1)).toFixed(1)}秒${c.speed && c.speed !== 1 ? ` ×${c.speed}倍速` : ''}${c.reframe?.mode === 'face' ? ' · 顔を追う' : ''}</li>`).join('')}</ul></details>
          <h3>音</h3>
          <div class="row"><label class="caption">BGM <select class="native" id="bgmSel">
            <option value="none" ${!plan.audio.bgm ? 'selected' : ''}>なし</option>${bgmList.map(b => `<option value="${esc(b.id)}" ${plan.audio.bgm === b.id ? 'selected' : ''}>${esc(b.name)}</option>`).join('')}</select></label>
            ${field('効果音', `<fluent-switch slot="input" id="sfxSw" ${plan.audio.sfx_enabled ? 'checked' : ''}></fluent-switch>`)}</div>
          <p class="caption">${plan.audio.sfx?.length ?? 0} 個の効果音${plan.audio.bgm ? ` · BGM ${plan.audio.duck ? 'は話している間だけ自動で小さくなります' : 'あり'}` : ''}。変更後は「プレビューを更新」で聞けます。</p>
          <details><summary class="caption">書き起こしを直す（誤認識の修正）</summary>
            <p class="caption">この動画に使われている範囲の単語です。直すと字幕が作り直されます（時刻はそのまま。字幕を手で直していた場合はやり直しになります）。</p>
            <div class="words">${tr.words.filter(w => plan.timeline.some(c => (w.t0 + w.t1) / 2 >= (c.audio_in ?? c.src_in) && (w.t0 + w.t1) / 2 <= (c.audio_in ?? c.src_in) + (c.src_out - c.src_in))).map(w => `<input class="native wd" data-id="${esc(w.id)}" value="${esc(w.text)}" size="${Math.max(2, [...w.text].length + 1)}" maxlength="30" aria-label="単語 ${esc(w.text)}">`).join('')}</div></details>
          ${plan.series ? '<p class="caption">前編・後編の区切りは前後のパートとつながっているため、ここでは動かせません（本数か1本あたりの長さを変えて作り直します）。</p>' : `<h3>区間の調整（文単位）</h3>
          <div class="row"><fluent-button size="small" data-adj="start:1">開始を1文前へ</fluent-button><fluent-button size="small" data-adj="start:-1">開始を1文後へ</fluent-button></div>
          <div class="row"><fluent-button size="small" data-adj="end:-1">終了を1文前へ</fluent-button><fluent-button size="small" data-adj="end:1">終了を1文後へ</fluent-button></div>`}
          <h3 style="margin-top:8px">自動チェック</h3>
          <div>${qaList(qa)}</div>
          <fluent-button appearance="primary" id="export" ${hardFail ? 'disabled' : ''}>書き出し（1080×1920）</fluent-button>
          ${hardFail ? '<span class="caption fail">不合格の項目を直すと書き出せます</span>' : ''}
          <div id="msg"></div>
        </section>
        <section class="card">
          <div class="row" style="justify-content:space-between"><h2>テロップ</h2>
            <div class="row">${['caption', 'emphasis', 'headline', 'counter', 'sfx', 'tsukkomi'].filter(t => plan.telops.some(x => x.type === t) || ['caption', 'emphasis', 'headline'].includes(t)).map(t => field(typeLabel[t], `<fluent-switch slot="input" data-type="${t}" ${plan.telops.filter(x => x.type === t).some(x => x.enabled) || !plan.telops.some(x => x.type === t) ? 'checked' : ''}></fluent-switch>`)).join('')}
              ${plan.telops.some(t => t.type === 'tsukkomi' && !t.approved) ? '<fluent-button size="small" id="approveAll">ツッコミをすべて承認</fluent-button>' : ''}</div></div>
          <p class="caption">文字を直すと、文字数・表示速度・安全領域の自動チェックがすぐにやり直されます。1行は全角${maxW}字まで、字幕は毎秒${maxCps}字までが目安です。</p>
          <div class="scroll"><table class="list"><thead><tr><th>種類</th><th>時刻</th><th style="width:55%">テキスト</th><th>字数</th><th>速さ</th><th>表示</th></tr></thead><tbody>
            ${plan.telops.map(t => {
              const w = width(t.text), cps = w / Math.max(0.05, t.end - t.start);
              return `<tr class="${t.enabled ? '' : 'off'}"><td><span class="tag ${esc(t.type)}">${esc(typeLabel[t.type] ?? t.type)}</span></td>
                <td class="caption">${t.start.toFixed(1)}–${t.end.toFixed(1)}</td>
                <td><input class="telop" data-id="${esc(t.id)}" value="${esc(t.text)}" maxlength="40" aria-label="テロップ ${esc(t.id)}"></td>
                <td class="${w > maxW && t.type !== 'headline' ? 'warn' : ''}">${w}</td>
                <td class="${t.type === 'caption' && cps > maxCps ? 'warn' : 'caption'}">${t.type === 'caption' ? cps.toFixed(1) : ''}</td>
                <td><fluent-switch data-id="${esc(t.id)}" class="en" ${t.enabled ? 'checked' : ''} aria-label="表示"></fluent-switch>${t.approved ? '' : `<fluent-button size="small" data-approve="${esc(t.id)}">承認</fluent-button>`}</td></tr>`;
            }).join('')}
          </tbody></table></div>
        </section>
      </div>`;
    wireSafeToggle();
    const patch = async (ops) => {
      try { const r = await api(`/api/editplans/${epId}`, { method: 'PATCH', body: { ops } }); plan = r.plan; qa = r.qa; stale = stale || r.preview_stale; draw(); }
      catch (e) { $('#msg').innerHTML = errorBar(e); }
    };
    $$('input.wd').forEach(inp => inp.addEventListener('change', async () => {
      try { tr = await api(`/api/assets/${plan.audio.master}/transcript`, { method: 'PATCH', body: { edits: [{ word_id: inp.dataset.id, text: inp.value }] } }); await patch([{ op: 'replan' }]); }
      catch (e) { $('#msg').innerHTML = errorBar(e); }
    }));
    $$('input.telop').forEach(inp => inp.addEventListener('change', () => patch([{ op: 'replace_text', target: inp.dataset.id, text: inp.value }])));
    $$('fluent-switch.en').forEach(sw => sw.addEventListener('change', () => patch([{ op: 'set_enabled', target: sw.dataset.id, enabled: sw.checked }])));
    $$('fluent-switch[data-type]').forEach(sw => sw.addEventListener('change', () => patch([{ op: 'set_type_enabled', type: sw.dataset.type, enabled: sw.checked }])));
    $$('[data-approve]').forEach(b => b.addEventListener('click', () => patch([{ op: 'approve', target: b.dataset.approve }])));
    $('#approveAll')?.addEventListener('click', () => patch([{ op: 'approve_all_tsukkomi' }]));
    $('#bgmSel')?.addEventListener('change', (e) => { patch([{ op: 'set_bgm', target: e.target.value }]); stale = true; });
    $('#sfxSw')?.addEventListener('change', (e) => { patch([{ op: 'set_sfx', enabled: e.target.checked }]); stale = true; });
    $$('[data-adj]').forEach(b => b.addEventListener('click', () => { const [edge, delta] = b.dataset.adj.split(':'); patch([{ op: 'adjust', edge, delta: Number(delta) }]); }));
    $('#repreview').addEventListener('click', async () => {
      $('#repreview').setAttribute('disabled', ''); $('#msg').innerHTML = '<fluent-spinner size="small"></fluent-spinner> プレビューを作成しています…';
      try { const r = await api(`/api/editplans/${epId}/preview`, { method: 'POST' }); previewUrl = r.preview_url; stale = false; qa = r.render.qa; draw(); }
      catch (e) { $('#msg').innerHTML = errorBar(e); $('#repreview').removeAttribute('disabled'); }
    });
    $('#export').addEventListener('click', async () => {
      try { const r = await api(`/api/editplans/${epId}/render`, { method: 'POST' }); location.hash = `#/render/${r.render_id}`; }
      catch (e) { $('#msg').innerHTML = errorBar(e) + (e.detail ? qaList(e.detail) : ''); }
    });
  };
  draw();
}

// ---------------- export ----------------
async function exportView(renderId) {
  setSteps(5);
  let timer = null;
  const poll = async () => {
    const s = await api(`/api/renders/${renderId}`);
    if (s.state === 'done') {
      clearInterval(timer);
      const r = s.render, files = r.outputs;
      const hard = r.qa.filter(q => q.check_id.startsWith('H-'));
      setSteps(5, 6);
      main.innerHTML = `
        <div class="row" style="justify-content:space-between"><h1>書き出しが完了しました</h1>${safeToggle()}</div>
        ${info(`自動チェック: ハード制約 ${hard.filter(q => q.status === 'pass').length}/${hard.length} 合格（ffprobe / loudnorm / 実測による検証）`, 'success')}
        <div class="grid cols-review" style="align-items:start">
          <section class="card stack">${phone(`/api/renders/${renderId}/files/mp4`)}
            <fluent-button id="open" appearance="primary">フォルダを開く</fluent-button>
            <p class="caption">${esc(r.path)}</p></section>
          <section class="card stack">
            <h2>ファイル</h2>
            <ul>${Object.entries(files).filter(([k]) => !k.startsWith('thumb')).map(([k, f]) => `<li><a href="/api/renders/${renderId}/files/${esc(k)}" target="_blank" rel="noopener">${esc(f.split(/[\\/]/).pop())}</a></li>`).join('')}</ul>
            <h3>サムネイル候補</h3>
            <div class="thumbs">${Object.keys(files).filter(k => k.startsWith('thumb')).map(k => `<a href="/api/renders/${renderId}/files/${k}" target="_blank" rel="noopener"><img src="/api/renders/${renderId}/files/${k}" alt="サムネイル候補 ${k.slice(5)}"></a>`).join('')}</div>
            <h3>自動チェック（QA）</h3>
            <div>${qaList(r.qa)}</div>
            <p class="caption">投稿用のタイトル・説明文の案は「.metadata.md」にあります。投稿前に内容を確認してください。</p>
          </section>
        </div>`;
      wireSafeToggle();
      $('#open').addEventListener('click', () => api(`/api/renders/${renderId}/open-folder`, { method: 'POST' }).catch(() => { }));
    } else if (s.state === 'failed') {
      clearInterval(timer);
      main.innerHTML = `<h1>書き出しできませんでした</h1>${errorBar({ message: s.message, code: s.error_code })}${s.failures ? `<section class="card">${qaList(s.failures)}</section>` : ''}
        <fluent-anchor-button href="#/ep/${esc(s.editplan_id)}" style="margin-top:12px">確認・修正に戻る</fluent-anchor-button>`;
    } else {
      main.innerHTML = `<h1>書き出しています</h1><section class="card"><p aria-live="polite">${esc(s.message)}</p><fluent-progress-bar aria-label="書き出し中"></fluent-progress-bar>
        <p class="caption">1080×1920 / H.264 / AAC 48kHz。音量は 2 パスで −14 LUFS に合わせ、書き出し後に自動チェックを行います。</p></section>`;
    }
  };
  await poll();
  timer = setInterval(() => poll().catch(() => { }), 1500);
  cleanup = () => clearInterval(timer);
}

refreshProvider();
router();
