# -*- coding: utf-8 -*-
"""Fix Active profile card: RU must show Игровой, EN must show Gaming."""
from pathlib import Path
import re

p = Path(__file__).resolve().parents[1] / "AntiLagNext" / "src" / "AntiLagNext.Ui" / "wwwroot" / "index.html"
t = p.read_text(encoding="utf-8")
assert "Игровой" in t, "HTML corrupted / missing Russian defaults"

# --- 1) Ensure PROFILE_LABELS + uiLang right after uiBooting (only once) ---
if "function uiLang()" not in t:
    needle = """    /** true until first successful getState */
    let uiBooting = true;

    function t(key, vars) {"""
    insert = """    /** true until first successful getState */
    let uiBooting = true;

    // Built-in profile names by UI language (Active profile card source of truth).
    const PROFILE_LABELS = {
      en: { gaming: 'Gaming', office: 'Office', max: 'Maximum', default: 'Default' },
      ru: {
        gaming: '\\u0418\\u0433\\u0440\\u043e\\u0432\\u043e\\u0439',
        office: '\\u041e\\u0444\\u0438\\u0441\\u043d\\u044b\\u0439',
        max: '\\u041c\\u0430\\u043a\\u0441\\u0438\\u043c\\u0430\\u043b\\u044c\\u043d\\u0430\\u044f',
        default: '\\u041f\\u043e \\u0443\\u043c\\u043e\\u043b\\u0447\\u0430\\u043d\\u0438\\u044e'
      }
    };

    function uiLang() {
      return (lang === 'en' || lang === 'ru') ? lang : 'ru';
    }

    function t(key, vars) {"""
    if needle not in t:
        raise SystemExit("insert needle not found")
    t = t.replace(needle, insert, 1)

# --- 2) Remove ANY duplicate PROFILE_LABELS / CYRILLIC_RE / old profileLabel blocks ---
# Remove block from "// Hardcoded profile labels" or second "const PROFILE_LABELS" through end of updateProfileNameDisplay
while True:
    # find second PROFILE_LABELS if any, or Hardcoded comment
    idx_hard = t.find("    // Hardcoded profile labels")
    # find profileLabel function - replace whole section until applyDynamicTexts
    m = re.search(
        r"\n    // Hardcoded profile labels[\s\S]*?\n    function applyDynamicTexts\(\)",
        t,
    )
    if m:
        t = t[: m.start()] + "\n    function applyDynamicTexts()" + t[m.end() :]
        continue
    break

# Remove duplicate PROFILE_LABELS that appears after normalizeProfileId (if still there)
# Count PROFILE_LABELS
count = t.count("const PROFILE_LABELS")
if count > 1:
    # keep first, remove subsequent blocks carefully
    first = t.find("const PROFILE_LABELS")
    second = t.find("const PROFILE_LABELS", first + 1)
    while second > 0:
        # remove from second back to previous newline block until next function applyDynamicTexts or function profileLabel
        # find end: next "\n    function applyDynamicTexts" or after updateProfileNameDisplay
        end = t.find("\n    function applyDynamicTexts()", second)
        if end < 0:
            break
        # also remove profileLabel/update if between second and end
        t = t[:second] + t[end + 1 :]  # keep "function applyDynamicTexts" line start
        # fix if we ate "function applyDynamicTexts"
        if "function applyDynamicTexts()" not in t[second : second + 80]:
            t = t[:second] + "function applyDynamicTexts()" + t[second:]
        second = t.find("const PROFILE_LABELS", first + 1)
        count = t.count("const PROFILE_LABELS")
        if count <= 1:
            break

# --- 3) Replace profileLabel + updateProfileNameDisplay with simple versions ---
# Remove existing profileLabel / updateProfileNameDisplay if present
t = re.sub(
    r"\n    function profileLabel\(id\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
    count=1,
)
t = re.sub(
    r"\n    /\*\* Active profile card:[\s\S]*?\n    function updateProfileNameDisplay\(\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
    count=1,
)
t = re.sub(
    r"\n    function updateProfileNameDisplay\(\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
    count=1,
)

