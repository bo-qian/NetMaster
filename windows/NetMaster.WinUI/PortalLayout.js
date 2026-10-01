(() => {
    if (!['http://10.10.9.9', 'http://10.10.9.9:8080'].includes(location.origin)
        || window.__netMasterPortalLayout) return;
    window.__netMasterPortalLayout = true;

    // The school uses fixed-width layouts, including documents inside frames.
    // Fit each document to its own viewport without replacing forms or handlers.
    let pending = false;
    const observer = new MutationObserver(schedule);
    const observation = { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style', 'width'] };
    function fit() {
        pending = false;
        const root = document.documentElement;
        if (!root || !document.body || window.innerWidth <= 0) return;
        observer.disconnect();
        // Always measure at the original scale so repeated resizes cannot
        // accumulate shrinking and a wider window restores the original size.
        root.style.zoom = '1';
        const viewport = root.clientWidth;
        const width = Math.max(root.scrollWidth, document.body.scrollWidth, viewport);
        root.style.zoom = width > viewport + 1 ? String((viewport - 1) / width) : '1';
        observer.observe(root, observation);
    }
    function schedule() {
        if (!pending) { pending = true; requestAnimationFrame(fit); }
    }
    window.addEventListener('resize', schedule);
    window.addEventListener('load', schedule, true);
    document.addEventListener('DOMContentLoaded', schedule);
    document.fonts?.ready.then(schedule);
    schedule();
})();
