# -*- coding: utf-8 -*-
"""Surgical UTF-8-safe patch for Active profile i18n in Photino UI."""
from pathlib import Path
import re

p = Path(__file__).resolve().parents[1] / "AntiLagNext" / "src" / "AntiLagNext.Ui" / "wwwroot" / "index.html"
t = p.read_text(encoding="utf-8")
assert "Игровой" in t, "source HTML lost Russian text"

old1 = """    function profileLabel(id) {
      const map = { gaming: 'profile.gaming', office: 'profile.office', max: 'profile.max', default: 'profile.default' };
      const key = map[normalizeProfileId(id)] || 'profile.gaming';
      return t(key);
    }

    /** Always paint Active profile card from i18n + selectedProfileId (never raw backend Name). */
    function updateProfileNameDisplay() {
      const el = document.getElementById('profileName');
      if (!el) return;
      const id = normalizeProfileId(
        state.selectedProfileId || state.profileKey || state.profileKind || selectedProfile
      );
      const label = profileLabel(id);
      if (el.textContent !== label) {
        el.textContent = label;
        el.setAttribute('aria-label', (t('metric.profile') || 'Profile') + ': ' + label);
      }
    }"""

new1 = r"""    // Hardcoded profile labels (Photino file:// fetch can lag; never show RU while lang=en)
    const PROFILE_LABELS = {
      en: { gaming: 'Gaming', office: 'Office', max: 'Maximum', default: 'Default' },
      ru: {
        gaming: '\u0418\u0433\u0440\u043e\u0432\u043e\u0439',
        office: '\u041e\u0444\u0438\u0441\u043d\u044b\u0439',
        max: '\u041c\u0430\u043a\u0441\u0438\u043c\u0430\u043b\u044c\u043d\u0430\u044f',
        default: '\u041f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e'
      }
    };
    const CYRILLIC_RE = /[\u0400-\u04FF]/;

    function profileLabel(id) {
      const nid = normalizeProfileId(id);
      const key = 'profile.' + nid;
      let s = i18n[key];
      const fb = (PROFILE_LABELS[lang] || PROFILE_LABELS.en)[nid] || PROFILE_LABELS.en.gaming;
      if (s == null || s === '' || s === key) return fb;
      if (lang === 'en' && CYRILLIC_RE.test(s)) return fb;
      if (lang === 'ru' && PROFILE_LABELS.en[nid] && s === PROFILE_LABELS.en[nid]) return fb;
      return s;
    }

    /** Active profile card: Kind id + lang only — NEVER state.profile raw text. */
    function updateProfileNameDisplay() {
      const el = document.getElementById('profileName');
      if (!el) return;
      const id = normalizeProfileId(
        state.selectedProfileId || state.profileKey || state.profileKind || selectedProfile || 'gaming'
      );
      let label = profileLabel(id);
      if (lang === 'en' && CYRILLIC_RE.test(label)) label = PROFILE_LABELS.en[id] || 'Gaming';
      if (lang === 'ru' && !CYRILLIC_RE.test(label)) label = PROFILE_LABELS.ru[id] || PROFILE_LABELS.ru.gaming;
      el.textContent = label;
      el.setAttribute('data-profile-id', id);
      el.setAttribute('aria-label', (t('metric.profile') || 'Active profile') + ': ' + label);
    }"""

if old1 not in t:
    raise SystemExit("old1 block not found")
t = t.replace(old1, new1)

old2 = """    async function loadI18n(code) {
      try {
        const res = await fetch('i18n/' + code + '.json', { cache: 'no-cache' });
        if (!res.ok) throw new Error(res.status);
        i18n = await res.json();
        lang = code;
        applyI18n();
      } catch (e) {
        console.warn('i18n load failed', e);
      }
    }"""

new2 = r"""    async function loadI18n(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try {
        const res = await fetch('i18n/' + want + '.json', { cache: 'no-cache' });
        if (!res.ok) throw new Error(res.status);
        const pack = await res.json();
        if (!pack || typeof pack !== 'object') throw new Error('empty i18n pack');
        i18n = pack;
        lang = want;
        const fb = PROFILE_LABELS[want] || PROFILE_LABELS.en;
        for (const k of ['gaming', 'office', 'max', 'default']) {
          const pk = 'profile.' + k;
          if (!i18n[pk]) i18n[pk] = fb[k];
          if (want === 'en' && CYRILLIC_RE.test(i18n[pk] || '')) i18n[pk] = fb[k];
        }
        applyI18n();
        return true;
      } catch (e) {
        console.warn('i18n load failed', e);
        const fb = PROFILE_LABELS[want] || PROFILE_LABELS.en;
        lang = want;
        i18n = Object.assign({}, i18n, {
          'profile.gaming': fb.gaming,
          'profile.office': fb.office,
          'profile.max': fb.max,
          'profile.default': fb.default,
          'metric.profile': want === 'en' ? 'Active profile' : '\u0410\u043a\u0442\u0438\u0432\u043d\u044b\u0439 \u043f\u0440\u043e\u0444\u0438\u043b\u044c'
        });
        applyI18n();
        return false;
      }
    }"""

