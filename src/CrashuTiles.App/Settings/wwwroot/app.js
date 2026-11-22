'use strict';
// CrashuTiles settings page. Plain JS, no build step. Talks to the tray app's local API:
//   GET  /api/config   the config as JSON          POST /api/config   save it (the dock reloads)
//   GET  /api/catalog  sensors, batteries, outputs  POST /api/render   PNG preview of one key

const KEYS = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12', 'small'];

const TYPES = [
  { id: 'empty', name: 'Empty' },
  { id: 'label', name: 'Shortcut', help: 'An icon and a label. What it does is set under Action.' },
  { id: 'sensor', name: 'Sensor', help: 'One reading, as a big number with a one-minute sparkline or as a gauge.' },
  { id: 'sensors', name: 'Three sensors', help: 'Up to three readings on one key, each with a bar.' },
  { id: 'battery', name: 'Battery (one device)' },
  { id: 'batteries', name: 'Batteries (three per key)', help: 'Each further "Batteries" key on the page shows the next three devices.' },
  { id: 'media', name: 'Media' },
  { id: 'audio', name: 'Audio' },
  { id: 'clock', name: 'Clock' },
  { id: 'graph', name: 'Graph', help: 'Up to three readings over the last minute. Best on the wide screen.' },
  { id: 'native', name: "Dock's own screen", smallOnly: true, help: 'Let the dock draw its built-in clock or stats.' },
];

const MEDIA_CONTROLS = [['info', 'Now playing (art, title, progress)'], ['playpause', 'Play / pause'], ['next', 'Next track'], ['prev', 'Previous track']];
const AUDIO_CONTROLS = [['output', 'Output device (opens the picker)'], ['mute', 'Mute'], ['mic', 'Microphone mute'], ['volup', 'Volume up'], ['voldown', 'Volume down']];
const NATIVE = [['clock', 'Time'], ['clock-date', 'Time and date'], ['clock-weekday', 'Time and weekday'], ['clock-full', 'Date, time and weekday'], ['dial', 'Analog clock'], ['stats', 'CPU / RAM / GPU stats']];
const BATTERY_SOURCES = [['any', 'Any source'], ['logitech', 'Logitech'], ['corsair', 'Corsair (iCUE)'], ['bluetooth', 'Bluetooth / AirPods'], ['xinput', 'Xbox controller']];
const ACTIONS = [
  ['', 'Default'], ['launch', 'Open an app or file'], ['url', 'Open a website'], ['hotkey', 'Press keys'],
  ['media', 'Media control'], ['audio', 'Audio'], ['page', 'Go to page'], ['back', 'Go back'], ['config', 'Open config file'],
];
const MEDIA_COMMANDS = [['playpause', 'Play / pause'], ['play', 'Play'], ['pause', 'Pause'], ['next', 'Next'], ['prev', 'Previous']];
const AUDIO_COMMANDS = [['mute', 'Toggle mute'], ['micmute', 'Toggle microphone'], ['volup', 'Volume up'], ['voldown', 'Volume down'], ['output', 'Switch output to…']];

// Common Segoe Fluent Icons glyphs [code, name].
const GLYPHS = [
  ['E768', 'Play'], ['E769', 'Pause'], ['E892', 'Previous'], ['E893', 'Next'], ['E767', 'Volume'], ['E74F', 'Mute'],
  ['E720', 'Microphone'], ['E7F6', 'Headphones'], ['E7F5', 'Speakers'], ['E8D6', 'Music'], ['E714', 'Video'], ['E722', 'Camera'],
  ['E7FC', 'Game'], ['E8EF', 'Calculator'], ['E9D9', 'Diagnostics'], ['E7EF', 'Admin'], ['E7F4', 'Monitor'], ['E713', 'Settings'],
  ['E70F', 'Edit'], ['E8B7', 'Folder'], ['E8A5', 'Document'], ['E774', 'Globe'], ['E80F', 'Home'], ['E715', 'Mail'],
  ['E8BD', 'Message'], ['E717', 'Phone'], ['E77B', 'Person'], ['E721', 'Search'], ['E72C', 'Refresh'], ['E7E8', 'Power'],
  ['E72E', 'Lock'], ['E706', 'Brightness'], ['E708', 'Night'], ['E734', 'Star'], ['E946', 'Info'], ['E7BA', 'Warning'],
  ['E73E', 'Check'], ['E711', 'Close'], ['E710', 'Add'], ['E738', 'Remove'], ['E712', 'More'], ['E72B', 'Back'],
  ['E72A', 'Forward'], ['E8C8', 'Copy'], ['E77F', 'Paste'],
];

