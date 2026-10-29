// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

(() => {
  if (window.__pulsoidVrcReader) return;
  const state = { element: null, selector: '', value: null, revision: 0, changedAt: 0, selecting: true };
  const options = window.__pulsoidVrcOptions || {};
  const instance = crypto.randomUUID();
  const panel = document.createElement('div');
  panel.id = '__pulsoid_vrc_panel';
  panel.style.cssText = 'position:fixed!important;left:16px!important;bottom:16px!important;z-index:2147483647!important;background:#172338!important;color:white!important;padding:12px 16px!important;border-radius:10px!important;font:14px/1.7 sans-serif!important;box-shadow:0 4px 16px #0005!important;max-width:420px!important;';
  const label = document.createElement('span');
  const button = document.createElement('button');
  button.textContent = '重新选择';
  button.style.cssText = 'margin-left:12px;padding:4px 8px;cursor:pointer;color:#172338;background:white;border:0;border-radius:4px;font:14px sans-serif';
  panel.append(label, button);
  (document.body || document.documentElement).append(panel);

  function number(el) {
    if (!(el instanceof HTMLElement) || !el.isConnected || panel.contains(el)) return null;
    if (el.checkVisibility && !el.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })) return null;
    const style = getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0 || !el.getClientRects().length) return null;
    // Only accept an unambiguous numeric label, optionally decorated with a heart or BPM.
    const text = el.innerText.trim();
    const match = text.match(/^[♥❤♡💓💗💖❤️\s]*(\d{1,3})\s*(?:BPM|次\s*\/\s*分(?:钟)?)?$/iu);
    const value = match ? Number(match[1]) : 0;
    return value >= 1 && value <= 300 ? value : null;
  }
  function path(el) {
    if (el.id && document.querySelectorAll('#' + CSS.escape(el.id)).length === 1) return '#' + CSS.escape(el.id);
    const parts = [];
    while (el && el !== document.body) {
      const tag = el.localName;
      const siblings = [...el.parentElement.children].filter(x => x.localName === tag);
      parts.unshift(tag + ':nth-of-type(' + (siblings.indexOf(el) + 1) + ')');
      el = el.parentElement;
    }
    return 'body > ' + parts.join(' > ');
  }
  function choose(el, preserveAge = false) {
    const value = number(el);
    if (value === null) return false;
    state.element = el; state.selector = path(el); state.selecting = false;
    if (!preserveAge || value !== state.value) {
      state.value = value; state.revision++; state.changedAt = performance.now();
    }
    label.textContent = '已选中心率 ' + value + ' BPM';
    return true;
  }
  button.addEventListener('click', () => {
    state.selecting = true; state.element = null; state.value = null;
    label.textContent = '请点击网页上正在变化的心率数字';
  });
  document.addEventListener('click', event => {
    if (!state.selecting || panel.contains(event.target)) return;
    event.preventDefault(); event.stopImmediatePropagation();
    let el = event.target;
    for (let i = 0; el && i < 3; i++, el = el.parentElement) {
      if (choose(el)) return;
    }
    label.textContent = '未识别到单个心率数值，请直接点击数字（支持 1–300）';
  }, true);
  label.textContent = '请点击网页上正在变化的心率数字';
  window.__pulsoidVrcReader = {
    read() {
      if (options.automatic) {
        let candidates;
        if (options.selector) {
          try { candidates = [...document.querySelectorAll(options.selector)]; }
          catch { return { kind: 'invalid', instance }; }
        } else {
          const numbers = [...document.querySelectorAll('body *')].filter(el =>
            !el.closest('button,input,select,textarea,nav,header,footer') && number(el) !== null);
          // Ignore wrappers around the same numeric text; require exactly one numeric leaf.
          candidates = numbers.filter(el => !numbers.some(other => other !== el && el.contains(other)));
        }
        if (candidates.length > 1) return { kind: 'ambiguous', instance };
        if (candidates.length !== 1 || number(candidates[0]) === null) return { kind: 'missing', instance };
        choose(candidates[0], true);
      }
      if (state.selecting) return { kind: 'select', instance };
      // A replaced element must be selected again; do not silently read a different label.
      const value = number(state.element);
      if (value === null) { label.textContent = '数字已失效，请重新选择'; return { kind: 'invalid', instance }; }
      if (value !== state.value) {
        state.value = value; state.revision++; state.changedAt = performance.now();
      }
      const age = performance.now() - state.changedAt;
      label.textContent = age > 15000 ? '数字 15 秒未变化，暂停发送；变化后恢复' : '正在读取 ' + value + ' BPM';
      return { kind: 'value', instance, bpm: value, revision: state.revision, ageMs: age, selector: state.selector };
    }
  };
})();
