window.inventoryWorkProjectMindFlow = {
  attachDrag: (svgId, dotnet) => {
    const svg = document.getElementById(svgId);
    if (!svg) return;
    if (svg._wpMindFlowDragController) {
      svg._wpMindFlowDragController.dotnet = dotnet;
      return;
    }
    const controller = { dotnet };
    svg._wpMindFlowDragController = controller;
    svg.dataset.wpMindFlowDrag = '1';

    let active = null;
    const groups = () => [...svg.querySelectorAll('.wpmf-node')];
    const byId = id => groups().find(g => g.dataset.nodeId === id);
    const eventElement = e => {
      const path = typeof e.composedPath === 'function' ? e.composedPath() : [];
      return path.find(x => x instanceof Element && x.matches?.('.wpmf-node'))
        || e.target?.closest?.('.wpmf-node') || null;
    };
    const pointOf = e => {
      const matrix = svg.getScreenCTM();
      if (!matrix) return null;
      const p = svg.createSVGPoint(); p.x = e.clientX; p.y = e.clientY;
      return p.matrixTransform(matrix.inverse());
    };
    const refreshLinks = () => svg.querySelectorAll('.wpmf-link').forEach(path => {
      const a = byId(path.dataset.parentId), b = byId(path.dataset.nodeId);
      if (!a || !b) return;
      const ax = +a.dataset.x, ay = +a.dataset.y, bx = +b.dataset.x, by = +b.dataset.y;
      const dir = bx >= ax ? 1 : -1;
      const sx = ax + dir * (+a.dataset.width / 2), ex = bx - dir * (+b.dataset.width / 2), mid = (sx + ex) / 2;
      path.setAttribute('d', `M ${sx} ${ay} C ${mid} ${ay}, ${mid} ${by}, ${ex} ${by}`);
    });
    const clearDropTargets = () => groups().forEach(n => n.classList.remove('drop-target'));
    const dropTargetAt = e => {
      const el = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('.wpmf-node');
      const id = el?.dataset.nodeId;
      return el && id && active && !active.subtree.has(id) ? { el, id } : null;
    };

    svg.addEventListener('pointerdown', e => {
      const g = eventElement(e);
      if (!g || e.button !== 0 || svg.dataset.readonly?.toLowerCase() === 'true') return;
      if (e.target?.closest?.('.wpmf-node-action, a, button, input, textarea, select')) return;
      const start = pointOf(e);
      if (!start) return;

      const rootId = g.dataset.nodeId, subtree = new Set([rootId]);
      let again = true;
      while (again) {
        again = false;
        for (const n of groups()) {
          if (subtree.has(n.dataset.parentId) && !subtree.has(n.dataset.nodeId)) {
            subtree.add(n.dataset.nodeId); again = true;
          }
        }
      }
      active = {
        rootId, subtree, start, pointerId: e.pointerId, moved: false,
        originals: groups().filter(n => subtree.has(n.dataset.nodeId)).map(n => ({
          el: n, id: n.dataset.nodeId, x: +n.dataset.x, y: +n.dataset.y
        }))
      };
      try { svg.setPointerCapture(e.pointerId); } catch { /* window-level pointer events still complete the drag */ }
      e.preventDefault();
    }, true);

    svg.addEventListener('pointermove', e => {
      if (!active || active.pointerId !== e.pointerId) return;
      const p = pointOf(e);
      if (!p) return;
      const dx = p.x - active.start.x, dy = p.y - active.start.y;
      if (Math.abs(dx) + Math.abs(dy) > 4) active.moved = true;
      if (active.moved) {
        for (const n of active.originals) {
          const x = n.x + dx, y = n.y + dy;
          n.el.dataset.x = x; n.el.dataset.y = y;
          n.el.setAttribute('transform', `translate(${x},${y})`);
        }
        refreshLinks();
      }
      clearDropTargets();
      const drop = dropTargetAt(e);
      if (drop && active.moved) drop.el.classList.add('drop-target');
    }, true);

    const end = async e => {
      if (!active || active.pointerId !== e.pointerId) return;
      const a = active;
      const p = pointOf(e);
      const candidate = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('.wpmf-node');
      const candidateId = candidate?.dataset.nodeId;
      const targetId = candidate && candidateId && !a.subtree.has(candidateId) ? candidateId : null;
      active = null; clearDropTargets();
      try { svg.releasePointerCapture(e.pointerId); } catch { }

      if (!a.moved || !p) {
        try { await controller.dotnet.invokeMethodAsync('SelectNodeFromCanvas', a.rootId); }
        catch (err) { console.error('MindFlow node selection failed', err); }
        return;
      }

      const dx = p.x - a.start.x, dy = p.y - a.start.y;
      try {
        await controller.dotnet.invokeMethodAsync('CommitDrag', a.rootId,
          a.originals.map(n => ({ id: n.id, x: n.x + dx, y: n.y + dy })), targetId);
      } catch (err) {
        for (const n of a.originals) {
          n.el.dataset.x = n.x; n.el.dataset.y = n.y;
          n.el.setAttribute('transform', `translate(${n.x},${n.y})`);
        }
        refreshLinks();
        console.error('MindFlow drag commit failed', err);
      }
    };
    const cancel = e => {
      if (!active || active.pointerId !== e.pointerId) return;
      const a = active; active = null; clearDropTargets();
      for (const n of a.originals) {
        n.el.dataset.x = n.x; n.el.dataset.y = n.y;
        n.el.setAttribute('transform', `translate(${n.x},${n.y})`);
      }
      refreshLinks();
      try { svg.releasePointerCapture(e.pointerId); } catch { }
    };

    svg.addEventListener('pointerup', end, true);
    svg.addEventListener('pointercancel', cancel, true);
    svg.addEventListener('lostpointercapture', cancel, true);
  }
};