// ---------- state ----------

const token = (location.hash.match(/token=([0-9a-f]+)/) || [])[1] || sessionStorage.getItem('ct-token');
if (location.hash.includes('token=')) history.replaceState(null, '', '/');
if (token) sessionStorage.setItem('ct-token', token);

let cfg = null;        // the config being edited
let saved = '';        // JSON of the last saved config
let catalog = { sensors: [], batteries: [], outputs: [] };
let page = '';         // page shown
let selected = null;   // key id being edited
const images = {};     // key id -> object URL of its last preview

const $ = sel => document.querySelector(sel);

function el(tag, attrs = {}, ...children) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === 'class') e.className = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (k === 'text') e.textContent = v;
    else e.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) if (c != null) e.append(c);
  return e;
}

async function api(path, options = {}) {
  const res = await fetch(path, {
    ...options,
    headers: { 'Content-Type': 'application/json', 'X-CrashuTiles-Token': token || '' },
  });
  if (!res.ok) {
    let message = res.statusText;
    try { message = (await res.json()).error || message; } catch { /* not JSON */ }
    const err = new Error(message);
    err.status = res.status;
    throw err;
  }
  return res;
}

// ---------- config helpers ----------

const keysOf = p => cfg.pages[p] || (cfg.pages[p] = {});
const isDirty = () => JSON.stringify(cfg) !== saved;
const clean = obj => JSON.parse(JSON.stringify(obj)); // drops undefined

function kindOf(k) {
  if (!k) return 'empty';
  if (k.native) return 'native';
  const t = (k.tile || 'label').toLowerCase();
  return ['cpu', 'ram', 'gpu'].includes(t) ? 'sensor' : t;
}

// Older shortcuts ("tile": "cpu") become plain sensor tiles so the editor has one shape to deal with.
function normalize(config) {
  for (const keys of Object.values(config.pages)) {
    for (const k of Object.values(keys)) {
      const t = (k.tile || '').toLowerCase();
      if (['cpu', 'ram', 'gpu'].includes(t)) { k.sensor = k.sensor || t + '.load'; k.tile = 'sensor'; }
    }
  }
  return config;
}

function keyName(id) {
  if (id === 'small') return ['Wide screen', 'row 3, the two right-hand cells'];
  const n = Number(id);
  return [`Key ${n}`, `row ${Math.floor(n / 5) + 1}, column ${(n % 5) + 1}`];
}

/** "batteries" keys take the next three devices in key order. */
function batteryGroup(p, id) {
  let g = 0;
  for (const k of KEYS) {
    if (k === id) return g;
    if (kindOf(keysOf(p)[k]) === 'batteries') g++;
  }
  return 0;
}

function setStatus(text, kind = '') {
  const s = $('#status');
  s.textContent = text;
  s.className = 'status ' + kind;
}

function changed() {
  const dirty = isDirty();
  $('#save').disabled = !dirty;
  $('#revert').disabled = !dirty;
  setStatus(dirty ? 'Unsaved changes' : 'All changes saved', dirty ? 'dirty' : 'ok');
}

let toastTimer;
function toast(text) {
  const t = $('#toast');
  t.textContent = text;
  t.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { t.hidden = true; }, 3500);
}

// ---------- previews ----------

const pending = new Map();
function schedulePreview(id, delay = 250) {
  clearTimeout(pending.get(id));
  pending.set(id, setTimeout(() => preview(id), delay));
}

async function preview(id) {
  const k = keysOf(page)[id];
  const cell = document.querySelector(`.key[data-key="${id}"]`);
  if (!cell) return;
  cell.classList.toggle('empty', !k);
  let img = cell.querySelector('img');
  if (!k) { img?.remove(); return; }
  try {
    const res = await api('/api/render', {
      method: 'POST',
      body: JSON.stringify({ key: k, small: id === 'small', group: batteryGroup(page, id) }),
    });
    const url = URL.createObjectURL(await res.blob());
    if (!img) { img = el('img', { alt: '' }); cell.prepend(img); }
    img.src = url;
    if (images[id]) URL.revokeObjectURL(images[id]);
    images[id] = url;
  } catch (e) {
    if (e.status === 401) lock();
  }
}

