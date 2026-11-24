const { load, fire } = require("./mock.js");
const data = { boundary: [[100,100],[900,100],[900,900],[100,900]], lengths: [null,null,null,null], zones: [], knownArea: 300, neighbors: [], cutLine: null, beds: [], placed: [] };
let h;
try { h = load(data); } catch (e) { console.log("LOAD ERROR", e.stack.split("\n").slice(0,4).join("\n")); process.exit(1); }
const { els } = h;
const svg = els.plot;
console.log("click handlers on svg:", (svg.listeners.click||[]).length);
function step(name, f) { try { f(); console.log("ok  ", name); } catch (e) { console.log("FAIL", name, "\n   ", e.stack.split("\n").slice(0,3).join("\n    ")); } }
step("press cut-draw", () => fire(els["cut-draw"], "click", {}));
console.log("cut-msg hidden:", els["cut-msg"].hidden, JSON.stringify(els["cut-msg"].textContent));
step("pointerdown svg", () => fire(svg, "pointerdown", { clientX: 500, clientY: 200 }));
step("click 1", () => fire(svg, "click", { clientX: 500, clientY: 200 }));
step("pointerdown svg", () => fire(svg, "pointerdown", { clientX: 500, clientY: 800 }));
step("click 2", () => fire(svg, "click", { clientX: 500, clientY: 800 }));
console.log("cut-result hidden:", els["cut-result"].hidden, "cut-clear hidden:", els["cut-clear"].hidden);
