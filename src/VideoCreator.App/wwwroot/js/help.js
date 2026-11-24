// Hover help (pop-up explanations for buttons) and the first-run tour. Both can be switched off in the settings screen.
import { trUi } from './i18n.js';

const KEY_TIPS = 'vc.help.tips', KEY_TOUR = 'vc.help.tour', KEY_SEEN = 'vc.help.tourSeen';
const read = key => { try { return globalThis.localStorage?.getItem(key); } catch { return null; } };
const write = (key, value) => { try { globalThis.localStorage?.setItem(key, value); } catch { /* In-memory defaults still work. */ } };
export const tipsEnabled = () => read(KEY_TIPS) !== 'off';
export const tourEnabled = () => read(KEY_TOUR) !== 'off';
export const setTipsEnabled = on => write(KEY_TIPS, on ? 'on' : 'off');
export const setTourEnabled = on => write(KEY_TOUR, on ? 'on' : 'off');

// [selector, resource key]. The nearest matching element wins; the first matching selector gives its text.
export const TIPS = [
  ['.brand', 'tip.brand'], ['#settings-btn', 'tip.settings'], ['.ui-language', 'tip.language'], ['a.step', 'tip.step'],
  ['#new fluent-button', 'tip.create'], ['.project-card .del', 'tip.projectDelete'],
  ['#delete-confirm', 'tip.deleteConfirm'], ['#delete-cancel', 'tip.deleteCancel'], ['#settings-close', 'tip.settingsClose'],
  ['[data-draft-discard]', 'tip.draftDiscard'],
  ['#pdirBrowse', 'tip.pdirBrowse'], ['#pdirCheck', 'tip.pdirCheck'], ['#pdirApply', 'tip.pdirApply'], ['#pdirRestore', 'tip.pdirRestore'],
  ['#settings-body [id$="Pick"]', 'tip.aiPick'], ['#settings-body [id$="Test"]', 'tip.aiTest'], ['#settings-body [id$="Save"]', 'tip.aiSave'],
  ['#settings-body [id$="Forget"]', 'tip.aiForget'], ['#cLoad', 'tip.copilotLoad'], ['#replay-tour', 'tip.replayTour'],
  ['#rename', 'tip.rename'], ['#rename-cancel', 'tip.renameCancel'], ['#pick', 'tip.pickFile'], ['#byPath fluent-button', 'tip.byPath'],
  ['.deck-del', 'tip.deckRemove'], ['.pip-scenes', 'tip.pipScenes'], ['.pip-save', 'tip.pipSave'],
  ['[data-share-bgm]', 'tip.bgmShare'], ['[data-del-bgm]', 'tip.bgmRemove'], ['#bgmForm fluent-button[type=submit]', 'tip.bgmAdd'],
  ['[data-del-shared]', 'tip.sharedBgmRemove'], ['#sharedBgmForm fluent-button[type=submit]', 'tip.sharedBgmAdd'],
  ['#kitBrowse', 'tip.kitBrowse'], ['#kitForm fluent-button[type=submit]', 'tip.kitRegister'], ['#kitClear', 'tip.kitClear'],
  ['#enhanceBtn', 'tip.enhance'], ['#makeCards', 'tip.makeCards'],
  ['#addProperNoun', 'tip.properAdd'], ['#saveProperNouns', 'tip.properSave'], ['.removeProperNoun', 'tip.properRemove'],
  ['#seriesSw', 'tip.sw.series'], ['#sfx', 'tip.sw.sfx'], ['#tsukkomi', 'tip.sw.tsukkomi'], ['#engageImageChoice', 'tip.sw.engage'], ['.safe-toggle', 'tip.sw.safe'],
  ['#gen', 'tip.gen'], ['.reuse', 'tip.patReuse'], ['.rm', 'tip.patDelete'], ['.rm-ok', 'tip.patDeleteOk'], ['.rm-cancel', 'tip.patDeleteCancel'],
  ['#cancel', 'tip.jobCancel'], ['#regen', 'tip.regen'],
  ['.appr-btn', 'tip.approve'], ['[data-approve]', 'tip.approve'], ['[data-approve-card]', 'tip.approve'],
  ['#pickAll', 'tip.pickAll'], ['#batchExport', 'tip.batchExport'], ['.exp-cancel', 'tip.expCancel'],
  ['[data-qa-approve]', 'tip.qaApprove'], ['#qaApproveAll', 'tip.qaApproveAll'], ['[data-repair-one]', 'tip.qaRepairOne'],
  ['#qaRepair', 'tip.qaRepair'], ['#qaUndo', 'tip.qaUndo'], ['#repreview', 'tip.repreview'], ['[data-adj]', 'tip.adjust'],
  ['#approveNext', 'tip.approveNext'], ['#approveBack', 'tip.approveBack'], ['#export', 'tip.export'],
  ['#approveAll', 'tip.approveAllQuips'], ['#approveCards', 'tip.approveAllCards'],
  ['.artifact-retry', 'tip.artifactRetry'], ['.path-copy', 'tip.pathCopy'], ['.folder-open', 'tip.folderOpen'],
  ['.post-create', 'tip.postCreate'], ['.post-copy', 'tip.postCopy'], ['.post-again', 'tip.postAgain'],

  ['#new #name', 'tip.newName'], ['#new-video-language', 'tip.videoLang'], ['#project-video-language', 'tip.videoLang'],
  ['#rename-input', 'tip.renameInput'], ['#path', 'tip.pathInput'], ['#kitPath', 'tip.kitPath'], ['#pdirPath', 'tip.pdirPath'],
  ['#pdirMove fluent-radio[value="move"]', 'tip.pdirMoveAll'], ['#pdirMove fluent-radio[value="keep"]', 'tip.pdirKeep'],
  ['#llm fluent-radio', 'tip.llm'], ['#cAuth fluent-radio', 'tip.auth'], ['#fAuth fluent-radio', 'tip.auth'],
  ['#cEffort fluent-radio', 'tip.effort'], ['#cTier fluent-radio', 'tip.tier'],
  ['#settings-body [id$="Resource"]', 'tip.resource'], ['#fChatDep', 'tip.chatDep'], ['#fEmbDep', 'tip.embDep'], ['#fImageDep', 'tip.imgDep'], ['#cModel', 'tip.cModel'],
  ['.rights', 'tip.rights'], ['#bgmRights', 'tip.rights'], ['#sharedBgmRights', 'tip.rights'], ['#kitRights', 'tip.rights'],
  ['#kitUpscale', 'tip.kitUpscale'], ['#kitCardSec', 'tip.kitSec'], ['#cardCount', 'tip.cardCount'],
  ['#partCount', 'tip.partCount'], ['#partMin', 'tip.partMin'], ['#shortCount', 'tip.shortCount'],
  ['input[name="seriesBy"][value="minutes"]', 'tip.seriesMinutes'], ['input[name="seriesBy"][value="parts"]', 'tip.seriesParts'],
  ['#maskWords', 'tip.maskWords'], ['#orientation fluent-radio', 'tip.orientation'], ['#shortMode fluent-radio', 'tip.shortMode'],
  ['#tsukkomiStyle fluent-radio', 'tip.tsukkomiStyle'], ['#analysisReuse fluent-radio', 'tip.analysisReuse'], ['#cardMake fluent-radio', 'tip.cardMake'],
  ['#openingSw', 'tip.sw.opening'], ['#closingSw', 'tip.sw.closing'], ['#cardsSw', 'tip.sw.cards'], ['#sfxSw', 'tip.sw.sfxPlan'], ['#useSharedBgm', 'tip.sw.sharedBgm'],
  ['fluent-switch.en', 'tip.sw.telopOne'], ['fluent-switch.ins-en', 'tip.sw.insertOne'], ['fluent-switch[data-type]', 'tip.sw.telopType'],
  ['.exp-pick', 'tip.expPick'], ['.pip-use', 'tip.pipUse'], ['.pip-start', 'tip.pipTime'], ['.pip-end', 'tip.pipTime'],
  ['input.telop', 'tip.telopEdit'], ['input.wd', 'tip.wordEdit'], ['input.card-text', 'tip.cardEdit'],
  ['#instr', 'tip.instr'], ['#chips fluent-button', 'tip.instrChip'], ['#videoSel', 'tip.videoSel'],
  ['#prevVideo', 'tip.prevVideo'], ['#nextVideo', 'tip.nextVideo'], ['#toExports', 'tip.toExports'],
  ['#bgm', 'tip.bgmPick'], ['#bgmSel', 'tip.bgmPick'], ['#bgmLd', 'tip.bgmLd'], ['#bgmGap', 'tip.bgmGap'], ['.bgm-trim', 'tip.bgmTrim'], ['.bgm-cue-track', 'tip.bgmCue'],
  ['#bgmFile', 'tip.bgmFile'], ['#sharedBgmFile', 'tip.bgmFile'], ['.deck-file', 'tip.deckFile'],
  ['.artifact-path', 'tip.pathText'], ['.post-text', 'tip.postText'],
];