// Live data changes, so keep the previews fresh (one key at a time, never piling up).
let refreshing = false;
async function refreshAll() {
  if (refreshing || locked || document.hidden || !cfg) return;
  refreshing = true;
  try { for (const id of KEYS) await preview(id); } finally { refreshing = false; }
}

// ---------- page tabs ----------

function renderPages() {
  const nav = $('#pages');
  nav.replaceChildren();
  for (const p of Object.keys(cfg.pages)) {
    nav.append(el('button', {
      type: 'button', class: 'tab' + (p === page ? ' active' : ''),
      title: p === cfg.startPage ? 'Start page' : null,
      onclick: () => showPage(p),
    }, p, p === cfg.startPage ? el('span', { class: 'star', text: '★' }) : null));
  }
  nav.append(
    el('button', { type: 'button', class: 'small', onclick: addPage }, '+ Page'),
    el('span', { class: 'spacer' }),
    el('button', { type: 'button', class: 'small', onclick: renamePage }, 'Rename'),
    el('button', { type: 'button', class: 'small danger', onclick: deletePage, disabled: Object.keys(cfg.pages).length < 2 }, 'Delete page'),
  );

  const start = $('#startPage');
  start.replaceChildren(...Object.keys(cfg.pages).map(p => el('option', { value: p, selected: p === cfg.startPage }, p)));
}

function validPageName(name) {
  if (!name) return false;
  name = name.trim();
  if (!name || name.startsWith('__')) { toast('Page names can’t be empty or start with "__".'); return false; }
  if (Object.keys(cfg.pages).some(p => p.toLowerCase() === name.toLowerCase())) { toast(`There is already a page called "${name}".`); return false; }
  return true;
}

function addPage() {
  const name = prompt('Name of the new page:');
  if (!validPageName(name)) return;
  cfg.pages[name.trim()] = { '0': { label: 'Back', icon: 'glyph:E72B', color: '#F5A623', action: { type: 'back' } } };
  changed();
  showPage(name.trim());
  toast('Page added with a Back key. Point a key’s action at it to open it.');
}

function renamePage() {
  const name = prompt(`Rename "${page}" to:`, page);
  if (!name || name.trim() === page || !validPageName(name)) return;
  const to = name.trim();
  const pages = {};
  for (const [p, keys] of Object.entries(cfg.pages)) pages[p === page ? to : p] = keys;
  cfg.pages = pages;
  if (cfg.startPage === page) cfg.startPage = to;
  // Keep "go to page" actions pointing at it.
  for (const keys of Object.values(cfg.pages))
    for (const k of Object.values(keys))
      for (const a of [k.action, k.hold]) if (a && a.type === 'page' && a.page === page) a.page = to;
  page = to;
  changed();
  renderPages();
}

function deletePage() {
  if (Object.keys(cfg.pages).length < 2) return;
  if (!confirm(`Delete the page "${page}" and all its keys?`)) return;
  delete cfg.pages[page];
  if (cfg.startPage === page) cfg.startPage = Object.keys(cfg.pages)[0];
  page = cfg.startPage;
  changed();
  showPage(page);
}

// ---------- the dock grid ----------

function renderDeck() {
  const deck = $('#deck');
  deck.replaceChildren();
  for (const id of KEYS) {
    const k = keysOf(page)[id];
    const cell = el('div', {
      class: 'key' + (id === 'small' ? ' small' : '') + (k ? '' : ' empty') + (id === selected ? ' selected' : ''),
      'data-key': id, draggable: 'true', tabindex: '0', role: 'button',
      'aria-label': keyName(id)[0],
      onclick: () => select(id),
      onkeydown: e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(id); } },
      ondragstart: e => e.dataTransfer.setData('text/plain', id),
      ondragover: e => { e.preventDefault(); cell.classList.add('drop'); },
      ondragleave: () => cell.classList.remove('drop'),
      ondrop: e => { e.preventDefault(); cell.classList.remove('drop'); swap(e.dataTransfer.getData('text/plain'), id); },
    });
    if (images[id] && k) cell.append(el('img', { src: images[id], alt: '' }));
    deck.append(cell);
  }
  for (const id of KEYS) schedulePreview(id, 0);
}

