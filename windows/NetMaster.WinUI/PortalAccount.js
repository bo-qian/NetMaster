(() => {
    if (!['http://10.10.9.9', 'http://10.10.9.9:8080'].includes(location.origin)) return null;
    const body = document.body?.innerText || '';
    if (!/成功连接|当前登录的用户名|注销|下线/.test(body)) return null;
    // Prefer the explicit current-account label. The fallback matches Python's
    // numeric-account extraction, restricted to the school's online page.
    const labelled = body.match(/(?:您当前登录的用户名为|当前登录的用户名|当前登录账号)\s*[:：]\s*([A-Za-z0-9_.@-]{1,128})/);
    if (labelled) return labelled[1];
    return body.match(/\d{6,}/)?.[0] || null;
})();