// Anything without its own text above is explained from its kind and its label.
export const GENERIC = [
  ['fluent-button', 'tip.g.button'], ['fluent-anchor-button, a[href]', 'tip.g.link'], ['fluent-text-input, input[type=text], input:not([type])', 'tip.g.input'],
  ['input[type=number]', 'tip.g.number'], ['select', 'tip.g.select'], ['fluent-radio, input[type=radio]', 'tip.g.radio'],
  ['fluent-checkbox, fluent-switch, input[type=checkbox]', 'tip.g.check'], ['input[type=file]', 'tip.g.file'], ['textarea', 'tip.g.text'], ['summary', 'tip.g.details'],
];
const GENERIC_ALL = GENERIC.map(([selector]) => selector).join(',');
const clean = text => (text ?? '').replace(/\s+/g, ' ').trim();
const clip = text => (text.length > 50 ? `${text.slice(0, 50)}…` : text);

function labelOf(el) {
  const aria = clean(el.getAttribute('aria-label'));
  if (aria) return clip(aria);
  if (el.matches('fluent-button, fluent-anchor-button, a, summary, fluent-radio')) { const own = clean(el.textContent); if (own) return clip(own); }
  const wrap = el.closest('label');
  if (wrap) {
    const copy = wrap.cloneNode(true);
    copy.querySelectorAll('select, input, textarea, fluent-text-input, fluent-switch, fluent-checkbox').forEach(n => n.remove());
    const text = clean(copy.textContent); if (text) return clip(text);
  }
  const fieldLabel = clean(el.closest('fluent-field')?.querySelector('[slot=label]')?.textContent); if (fieldLabel) return clip(fieldLabel);
  return clip(clean(el.getAttribute('placeholder'))) || trUi('tip.g.unnamed');
}

