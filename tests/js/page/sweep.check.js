const { load, fire } = require("./mock.js");
const zone = (name, pts, owner = 0) => ({ name, kind: "Garden", isShape: false, points: pts, owner });
const data = { boundary: [[100,100],[900,100],[900,900],[100,900]], lengths: [null,null,null,null], zones: [zone("A", [[200,200],[400,200],[400,400],[200,400]]), zone("B", [[500,500],[700,500],[700,700],[500,700]], 1)], knownArea: 300, neighbors: ["N"], cutLine: { a: [500, 50], b: [500, 950], owner1: 0, owner2: 1 }, beds: [{ id: 1, name: "b1", shape: "Rectangle", widthCm: 100, lengthCm: 200, hasCover: false }], placed: [{ bedId: 1, x: 300, y: 300, angle: 0 }] };
let errors = 0, fired = 0;
for (const mode of ["setup", "edit"]) {
  global.MODE = mode;
  const h = load(data), { els } = h;
  for (const id of Object.keys(els)) {
    for (const type of ["click", "change", "input", "pointerdown"]) {
      for (const f of (els[id].listeners[type] || [])) {
        try { fired++; f({ preventDefault() {}, stopPropagation() {}, target: els[id], clientX: 300, clientY: 300, pointerId: 1, key: "" }); }
        catch (e) { errors++; console.log(mode, id, type, "->", e.message.slice(0, 100)); }
      }
    }
  }
  for (const k of ["Delete", "Escape"]) (h.doc.l.keydown || []).forEach(f => { try { f({ key: k, preventDefault() {}, target: {} }); } catch (e) { errors++; console.log(mode, "keydown", k, e.message.slice(0, 100)); } });
}
console.log("handlers fired", fired, "errors", errors);
