const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../windows/NetMaster/PortalLayout.js'), 'utf8');

function setup(origin = 'http://10.10.9.9') {
    let viewport = 800, content = 1200;
    const queue = [], events = {};
    const root = { style: {}, get clientWidth() { return viewport; }, get scrollWidth() {
        assert.equal(root.style.zoom, '1', 'measurement must use the original scale');
        return Math.max(viewport, content);
    } };
    const body = { get scrollWidth() { return content; } };
    const document = { documentElement: root, body, addEventListener: (name, callback) => events[name] = callback };
    const window = { innerWidth: viewport, addEventListener: (name, callback) => events[name] = callback };
    let mutation;
    class Observer { constructor(callback) { mutation = callback; } disconnect() {} observe() {} }
    const context = { location: { origin }, window, document, MutationObserver: Observer, requestAnimationFrame: callback => queue.push(callback) };
    vm.runInNewContext(script, context);
    return { root, body, context, events, queue, flush() { while (queue.length) queue.shift()(); },
        resize(value) { viewport = window.innerWidth = value; events.resize(); },
        changeWidth(value) { content = value; mutation(); } };
}

test('fixed-width page fits, repeated narrow resize stays stable, widening restores size', () => {
    const page = setup(); page.flush();
    assert.ok(Number(page.root.style.zoom) * 1200 <= 800);
    page.resize(600); page.flush(); const scale = page.root.style.zoom;
    page.resize(600); page.flush(); assert.equal(page.root.style.zoom, scale);
    page.resize(1600); page.flush(); assert.equal(page.root.style.zoom, '1');
});
test('asynchronous school content and frame loads refit without changing form state', () => {
    const page = setup(); page.body.form = { password: 'fake', submit: () => 'original' }; page.flush();
    page.changeWidth(1800); page.events.load(); assert.equal(page.queue.length, 1); page.flush();
    assert.ok(Number(page.root.style.zoom) * 1800 <= 800);
    assert.equal(page.body.form.password, 'fake'); assert.equal(page.body.form.submit(), 'original');
});
test('only approved school origins are adapted; injection is idempotent', () => {
    const other = setup('https://example.com'); assert.equal(other.queue.length, 0); assert.deepEqual(other.root.style, {});
    const school = setup('http://10.10.9.9:8080'); vm.runInNewContext(script, school.context);
    assert.equal(school.queue.length, 1); school.flush(); assert.ok(Number(school.root.style.zoom) < 1);
});