const ALL = TIPS.map(([selector]) => selector).join(',');

// target: the element to point at (none = centered card)
export const TOUR = [
  { id: 'welcome', target: null },
  { id: 'new', target: '#new' },
  { id: 'steps', target: null },
  { id: 'output', target: null },
  { id: 'language', target: '.ui-language' },
  { id: 'settings', target: '#settings-btn' },
];

const popover = el => { el.popover = 'manual'; return el; };
const show = el => { try { el.showPopover(); } catch { el.hidden = false; } };
const hide = el => { try { el.hidePopover(); } catch { el.hidden = true; } };

// ---------------- hover help ----------------
function tipFor(target) {
  const start = target instanceof Element ? target : null;
  if (!start || start.closest('.vc-tour, #vc-tip')) return null;
  const el = start.closest(ALL);
  if (el) { const hit = TIPS.find(([selector]) => el.matches(selector)); if (hit) return { el, text: () => trUi(hit[1]) }; }
  const generic = start.closest(GENERIC_ALL);
  if (!generic) return null;
  const hit = GENERIC.find(([selector]) => generic.matches(selector));
  return hit ? { el: generic, text: () => trUi(hit[1], [labelOf(generic)]) } : null;
}

export function initTips() {
  const tip = popover(document.createElement('div'));
  tip.id = 'vc-tip'; tip.className = 'vc-tip'; tip.setAttribute('role', 'tooltip');
  document.body.append(tip);
  let timer = 0, owner = null;
  const close = () => {
    clearTimeout(timer); timer = 0;
    if (owner) { owner.removeAttribute('aria-describedby'); owner = null; }
    hide(tip);
  };
  const open = ({ el, text }) => {
    tip.textContent = text();
    owner = el;
    el.setAttribute('aria-describedby', tip.id);
    show(tip);
    const r = el.getBoundingClientRect(), t = tip.getBoundingClientRect();
    const below = r.bottom + 8 + t.height <= innerHeight;
    tip.style.top = `${Math.max(4, below ? r.bottom + 8 : r.top - 8 - t.height)}px`;
    tip.style.left = `${Math.min(Math.max(4, r.left + r.width / 2 - t.width / 2), innerWidth - t.width - 4)}px`;
  };
  const schedule = (found, delay) => {
    close();
    if (!found || !tipsEnabled()) return;
    timer = setTimeout(() => open(found), delay);
  };
  document.addEventListener('pointerover', e => {
    if (e.pointerType === 'touch') return;
    const found = tipFor(e.target);
    if (found?.el === owner) return;
    schedule(found, 350);
  });
  document.addEventListener('pointerout', e => {
    const to = e.relatedTarget instanceof Element ? tipFor(e.relatedTarget)?.el : null;
    if (owner && to !== owner) close(); else if (!owner && timer && !to) close();
  });
  document.addEventListener('focusin', e => { const found = tipFor(e.target); if (found && e.target.matches?.(':focus-visible')) schedule(found, 0); });
  document.addEventListener('focusout', close);
  document.addEventListener('pointerdown', close, true);
  document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); }, true);
  document.addEventListener('scroll', close, true);
  addEventListener('hashchange', close);
}

