// No npm packages required: node --test inventory/tests/OfficeGuides/print-guide.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ui = fs.readFileSync(path.join(__dirname, '../../src/Inventory.Client/wwwroot/js/ui.js'), 'utf8');
const printHelper = ui.slice(ui.indexOf('window.officeGuides ='));

function setup() {
    const listeners = new Map();
    const window = {
        addEventListener: (event, handler) => listeners.set(event, handler),
        removeEventListener: (event) => listeners.delete(event),
        print: () => {},
    };
    vm.runInNewContext(printHelper, { window, Array });
    const details = [{ open: true }, { open: false }, { open: false }];
    const root = { querySelectorAll: () => details.filter(item => !item.open) };
    return { window, listeners, details, root };
}

test('prints collapsed FAQ answers, then restores the exact reader state', () => {
    const { window, listeners, details, root } = setup();
    window.print = () => assert.ok(details.every(item => item.open));
    window.officeGuides.print(root);
    assert.ok(details.every(item => item.open));
    listeners.get('afterprint')();
    assert.deepEqual(details.map(item => item.open), [true, false, false]);
    assert.equal(listeners.size, 0);
});

test('restores FAQ state and removes listener if print fails', () => {
    const { window, listeners, details, root } = setup();
    window.print = () => { throw new Error('Print unavailable'); };
    assert.throws(() => window.officeGuides.print(root), /Print unavailable/);
    assert.deepEqual(details.map(item => item.open), [true, false, false]);
    assert.equal(listeners.size, 0);
});

test('does nothing when the component is no longer mounted', () => {
    const { window, listeners } = setup();
    window.print = () => assert.fail('Unexpected print');
    window.officeGuides.print(null);
    assert.equal(listeners.size, 0);
});

test('TOC only scrolls inside its own guide, including cached-tab copies', () => {
    const { window } = setup();
    let calls = 0;
    const section = { id: 'incoming-overview', scrollIntoView: options => {
        assert.equal(options.block, 'start');
        assert.equal(options.behavior, 'auto');
        calls++;
    } };
    window.matchMedia = () => ({ matches: true });
    const root = { querySelectorAll: () => [section] };
    window.officeGuides.scrollTo(root, 'missing');
    window.officeGuides.scrollTo(null, section.id);
    window.officeGuides.scrollTo(root, section.id);
    assert.equal(calls, 1);
});
