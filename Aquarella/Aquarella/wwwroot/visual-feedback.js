// Presentation only: no persistence, fetch, validation or interception of business actions.
(() => {
    if (window.aquarellaVisualFeedback) return;
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const running = new Set();
    function animate(element, frames, speed = 'normal') {
        if (reduced.matches || !element) return;
        element.getAnimations().forEach(animation => animation.cancel());
        const style = getComputedStyle(document.documentElement);
        const duration = parseFloat(style.getPropertyValue(`--motion-${speed}`)) || 200;
        const animation = element.animate(frames, { duration, easing: style.getPropertyValue('--ease-soft').trim() || 'ease-out' });
        running.add(animation);
        animation.finished.then(() => running.delete(animation), () => running.delete(animation));
        return animation;
    }
    function exitSurface(element, rect) {
        if (reduced.matches || !rect?.width || !rect?.height) return;
        if (rect.top + rect.height <= 0 || rect.top >= window.innerHeight || rect.left >= window.innerWidth) return;
        if (element.querySelectorAll('*').length > 80) return;
        // A brief inert snapshot leaves without delaying the actual action or data update.
        const copy = element.cloneNode(true);
        copy.classList.add('motion-exit'); copy.inert = true; copy.setAttribute('aria-hidden', 'true');
        for (const node of [copy, ...copy.querySelectorAll('*')]) {
            node.removeAttribute('id'); node.removeAttribute('aria-live');
            node.removeAttribute('autofocus'); node.removeAttribute('name');
        }
        Object.assign(copy.style, { position: 'fixed', left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px`, maxHeight: 'none', boxSizing: 'border-box', zIndex: '1000', visibility: 'visible', overflow: 'hidden' });
        document.body.append(copy);
        const animation = animate(copy, [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(4px)' }], 'fast');
        if (animation) animation.finished.then(() => copy.remove(), () => copy.remove()); else copy.remove();
    }
    window.aquarellaVisualFeedback = { animate, exitSurface };
    const seen = new WeakMap();
    const months = new WeakMap();
    const surfaceRects = new Map();
    let toast, timer, frame;
    let drawer, drawerFocus;
    const inertSiblings = new Map();
    function synchronizeDrawer() {
        const open = document.querySelector('.drawer-layer.is-open');
        if (open === drawer) return;
        for (const [element, wasInert] of inertSiblings) element.inert = wasInert;
        inertSiblings.clear();
        if (!open && drawerFocus?.isConnected && (!document.activeElement || document.activeElement === document.body || drawer?.contains(document.activeElement)))
            drawerFocus.focus({ preventScroll: true });
        drawer = open;
        if (!open) { drawerFocus = null; return; }
        drawerFocus = document.getElementById('tour-menu');
        for (const element of open.parentElement.children) {
            if (element === open) continue;
            inertSiblings.set(element, element.inert);
            element.inert = true;
        }
    }
    document.addEventListener('keydown', event => {
        if (event.key !== 'Tab' || !drawer) return;
        const controls = [...drawer.querySelectorAll('a[href], button:not(:disabled), input:not(:disabled), [tabindex="0"]')]
            .filter(element => element.tabIndex >= 0 && element.getClientRects().length);
        const first = controls[0], last = controls.at(-1);
        if (!first) return;
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    });
    function synchronize() {
        frame = null;
        synchronizeDrawer();
        for (const [element, oldRect] of surfaceRects) {
            if (!element.isConnected) {
                const samePage = element.matches('.notice-group') ? document.querySelector('.notices-page')
                    : element.matches('.product-form') ? document.querySelector('.stock-page') : document.querySelector('.business-header');
                if (samePage) exitSurface(element, { ...oldRect, top: oldRect.top + oldRect.scrollY - window.scrollY });
                surfaceRects.delete(element);
            }
        }
        for (const element of document.querySelectorAll('.page-content .notice-group, .search-results, .stock-page .product-form')) {
            if (element.getAnimations().some(animation => running.has(animation))) continue;
            const rect = element.getBoundingClientRect(), old = surfaceRects.get(element);
            const shift = old ? old.top + old.scrollY - window.scrollY - rect.top : 0;
            if (element.matches('.notice-group') && Math.abs(shift) > 1)
                animate(element, [{ transform: `translateY(${shift}px)` }, { transform: 'none' }]);
            surfaceRects.set(element, { top: rect.top, left: rect.left, width: rect.width, height: rect.height, scrollY: window.scrollY });
        }
        for (const link of document.querySelectorAll('.menu-panel nav a[href]')) {
            const current = new URL(link.href, location.href).pathname === location.pathname;
            if (current) link.setAttribute('aria-current', 'page'); else link.removeAttribute('aria-current');
        }
        const notifications = new Map();
        for (const element of document.querySelectorAll('[data-feedback], .account-status')) {
            const text = element.textContent.trim();
            if (seen.get(element) === text) continue;
            seen.set(element, text);
            if (!text) continue;
            const error = /no se|no pud|revis[áa]|ingres[áa]|debe|incorrect|inv[áa]lid|no es v[áa]lid|no est[áa] disponible/i.test(text);
            const success = !error && /guardad[oa]|actualizad[oa]|eliminad[oa]|correctamente/i.test(text);
            if (!error && !success) continue;
            element.dataset.feedbackKind = error ? 'error' : 'success';
            notifications.set(text, error ? 'error' : 'success');
        }
        for (const [text, kind] of notifications) show(text, kind);
        for (const heading of document.querySelectorAll('.month-navigation h2')) {
            const month = heading.textContent;
            if (months.has(heading) && months.get(heading) !== month && !reduced.matches)
                animate(document.querySelector('.calendar'), [{ opacity: .8, transform: 'translateY(4px)' }, { opacity: 1, transform: 'none' }]);
            months.set(heading, month);
        }
    }
    function show(text, kind) {
        if (!toast) {
            toast = document.createElement('div'); toast.className = 'feedback-toast';
            // The original inline live region announces the message; do not announce twice.
            toast.setAttribute('aria-hidden', 'true');
            const symbol = document.createElement('span'); symbol.className = 'feedback-symbol';
            toast.append(symbol, document.createElement('span')); document.body.append(toast);
        }
        toast.firstChild.textContent = kind === 'error' ? '!' : '✓';
        toast.lastChild.textContent = text; toast.dataset.kind = kind;
        toast.classList.add('is-visible'); clearTimeout(timer);
        timer = setTimeout(() => toast.classList.remove('is-visible'), 2800);
    }
    const observer = new MutationObserver(records => {
        const changed = new Set();
        const addedRows = new Set();
        for (const record of records) {
            if (record.type === 'characterData') {
                const value = record.target.parentElement?.closest('.product-info strong, .calculated, .kpi strong, .profit-value, .group-count');
                if (value) changed.add(value);
            }
            if (record.type === 'childList') {
                const rows = [...record.addedNodes].filter(node => node.nodeType === 1 && node.matches('.product-row'));
                for (const row of rows) addedRows.add(row);
            }
            if (record.type === 'attributes' && record.attributeName === 'aria-busy' && record.target.getAttribute('aria-busy') === 'true')
                document.querySelectorAll('[data-feedback]').forEach(element => seen.delete(element));
        }
        if (addedRows.size === 1) {
            const row = [...addedRows][0];
            if (row.parentElement?.querySelectorAll('.product-row').length > 1)
                animate(row, [{ opacity: .8, transform: 'translateY(4px)' }, { opacity: 1, transform: 'none' }]);
        }
        if (changed.size <= 12)
            for (const value of changed) animate(value, [{ opacity: .7 }, { opacity: 1 }], 'fast');
        if (frame === null || frame === undefined) frame = requestAnimationFrame(synchronize);
    });
    observer.observe(document.body, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ['aria-busy', 'aria-expanded'] });
    reduced.addEventListener('change', () => {
        if (reduced.matches) { for (const animation of running) animation.cancel(); running.clear(); }
    });
    synchronize();
})();
