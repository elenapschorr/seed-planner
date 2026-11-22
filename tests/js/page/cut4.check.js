const { load, fire } = require("./mock.js");
const data = { boundary: [], lengths: [], zones: [], knownArea: 0, neighbors: [], cutLine: null, beds: [], placed: [] };
const h = load(data), { els } = h, svg = els.plot;
fire(els["cut-draw"], "click", {});
fire(svg, "click", { clientX: 500, clientY: 200 }); fire(svg, "pointermove", { clientX: 500, clientY: 800 }); fire(svg, "click", { clientX: 500, clientY: 800 });
console.log("no outline, overlay:", els.plot.children.filter(c => c.tag === "g").map(g => g.children.map(c => c.tag).join(",")));
