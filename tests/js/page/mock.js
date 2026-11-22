// minimal DOM mock to run garden-page.js outside the browser
const G = require("../../../src/SeedPlanner.Api/wwwroot/js/garden-geometry.js");
const TR = require("../../../src/SeedPlanner.Api/wwwroot/js/garden-trace.js");
function el(tag, id) {
  const e = { tag, id, style: {}, children: [], listeners: {}, attrs: {}, dataset: {}, hidden: false, value: "", textContent: "", checked: false, disabled: false, open: true, className: "", options: [] };
  e.classList = { add() {}, remove() {}, toggle() {}, contains() { return false; } };
  e.addEventListener = (t, f) => { (e.listeners[t] = e.listeners[t] || []).push(f); };
  e.setAttribute = (k, v) => { e.attrs[k] = v; }; e.getAttribute = k => e.attrs[k]; e.removeAttribute = k => { delete e.attrs[k]; };
  e.appendChild = c => { e.children.push(c); return c; }; e.replaceChildren = (...c) => { e.children = c; };
  e.querySelector = () => el("x"); e.querySelectorAll = () => []; e.closest = () => null; e.contains = () => false;
  e.setPointerCapture = () => {}; e.blur = () => {}; e.focus = () => {}; e.scrollIntoView = () => {};
  e.getBoundingClientRect = () => ({ left: 0, top: 0, width: 1000, height: 1000 });
  e.createSVGPoint = () => ({ x: 0, y: 0, matrixTransform() { return { x: this.x, y: this.y }; } });
  e.getScreenCTM = () => ({ inverse() { return {}; } });
  e.clientWidth = 1000; e.clientHeight = 1000; e.scrollLeft = 0; e.scrollTop = 0; e.scrollWidth = 1000;
  e.insertBefore = c => c; e.remove = () => {}; e.cloneNode = () => el(tag);
  return e;
}
const els = {};
const fire = (e, type, ev) => (e.listeners[type] || []).forEach(f => f(Object.assign({ preventDefault() {}, stopPropagation() {}, target: e, pointerId: 1, ctrlKey: false, key: "" }, ev)));
function load(data, texts) {
  const store = {}; const pending = [];
  const doc = {
    getElementById: id => (els[id] = els[id] || el("x", id)),
    createElementNS: (ns, t) => el(t), createElement: t => el(t), createTextNode: t => ({ textContent: t }),
    querySelectorAll: () => [], querySelector: () => el("x"), addEventListener: (t, f) => { (doc.l[t] = doc.l[t] || []).push(f); }, l: {}, body: el("body"), activeElement: null, documentElement: { lang: "de" },
  };
  const t = (o) => new Proxy(o, { get: (a, k) => (k in a ? a[k] : (typeof k === "string" ? "txt" : undefined)) });
  els["garden-data"] = Object.assign(el("x", "garden-data"), { textContent: JSON.stringify(data) });
  els["garden-text"] = Object.assign(el("x", "garden-text"), { textContent: JSON.stringify(texts || {}) });
  els.plot = Object.assign(el("svg", "plot"), { dataset: { src: "x.png" } });
  els.stage = Object.assign(el("div", "stage"), { attrs: { "data-mode": global.MODE || "setup" } });
  els["scale-mode"] = Object.assign(el("select", "scale-mode"), { value: "area" });
  els["plot-area"] = Object.assign(el("input", "plot-area"), { value: "300" });
  els["zone-kind"] = Object.assign(el("select"), { value: "Garden" });
  els["zone-owner"] = Object.assign(el("select"), { value: "0" });
  els["shape-type"] = Object.assign(el("select"), { value: "rect" });
  els["shape-orient"] = Object.assign(el("select"), { value: "h" });
  els["cut-owner1"] = Object.assign(el("select"), { value: "0" }); els["cut-owner2"] = Object.assign(el("select"), { value: "0" });
  global.document = doc;
  global.window = Object.assign({ GardenGeometry: G, GardenTrace: TR }, (() => { const fs = require('fs'), dir = __dirname + '/../../../src/SeedPlanner.Api/wwwroot/js/'; const o = {}; fs.readdirSync(dir).filter(f => /^garden-.*\.js$/.test(f)).forEach(f => { const g = /root\.(Garden\w+) = api/.exec(fs.readFileSync(dir + f, 'utf8')); if (g) o[g[1]] = require(dir + f); }); return o; })(), { confirm: () => true, addEventListener() {}, location: {} });
  global.localStorage = { getItem: k => store[k] || null, setItem: (k, v) => { store[k] = v; } };
  global.navigator = { language: "de" };
  global.Image = function () { const i = {}; setTimeout(() => {}, 0); Object.defineProperty(i, "src", { set(v) { i.naturalWidth = 1000; i.naturalHeight = 1000; i._v = v; pending.push(i); } }); return i; };
  global.setTimeout = (f) => 0; global.requestAnimationFrame = f => 0; global.getComputedStyle = () => ({});
  const src = require("fs").readFileSync(__dirname + "/../../../src/SeedPlanner.Api/wwwroot/js/garden-page.js", "utf8");
  // the texts: any key gives a string
  const orig = JSON.parse;
  JSON.parse = function (s, r) { const v = orig(s, r); return (v && v.cutDrawing === undefined && v.kinds === undefined && !Array.isArray(v) && Object.keys(v).length === 0) ? v : v; };
  const T = t({ kinds: t({}), shapes: t({ rect: ["x", "a", "b"], circle: ["x", "a", ""], ellipse: ["x", "a", "b"], triangle: ["x", "a", "b"], righttrapezoid: ["x", "a", "b", "c"], trapezoid: ["x", "a", "b", "c"] }), props: t({ use: {}, sun: {}, soil: {}, slope: {} }) });
  const realParse = JSON.parse;
  JSON.parse = function (s) { if (s === els["garden-text"].textContent) return T; return realParse(s); };
  new Function("window", "document", "localStorage", "navigator", "Image", src)(global.window, doc, global.localStorage, global.navigator, global.Image);
  JSON.parse = realParse;
  pending.forEach(i => i.onload && i.onload());
  return { els, fire, doc };
}
module.exports = { load, fire, els };
