let cleanup, returnFocus;
export function stop(restoreFocus = true) {
    cleanup?.(); cleanup = null;
    if (restoreFocus && returnFocus?.isConnected && (document.activeElement === document.body || document.getElementById('agenda-popover')?.contains(document.activeElement)))
        returnFocus.focus({ preventScroll: true });
    returnFocus = null;
}
export function open(anchorId) {
    stop(false);
    const anchor = document.getElementById(anchorId);
    const panel = document.getElementById('agenda-popover');
    if (!anchor || !panel) return;
    returnFocus = anchor;
    const place = () => {
        const margin = 12, gap = 10;
        const viewport = window.visualViewport;
        const viewportWidth = viewport?.width ?? window.innerWidth;
        const viewportHeight = viewport?.height ?? window.innerHeight;
        const viewportLeft = viewport?.offsetLeft ?? 0;
        const viewportTop = viewport?.offsetTop ?? 0;
        const width = Math.min(360, viewportWidth - margin * 2);
        panel.style.width = `${width}px`;
        panel.style.maxHeight = `${viewportHeight - margin * 2}px`;
        const rect = anchor.getBoundingClientRect();
        const height = panel.getBoundingClientRect().height;
        let left = rect.right + gap;
        if (left + width > viewportLeft + viewportWidth - margin) left = rect.left - width - gap;
        if (left < viewportLeft + margin) left = viewportLeft + (viewportWidth - width) / 2;
        const top = Math.max(viewportTop + margin, Math.min(rect.top, viewportTop + viewportHeight - height - margin));
        panel.style.left = `${Math.max(viewportLeft + margin, left)}px`;
        panel.style.top = `${top}px`;
        panel.style.visibility = 'visible';
    };
    const observer = new ResizeObserver(place);
    observer.observe(panel);
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    window.visualViewport?.addEventListener('resize', place);
    window.visualViewport?.addEventListener('scroll', place);
    place();
    panel.querySelector('input')?.focus({ preventScroll: true });
    cleanup = () => { observer.disconnect(); window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true); window.visualViewport?.removeEventListener('resize', place); window.visualViewport?.removeEventListener('scroll', place); };
}