function swap(from, to) {
  if (!from || from === to) return;
  const keys = keysOf(page);
  const a = keys[from], b = keys[to];
  if ((a && a.native && to !== 'small') || (b && b.native && from !== 'small')) {
    toast('The dock’s own screen only fits the wide screen.');
    return;
  }
  if (b) keys[from] = b; else delete keys[from];
  if (a) keys[to] = a; else delete keys[to];
  selected = to;
  changed();
  renderDeck();
  renderEditor();
}

function select(id) {
  selected = id;
  document.querySelectorAll('.key').forEach(c => c.classList.toggle('selected', c.dataset.key === id));
  renderEditor();
}

function showPage(p) {
  page = p;
  selected = null;
  for (const id of Object.keys(images)) { URL.revokeObjectURL(images[id]); delete images[id]; }
  renderPages();
  renderDeck();
  renderEditor();
}

// ---------- editor building blocks ----------

function field(label, control, help) {
  return el('div', { class: 'field' },
    el('label', {}, label), control, help ? el('div', { class: 'help', text: help }) : null);
}

/** Binds an input to obj[prop]; empty values are removed. */
function bind(input, obj, prop, id, parse = v => v) {
  input.addEventListener('input', () => {
    const v = input.type === 'checkbox' ? input.checked : input.value;
    const parsed = v === '' || v == null ? undefined : parse(v);
    if (parsed === undefined) delete obj[prop]; else obj[prop] = parsed;
    changed();
    schedulePreview(id);
  });
  return input;
}

function text(obj, prop, id, placeholder) {
  return bind(el('input', { type: 'text', value: obj[prop] ?? '', placeholder }), obj, prop, id);
}

function number(obj, prop, id, placeholder) {
  return bind(el('input', { type: 'number', value: obj[prop] ?? '', placeholder, step: 'any' }), obj, prop, id, Number);
}

function choice(obj, prop, id, options, emptyLabel) {
  const opts = emptyLabel != null ? [['', emptyLabel], ...options] : options;
  const current = obj[prop] ?? '';
  const select = el('select', {}, opts.map(([v, l]) => el('option', { value: v, selected: v === current }, l)));
  // Keep a value the list doesn't know (hand-edited configs) instead of silently changing it.
  if (current && !opts.some(([v]) => v === current)) select.prepend(el('option', { value: current, selected: true }, `${current} (custom)`));
  return bind(select, obj, prop, id);
}

function colorPicker(obj, id, defaultHint) {
  const has = !!obj.color;
  const picker = el('input', { type: 'color', value: has ? obj.color : '#3fa9f5', class: 'fixed', disabled: !has });
  const toggle = el('input', { type: 'checkbox', checked: has, id: 'colorOn' });
  toggle.addEventListener('input', () => {
    picker.disabled = !toggle.checked;
    if (toggle.checked) obj.color = picker.value.toUpperCase(); else delete obj.color;
    changed(); schedulePreview(id);
  });
  picker.addEventListener('input', () => { obj.color = picker.value.toUpperCase(); changed(); schedulePreview(id); });
  return field('Colour', el('div', { class: 'inline' },
    el('label', { class: 'fixed inline' }, toggle, ' Custom colour'), picker),
    has ? null : defaultHint);
}

function iconPicker(obj, id) {
  const input = el('input', { type: 'text', value: obj.icon ?? '', placeholder: 'glyph:E768 or C:\\path\\to\\icon.png' });
  const glyphPreview = el('span', { class: 'glyph-preview glyph fixed' });
  const showGlyph = () => {
    const m = /^glyph:([0-9a-f]{4,5})$/i.exec(input.value.trim());
    glyphPreview.textContent = m ? String.fromCodePoint(parseInt(m[1], 16)) : '';
  };
  bind(input, obj, 'icon', id);
  input.addEventListener('input', showGlyph);
  showGlyph();

  const grid = el('div', { class: 'glyphs', hidden: true },
    GLYPHS.map(([code, name]) => el('button', {
      type: 'button', class: 'glyph', title: name,
      onclick: () => { input.value = 'glyph:' + code; input.dispatchEvent(new Event('input')); },
    }, String.fromCodePoint(parseInt(code, 16)))));
  const more = el('button', { type: 'button', class: 'small fixed', onclick: () => { grid.hidden = !grid.hidden; } }, 'Icons');
  return field('Icon', el('div', {}, el('div', { class: 'inline' }, glyphPreview, input, more), grid),
    'Leave empty on an "Open an app" key to show the app’s own icon.');
}

