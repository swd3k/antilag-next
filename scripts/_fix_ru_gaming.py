# -*- coding: utf-8 -*-
from pathlib import Path
import re

p = Path(__file__).resolve().parents[1] / "AntiLagNext" / "src" / "AntiLagNext.Ui" / "wwwroot" / "index.html"
t = p.read_text(encoding="utf-8")
assert "Игровой" in t

old = """    function profileLabel(id) {
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

new = """    function profileLabel(id) {
      const nid = normalizeProfileId(id);
      // Strict: title follows UI language only (ru → Игровой, en → Gaming)
      const L = (lang === 'en' || lang === 'ru') ? lang : 'ru';
      const pack = PROFILE_LABELS[L] || PROFILE_LABELS.ru;
      return pack[nid] || pack.gaming;
    }

    function updateProfileNameDisplay() {
      const el = document.getElementById('profileName');
      if (!el) return;
      const id = normalizeProfileId(
        state.selectedProfileId || state.profileKey || state.profileKind || selectedProfile || 'gaming'
      );
      const label = profileLabel(id);
      el.textContent = label;
      el.setAttribute('data-profile-id', id);
      el.setAttribute('data-ui-lang', (lang === 'en' || lang === 'ru') ? lang : 'ru');
      const mt = t('metric.profile');
      el.setAttribute('aria-label', (mt && mt !== 'metric.profile' ? mt : 'profile') + ': ' + label);
    }"""

if old not in t:
    raise SystemExit("old profileLabel block not found")
t = t.replace(old, new, 1)

# setLang: keep chosen language
m = re.search(r"async function setLang\(code\) \{[\s\S]*?\n    \}\n", t)
if not m:
    raise SystemExit("setLang not found")
new_set = """async function setLang(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try { localStorage.setItem('al_lang', want); } catch (e) {}
      lang = want;
      await loadI18n(want);
      try {
        const r = await send('setLanguage', { lang: want });
        if (r && r.state) {
          r.state.lang = want;
          await applyState(r.state);
        }
      } catch (e) { /* offline preview */ }
      lang = want;
      applyDynamicTexts();
      updateProfileNameDisplay();
      renderPlugins(state.plugins || []);
      renderLogs(state.logs || []);
      updateChartPanels();
    }
"""
t = t[: m.start()] + new_set + t[m.end() :]

# loadI18n: always overwrite profile.* from PROFILE_LABELS for current lang
t = t.replace(
    """        for (const k of ['gaming', 'office', 'max', 'default']) {
          const pk = 'profile.' + k;
          if (!i18n[pk]) i18n[pk] = fb[k];
          if (want === 'en' && CYRILLIC_RE.test(i18n[pk] || '')) i18n[pk] = fb[k];
        }""",
    """        for (const k of ['gaming', 'office', 'max', 'default']) {
          i18n['profile.' + k] = fb[k];
        }""",
)

# default fb for loadI18n should prefer matching want, not always .en
t = t.replace(
    "const fb = PROFILE_LABELS[want] || PROFILE_LABELS.en;",
    "const fb = PROFILE_LABELS[want] || PROFILE_LABELS.ru;",
)

p.write_text(t, encoding="utf-8")
tt = p.read_text(encoding="utf-8")
assert "pack[nid] || pack.gaming" in tt
assert "Игровой" in tt
print("OK")
print(tt[tt.find("function profileLabel") : tt.find("function profileLabel") + 400])
