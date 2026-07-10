# -*- coding: utf-8 -*-
"""
Fix profile name language:
  lang=ru → Игровой / Офисный / ...
  lang=en → Gaming / Office / ...
Always from PROFILE_LABELS[lang], never heuristics / mixed packs.
"""
from pathlib import Path

p = Path(__file__).resolve().parents[1] / "AntiLagNext" / "src" / "AntiLagNext.Ui" / "wwwroot" / "index.html"
t = p.read_text(encoding="utf-8")
assert "Игровой" in t

# Remove existing PROFILE_LABELS / CYRILLIC_RE / profileLabel / updateProfileNameDisplay blocks
# so we can re-insert a clean version once at the top.

def remove_between(text, start_pat, end_pat):
    s = text.find(start_pat)
    if s < 0:
        return text
    e = text.find(end_pat, s)
    if e < 0:
        return text
    return text[:s] + text[e:]

# Remove "Hardcoded profile labels" section through end of updateProfileNameDisplay
if "// Hardcoded profile labels" in t:
    t = remove_between(t, "    // Hardcoded profile labels", "    function applyDynamicTexts()")

# Remove any remaining profileLabel / updateProfileNameDisplay before applyDynamicTexts
import re
t = re.sub(
    r"\n    function profileLabel\(id\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
)
t = re.sub(
    r"\n    /\*\* Active profile card:[\s\S]*?\n    function updateProfileNameDisplay\(\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
)
t = re.sub(
    r"\n    function updateProfileNameDisplay\(\) \{[\s\S]*?\n    \}\n",
    "\n",
    t,
)
t = re.sub(r"\n    const CYRILLIC_RE = /[^;]+;\n", "\n", t)

# Remove any leftover const PROFILE_LABELS blocks (except we will add one)
while "const PROFILE_LABELS" in t:
    s = t.find("const PROFILE_LABELS")
    # include leading spaces/newlines
    line_start = t.rfind("\n", 0, s) + 1
    # find closing }; of object — naive brace match from first {
    brace = t.find("{", s)
    depth = 0
    i = brace
    while i < len(t):
        if t[i] == "{":
            depth += 1
        elif t[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                if end < len(t) and t[end] == ";":
                    end += 1
                t = t[:line_start] + t[end:]
                # drop trailing newline clutter
                break
        i += 1
    else:
        break

# Insert clean definitions right after `let lang = 'ru';`
anchor = "    let lang = 'ru';\n"
if anchor not in t:
    raise SystemExit("lang anchor missing")

block = r"""    let lang = 'ru';

    // Built-in profile titles by UI language (Active profile card).
    // Always use this table — never state.profile / mixed i18n packs.
    const PROFILE_LABELS = {
      en: { gaming: 'Gaming', office: 'Office', max: 'Maximum', default: 'Default' },
      ru: {
        gaming: '\u0418\u0433\u0440\u043e\u0432\u043e\u0439',
        office: '\u041e\u0444\u0438\u0441\u043d\u044b\u0439',
        max: '\u041c\u0430\u043a\u0441\u0438\u043c\u0430\u043b\u044c\u043d\u0430\u044f',
        default: '\u041f\u043e \u0443\u043c\u043e\u043b\u0447\u0430\u043d\u0438\u044e'
      }
    };

    function uiLang() {
      return (lang === 'en' || lang === 'ru') ? lang : 'ru';
    }

"""
t = t.replace(anchor, block, 1)

# Insert profileLabel + updateProfileNameDisplay before applyDynamicTexts
funcs = r"""
    function profileLabel(id) {
      const nid = normalizeProfileId(id);
      const L = uiLang();
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
      el.setAttribute('data-ui-lang', uiLang());
      const mt = t('metric.profile');
      el.setAttribute('aria-label', (mt && mt !== 'metric.profile' ? mt : 'profile') + ': ' + label);
    }

"""
if "function profileLabel(id)" not in t:
    if "    function applyDynamicTexts()" not in t:
        raise SystemExit("applyDynamicTexts missing")
    t = t.replace("    function applyDynamicTexts()", funcs + "    function applyDynamicTexts()", 1)

# Fix loadI18n: sync profile.* from PROFILE_LABELS always
load_start = t.find("    async function loadI18n(code)")
load_end = t.find("    function resolveTheme", load_start)
if load_start < 0 or load_end < 0:
    raise SystemExit("loadI18n bounds missing")

new_load = r"""    async function loadI18n(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try {
        const res = await fetch('i18n/' + want + '.json', { cache: 'no-cache' });
        if (!res.ok) throw new Error(res.status);
        const pack = await res.json();
        if (!pack || typeof pack !== 'object') throw new Error('empty i18n pack');
        i18n = pack;
        lang = want;
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
          'metric.profile': want === 'en' ? 'Active profile' : '\u0410\u043a\u0442\u0438\u0432\u043d\u044b\u0439 \u043f\u0440\u043e\u0444\u0438\u043b\u044c'
        });
        applyI18n();
        return false;
      }
    }

"""
t = t[:load_start] + new_load + t[load_end:]

# Fix setLang to force lang before/after and not lose RU
set_start = t.find("    async function setLang(code)")
set_end = t.find("    async function setTheme", set_start)
if set_start < 0 or set_end < 0:
    # try function setTheme without async
    set_end = t.find("\n    async function setTheme", set_start)
    if set_end < 0:
        set_end = t.find("\n    function setTheme", set_start)
if set_start < 0 or set_end < 0:
    raise SystemExit("setLang bounds missing")

new_set = r"""    async function setLang(code) {
      const want = (code === 'en' || code === 'ru') ? code : 'ru';
      try { localStorage.setItem('al_lang', want); } catch (e) {}
      lang = want;
      await loadI18n(want);
      try {
        const r = await send('setLanguage', { lang: want });
        if (r && r.state) {
          // Never let host echo flip language back mid-switch
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
t = t[:set_start] + new_set + t[set_end:]

# applyState: when adopting host lang, then always updateProfileNameDisplay (already does)
# Also: if host lang differs from current, load it — keep as is

assert t.count("const PROFILE_LABELS") == 1, t.count("const PROFILE_LABELS")
assert "function uiLang()" in t
assert "pack[nid] || pack.gaming" in t
assert "CYRILLIC_RE" not in t
assert "Игровой" in t

p.write_text(t, encoding="utf-8")
print("OK")
print("profileLabel snippet:")
i = t.find("function profileLabel")
print(t[i : i + 280])
