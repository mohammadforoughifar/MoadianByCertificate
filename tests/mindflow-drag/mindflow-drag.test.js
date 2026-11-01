// Smoke test for the mind-flow drag controller (runs under jsdom).
const fs = require('fs');
const { JSDOM } = require('jsdom');

const html = `<!doctype html><html><body>
<svg id="wpmf-canvas-7" class="wpmf-canvas" data-readonly="false" viewBox="0 0 1600 900">
  <path class="wpmf-link" data-parent-id="a" data-node-id="b" d="M 0 0 L 1 1" />
  <g class="wpmf-node" data-node-id="a" data-x="800" data-y="450" data-width="250" data-height="82" transform="translate(800,450)">
    <rect x="-125" y="-41" width="250" height="82"/></g>
  <g class="wpmf-node" data-node-id="b" data-parent-id="a" data-x="520" data-y="270" data-width="150" data-height="62" transform="translate(520,270)">
    <rect x="-75" y="-31" width="150" height="62"/></g>
  <g class="wpmf-node" data-node-id="c" data-parent-id="a" data-x="1080" data-y="270" data-width="150" data-height="62" transform="translate(1080,270)">
    <rect x="-75" y="-31" width="150" height="62"/></g>
</svg></body></html>`;

const dom = new JSDOM(html, { pretendToBeVisual: true, runScripts: 'outside-only' });
const { window } = dom;
global.window = window;
global.document = window.document;
global.Element = window.Element;
global.HTMLElement = window.HTMLElement;
global.SVGElement = window.SVGElement;
global.Element.prototype.matches = window.Element.prototype.matches || function () { return false; };

// --- jsdom stubs: geometry + pointer capture ---
window.SVGElement.prototype.getScreenCTM = function () {
  return { inverse: () => ({}) };
};
window.SVGElement.prototype.createSVGPoint = function () {
  const p = { x: 0, y: 0, matrixTransform: () => ({ x: p.x, y: p.y }) };
  return p;
};
window.Element.prototype.setPointerCapture = function () { };
window.Element.prototype.releasePointerCapture = function () { };
window.Element.prototype.closest = window.Element.prototype.closest || function () { return null; };
// elementFromPoint: return the node under (x,y) using a fake layout map
const centers = { a: [10, 10], b: [400, 300], c: [900, 300] };
window.document.elementFromPoint = (x, y) => {
  for (const [id, [cx, cy]] of Object.entries(centers)) {
    if (Math.abs(x - cx) <= 40 && Math.abs(y - cy) <= 40) {
      return window.document.querySelector(`[data-node-id="${id}"]`);
    }
  }
  return null;
};

// load the controller
const code = fs.readFileSync(new URL('../../src/TotallSoftwareForoughAriya.Client/wwwroot/js/inventory-workproject-mindflow.js', require('url').pathToFileURL(__filename)), 'utf8');
window.eval(code);

const api = window.inventoryWorkProjectMindFlow;
if (!api || typeof api.attachDrag !== 'function') throw new Error('global api missing');

const calls = [];
const dotnet = { invokeMethodAsync: (name, ...args) => { calls.push([name, args]); return Promise.resolve(); } };

const svg = window.document.getElementById('wpmf-canvas-7');
const ok = api.attachDrag('wpmf-canvas-7', dotnet);
if (!ok) throw new Error('attachDrag returned false');
if (api.attachDrag('wpmf-canvas-7', dotnet) !== true) throw new Error('re-attach must reuse controller');
// unknown id falls back to the only canvas on the page (defensive: id may lag behind render)
if (api.attachDrag('does-not-exist', dotnet) !== true) throw new Error('fallback to single canvas failed');
if (api.attachDrag('wpmf-canvas-7', null) !== false) throw new Error('null dotnet must return false');

// resolve by element as well
if (api.attachDrag(svg, dotnet) !== true) throw new Error('element form must work');

const ev = (type, opts) => {
  const e = new window.Event(type, { bubbles: true, cancelable: true });
  for (const k of Object.keys(opts)) {
    if (k === 'target') continue; // target comes from dispatchEvent
    Object.defineProperty(e, k, { value: opts[k], configurable: true });
  }
  return e;
};

