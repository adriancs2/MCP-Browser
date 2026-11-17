using Newtonsoft.Json;

namespace MCP_Browser.Browser
{
    /// <summary>
    /// JavaScript injected into pages to locate elements the way a person would:
    /// by visible text, or — for form fields — by their visible label.
    /// </summary>
    internal static class PageScripts
    {
        /// <summary>
        /// Builds JS that finds visible elements whose rendered text (or aria-label /
        /// title / value / alt) matches <paramref name="text"/>. Returns a JSON array of
        /// candidates with a generated unique CSS selector and viewport-centre coords.
        /// </summary>
        public static string BuildFindByTextJs(string text, bool exact, bool clickableOnly, bool scrollTo, int index)
        {
            string q = JsonConvert.SerializeObject(text ?? "");
            string clickableSel =
                "a,button,input[type=submit],input[type=button],input[type=reset],input[type=image]," +
                "[role=button],[role=link],[role=tab],[role=menuitem],[role=option],summary,label,[onclick]";
            string sel = JsonConvert.SerializeObject(clickableOnly ? clickableSel : "*");

            return @"(function(){
  var q = " + q + @", exact = " + (exact ? "true" : "false") + @", doScroll = " + (scrollTo ? "true" : "false") + @", pick = " + index + @";
  function norm(s){ return (s||'').replace(/\s+/g,' ').trim(); }
  function visible(e){
    var r = e.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return false;
    var st = window.getComputedStyle(e);
    return st.visibility !== 'hidden' && st.display !== 'none' && st.opacity !== '0';
  }
  function labelOf(e){
    var t = norm(e.innerText || e.textContent);
    if (!t) t = norm(e.getAttribute('aria-label') || e.getAttribute('title') || e.value || e.getAttribute('alt') || '');
    return t;
  }
  function cssPath(e){
    if (e.id && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1) return '#' + CSS.escape(e.id);
    var parts = [], depth = 0;
    while (e && e.nodeType === 1 && e !== document.documentElement && depth < 40) {
      if (e.id && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1) { parts.unshift('#' + CSS.escape(e.id)); break; }
      var part = e.tagName.toLowerCase(), par = e.parentElement;
      if (par) {
        var same = Array.prototype.filter.call(par.children, function(c){ return c.tagName === e.tagName; });
        if (same.length > 1) part += ':nth-of-type(' + (same.indexOf(e) + 1) + ')';
      }
      parts.unshift(part);
      if (document.querySelectorAll(parts.join(' > ')).length === 1) break;   // shortest unique path
      e = par; depth++;
    }
    return parts.join(' > ');
  }
  var ql = q.toLowerCase();
  var els = Array.prototype.slice.call(document.querySelectorAll(" + sel + @"));
  var hits = [];
  for (var i = 0; i < els.length; i++) {
    var e = els[i];
    if (!visible(e)) continue;
    var t = labelOf(e);
    if (!t) continue;
    var tl = t.toLowerCase();
    if (exact ? (tl !== ql) : (tl.indexOf(ql) < 0)) continue;
    hits.push(e);
  }
  // Prefer the innermost match: drop any hit that contains another hit.
  var inner = hits.filter(function(e){
    return !hits.some(function(o){ return o !== e && e.contains(o); });
  });
  if (pick >= 0 && pick < inner.length && doScroll) {
    inner[pick].scrollIntoView({ block: 'center', inline: 'center' });
  }
  var out = inner.map(function(e, i){
    var r = e.getBoundingClientRect();
    return {
      index: i,
      tag: e.tagName.toLowerCase(),
      text: labelOf(e).slice(0, 120),
      selector: cssPath(e),
      x: Math.round(r.left + r.width / 2),
      y: Math.round(r.top + r.height / 2),
      in_viewport: r.top >= 0 && r.left >= 0 && r.bottom <= window.innerHeight && r.right <= window.innerWidth
    };
  });
  return JSON.stringify(out);
})()";
        }

        public static string DecodeJsString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            try { return JsonConvert.DeserializeObject<string>(raw) ?? raw; }
            catch { return raw; }
        }

        // ---- Form-field targeting by label ----------------------------------

        /// <summary>
        /// Builds JS that locates form fields by their human-visible label. Real pages
        /// attach labels in many ways, so each strategy is tried in order of reliability
        /// and the winning one is reported back as 'matched_by' with a numeric rank.
        /// </summary>
        public static string BuildFindFieldJs(string label, bool exact)
        {
            string q = JsonConvert.SerializeObject(label ?? "");

            return @"(function(){
  var q = " + q + @", exact = " + (exact ? "true" : "false") + @";
  var ql = q.toLowerCase();
  function norm(s){ return (s||'').replace(/\s+/g,' ').trim(); }
  function match(t){
    t = norm(t).toLowerCase();
    if (!t) return false;
    return exact ? (t === ql) : (t.indexOf(ql) >= 0);
  }
  // A caption is short, inert text. Rejecting form controls matters: a <select>'s
  // innerText is its concatenated options, so a neighbouring dropdown would otherwise
  // look like a label reading 'All Types Lend Only Return Only'. Blocks that contain
  // controls are whole sections, not captions.
  function labelish(el){
    if (!el) return false;
    if (/^(INPUT|SELECT|TEXTAREA|BUTTON|OPTION|FORM)$/.test(el.tagName)) return false;
    if (el.querySelector && el.querySelector('input,select,textarea,button')) return false;
    var t = norm(el.innerText || el.textContent);
    return t.length > 0 && t.length <= 200 && match(t);
  }
  function visible(e){
    var r = e.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return false;
    var st = window.getComputedStyle(e);
    return st.visibility !== 'hidden' && st.display !== 'none';
  }
  function cssPath(e){
    if (e.id && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1) return '#' + CSS.escape(e.id);
    var parts = [], depth = 0;
    while (e && e.nodeType === 1 && e !== document.documentElement && depth < 40) {
      if (e.id && document.querySelectorAll('#' + CSS.escape(e.id)).length === 1) { parts.unshift('#' + CSS.escape(e.id)); break; }
      var part = e.tagName.toLowerCase(), par = e.parentElement;
      if (par) {
        var same = Array.prototype.filter.call(par.children, function(c){ return c.tagName === e.tagName; });
        if (same.length > 1) part += ':nth-of-type(' + (same.indexOf(e) + 1) + ')';
      }
      parts.unshift(part);
      if (document.querySelectorAll(parts.join(' > ')).length === 1) break;   // shortest unique path
      e = par; depth++;
    }
    return parts.join(' > ');
  }
  function ownText(el){
    var t = '';
    for (var i = 0; i < el.childNodes.length; i++)
      if (el.childNodes[i].nodeType === 3) t += el.childNodes[i].nodeValue + ' ';
    return t;
  }

  var fields = Array.prototype.slice.call(
    document.querySelectorAll('input:not([type=hidden]),textarea,select,[contenteditable=""true""]'));
  var out = [];

  for (var i = 0; i < fields.length; i++) {
    var e = fields[i];
    if (!visible(e)) continue;

    var why = null, lab = '', rank = 99;

    if (e.id) {
      var l = document.querySelector('label[for=""' + CSS.escape(e.id) + '""]');
      if (l && match(l.innerText)) { why = 'label-for'; lab = norm(l.innerText); rank = 1; }
    }
    if (!why) {
      var anc = e.closest('label');
      if (anc && match(anc.innerText)) { why = 'ancestor-label'; lab = norm(anc.innerText); rank = 2; }
    }
    if (!why && match(e.getAttribute('aria-label'))) {
      why = 'aria-label'; lab = norm(e.getAttribute('aria-label')); rank = 3;
    }
    if (!why) {
      var ab = e.getAttribute('aria-labelledby');
      if (ab) {
        var ael = document.getElementById(ab);
        if (ael && match(ael.innerText)) { why = 'aria-labelledby'; lab = norm(ael.innerText); rank = 3; }
      }
    }
    if (!why && match(e.placeholder)) { why = 'placeholder'; lab = norm(e.placeholder); rank = 4; }
    if (!why && match(e.getAttribute('title'))) { why = 'title'; lab = norm(e.getAttribute('title')); rank = 5; }
    if (!why) {
      // Classic table layouts put the caption in the cell to the left.
      var td = e.closest('td');
      var prev = td ? td.previousElementSibling : null;
      if (labelish(prev)) { why = 'table-cell'; lab = norm(prev.innerText || prev.textContent); rank = 6; }
    }
    if (!why) {
      var sib = e.previousElementSibling, hops = 0;
      while (sib && hops < 3 && !why) {
        if (labelish(sib)) { why = 'preceding-text'; lab = norm(sib.innerText || sib.textContent); rank = 7; }
        sib = sib.previousElementSibling; hops++;
      }
    }
    if (!why) {
      // Very common wrapper pattern: the caption is a sibling of an ANCESTOR of the
      // field, e.g. <div><div>Caption</div><div class=group><input></div></div>.
      // Nearer ancestors rank better, so the closest caption wins.
      var anc = e.parentElement, up = 0;
      while (anc && up < 4 && !why) {
        var s = anc.previousElementSibling, hops2 = 0;
        while (s && hops2 < 3 && !why) {
          if (labelish(s)) {
            why = 'ancestor-preceding-text';
            lab = norm(s.innerText || s.textContent);
            rank = 8 + up;
          }
          s = s.previousElementSibling; hops2++;
        }
        anc = anc.parentElement; up++;
      }
    }
    if (!why && e.parentElement && match(ownText(e.parentElement))) {
      why = 'parent-text'; lab = norm(ownText(e.parentElement)); rank = 12;
    }
    if (!why && (match(e.name) || match(e.id))) {
      why = 'name-or-id'; lab = norm(e.name || e.id); rank = 13;
    }
    if (!why) continue;

    var r = e.getBoundingClientRect();
    out.push({
      tag: e.tagName.toLowerCase(),
      type: (e.getAttribute('type') || '').toLowerCase(),
      name: e.name || '',
      id: e.id || '',
      label: lab.slice(0, 120),
      matched_by: why,
      rank: rank,
      selector: cssPath(e),
      value: e.value !== undefined ? String(e.value).slice(0, 120) : '',
      x: Math.round(r.left + r.width / 2),
      y: Math.round(r.top + r.height / 2)
    });
  }

  // Most reliable strategy first, so index 0 is the best-supported guess.
  out.sort(function(a, b){ return a.rank - b.rank; });
  out.forEach(function(o, i){ o.index = i; });
  return JSON.stringify(out);
})()";
        }
    }
}