if old2 not in t:
    raise SystemExit("old2 block not found")
t = t.replace(old2, new2)

old3 = """    function applyState(s) {
      if (!s || s.preview) return;
      const wasBooting = uiBooting;
      state = Object.assign(state, s);
      if (s.lang && s.lang !== lang) {
        loadI18n(s.lang).then(() => applyDynamicTexts());
      }
      if (s.theme) applyTheme(s.theme === 'light' ? 'light' : 'dark');"""

new3 = """    async function applyState(s) {
      if (!s || s.preview) return;
      const wasBooting = uiBooting;
      state = Object.assign(state, s);
      // Sync language BEFORE painting (prevents RU pack painting under EN chrome)
      if (s.lang === 'en' || s.lang === 'ru') {
        if (s.lang !== lang || !i18n['profile.gaming'] ||
            (s.lang === 'en' && CYRILLIC_RE.test(i18n['profile.gaming'] || ''))) {
          await loadI18n(s.lang);
        }
      }
      if (s.theme) applyTheme(s.theme === 'light' ? 'light' : 'dark');"""

if old3 not in t:
    raise SystemExit("old3 block not found")
t = t.replace(old3, new3)

old4 = """      applyDynamicTexts();
      renderPlugins(s.plugins || []);
      renderLogs(s.logs || []);
      updateChartPanels();
      scheduleChartDraw();
      document.getElementById('engineLabel').textContent = t('engine.ready');
    }"""

new4 = """      applyDynamicTexts();
      updateProfileNameDisplay();
      renderPlugins(s.plugins || []);
      renderLogs(s.logs || []);
      updateChartPanels();
      scheduleChartDraw();
      document.getElementById('engineLabel').textContent = t('engine.ready');
    }"""

if old4 not in t:
    raise SystemExit("old4 block not found")
t = t.replace(old4, new4)

# await applyState calls
t = re.sub(r"(?<!await )(?<!function )applyState\(", "await applyState(", t)
t = t.replace("await await applyState(", "await applyState(")
t = t.replace("async function await applyState(", "async function applyState(")

if "async function refresh()" not in t:
    t = t.replace("    function refresh()", "    async function refresh()")

# setLang
m = re.search(r"async function setLang\(code\) \{.*?\n    \}", t, re.S)
if not m:
    raise SystemExit("setLang not found")
new_setlang = """async function setLang(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'en';
      try { localStorage.setItem('al_lang', want); } catch (e) {}
      await loadI18n(want);
      try {
        const r = await send('setLanguage', { lang: want });
        if (r && r.state) await applyState(r.state);
      } catch (e) { /* offline preview */ }
      applyDynamicTexts();
      updateProfileNameDisplay();
      renderPlugins(state.plugins || []);
      renderLogs(state.logs || []);
      updateChartPanels();
    }"""
t = t[: m.start()] + new_setlang + t[m.end() :]

t = t.replace(
    "const pid = s.selectedProfileId || s.profileKey;",
    "const pid = s.selectedProfileId || s.profileKey || s.profileKind;",
)

# normalizeProfileId russian via unicode escapes
t = t.replace(
    "if (/игровой|gaming/i.test(s)) return 'gaming';\n"
    "      if (/офисн|office/i.test(s)) return 'office';\n"
    "      if (/максимал|maximum|max perf/i.test(s)) return 'max';\n"
    "      if (/умолчани|default/i.test(s)) return 'default';",
    "if (/\\u0438\\u0433\\u0440\\u043e\\u0432\\u043e\\u0439|gaming/i.test(s)) return 'gaming';\n"
    "      if (/\\u043e\\u0444\\u0438\\u0441\\u043d|office/i.test(s)) return 'office';\n"
    "      if (/\\u043c\\u0430\\u043a\\u0441\\u0438\\u043c\\u0430\\u043b|maximum|max perf/i.test(s)) return 'max';\n"
    "      if (/\\u0443\\u043c\\u043e\\u043b\\u0447\\u0430\\u043d|default/i.test(s)) return 'default';",
)

p.write_text(t, encoding="utf-8")
tt = p.read_text(encoding="utf-8")
assert "Игровой" in tt
assert "PROFILE_LABELS" in tt
assert "async function applyState" in tt
assert r"[\u0400-\u04FF]" in tt
print("OK: patched", p)