// --- 1) drag node "a" by +50/+20 and drop on empty space
const nodeA = svg.querySelector('.wpmf-node[data-node-id="a"]');
nodeA.dispatchEvent(ev('pointerdown', { button: 0, pointerId: 1, clientX: 10, clientY: 10, target: nodeA }));
svg.dispatchEvent(ev('pointermove', { pointerId: 1, clientX: 160, clientY: 60 }));
svg.dispatchEvent(ev('pointerup', { pointerId: 1, clientX: 160, clientY: 60 }));

setTimeout(() => {
  // --- 1) subtree drag on empty space
  const drag = calls.find(c => c[0] === 'CommitDrag');
  if (!drag) throw new Error('CommitDrag was not invoked: ' + JSON.stringify(calls));
  const [rootId, points, target] = drag[1];
  if (rootId !== 'a') throw new Error('wrong root: ' + rootId);
  if (points.length !== 3) throw new Error('subtree should include a+b+c, got ' + points.length);
  const a = points.find(p => p.id === 'a');
  if (Math.abs(a.x - 950) > 0.01 || Math.abs(a.y - 500) > 0.01) throw new Error('bad position ' + JSON.stringify(a));
  if (target !== null) throw new Error('no drop target expected, got ' + target);
  const link = svg.querySelector('.wpmf-link');
  if (!link.getAttribute('d').includes('825 500')) throw new Error('links were not refreshed: ' + link.getAttribute('d'));

  // --- 2) drag leaf b (now shifted +150/+50) onto a -> reparent target
  const nodeB = svg.querySelector('.wpmf-node[data-node-id="b"]');
  nodeB.dispatchEvent(ev('pointerdown', { button: 0, pointerId: 9, clientX: 400, clientY: 300, target: nodeB }));
  svg.dispatchEvent(ev('pointermove', { pointerId: 9, clientX: 15, clientY: 15 }));
  svg.dispatchEvent(ev('pointerup', { pointerId: 9, clientX: 15, clientY: 15 }));

  const drop = calls.filter(c => c[0] === 'CommitDrag').pop();
  if (drop[1][0] !== 'b') throw new Error('expected dragged root b, got ' + drop[1][0]);
  if (drop[1][2] !== 'a') throw new Error('expected drop target a, got ' + drop[1][2]);
  if (nodeB.classList.contains('drop-target')) throw new Error('drop-target class left behind');

  // --- 3) click without movement selects the node
  calls.length = 0;
  nodeA.dispatchEvent(ev('pointerdown', { button: 0, pointerId: 2, clientX: 10, clientY: 10, target: nodeA }));
  svg.dispatchEvent(ev('pointerup', { pointerId: 2, clientX: 10, clientY: 10 }));
  setTimeout(() => {
    const sel = calls.find(c => c[0] === 'SelectNodeFromCanvas');
    if (!sel || sel[1][0] !== 'a') throw new Error('click should select node: ' + JSON.stringify(calls));

    // --- 4) readonly canvas ignores pointerdown
    svg.dataset.readonly = 'true';
    calls.length = 0;
    nodeA.dispatchEvent(ev('pointerdown', { button: 0, pointerId: 4, clientX: 10, clientY: 10, target: nodeA }));
    svg.dispatchEvent(ev('pointerup', { pointerId: 4, clientX: 10, clientY: 10 }));
    setTimeout(() => {
      if (calls.length) throw new Error('readonly canvas must not drag: ' + JSON.stringify(calls));

      // --- 5) pointercancel reverts live positions
      delete svg.dataset.readonly;
      const before = nodeA.getAttribute('transform');
      nodeA.dispatchEvent(ev('pointerdown', { button: 0, pointerId: 5, clientX: 10, clientY: 10, target: nodeA }));
      svg.dispatchEvent(ev('pointermove', { pointerId: 5, clientX: 210, clientY: 210 }));
      if (nodeA.getAttribute('transform') === before) throw new Error('live move did not update transform');
      svg.dispatchEvent(ev('pointercancel', { pointerId: 5 }));
      if (nodeA.getAttribute('transform') !== before) throw new Error('cancel did not revert');

      // --- 6) detach clears the controller and a later attach works again
      if (api.detachDrag('wpmf-canvas-7') !== true) throw new Error('detachDrag must return true');
      if (svg._wpMindFlowDragController !== null) throw new Error('controller must be cleared on detach');
      if (api.attachDrag('wpmf-canvas-7', dotnet) !== true) throw new Error('re-attach after detach must work');

      console.log('ALL MIND-FLOW DRAG TESTS PASSED');
    }, 20);
  }, 20);
}, 30);