# Insert clean functions before applyDynamicTexts
marker = "    function applyDynamicTexts()"
funcs = """    function profileLabel(id) {
      const nid = normalizeProfileId(id);
      const L = uiLang();
      const pack = PROFILE_LABELS[L] || PROFILE_LABELS.ru;
      return pack[nid] || pack.gaming;
    }

    /** Active profile card follows UI language only (RU → Игровой, EN → Gaming). */
    function updateProfileNameDisplay() {
      const el = document.getElementById('profileName');
      if (!el) return;
      const id = normalizeProfileId(
        state.selectedProfileId || state.profileKey || state.profileKind || selectedProfile || 'gaming'
      );
      const label = profileLabel(id);
      el.textContent = label;
      el.setAttribute('data-profile-id', id);
      el.setAttribute('data-ui-lang', uiLang());
      const mt = t('metric.profile');
      el.setAttribute('aria-label', (mt && mt !== 'metric.profile' ? mt : 'profile') + ': ' + label);
    }

"""
if marker not in t:
    raise SystemExit("applyDynamicTexts marker missing")
if "function profileLabel(id)" not in t:
    t = t.replace(marker, funcs + marker, 1)

# --- 4) Simplify loadI18n (no CYRILLIC_RE) ---
t = re.sub(
    r"    async function loadI18n\(code\) \{[\s\S]*?\n    \}\n\n    function resolveTheme",
    """    async function loadI18n(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try {
        const res = await fetch('i18n/' + want + '.json', { cache: 'no-cache' });
        if (!res.ok) throw new Error(res.status);
        const pack = await res.json();
        if (!pack || typeof pack !== 'object') throw new Error('empty i18n pack');
        i18n = pack;
        lang = want;
        // Force built-in profile strings to match UI language table
        const fb = PROFILE_LABELS[want] || PROFILE_LABELS.ru;
        for (const k of ['gaming', 'office', 'max', 'default']) {
          i18n['profile.' + k] = fb[k];
        }
        applyI18n();
        return true;
      } catch (e) {
        console.warn('i18n load failed', e);
        const fb = PROFILE_LABELS[want] || PROFILE_LABELS.ru;
        lang = want;
        i18n = Object.assign({}, i18n, {
          'profile.gaming': fb.gaming,
          'profile.office': fb.office,
          'profile.max': fb.max,
          'profile.default': fb.default,
          'metric.profile': want === 'en' ? 'Active profile' : '\\u0410\\u043a\\u0442\\u0438\\u0432\\u043d\\u044b\\u0439 \\u043f\\u0440\\u043e\\u0444\\u0438\\u043b\\u044c'
        });
        applyI18n();
        return false;
      }
    }

    function resolveTheme""",
    t,
    count=1,
)

# --- 5) setLang: always finish with updateProfileNameDisplay ---
t = re.sub(
    r"async function setLang\(code\) \{[\s\S]*?\n    \}",
    """async function setLang(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try { localStorage.setItem('al_lang', want); } catch (e) {}
      lang = want;
      await loadI18n(want);
      try {
        const r = await send('setLanguage', { lang: want });
        if (r && r.state) {
          // Keep UI language the user just chose even if state echoes late
          if (r.state.lang && r.state.lang !== want) r.state.lang = want;
          await applyState(r.state);
        }
      } catch (e) { /* offline preview */ }
      lang = want;
      applyDynamicTexts();
      updateProfileNameDisplay();
      renderPlugins(state.plugins || []);
      renderLogs(state.logs || []);
      updateChartPanels();
    }""",
    t,
    count=1,
)

# Final checks
assert t.count("const PROFILE_LABELS") == 1, t.count("const PROFILE_LABELS")
assert "function uiLang()" in t
assert "function profileLabel(id)" in t
assert "CYRILLIC_RE" not in t
assert "Игровой" in t
assert "pack[nid] || pack.gaming" in t

p.write_text(t, encoding="utf-8")
print("OK", p)
print("PROFILE_LABELS count", t.count("const PROFILE_LABELS"))
