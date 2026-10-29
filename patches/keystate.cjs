'use strict';
const { spawn } = require('child_process');
const path = require('path');
const DEFAULT_KEYS = ['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight', 'KeyP', 'KeyR', 'KeyF', 'KeyQ', 'KeyE', 'KeyZ', 'Space'];
const MAX_EVENTS = 256;
function createManager(options = {}) {
  let enabled = false, focused = false, allowed = new Set(), owner, detach;
  let down = new Set(), pressed = new Set(), released = new Set(), events = [];
  let aliases = new Map(), diagnostics = false, nativeWorker, workerGeneration = 0, lastNative = 0, error = null;
  const downAt = new Map();
  let raw = [];
  function trace(input, source) { if (!enabled || !diagnostics) return; raw.push({source, type:input.type, code:input.code, key:input.key, repeat:input.isAutoRepeat}); raw = raw.slice(-12); }
  let sequence = 0, overflow = false, generation = 0;
  function record(type, code, reason) {
    if (events.length === MAX_EVENTS) { events.shift(); overflow = true; }
    events.push({ sequence: ++sequence, type, code, reason });
  }
  function clear(reason) {
    pressed.clear(); released.clear(); events = []; overflow = false;
    for (const code of down) { released.add(code); record('keyup', code, reason); }
    down.clear(); downAt.clear(); aliases.clear();
    record('reset', null, reason);
  }
  function disable(reason = 'disabled') {
    stopNative();
    enabled = false; error = null; downAt.clear(); allowed.clear(); aliases.clear(); raw = []; down.clear(); pressed.clear(); released.clear(); events = []; overflow = false;
    generation += 1;
    return { version: '1.1.0', enabled: false, reason, generation };
  }
  function read(config = {}) {
    if (enabled && focused && nativeWorker && Date.now() - lastNative > 500) clear('native-timeout');
    const snapshot = { version: '1.1.0', enabled, focused, generation, sequence, overflow,
      nativePolling: Boolean(nativeWorker), error, raw: raw.map(e => ({...e})), down: [...down], pressed: [...pressed], released: [...released], events: events.map((e) => ({ ...e })) };
    if (config.consume !== false) { pressed.clear(); released.clear(); events = []; overflow = false; }
    return snapshot;
  }
  function enable(config = {}) {
    const keys = config.keys === undefined ? DEFAULT_KEYS : config.keys;
    if (!Array.isArray(keys) || keys.length > 64 || !keys.length || keys.some((key) => typeof key !== 'string' || !/^(Key[A-Z]|Digit[0-9]|Arrow(Up|Down|Left|Right)|Space|Enter|Escape|Tab|Backspace|Shift(Left|Right))$/.test(key))) {
      throw new TypeError('keystate.keys must contain 1..64 supported physical key codes');
    }
    disable('enable');
    allowed = new Set(keys); diagnostics = config.diagnostics === true; enabled = true; focused = Boolean(owner && owner.isFocused());
    if (focused) startNative();
    return read({ consume: false });
  }
  function reconcile(held, stamp) {
    if (!enabled || !focused) return;
    lastNative = Date.now();
    for (const code of [...down]) {
      // Ignore a snapshot sampled before this keydown; allow IPC scheduling slack.
      if (!held.has(code) && stamp - (downAt.get(code) || 0) >= 12) {
        down.delete(code); downAt.delete(code); released.add(code); record('keyup', code, 'native-state');
        for (const [key, codes] of aliases) { codes.delete(code); if (!codes.size) aliases.delete(key); }
      }
    }
  }
  function stopNative() {
    workerGeneration += 1;
    if (nativeWorker) { nativeWorker.removeAllListeners('exit'); nativeWorker.kill(); nativeWorker = undefined; }
  }
  function startNative() {
    if (options.native === false || nativeWorker) return;
    const generation = ++workerGeneration;
    const child = (options.spawn || spawn)(path.join(path.dirname(process.execPath), 'resources', 'KeyStateProbe.exe'), [String(process.pid), [...allowed].join(',')], { windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] });
    nativeWorker = child; lastNative = Date.now(); let pending = '';
    child.stdout.on('data', (chunk) => {
      if (generation !== workerGeneration) return;
      pending += chunk.toString();
      if (pending.length > 16384) { pending = ''; clear('native-protocol'); return; }
      let end;
      while ((end = pending.indexOf('\n')) >= 0) {
        const line = pending.slice(0, end).trim(); pending = pending.slice(end + 1);
        const split = line.indexOf('|'); const stamp = Number(line.slice(0, split));
        if (split < 1 || !Number.isFinite(stamp)) continue;
        reconcile(new Set(line.slice(split + 1).split(',').filter((key) => allowed.has(key))), stamp);
      }
    });
    const fail = () => {
      if (generation !== workerGeneration) return;
      stopNative(); clear('native-unavailable'); enabled = false; error = 'Native key-state helper stopped';
    };
    child.on('error', fail); child.on('exit', fail);
  }
  function identity(input) {
    if (typeof input.code === 'string' && input.code.length) return input.code;
    const key = String(input.key || '').toLowerCase();
    const matches = aliases.get(key);
    if (matches && matches.size === 1) return [...matches][0];
    if (/^[a-z]$/.test(key)) return 'Key' + key.toUpperCase();
    if (/^[0-9]$/.test(key)) return 'Digit' + key;
    const special = { arrowup: 'ArrowUp', arrowdown: 'ArrowDown', arrowleft: 'ArrowLeft', arrowright: 'ArrowRight',
      ' ': 'Space', space: 'Space', enter: 'Enter', escape: 'Escape', tab: 'Tab', backspace: 'Backspace',
      shift: input.location === 2 ? 'ShiftRight' : 'ShiftLeft' };
    return special[key];
  }
  function handle(event, input) {
    trace(input, 'before');
    if (!enabled || !focused) return;
    if (input.control || input.meta || input.alt) { clear('shortcut'); return; }
    const code = identity(input);
    if (!allowed.has(code) || !['keyDown', 'keyUp'].includes(input.type)) return;
    event.preventDefault();
    if (input.type === 'keyDown') {
      if (input.isAutoRepeat || down.has(code)) return;
      down.add(code); downAt.set(code, Date.now()); pressed.add(code); record('keydown', code);
      const key = String(input.key || '').toLowerCase();
      if (key) { if (!aliases.has(key)) aliases.set(key, new Set()); aliases.get(key).add(code); }
    } else if (down.delete(code)) {
      downAt.delete(code); released.add(code); record('keyup', code);
      for (const [key, codes] of aliases) { codes.delete(code); if (!codes.size) aliases.delete(key); }
    }
  }

  function attach(win) {
    if (owner === win) return;
    if (detach) detach();
    disable('window'); owner = win; focused = win.isFocused();
    const contents = win.webContents;
    const blur = () => { focused = false; stopNative(); clear('blur'); };
    const focus = () => { focused = true; if (enabled) startNative(); };
    const reset = () => disable('renderer-reset');
    const close = () => { disable('closed'); detach(); };
    const observe = (_, input) => { if (/key/i.test(input.type || '')) trace(input, 'input'); };
    contents.on('input-event', observe);
    contents.on('before-input-event', handle);
    contents.on('did-start-loading', reset);
    contents.on('render-process-gone', reset);
    win.on('blur', blur); win.on('focus', focus); win.on('closed', close);
    detach = () => {
      contents.removeListener('input-event', observe);
      contents.removeListener('before-input-event', handle);
      contents.removeListener('did-start-loading', reset);
      contents.removeListener('render-process-gone', reset);
      win.removeListener('blur', blur); win.removeListener('focus', focus); win.removeListener('closed', close);
      owner = undefined; detach = undefined; focused = false;
    };
  }
  return { attach, enable, read, disable };
}
module.exports = createManager();
module.exports.createManager = createManager;
