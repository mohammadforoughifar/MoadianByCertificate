/*
 * «نگار ذهنی» — کشیدن گره‌ها با ماوس/لمس.
 *
 * این فایل به‌صورت «اسکریپت کلاسیک» در index.html هم بارگذاری می‌شود و هم می‌تواند
 * با import پویا (از طریق فایل پوششی inventory-workproject-mindflow.module.js)
 * لود شود؛ پس هر دو مسیر کار می‌کنند و نبودِ تگ <script> در یک index.html قدیمی
 * (کش مرورگر/سرویس‌ورکر) دیگر باعث خطای
 * «Could not find 'inventoryWorkProjectMindFlow.attachDrag'» نمی‌شود.
 */
(function (global) {
  'use strict';

  if (global.inventoryWorkProjectMindFlow &&
      typeof global.inventoryWorkProjectMindFlow.attachDrag === 'function') {
    return; // قبلاً بارگذاری شده — دوباره‌کاری نکن
  }

  function resolveSvg(svgId) {
    if (!svgId) return null;
    if (typeof svgId === 'object' && svgId.nodeType === 1) return svgId;
    var byId = document.getElementById(svgId);
    if (byId) return byId;
    // id ممکن است با # فرستاده شود
    var raw = String(svgId).replace(/^#/, '');
    byId = document.getElementById(raw);
    if (byId) return byId;
    // آخرین تلاش: اگر Blazor هنوز id دقیق را نگذاشته باشد، تنها svg نقشه را بگیر
    var all = document.querySelectorAll('svg.wpmf-canvas');
    return all.length === 1 ? all[0] : null;
  }

  /**
   * اتصال کنترلر کشیدن به svg نقشه.
   * @returns {boolean} true اگر کنترلر متصل شد.
   */
  function attachDrag(svgId, dotnet) {
    var svg = resolveSvg(svgId);
    if (!svg) return false;
    if (typeof dotnet !== 'object' || dotnet === null) return false;

    if (svg._wpMindFlowDragController) {
      // همان عنصر، نشست/مرجع جدید: فقط مرجع دات‌نت را تازه کن
      svg._wpMindFlowDragController.dotnet = dotnet;
      return true;
    }

    var controller = { dotnet: dotnet };
    svg._wpMindFlowDragController = controller;
    svg.dataset.wpMindFlowDrag = '1';

    var active = null;
    var groups = function () { return Array.prototype.slice.call(svg.querySelectorAll('.wpmf-node')); };
    var byId = function (id) {
      var list = groups();
      for (var i = 0; i < list.length; i++) if (list[i].dataset.nodeId === id) return list[i];
      return null;
    };
    var eventElement = function (e) {
      var path = typeof e.composedPath === 'function' ? e.composedPath() : [];
      for (var i = 0; i < path.length; i++) {
        var x = path[i];
        if (x && x.nodeType === 1 && typeof x.matches === 'function' && x.matches('.wpmf-node')) return x;
      }
      var t = e.target;
      return (t && typeof t.closest === 'function') ? t.closest('.wpmf-node') : null;
    };
    var pointOf = function (e) {
      var matrix = null;
      try { matrix = svg.getScreenCTM(); } catch (err) { matrix = null; }
      if (!matrix) return null;
      var p = svg.createSVGPoint();
      p.x = e.clientX; p.y = e.clientY;
      return p.matrixTransform(matrix.inverse());
    };
    var refreshLinks = function () {
      Array.prototype.forEach.call(svg.querySelectorAll('.wpmf-link'), function (path) {
        var a = byId(path.dataset.parentId), b = byId(path.dataset.nodeId);
        if (!a || !b) return;
        var ax = +a.dataset.x, ay = +a.dataset.y, bx = +b.dataset.x, by = +b.dataset.y;
        var dir = bx >= ax ? 1 : -1;
        var sx = ax + dir * (+a.dataset.width / 2), ex = bx - dir * (+b.dataset.width / 2), mid = (sx + ex) / 2;
        path.setAttribute('d', 'M ' + sx + ' ' + ay + ' C ' + mid + ' ' + ay + ', ' + mid + ' ' + by + ', ' + ex + ' ' + by);
      });
    };
    var clearDropTargets = function () {
      groups().forEach(function (n) { n.classList.remove('drop-target'); });
    };
    var nodeAtPoint = function (e) {
      try {
        var el = document.elementFromPoint(e.clientX, e.clientY);
        return el && typeof el.closest === 'function' ? el.closest('.wpmf-node') : null;
      } catch (err) { return null; }
    };
    var dropTargetAt = function (e) {
      var el = nodeAtPoint(e);
      var id = el && el.dataset.nodeId;
      return el && id && active && !active.subtree.has(id) ? { el: el, id: id } : null;
    };
    var isReadOnly = function () {
      return String(svg.dataset.readonly || '').toLowerCase() === 'true';
    };
    var isInteractive = function (target) {
      return !!(target && typeof target.closest === 'function' &&
        target.closest('.wpmf-node-action, a, button, input, textarea, select'));
    };

    /* ---------- pointerdown: شروع کشیدن ---------- */
    svg.addEventListener('pointerdown', function (e) {
      var g = eventElement(e);
      if (!g || e.button !== 0 || isReadOnly() || isInteractive(e.target)) return;
      var start = pointOf(e);
      if (!start) return;

      var rootId = g.dataset.nodeId, subtree = new Set([rootId]);
      var again = true;
      while (again) {
        again = false;
        groups().forEach(function (n) {
          if (subtree.has(n.dataset.parentId) && !subtree.has(n.dataset.nodeId)) {
            subtree.add(n.dataset.nodeId); again = true;
          }
        });
      }
      active = {
        rootId: rootId,
        subtree: subtree,
        start: start,
        pointerId: e.pointerId,
        moved: false,
        originals: groups()
          .filter(function (n) { return subtree.has(n.dataset.nodeId); })
          .map(function (n) { return { el: n, id: n.dataset.nodeId, x: +n.dataset.x, y: +n.dataset.y }; })
      };
      try { svg.setPointerCapture(e.pointerId); } catch (err) { /* capture اختیاری است */ }
      try { e.preventDefault(); } catch (err) { /* noop */ }
    }, true);

    /* ---------- pointermove: جابه‌جایی زنده ---------- */
    var onMove = function (e) {
      if (!active || active.pointerId !== e.pointerId) return;
      var p = pointOf(e);
      if (!p) return;
      var dx = p.x - active.start.x, dy = p.y - active.start.y;
      if (Math.abs(dx) + Math.abs(dy) > 4) active.moved = true;
      if (!active.moved) return;
      try { e.preventDefault(); } catch (err) { /* noop */ }
      for (var i = 0; i < active.originals.length; i++) {
        var n = active.originals[i];
        var x = n.x + dx, y = n.y + dy;
        n.el.dataset.x = x; n.el.dataset.y = y;
        n.el.setAttribute('transform', 'translate(' + x + ',' + y + ')');
      }
      refreshLinks();
      clearDropTargets();
      var drop = dropTargetAt(e);
      if (drop) drop.el.classList.add('drop-target');
    };

    /* ---------- پایان کشیدن (چه داخل svg، چه بیرون آن) ---------- */
    var finish = function (e) {
      if (!active || (e && e.pointerId !== undefined && active.pointerId !== e.pointerId)) return;
      var a = active;
      if (!controller.dotnet) { abort(e); return; } // کامپوننت Dispose شده است
      var p = pointOf(e);
      var candidate = nodeAtPoint(e);
      var candidateId = candidate && candidate.dataset.nodeId;
      var targetId = candidateId && !a.subtree.has(candidateId) ? candidateId : null;
      active = null;
      clearDropTargets();
      try { if (e && e.pointerId !== undefined) svg.releasePointerCapture(e.pointerId); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointermove', onMove, true); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointerup', finish, true); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointercancel', abort, true); } catch (err) { /* noop */ }

      if (!a.moved || !p) {
        try {
          if (controller.dotnet && controller.dotnet.invokeMethodAsync) {
            controller.dotnet.invokeMethodAsync('SelectNodeFromCanvas', a.rootId)
              .catch(function () { /* انتخاب فقط برای UI است */ });
          }
        } catch (err) { /* noop */ }
        return;
      }

      var dx = p.x - a.start.x, dy = p.y - a.start.y;
      try {
        var points = a.originals.map(function (n) { return { id: n.id, x: n.x + dx, y: n.y + dy }; });
        if (!controller.dotnet || !controller.dotnet.invokeMethodAsync) throw new Error('dotnet reference lost');
        controller.dotnet.invokeMethodAsync('CommitDrag', a.rootId, points, targetId)
          .catch(function (err) {
            revert(a.originals);
            console.error('MindFlow drag commit failed', err);
          });
      } catch (err) {
        revert(a.originals);
        console.error('MindFlow drag commit failed', err);
      }
    };

    var revert = function (originals) {
      for (var i = 0; i < originals.length; i++) {
        var n = originals[i];
        n.el.dataset.x = n.x; n.el.dataset.y = n.y;
        n.el.setAttribute('transform', 'translate(' + n.x + ',' + n.y + ')');
      }
      refreshLinks();
    };

    var abort = function (e) {
      if (!active || (e && e.pointerId !== undefined && active.pointerId !== e.pointerId)) return;
      var a = active;
      active = null;
      clearDropTargets();
      revert(a.originals);
      try { if (e && e.pointerId !== undefined) svg.releasePointerCapture(e.pointerId); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointermove', onMove, true); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointerup', finish, true); } catch (err) { /* noop */ }
      try { global.removeEventListener('pointercancel', abort, true); } catch (err) { /* noop */ }
    };

    svg.addEventListener('pointermove', onMove, true);
    svg.addEventListener('pointerup', finish, true);
    svg.addEventListener('pointercancel', abort, true);
    svg.addEventListener('lostpointercapture', abort, true);

    /* پشتیبان: اگر اشاره‌گر از داخل svg بیرون رفت یا capture از دست رفت،
       کشیدن با رویدادهای سطح window هم کامل می‌شود. */
    svg.addEventListener('pointerdown', function () {
      try { global.addEventListener('pointermove', onMove, true); } catch (err) { /* noop */ }
      try { global.addEventListener('pointerup', finish, true); } catch (err) { /* noop */ }
      try { global.addEventListener('pointercancel', abort, true); } catch (err) { /* noop */ }
    }, true);

    // جلوگیری از drag & drop بومی مرورگر روی نقشه
    svg.addEventListener('dragstart', function (e) { e.preventDefault(); });

    return true;
  }

  /**
   * آزادسازی اتصال (هنگام Dispose کامپوننت). کنترلر را غیرفعال می‌کند تا
   * callbackهای JS به کامپوننت Dispose‌شده فرستاده نشوند.
   */
  function detachDrag(svgId) {
    var list = svgId
      ? [resolveSvg(svgId)]
      : Array.prototype.slice.call(document.querySelectorAll('svg.wpmf-canvas'));
    list.forEach(function (svg) {
      if (!svg || !svg._wpMindFlowDragController) return;
      svg._wpMindFlowDragController.dotnet = null;
      svg._wpMindFlowDragController = null;
      delete svg.dataset.wpMindFlowDrag;
    });
    return true;
  }

  var api = { attachDrag: attachDrag, init: attachDrag, detachDrag: detachDrag, version: 2 };
  global.inventoryWorkProjectMindFlow = api;
})(typeof globalThis !== 'undefined' ? globalThis : window);
