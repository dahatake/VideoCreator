// VideoCreator site — progressive enhancement only; every page is readable without this script.
(() => {
  const doc = document.documentElement;
  const root = doc.dataset.root || './';
  const lang = doc.dataset.lang || 'ja';
  const i18n = document.querySelector('.i18n')?.dataset ?? {};
  const reduce = matchMedia('(prefers-reduced-motion: reduce)');
  const store = {
    get: key => { try { return localStorage.getItem(key); } catch { return null; } },
    set: (key, value) => { try { localStorage.setItem(key, value); } catch { /* private mode */ } },
  };
  const $$ = (sel, el = document) => [...el.querySelectorAll(sel)];

  // ---- theme ----
  const themeBtn = document.querySelector('.theme-toggle');
  const currentTheme = () => doc.dataset.theme || (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  const syncTheme = () => themeBtn?.setAttribute('aria-pressed', String(currentTheme() === 'light'));
  themeBtn?.addEventListener('click', () => {
    const next = currentTheme() === 'light' ? 'dark' : 'light';
    doc.dataset.theme = next;
    store.set('vc-theme', next);
    syncTheme();
  });
  syncTheme();

  // ---- language ----
  $$('[data-lang-switch]').forEach(a => a.addEventListener('click', () => store.set('vc-lang', a.dataset.langSwitch)));
  const suggest = document.querySelector('.lang-suggest');
  if (suggest && !store.get('vc-lang')) {
    const options = JSON.parse(suggest.dataset.suggest || '{}');
    const wanted = (navigator.languages || [navigator.language]).map(l => l.toLowerCase());
    const match = wanted.map(w => Object.keys(options).find(code => w === code.toLowerCase() || w.split('-')[0] === code.split('-')[0].toLowerCase()))
      .find(Boolean);
    const prefersCurrent = wanted.findIndex(w => w.split('-')[0] === lang.split('-')[0].toLowerCase());
    const matchIndex = match ? wanted.findIndex(w => w.split('-')[0] === match.split('-')[0].toLowerCase()) : -1;
    if (match && (prefersCurrent < 0 || matchIndex < prefersCurrent)) {
      const link = document.createElement('a');
      link.href = options[match].url;
      link.lang = match;
      link.textContent = options[match].text;
      link.addEventListener('click', () => store.set('vc-lang', match));
      const close = document.createElement('button');
      close.type = 'button';
      close.className = 'icon-btn';
      close.textContent = '×';
      close.setAttribute('aria-label', document.querySelector('.search-box .icon-btn')?.getAttribute('aria-label') || 'Close');
      close.addEventListener('click', () => { store.set('vc-lang', lang); suggest.hidden = true; });
      suggest.append(link, close);
      suggest.hidden = false;
    }
  }

  // ---- mobile menu ----
  const menuBtn = document.querySelector('.menu-toggle');
  const sidebar = document.getElementById('sidebar');
  if (!sidebar) menuBtn?.remove();
  menuBtn?.addEventListener('click', () => {
    const open = sidebar.classList.toggle('open');
    menuBtn.setAttribute('aria-expanded', String(open));
  });
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && sidebar?.classList.contains('open')) { sidebar.classList.remove('open'); menuBtn?.setAttribute('aria-expanded', 'false'); menuBtn?.focus(); }
  });
  document.querySelector('.lang-menu')?.addEventListener('keydown', e => { if (e.key === 'Escape') { e.currentTarget.open = false; e.currentTarget.querySelector('summary').focus(); } });
  document.addEventListener('click', e => { const menu = document.querySelector('.lang-menu[open]'); if (menu && !menu.contains(e.target)) menu.open = false; });

  // ---- search ----
  const dialog = document.querySelector('.search-dialog');
  if (dialog) {
    const input = dialog.querySelector('input');
    const list = dialog.querySelector('.search-results');
    let index = null;
    let selected = -1;
    const load = async () => {
      if (index) return index;
      const res = await fetch(`${root}assets/search-${lang}.json`);
      index = (await res.json()).map(entry => ({ ...entry, key: entry.t.toLowerCase() }));
      return index;
    };
    const select = n => {
      const links = $$('a', list);
      if (!links.length) return;
      selected = (n + links.length) % links.length;
      links.forEach((a, i) => a.setAttribute('aria-selected', String(i === selected)));
      links[selected].scrollIntoView({ block: 'nearest' });
    };
    const render = async () => {
      const q = input.value.trim().toLowerCase();
      list.replaceChildren();
      selected = -1;
      if (!q) return;
      const terms = q.split(/\s+/);
      const entries = await load();
      if (input.value.trim().toLowerCase() !== q) return; // a newer keystroke owns the list
      list.replaceChildren();
      const hits = entries.filter(e => terms.every(term => e.key.includes(term)))
        .sort((a, b) => (a.u.includes('#') - b.u.includes('#')) || (a.key.startsWith(q) ? 0 : 1) - (b.key.startsWith(q) ? 0 : 1) || a.t.length - b.t.length).slice(0, 40);
      if (!hits.length) {
        const li = document.createElement('li');
        li.className = 'empty';
        li.textContent = input.dataset.empty;
        list.append(li);
        return;
      }
      for (const hit of hits) {
        const li = document.createElement('li');
        const a = document.createElement('a');
        a.href = root + hit.u;
        a.setAttribute('role', 'option');
        a.textContent = hit.t;
        const small = document.createElement('small');
        small.textContent = hit.g;
        a.append(small);
        li.append(a);
        list.append(li);
      }
      select(0);
    };
    const open = () => { if (!dialog.open) { dialog.showModal(); input.select(); load().catch(() => {}); } };
    $$('.search-open').forEach(b => b.addEventListener('click', open));
    document.addEventListener('keydown', e => {
      const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable;
      if ((e.key === 'k' && (e.ctrlKey || e.metaKey)) || (e.key === '/' && !typing)) { e.preventDefault(); open(); }
    });
    input.addEventListener('input', render);
    input.addEventListener('keydown', e => {
      if (e.key === 'ArrowDown') { e.preventDefault(); select(selected + 1); }
      if (e.key === 'ArrowUp') { e.preventDefault(); select(selected - 1); }
      if (e.key === 'Enter') { e.preventDefault(); const a = $$('a', list)[Math.max(selected, 0)]; if (a) location.href = a.href; }
    });
    list.addEventListener('click', e => { if (e.target.closest('a')) dialog.close(); });
    dialog.addEventListener('click', e => { if (e.target === dialog) dialog.close(); });
  }

  // ---- pointer glow ----
  document.addEventListener('pointermove', e => {
    const card = e.target.closest?.('.glow');
    if (!card) return;
    const r = card.getBoundingClientRect();
    card.style.setProperty('--mx', `${e.clientX - r.left}px`);
    card.style.setProperty('--my', `${e.clientY - r.top}px`);
  }, { passive: true });

  // ---- reveal ----
  const reveals = $$('.reveal');
  if (reduce.matches || !('IntersectionObserver' in window)) reveals.forEach(el => el.classList.add('in'));
  else {
    const io = new IntersectionObserver(entries => entries.forEach(entry => {
      if (entry.isIntersecting) { entry.target.classList.add('in'); io.unobserve(entry.target); }
    }), { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    reveals.forEach(el => io.observe(el));
  }

  // ---- reading progress & TOC ----
  const progress = document.querySelector('.read-progress span');
  if (progress) {
    let ticking = false;
    const update = () => {
      const max = doc.scrollHeight - innerHeight;
      progress.style.setProperty('--p', max > 0 ? Math.min(1, scrollY / max).toFixed(4) : 0);
      ticking = false;
    };
    addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, { passive: true });
    update();
  }
  const tocLinks = $$('.toc a[href^="#"]').filter(a => a.getAttribute('href').length > 1 && !a.classList.contains('to-top'));
  if (tocLinks.length && 'IntersectionObserver' in window) {
    const targets = tocLinks.map(a => document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)))).filter(Boolean);
    const visible = new Set();
    const mark = () => {
      const first = targets.find(t => visible.has(t)) ?? targets.filter(t => t.getBoundingClientRect().top < 120).at(-1);
      tocLinks.forEach(a => a.classList.toggle('active', first && a.getAttribute('href') === `#${first.id}`));
    };
    const io = new IntersectionObserver(entries => { entries.forEach(e => (e.isIntersecting ? visible.add(e.target) : visible.delete(e.target))); mark(); },
      { rootMargin: '-70px 0px -65% 0px' });
    targets.forEach(t => io.observe(t));
  }

  // ---- copy buttons ----
  $$('.code pre').forEach(pre => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'copy-btn';
    btn.textContent = i18n.copy || 'Copy';
    btn.addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(pre.innerText); btn.textContent = i18n.copied || 'Copied'; } catch { btn.textContent = '×'; }
      setTimeout(() => { btn.textContent = i18n.copy || 'Copy'; }, 1600);
    });
    pre.parentElement.append(btn);
  });

  // ---- hero studio (F-3.1.5 cinematic, stops for reduced motion) ----
  const studio = document.querySelector('.studio');
  if (studio) {
    const wrap = studio.closest('.studio-wrap');
    const toggle = wrap.querySelector('.motion-toggle');
    let stage = 4;
    let timer = 0;
    let paused = reduce.matches;
    let onScreen = true;
    const tick = () => {
      stage = (stage + 1) % 5;
      studio.dataset.stage = String(stage);
      timer = setTimeout(tick, stage === 4 ? 3200 : 1700);
    };
    const run = () => {
      clearTimeout(timer);
      if (paused || !onScreen || document.hidden) { wrap.classList.toggle('paused', paused); if (paused) studio.dataset.stage = '4'; return; }
      wrap.classList.remove('paused');
      timer = setTimeout(tick, 1200);
    };
    toggle.addEventListener('click', () => {
      paused = !paused;
      toggle.setAttribute('aria-pressed', String(paused));
      toggle.textContent = paused ? (i18n.motionOn || 'Play') : (i18n.motion || 'Pause');
      run();
    });
    if (reduce.matches) { toggle.setAttribute('aria-pressed', 'true'); toggle.textContent = i18n.motionOn || 'Play'; }
    reduce.addEventListener?.('change', e => { paused = e.matches; run(); });
    document.addEventListener('visibilitychange', run);
    if ('IntersectionObserver' in window) new IntersectionObserver(([e]) => { onScreen = e.isIntersecting; run(); }).observe(studio);
    wrap.addEventListener('pointermove', e => {
      if (paused || e.pointerType !== 'mouse') return;
      const r = studio.getBoundingClientRect();
      studio.style.setProperty('--ry', `${((e.clientX - r.left) / r.width - 0.5) * 10}deg`);
      studio.style.setProperty('--rx', `${((e.clientY - r.top) / r.height - 0.5) * -8}deg`);
    });
    wrap.addEventListener('pointerleave', () => { studio.style.setProperty('--rx', '0deg'); studio.style.setProperty('--ry', '0deg'); });
    run();
  }

  // ---- step tabs (WAI-ARIA tabs) ----
  const tabs = $$('.step-tab');
  if (tabs.length) {
    const tabList = tabs[0].closest('ol');
    tabList.setAttribute('role', 'tablist');
    $$('li', tabList).forEach(li => li.setAttribute('role', 'presentation'));
    const panels = tabs.map(tab => document.getElementById(tab.getAttribute('aria-controls')));
    let current = 0;
    let auto = !reduce.matches;
    let timer = 0;
    let started = 0;
    const DURATION = 6500;
    const show = (n, focus = false) => {
      current = (n + tabs.length) % tabs.length;
      tabs.forEach((tab, i) => {
        tab.setAttribute('aria-selected', String(i === current));
        tab.tabIndex = i === current ? 0 : -1;
        panels[i].classList.toggle('active', i === current);
        panels[i].hidden = false;
        panels[i].toggleAttribute('inert', i !== current);
      });
      if (focus) tabs[current].focus();
      started = performance.now();
    };
    tabs.forEach((tab, i) => {
      tab.setAttribute('role', 'tab');
      panels[i].setAttribute('role', 'tabpanel');
      tab.addEventListener('click', () => { auto = false; show(i); tabs[i].style.setProperty('--progress', 1); });
      tab.addEventListener('keydown', e => {
        const move = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
        if (move) { e.preventDefault(); auto = false; show(current + move, true); }
        if (e.key === 'Home') { e.preventDefault(); auto = false; show(0, true); }
        if (e.key === 'End') { e.preventDefault(); auto = false; show(tabs.length - 1, true); }
      });
    });
    const container = tabList.closest('.steps');
    let hover = false;
    container.addEventListener('pointerenter', () => { hover = true; });
    container.addEventListener('pointerleave', () => { hover = false; started = performance.now() - Number(tabs[current].style.getPropertyValue('--progress') || 0) * DURATION; });
    container.addEventListener('focusin', () => { auto = false; tabs.forEach(t => t.style.setProperty('--progress', 1)); });
    const loop = now => {
      if (!auto) { tabs.forEach(t => t.style.setProperty('--progress', 1)); return; }
      if (!hover && !document.hidden) {
        const p = Math.min(1, (now - started) / DURATION);
        tabs[current].style.setProperty('--progress', p.toFixed(3));
        if (p >= 1) show(current + 1);
      } else started = now - Number(tabs[current].style.getPropertyValue('--progress') || 0) * DURATION;
      timer = requestAnimationFrame(loop);
    };
    show(0);
    if (auto && 'IntersectionObserver' in window) {
      new IntersectionObserver(([e]) => {
        cancelAnimationFrame(timer);
        if (e.isIntersecting && auto) { started = performance.now() - Number(tabs[current].style.getPropertyValue('--progress') || 0) * DURATION; timer = requestAnimationFrame(loop); }
      }).observe(container);
    } else tabs.forEach(t => t.style.setProperty('--progress', 1));
  }

  // ---- chart sort & genre filter (11.4-AC5: table alternative is in the markup) ----
  $$('.chart').forEach(chart => {
    const bars = chart.querySelector('.bars');
    const rows = () => $$('.bar-row', bars);
    const number = () => rows().forEach((row, i) => row.style.setProperty('--n', i));
    number();
    $$('[data-sort]', chart).forEach(btn => btn.addEventListener('click', () => {
      $$('[data-sort]', chart).forEach(b => b.setAttribute('aria-pressed', String(b === btn)));
      const key = btn.dataset.sort;
      const sorted = rows().sort((a, b) => key === 'len'
        ? Number(b.dataset.len) - Number(a.dataset.len) || Number(b.dataset.min) - Number(a.dataset.min)
        : Number(a.dataset.id.slice(1)) - Number(b.dataset.id.slice(1)));
      const before = new Map(sorted.map(r => [r, r.getBoundingClientRect().top]));
      bars.append(...sorted);
      number();
      if (!reduce.matches) sorted.forEach(r => {
        const dy = before.get(r) - r.getBoundingClientRect().top;
        if (dy) r.animate([{ transform: `translateY(${dy}px)` }, { transform: 'none' }], { duration: 500, easing: 'cubic-bezier(.2,.8,.2,1)' });
      });
    }));
  });
  const genreButtons = $$('.genre-filter [data-genre]');
  genreButtons.forEach(btn => btn.addEventListener('click', () => {
    const genre = btn.dataset.genre;
    genreButtons.forEach(b => b.setAttribute('aria-pressed', String(b === btn)));
    const shown = new Set();
    $$('.pattern').forEach(card => {
      const on = !genre || card.dataset.genre === genre;
      card.hidden = !on;
      if (on) shown.add(card.id.toUpperCase());
    });
    $$('.bar-row').forEach(row => row.classList.toggle('dim', !shown.has(row.dataset.id)));
  }));
  addEventListener('hashchange', () => {
    const target = document.getElementById(location.hash.slice(1));
    if (target?.classList.contains('pattern') && target.hidden) genreButtons[0]?.click();
  });
  $$('details.more').forEach(d => d.addEventListener('keydown', e => { if (e.key === 'Escape' && d.open) { d.open = false; d.querySelector('summary').focus(); } }));

  // ---- table filter (customization map) ----
  $$('[data-filter-table]').forEach(input => {
    const table = document.querySelector(input.dataset.filterTable);
    const count = input.closest('section').querySelector('.filter-count');
    const rows = $$('tbody tr', table);
    const update = () => {
      const terms = input.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
      let n = 0;
      rows.forEach(row => {
        const on = terms.every(term => row.textContent.toLowerCase().includes(term));
        row.hidden = !on;
        if (on) n++;
      });
      count.textContent = terms.length ? count.dataset.template.replace('{n}', n) : '';
    };
    input.addEventListener('input', update);
  });

  // ---- Mermaid (self-hosted, loaded only where diagrams exist) ----
  const diagrams = $$('pre.mermaid');
  if (diagrams.length) {
    let loading = null;
    const render = () => {
      loading ??= new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = `${root}assets/vendor/mermaid.min.js`;
        s.onload = resolve;
        s.onerror = reject;
        document.head.append(s);
      }).then(() => {
        window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'default', fontFamily: 'inherit' });
        return window.mermaid.run({ nodes: diagrams });
      }).catch(() => { /* keep the source text visible */ });
      return loading;
    };
    if ('IntersectionObserver' in window) {
      const io = new IntersectionObserver(entries => { if (entries.some(e => e.isIntersecting)) { io.disconnect(); render(); } }, { rootMargin: '600px' });
      diagrams.forEach(d => io.observe(d));
    } else render();
  }
})();