function sensorOptions() {
  return catalog.sensors.map(s => [s.key, `${s.title}  (${s.key})`]);
}

function sensorList(k, id) {
  const list = [...(k.sensors || [])];
  const controls = [0, 1, 2].map(i => {
    const holder = { v: list[i] };
    const sel = choice(holder, 'v', id, sensorOptions(), '(none)');
    sel.addEventListener('input', () => {
      list[i] = holder.v;
      const picked = list.filter(Boolean);
      if (picked.length) k.sensors = picked; else delete k.sensors;
      changed(); schedulePreview(id);
    });
    return sel;
  });
  return field('Sensors', el('div', { style: 'display:grid;gap:6px' }, controls));
}

function batteryDevice(k, id) {
  const options = [['', 'First device from the source']];
  for (const b of catalog.batteries) options.push([`${b.source}|${b.name}`, `${b.shortName}  (${b.source}${b.online ? '' : ', offline'})`]);
  const current = k.match ? `${k.battery || 'any'}|${k.match}` : '';
  const holder = { v: current };
  const sel = choice(holder, 'v', id, options.slice(1), options[0][1]);
  sel.addEventListener('input', () => {
    if (!holder.v) { delete k.match; }
    else { const [source, ...name] = holder.v.split('|'); k.battery = source; k.match = name.join('|'); }
    changed(); schedulePreview(id);
    renderEditor();
  });
  return field('Device', sel);
}

// ---------- actions ----------

function defaultActionText(k, id) {
  const kind = kindOf(k);
  if (kind === 'batteries') return 'Default: opens the battery page.';
  if (kind === 'media') return (k.control || 'info') === 'info' ? 'Default: play / pause.' : 'Default: the media control shown on the key.';
  if (kind === 'audio') return (k.control || 'output') === 'output' ? 'Default: opens the output picker.' : 'Default: the audio control shown on the key.';
  return 'Default: nothing happens.';
}

function actionEditor(k, prop, id, title) {
  const a = k[prop] || {};
  const box = el('div', { class: 'section' }, el('h3', {}, title));
  const typeHolder = { v: a.type || '' };
  const options = prop === 'hold' ? [['', 'Nothing'], ...ACTIONS.slice(1)] : ACTIONS;
  const typeSel = choice(typeHolder, 'v', id, options);
  box.append(field('Does', typeSel, prop === 'action' && !a.type ? defaultActionText(k, id) : null));

  typeSel.addEventListener('input', () => {
    const t = typeHolder.v;
    if (!t) delete k[prop];
    else k[prop] = { type: t, ...(t === 'media' ? { command: 'playpause' } : t === 'audio' ? { command: 'mute' } : {}) };
    changed();
    renderEditor();
  });

  const t = a.type;
  if (t) {
    const act = k[prop];
    if (t === 'launch') {
      box.append(field('App, file or folder', text(act, 'path', id, 'notepad.exe or C:\\Games\\game.exe')),
        field('Arguments', text(act, 'args', id, 'optional')));
    } else if (t === 'url') {
      box.append(field('Address', text(act, 'url', id, 'https://… or ms-settings:')));
    } else if (t === 'hotkey') {
      box.append(field('Keys', text(act, 'keys', id, 'ctrl+shift+esc'),
        'Join keys with +. Names: ctrl, shift, alt, win, a–z, 0–9, f1–f24, esc, enter, tab, space, playpause, next, prev, mute, volup, voldown.'));
    } else if (t === 'media') {
      box.append(field('Command', choice(act, 'command', id, MEDIA_COMMANDS)));
    } else if (t === 'audio') {
      const cmd = choice(act, 'command', id, AUDIO_COMMANDS);
      cmd.addEventListener('input', () => renderEditor());
      box.append(field('Command', cmd));
      if (act.command === 'output')
        box.append(field('Output', choice(act, 'device', id, catalog.outputs.map(o => [o.name, o.name]), 'Pick a device')));
      if (act.command === 'volup' || act.command === 'voldown')
        box.append(field('Step (%)', number(act, 'step', id, '5')));
    } else if (t === 'page') {
      box.append(field('Page', choice(act, 'page', id, Object.keys(cfg.pages).map(p => [p, p]), 'Pick a page')));
    }
  }
  return box;
}

