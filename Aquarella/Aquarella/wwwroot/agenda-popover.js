let cleanup;
export function stop() { cleanup?.(); cleanup = null; }
export function open(anchorId) {
    stop();
    const anchor = document.getElementById(anchorId);
    const panel = document.getElementById('agenda-popover');
    if (!anchor || !panel) return;
    const place = () => {
        const margin = 12, gap = 10;
        const width = Math.min(360, window.innerWidth - margin * 2);
        panel.style.width = `${width}px`;
        panel.style.maxHeight = `${window.innerHeight - margin * 2}px`;
        const rect = anchor.getBoundingClientRect();
        const height = panel.getBoundingClientRect().height;
        let left = rect.right + gap;
        if (left + width > window.innerWidth - margin) left = rect.left - width - gap;
        if (left < margin) left = (window.innerWidth - width) / 2;
        const top = Math.max(margin, Math.min(rect.top, window.innerHeight - height - margin));
        panel.style.left = `${Math.max(margin, left)}px`;
        panel.style.top = `${top}px`;
        panel.style.visibility = 'visible';
    };
    const observer = new ResizeObserver(place);
    observer.observe(panel);
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    place();
    panel.querySelector('input')?.focus({ preventScroll: true });
    cleanup = () => { observer.disconnect(); window.removeEventListener('resize', place); window.removeEventListener('scroll', place, true); };
}
