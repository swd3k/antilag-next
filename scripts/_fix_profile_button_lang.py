# -*- coding: utf-8 -*-
"""
Active profile card must follow the visible RU/EN toggle, not host UiCulture races.
"""
from pathlib import Path
import re

p = Path(__file__).resolve().parents[1] / "AntiLagNext" / "src" / "AntiLagNext.Ui" / "wwwroot" / "index.html"
t = p.read_text(encoding="utf-8")
assert "Игровой" in t

# 1) HTML: embed both language labels on the element
old_html = """                <div class="metric-val" style="font-size:17px;font-family:inherit"
                     id="profileName" role="status" aria-live="polite" aria-atomic="true">—</div>"""

new_html = """                <div class="metric-val" style="font-size:17px;font-family:inherit"
                     id="profileName" role="status" aria-live="polite" aria-atomic="true"
                     data-gaming-en="Gaming" data-gaming-ru="\u0418\u0433\u0440\u043e\u0432\u043e\u0439"
                     data-office-en="Office" data-office-ru="\u041e\u0444\u0438\u0441\u043d\u044b\u0439"
                     data-max-en="Maximum" data-max-ru="\u041c\u0430\u043a\u0441\u0438\u043c\u0430\u043b\u044c\u043d\u0430\u044f"
                     data-default-en="Default" data-default-ru="\u041f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e">—</div>"""

if old_html not in t:
    raise SystemExit("profileName HTML block not found")
t = t.replace(old_html, new_html, 1)

# 2) Replace profileLabel + updateProfileNameDisplay with button-aware version
old_fn = """    function profileLabel(id) {
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

new_fn = """    /** Language for profile title: visible RU/EN toggle first, then lang var. */
    function resolveProfileUiLang() {
      const ruBtn = document.getElementById('langRu');
      const enBtn = document.getElementById('langEn');
      if (ruBtn && ruBtn.classList.contains('active')) return 'ru';
      if (enBtn && enBtn.classList.contains('active')) return 'en';
      try {
        const saved = localStorage.getItem('al_lang');
        if (saved === 'en' || saved === 'ru') return saved;
      } catch (e) {}
      return (lang === 'en' || lang === 'ru') ? lang : 'ru';
    }

    function profileLabel(id) {
      const nid = normalizeProfileId(id);
      const L = resolveProfileUiLang();
      // 1) data-* on the card (always present in HTML)
      const el = document.getElementById('profileName');
      if (el) {
        const fromDom = el.getAttribute('data-' + nid + '-' + L);
        if (fromDom) return fromDom;
      }
      // 2) PROFILE_LABELS table
      const pack = (typeof PROFILE_LABELS !== 'undefined' && PROFILE_LABELS[L])
        ? PROFILE_LABELS[L]
        : null;
      if (pack && pack[nid]) return pack[nid];
      // 3) last resort
      return L === 'en' ? 'Gaming' : '\\u0418\\u0433\\u0440\\u043e\\u0432\\u043e\\u0439';
    }

    function updateProfileNameDisplay() {
      const el = document.getElementById('profileName');
      if (!el) return;
      const id = normalizeProfileId(
        state.selectedProfileId || state.profileKey || state.profileKind || selectedProfile || 'gaming'
      );
      // Sync JS lang with the button the user actually sees
      const L = resolveProfileUiLang();
      if (L === 'en' || L === 'ru') lang = L;
      const label = profileLabel(id);
      el.textContent = label;
      el.setAttribute('data-profile-id', id);
      el.setAttribute('data-ui-lang', L);
      const mt = t('metric.profile');
      el.setAttribute('aria-label', (mt && mt !== 'metric.profile' ? mt : 'profile') + ': ' + label);
    }"""

if old_fn not in t:
    # try without the comment line difference
    m = re.search(
        r"    function profileLabel\(id\) \{[\s\S]*?\n    function updateProfileNameDisplay\(\) \{[\s\S]*?\n    \}\n\n    function applyDynamicTexts",
        t,
    )
    if not m:
        raise SystemExit("profileLabel block not found for replace")
    t = t[: m.start()] + new_fn + "\n\n    function applyDynamicTexts" + t[m.end() :]
else:
    t = t.replace(old_fn, new_fn, 1)

# 3) applyState: do NOT force host UiCulture over the language the user picked in the UI
old_sync = """      // Sync language BEFORE painting (prevents RU pack painting under EN chrome)
      if (s.lang === 'en' || s.lang === 'ru') {
        if (s.lang !== lang || !i18n['profile.gaming'] ||
            (s.lang === 'en' && CYRILLIC_RE.test(i18n['profile.gaming'] || ''))) {
          await loadI18n(s.lang);
        }
      }"""

new_sync = """      // Language: prefer user toggle / localStorage over host settings (avoids EN host
      // overwriting a visible RU UI and leaving profile title stuck as \"Gaming\").
      let preferLang = null;
      try { preferLang = localStorage.getItem('al_lang'); } catch (e) {}
      if (preferLang !== 'en' && preferLang !== 'ru') {
        const ruBtn = document.getElementById('langRu');
        if (ruBtn && ruBtn.classList.contains('active')) preferLang = 'ru';
        else if (document.getElementById('langEn')?.classList.contains('active')) preferLang = 'en';
        else if (s.lang === 'en' || s.lang === 'ru') preferLang = s.lang;
      }
      if (preferLang === 'en' || preferLang === 'ru') {
        if (preferLang !== lang || !i18n['profile.gaming']) {
          await loadI18n(preferLang);
        } else {
          lang = preferLang;
        }
      }"""

if old_sync not in t:
    # broader match
    m = re.search(
        r"      // Sync language BEFORE painting[\s\S]*?await loadI18n\(s\.lang\);\s*\}\s*\}",
        t,
    )
    if not m:
        raise SystemExit("lang sync block not found")
    t = t[: m.start()] + new_sync + t[m.end() :]
else:
    t = t.replace(old_sync, new_sync, 1)

# Ensure applyState always ends with updateProfileNameDisplay (already does)

assert "resolveProfileUiLang" in t
assert "data-gaming-ru" in t
assert "Игровой" in t

p.write_text(t, encoding="utf-8")
print("OK written")
print("has resolveProfileUiLang", "resolveProfileUiLang" in t)
print("has data-gaming-ru", "data-gaming-ru" in t)