// ---------- the key editor ----------

function renderEditor() {
  const ed = $('#editor');
  ed.replaceChildren();
  if (selected == null) {
    ed.append(el('h2', {}, 'Edit a key'), el('p', { class: 'empty-state' }, 'Click a key on the dock to change what it shows and does.'));
    return;
  }
  const id = selected;
  const keys = keysOf(page);
  const k = keys[id];
  const kind = kindOf(k);
  const [name, where] = keyName(id);

  ed.append(el('div', { class: 'head' }, el('h2', {}, name), el('span', { class: 'where' }, where)));

  const types = TYPES.filter(t => !t.smallOnly || id === 'small');
  const typeSel = el('select', {}, types.map(t => el('option', { value: t.id, selected: t.id === kind }, t.name)));
  typeSel.addEventListener('input', () => {
    const t = typeSel.value;
    // Keep the label and the action when switching types; start each type with sensible defaults.
    const keep = k ? { label: k.label, action: k.action, hold: k.hold } : {};
    const fresh = {
      empty: null,
      label: { label: keep.label || 'New key' },
      sensor: { tile: 'sensor', sensor: 'cpu.load' },
      sensors: { tile: 'sensors', sensors: ['cpu.load', 'gpu.load', 'ram.load'] },
      battery: { tile: 'battery', battery: 'any' },
      batteries: { tile: 'batteries' },
      media: { tile: 'media' },
      audio: { tile: 'audio', control: 'output' },
      clock: { tile: 'clock' },
      graph: { tile: 'graph', sensors: ['cpu.load', 'gpu.load', 'ram.load'] },
      native: { native: 'clock' },
    }[t];
    if (fresh === null) delete keys[id];
    else keys[id] = clean({ ...fresh, action: t === 'native' ? undefined : keep.action, hold: t === 'native' ? undefined : keep.hold });
    changed();
    schedulePreview(id, 0);
    renderEditor();
  });
  const typeInfo = types.find(t => t.id === kind);
  ed.append(field('Shows', typeSel, typeInfo && typeInfo.help));

  if (!k) return;

  switch (kind) {
    case 'label':
      ed.append(field('Label', text(k, 'label', id, 'Shown under the icon')), iconPicker(k, id), colorPicker(k, id, 'Black background.'));
      break;
    case 'sensor':
      ed.append(
        field('Reading', choice(k, 'sensor', id, sensorOptions()), 'CPU temperature, clock and power need admin rights and the PawnIO driver.'),
        field('Style', choice(k, 'style', id, [['spark', 'Number with sparkline'], ['gauge', 'Ring gauge']], null)),
        field('Title', text(k, 'label', id, 'Defaults to the reading’s name')),
        el('div', { class: 'inline' },
          field('Scale top', number(k, 'max', id, 'auto')),
          field('Orange from', number(k, 'warn', id, 'off')),
          field('Red from', number(k, 'crit', id, 'off'))),
        colorPicker(k, id, 'Uses the reading’s family colour (CPU blue, GPU green, RAM golden).'));
      break;
    case 'sensors':
      ed.append(sensorList(k, id),
        el('div', { class: 'inline' }, field('Orange from', number(k, 'warn', id, 'off')), field('Red from', number(k, 'crit', id, 'off'))));
      break;
    case 'graph':
      ed.append(sensorList(k, id), field('Scale top', number(k, 'max', id, 'auto')));
      break;
    case 'battery':
      ed.append(
        field('Source', choice(k, 'battery', id, BATTERY_SOURCES)),
        batteryDevice(k, id),
        field('Title', text(k, 'label', id, 'Defaults to the device name')),
        el('div', { class: 'inline' }, field('Orange at or below', number(k, 'warn', id, '30')), field('Red at or below', number(k, 'crit', id, '15'))));
      break;
    case 'batteries':
      ed.append(
        el('div', { class: 'note' }, catalog.batteries.length
          ? `Devices seen so far: ${catalog.batteries.map(b => b.shortName).join(', ')}.`
          : 'No battery devices seen yet.'),
        el('div', { class: 'inline' }, field('Orange at or below', number(k, 'warn', id, '30')), field('Red at or below', number(k, 'crit', id, '15'))));
      break;
    case 'media': {
      const c = choice(k, 'control', id, MEDIA_CONTROLS);
      c.addEventListener('input', () => renderEditor());
      ed.append(field('Control', c));
      if ((k.control || 'info') !== 'info') ed.append(field('Label', text(k, 'label', id, 'Defaults to Play, Pause, Next…')));
      break;
    }
    case 'audio': {
      const c = choice(k, 'control', id, AUDIO_CONTROLS);
      c.addEventListener('input', () => renderEditor());
      ed.append(field('Control', c));
      if (k.control === 'volup' || k.control === 'voldown') ed.append(field('Step (%)', number(k, 'step', id, '5')));
      ed.append(colorPicker(k, id, 'Black background; red while muted.'));
      break;
    }
    case 'clock':
      ed.append(colorPicker(k, id, 'Orange background.'));
      break;
    case 'native':
      ed.append(field('Screen', choice(k, 'native', id, NATIVE)));
      return; // the dock's own screen has no actions
  }

  ed.append(actionEditor(k, 'action', id, 'Action (tap)'));
  ed.append(actionEditor(k, 'hold', id, 'Hold (half a second)'));
}

