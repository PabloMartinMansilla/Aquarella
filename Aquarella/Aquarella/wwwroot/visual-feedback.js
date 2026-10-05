// Presentation only: no persistence, fetch, validation or interception of business actions.
(() => {
    if (window.aquarellaVisualFeedback) return;
    window.aquarellaVisualFeedback = true;
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const seen = new WeakMap();
    const months = new WeakMap();
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
                document.querySelector('.calendar')?.animate([{ opacity: .8, transform: 'translateY(4px)' }, { opacity: 1, transform: 'none' }], { duration: 160, easing: 'ease-out' });
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
        for (const record of records) {
            if (record.type === 'attributes' && record.attributeName === 'aria-busy' && record.target.getAttribute('aria-busy') === 'true')
                document.querySelectorAll('[data-feedback]').forEach(element => seen.delete(element));
        }
        if (frame === null || frame === undefined) frame = requestAnimationFrame(synchronize);
    });
    observer.observe(document.body, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ['aria-busy', 'aria-expanded'] });
    reduced.addEventListener('change', () => {
        if (reduced.matches) document.querySelectorAll('.calendar').forEach(element => element.getAnimations().forEach(animation => animation.cancel()));
    });
    synchronize();
})();
