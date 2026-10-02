(() => {
    const trustedOrigin = origin => ['http://10.10.9.9', 'http://10.10.9.9:8080'].includes(origin);
    if (!trustedOrigin(location.origin) || !window.chrome?.webview) return;
    const session = '__NETMASTER_SESSION__';
    const post = message => { try { window.chrome.webview.postMessage({ session, ...message }); } catch {} };
    const loginUrl = (url, method) => {
        try {
            const target = new URL(url, location.href);
            return String(method).toUpperCase() === 'POST' && trustedOrigin(target.origin) &&
                target.pathname.toLowerCase() === '/eportal/interface.do' && target.searchParams.get('method') === 'login';
        } catch { return false; }
    };
    const capture = (url, method, body) => {
        if (!loginUrl(url, method)) return null;
        const payload = typeof body === 'string' ? body : body instanceof URLSearchParams ? body.toString() : null;
        if (!payload || payload.length > 65536) return null;
        const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
        post({ kind: 'request', id, url: new URL(url, location.href).href, method: 'POST', payload });
        return id;
    };
    const respond = (id, response) => { if (id) post({ kind: 'response', id, response: typeof response === 'string' && response.length <= 131072 ? response : '' }); };
    const requests = new WeakMap();
    const open = XMLHttpRequest.prototype.open;
    const send = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function(method, url, ...args) {
        requests.set(this, { method, url });
        return open.call(this, method, url, ...args);
    };
    XMLHttpRequest.prototype.send = function(body) {
        const request = requests.get(this);
        const id = request ? capture(request.url, request.method, body) : null;
        if (id) this.addEventListener('loadend', () => {
            try { respond(id, this.responseType === 'json' ? JSON.stringify(this.response) : this.responseText); }
            catch { respond(id, ''); }
        }, { once: true });
        try { return send.call(this, body); } catch (error) { respond(id, ''); throw error; }
    };
    const fetch = window.fetch;
    window.fetch = function(input, init) {
        const url = input instanceof Request ? input.url : input;
        const method = init?.method || (input instanceof Request ? input.method : 'GET');
        // Clone Request bodies without consuming the school's original request.
        const captured = input instanceof Request && !init?.body && loginUrl(url, method)
            ? input.clone().text().then(body => capture(url, method, body), () => null)
            : Promise.resolve(capture(url, method, init?.body));
        let pending;
        try { pending = fetch.call(this, input, init); } catch (error) { captured.then(id => respond(id, '')); throw error; }
        pending.then(response => {
            const copy = response.clone();
            captured.then(id => { if (id) copy.text().then(body => respond(id, body), () => respond(id, '')); });
        }, () => captured.then(id => respond(id, '')));
        return pending;
    };
})();