// ---------- load / save ----------

let locked = false;

// The key is wrong (CrashuTiles restarted, which issues a new one): stop polling and say how to get back in.
function lock() {
  locked = true;
  sessionStorage.removeItem('ct-token');
  $('#app').hidden = true;
  $('#locked').hidden = false;
  setStatus('');
}

async function load() {
  if (!token) { lock(); return; }
  try {
    const [c, cat] = await Promise.all([api('/api/config').then(r => r.json()), api('/api/catalog').then(r => r.json())]);
    cfg = normalize(c);
    catalog = cat;
    saved = JSON.stringify(cfg);
  } catch (e) {
    if (e.status === 401) { lock(); return; }
    setStatus('Could not load: ' + e.message, 'error');
    return;
  }
  $('#app').hidden = false;
  $('#brightness').value = cfg.brightness ?? 80;
  $('#brightnessValue').textContent = `${cfg.brightness ?? 80}%`;
  showPage(cfg.pages[cfg.startPage] ? cfg.startPage : Object.keys(cfg.pages)[0]);
  changed();
}

async function save() {
  $('#save').disabled = true;
  setStatus('Saving…');
  try {
    await api('/api/config', { method: 'POST', body: JSON.stringify(cfg) });
    saved = JSON.stringify(cfg);
    changed();
    setStatus('Saved, the dock is updating', 'ok');
    api('/api/catalog').then(r => r.json()).then(c => { catalog = c; }).catch(() => {});
  } catch (e) {
    setStatus('Not saved: ' + e.message, 'error');
    $('#save').disabled = false;
  }
}

function revert() {
  if (!confirm('Throw away the unsaved changes?')) return;
  cfg = JSON.parse(saved);
  changed();
  $('#brightness').value = cfg.brightness ?? 80;
  $('#brightnessValue').textContent = `${cfg.brightness ?? 80}%`;
  showPage(cfg.pages[page] ? page : cfg.startPage);
}

$('#save').addEventListener('click', save);
$('#revert').addEventListener('click', revert);
$('#brightness').addEventListener('input', e => {
  cfg.brightness = Number(e.target.value);
  $('#brightnessValue').textContent = `${cfg.brightness}%`;
  changed();
});
$('#startPage').addEventListener('input', e => { cfg.startPage = e.target.value; changed(); renderPages(); });
document.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); if (isDirty()) save(); }
});
window.addEventListener('beforeunload', e => { if (cfg && isDirty()) { e.preventDefault(); e.returnValue = ''; } });

setInterval(refreshAll, 2500);
setInterval(() => {
  if (!locked) api('/api/catalog').then(r => r.json()).then(c => { catalog = c; }).catch(e => { if (e.status === 401) lock(); });
}, 30000);
load();