// ---------------- settings section ----------------
export function helpSettingsHtml() {
  const field = (label, control) => `<fluent-field label-position="after"><label slot="label">${label}</label>${control}</fluent-field>`;
  return `<section id="help-settings" aria-labelledby="help-title">
    <h3 id="help-title">${trUi('help.title')}</h3>
    ${field(trUi('help.tips'), `<fluent-switch slot="input" id="helpTips" ${tipsEnabled() ? 'checked' : ''}></fluent-switch>`)}
    <p class="caption">${trUi('help.tipsNote')}</p>
    ${field(trUi('help.tour'), `<fluent-switch slot="input" id="helpTour" ${tourEnabled() ? 'checked' : ''}></fluent-switch>`)}
    <p class="caption">${trUi('help.tourNote')}</p>
    <fluent-button id="replay-tour" type="button">${trUi('help.replay')}</fluent-button>
  </section>`;
}

export function wireHelpSettings(root, onReplay) {
  root.querySelector('#helpTips')?.addEventListener('change', e => setTipsEnabled(e.target.checked));
  root.querySelector('#helpTour')?.addEventListener('change', e => setTourEnabled(e.target.checked));
  root.querySelector('#replay-tour')?.addEventListener('click', onReplay);
}

// ---------------- first-run tour ----------------
let running = false;
export function startTour({ prepare } = {}) {
  if (running) return Promise.resolve();
  running = true;
  return (async () => {
    await prepare?.();
    const opener = document.activeElement;
    const layer = popover(document.createElement('div'));
    layer.className = 'vc-tour';
    layer.innerHTML = `<div class="vc-tour-spot" hidden></div>
      <div class="vc-tour-card" role="dialog" aria-modal="true" aria-labelledby="vc-tour-title" aria-describedby="vc-tour-body">
        <p class="caption" id="vc-tour-progress"></p><h2 id="vc-tour-title"></h2><p id="vc-tour-body"></p>
        <div class="vc-tour-actions"><fluent-button id="vc-tour-skip" type="button" appearance="subtle"></fluent-button><span class="spacer"></span>
        <fluent-button id="vc-tour-back" type="button"></fluent-button><fluent-button id="vc-tour-next" type="button" appearance="primary"></fluent-button></div>
      </div>`;
    document.body.append(layer);
    const $ = s => layer.querySelector(s);
    const spot = $('.vc-tour-spot'), card = $('.vc-tour-card');
    let i = 0;
    const finish = () => {
      removeEventListener('resize', place); document.removeEventListener('keydown', onKey, true);
      write(KEY_SEEN, '1');
      hide(layer); layer.remove(); running = false;
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus();
    };
    function place() {
      const step = TOUR[i];
      const target = step.target ? document.querySelector(step.target) : null;
      const r = target?.getBoundingClientRect();
      const visible = r && r.width > 0 && r.height > 0;
      spot.hidden = !visible;
      if (visible) Object.assign(spot.style, { top: `${r.top - 6}px`, left: `${r.left - 6}px`, width: `${r.width + 12}px`, height: `${r.height + 12}px` });
      layer.classList.toggle('dim', !visible);
      const c = card.getBoundingClientRect();
      let top = (innerHeight - c.height) / 2, left = (innerWidth - c.width) / 2;
      if (visible) {
        top = r.bottom + 16 + c.height <= innerHeight ? r.bottom + 16 : Math.max(8, r.top - 16 - c.height);
        left = Math.min(Math.max(8, r.left + r.width / 2 - c.width / 2), innerWidth - c.width - 8);
      }
      Object.assign(card.style, { top: `${top}px`, left: `${left}px` });
    }
    function render() {
      const step = TOUR[i], last = i === TOUR.length - 1;
      $('#vc-tour-progress').textContent = trUi('tour.progress', [i + 1, TOUR.length]);
      $('#vc-tour-title').textContent = trUi(`tour.${step.id}.title`);
      $('#vc-tour-body').textContent = trUi(`tour.${step.id}.body`);
      $('#vc-tour-skip').textContent = trUi('tour.skip'); $('#vc-tour-skip').hidden = last;
      $('#vc-tour-back').textContent = trUi('tour.back'); $('#vc-tour-back').hidden = i === 0;
      $('#vc-tour-next').textContent = trUi(last ? 'tour.finish' : 'tour.next');
      card.setAttribute('aria-label', trUi('tour.label'));
      place();
      $('#vc-tour-next').focus();
    }
    function onKey(e) {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); finish(); }
      else if (e.key === 'Tab') {
        const buttons = [...layer.querySelectorAll('fluent-button')].filter(b => !b.hidden);
        const at = buttons.indexOf(document.activeElement);
        e.preventDefault();
        buttons[(at + (e.shiftKey ? -1 : 1) + buttons.length) % buttons.length].focus();
      }
    }
    $('#vc-tour-next').addEventListener('click', () => { if (i === TOUR.length - 1) finish(); else { i++; render(); } });
    $('#vc-tour-back').addEventListener('click', () => { i--; render(); });
    $('#vc-tour-skip').addEventListener('click', finish);
    addEventListener('resize', place);
    document.addEventListener('keydown', onKey, true);
    show(layer);
    render();
  })();
}

export const shouldAutoStartTour = () => tourEnabled() && read(KEY_SEEN) !== '1';
