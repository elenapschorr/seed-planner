const { load, fire } = require("./mock.js");
const data = { boundary: [[100,100],[900,100],[900,900],[100,900]], lengths: [null,null,null,null], zones: [], knownArea: 300, neighbors: [], cutLine: null, beds: [], placedBeds: [] };
const ok = (name, cond) => console.log(cond ? "ok  " : "FAIL", name);
// a picture whose pixels can be read, and a tracer that finds a square
const realCreate = global.document && global.document.createElement;
const { els } = load(data);
global.window.GardenTrace.polygonAt = () => ({ status: "ok", points: [[200,200],[400,200],[400,400],[200,400]] });
const doc = global.document;
const old = doc.createElement.bind(doc);
doc.createElement = (t) => t === "canvas" ? { getContext: () => ({ drawImage() {}, getImageData: () => ({ data: new Uint8ClampedArray(4) }) }) } : old(t);
fire(els["trace-zone"], "click", {});
fire(els["plot"], "click", { clientX: 300, clientY: 300 });
ok("trace zone: the click ends without an error and the tool is released", els["trace-msg-zone"].hidden === true);
