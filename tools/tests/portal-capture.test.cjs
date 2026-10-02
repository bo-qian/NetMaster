const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '../../windows/NetMaster/PortalCapture.js'), 'utf8');
const endpoint = 'http://10.10.9.9/eportal/InterFace.do?method=login';
const accountScript = fs.readFileSync(path.join(__dirname, '../../windows/NetMaster/PortalAccount.js'), 'utf8');
function setup(origin = 'http://10.10.9.9') {
    const messages = [];
    class XHR {
        constructor() { this.listeners = []; this.responseType = ''; }
        open(method, url) { this.originalMethod = method; this.originalUrl = url; }
        send(body) { this.originalBody = body; return 'original-result'; }
        addEventListener(name, callback) { if (name === 'loadend') this.listeners.push(callback); }
        finish(body) { this.responseText = body; const listeners = this.listeners.splice(0); listeners.forEach(callback => callback()); }
    }
    const originalFetch = Promise.resolve(new Response('{"result":"success"}'));
    const window = { chrome: { webview: { postMessage: message => messages.push(message) } }, fetch: () => originalFetch };
    vm.runInNewContext(script, { window, XMLHttpRequest: XHR, URL, URLSearchParams, Request, Promise, location: { origin, href: origin + '/eportal/index.jsp' } });
    return { messages, XHR, window, originalFetch };
}

test('XHR captures at send time without changing the original payload or return value', () => {
    const { messages, XHR } = setup(); const xhr = new XHR(); xhr.open('POST', endpoint);
    assert.equal(xhr.send('userId=fixture&password=fake'), 'original-result');
    assert.equal(messages.length, 1); assert.equal(messages[0].kind, 'request');
    assert.equal(xhr.originalBody, 'userId=fixture&password=fake');
    xhr.finish('{"result":"success"}');
    assert.equal(messages[1].id, messages[0].id); assert.equal(messages[1].response, '{"result":"success"}');
});
test('empty response still leaves a request captured for explicit testing', () => {
    const { messages, XHR } = setup(); const xhr = new XHR(); xhr.open('POST', endpoint);
    xhr.send('userId=fixture&password=fake'); xhr.finish('');
    assert.equal(messages.length, 2); assert.equal(messages[1].response, '');
});
test('logout and other origins are excluded', () => {
    const { messages, XHR } = setup();
    for (const url of [endpoint.replace('login', 'logout'), endpoint.replace('10.10.9.9', 'example.com')]) {
        const xhr = new XHR(); xhr.open('POST', url); xhr.send('fixture'); xhr.finish('{}');
    }
    assert.equal(messages.length, 0);
    const other = setup('http://example.com'); const xhr = new other.XHR(); xhr.open('POST', endpoint); xhr.send('fixture');
    assert.equal(other.messages.length, 0);
});
test('fetch preserves the original promise and captures URLSearchParams', async () => {
    const { messages, window, originalFetch } = setup();
    assert.equal(window.fetch(endpoint, { method: 'POST', body: new URLSearchParams({ userId: 'fixture', password: 'fake' }) }), originalFetch);
    await new Promise(resolve => setTimeout(resolve, 30));
    assert.equal(messages[0].payload, 'userId=fixture&password=fake');
    assert.equal(messages[1].id, messages[0].id);
});
test('fetch Request bodies are cloned without consuming the original', async () => {
    const { messages, window } = setup();
    const request = new Request(endpoint, { method: 'POST', body: 'userId=fixture&password=fake' });
    await window.fetch(request); await new Promise(resolve => setTimeout(resolve, 30));
    assert.equal(request.bodyUsed, false); assert.equal(messages[0].kind, 'request');
    assert.equal(messages[1].response, '{"result":"success"}');
});

const readAccount = (body, origin = 'http://10.10.9.9') => vm.runInNewContext(accountScript, { location: { origin }, document: { body: { innerText: body } } });
test('reads current displayed account without userIndex or school JavaScript globals', () => {
    assert.equal(readAccount('通知 20261001 您已成功连接校园网！ 您当前登录的用户名为： 12345678 下线 Logout'), '12345678');
    assert.equal(readAccount('当前登录的用户名：fixture.user@school 下线'), 'fixture.user@school');
    assert.equal(readAccount('您当前登录的用户名为：12345678 下线', 'http://10.10.9.9:8080'), '12345678');
});
test('retains Python numeric account extraction on the school online page', () => {
    assert.equal(readAccount('12345678，早上好 您已成功连接校园网！'), '12345678');
});
test('does not extract account from other origins or a login form', () => {
    assert.equal(readAccount('成功连接 12345678', 'http://example.com'), null);
    assert.equal(readAccount('成功连接 12345678', 'http://10.10.9.9:8081'), null);
    assert.equal(readAccount('通知 20261001 请登录'), null);
});
