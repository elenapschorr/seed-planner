const { load, fire, els } = require("./mock.js");
function run(label, data, area, mode) {
  const h = load(data);
  els["scale-mode"].value = mode; els["plot-area"].value = area;
  fire(els["scale-mode"], "change", {});
  const svg = els.plot;
  const out = [];
  const step = (n, f) => { try { f(); } catch (e) { out.push("FAIL " + n + ": " + e.message + " @ " + (e.stack.split("\n")[1] || "").trim().slice(0, 90)); } };
  step("cut-draw", () => fire(els["cut-draw"], "click", {}));
  step("click1", () => { fire(svg, "pointerdown", { clientX: 500, clientY: 200 }); fire(svg, "click", { clientX: 500, clientY: 200 }); });
  step("click2", () => { fire(svg, "pointerdown", { clientX: 500, clientY: 800 }); fire(svg, "click", { clientX: 500, clientY: 800 }); });
  console.log(label, out.length ? out.join(" | ") : "ok", "| cut-result hidden:", els["cut-result"].hidden);
}
const base = { boundary: [[100,100],[900,100],[900,900],[100,900]], lengths: [null,null,null,null], zones: [], knownArea: null, neighbors: [], cutLine: null, beds: [], placed: [] };
run("edges, no scale ", JSON.parse(JSON.stringify(base)), "", "edges");
run("area 300        ", JSON.parse(JSON.stringify(Object.assign({}, base, { knownArea: 300 }))), "300", "area");
const withZone = Object.assign({}, base, { knownArea: 300, zones: [{ name: "Garage", kind: "Garage", isShape: false, points: [[200,200],[300,200],[300,300],[200,300]], owner: 0, splitGroup: 0, splitPart: 0 }] });
run("with zone       ", JSON.parse(JSON.stringify(withZone)), "300", "area");
run("no boundary     ", Object.assign({}, base, { boundary: [], lengths: [] }), "", "edges");
