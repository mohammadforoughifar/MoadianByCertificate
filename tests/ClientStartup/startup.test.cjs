// No packages, browser, .NET runtime, API process, or database needed.
// node --test inventory/tests/ClientStartup/startup.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../src');
const webroot = path.join(root, 'Inventory.Client/wwwroot');
const shell = fs.readFileSync(path.join(webroot, 'js/app-shell.js'), 'utf8');
const index = fs.readFileSync(path.join(webroot, 'index.html'), 'utf8');

function setup(url, stored = {}) {
    const location = new URL(url);
    const values = new Map(Object.entries(stored));
    const window = { location };
    const context = {
        window, document: { baseURI: url }, URL,
        localStorage: { getItem: key => values.get(key), setItem: (key, value) => values.set(key, value) },
        navigator: {}, setTimeout: () => 1, clearTimeout: () => {}, console,
    };
    vm.runInNewContext(shell, context, { filename: 'app-shell.js' });
    return { window, context };
}

test('only the source client opens a browser in every launch profile', () => {
    for (const [project, expected] of [['Inventory.Api', false], ['Inventory.Client', true]]) {
        const settings = JSON.parse(fs.readFileSync(path.join(root, project, 'Properties/launchSettings.json'), 'utf8').replace(/^\uFEFF/, ''));
        for (const [name, profile] of Object.entries(settings.profiles))
            assert.equal(profile.launchBrowser, expected, `${project}: ${name}`);
    }
    const launch = JSON.parse(fs.readFileSync(path.join(root, '../Inventory.slnLaunch'), 'utf8'));
    assert.equal(launch[0].Projects.filter(p => p.Action === 'Start').length, 2, 'API must still be started');
});

test('page interop loads synchronously before Blazor, outside the HTML parser', () => {
    assert.match(index, /<script src="js\/app-shell\.js\?v=1"><\/script>/);
    assert.ok(index.indexOf('js/app-shell.js') < index.indexOf('_framework/blazor.webassembly.js'));
    assert.ok(!index.includes('window.ltxPrintWindows'), 'print document strings must not be embedded in index');
    assert.ok(!index.includes('window.DEFAULT_API_BASE'), 'startup API routing must be in the external file');
    for (const match of index.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)) {
        if (!/\bsrc=/.test(match[1])) new vm.Script(match[2]);
    }
});

test('local development ignores the previously saved production API', async () => {
    for (const [page, api] of [['http://localhost:5210/', 'http://localhost:5100'], ['https://localhost:7232/', 'https://localhost:7021']]) {
        const { window } = setup(page, { apiBaseUrl: 'http://192.168.30.104:8181' });
        assert.equal(window.inventoryApiBase(), api);
        const probes = [];
        window.apiBaseIsReachable = async url => { probes.push(url); return true; };
        assert.equal(await window.inventoryResolveApiBase(), api);
        assert.deepEqual(probes, [api], 'development must not probe a production database installation');
    }
});

test('published host and IIS subpath keep same-origin routing', () => {
    for (const page of ['https://erp.example.test/', 'https://erp.example.test/erp/', 'http://localhost:8181/']) {
        const { window } = setup(page);
        assert.equal(window.inventoryApiBase(), page.replace(/\/$/, ''));
        assert.equal(window.apiBaseIsSane('http://localhost:5100'), page.includes('localhost'));
    }
});

test('print interop still creates a separate document and escapes errors', () => {
    const { window } = setup('http://localhost:5210/');
    const writes = [];
    const popup = { document: { open() {}, write: html => writes.push(html), close() {} } };
    window.open = () => popup;
    const id = window.ltxPrintBegin();
    assert.ok(id);
    assert.match(writes[0], /<!doctype html>/);
    window.ltxPrintFail(id, '<script>alert(1)</script>');
    assert.match(writes[1], /&lt;script&gt;/);
    assert.ok(!writes[1].includes('<script>'));
    assert.equal(window.ltxPrintWindows[id], undefined);
});

test('development service worker replaces old published shell without deleting user caches', async () => {
    const handlers = new Map();
    const deleted = [];
    let skipped = false, claimed = false;
    const self = {
        addEventListener: (event, handler) => handlers.set(event, handler),
        skipWaiting: async () => { skipped = true; },
        clients: { claim: async () => { claimed = true; } },
        importScripts: script => assert.equal(script, './push-worker.js'),
    };
    vm.runInNewContext(fs.readFileSync(path.join(webroot, 'service-worker.js'), 'utf8'), {
        self,
        caches: {
            keys: async () => ['offline-cache-old', 'doc-archive-api-v1', 'push-data'],
            delete: async key => { deleted.push(key); },
        },
    });
    await handlers.get('install')({ waitUntil: promise => promise });
    // waitUntil owns the asynchronous work; capture its promise as a real browser would.
    let activation;
    handlers.get('activate')({ waitUntil: promise => { activation = promise; } });
    await activation;
    assert.equal(skipped, true);
    assert.equal(claimed, true);
    assert.deepEqual(deleted, ['offline-cache-old']);
    handlers.get('fetch')({ respondWith: () => assert.fail('development must use the network') });
});
