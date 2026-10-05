// Presentation only. Final DOM data/labels are produced by .NET, never by animation.
const controllers = new WeakMap();
export function dispose(root) { controllers.get(root)?.stop(); controllers.delete(root); }
export function enter(root) {
    dispose(root);
    if (!root?.isConnected) return;
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const counters = [...root.querySelectorAll('[data-metric-counter]')];
    const animations = new Set();
    let frame = 0, stopped = false, introObserver, resolveCount;
    function restore() { counters.forEach(el => { el.textContent = el.dataset.final; }); }
    function stop() {
        if (stopped) return;
        stopped = true;
        cancelAnimationFrame(frame);
        resolveCount?.();
        introObserver?.disconnect();
        reduced.removeEventListener('change', reduce);
        for (const animation of animations) animation.cancel();
        animations.clear();
        restore();
        root.dataset.metricsMotion = 'complete';
    }
    function reduce() { if (reduced.matches) stop(); }
    controllers.set(root, {stop});
    if (!counters.length) { stop(); return; }
    reduced.addEventListener('change', reduce);
    if (reduced.matches) { stop(); return; }
    const styles = getComputedStyle(document.documentElement);
    const normal = parseFloat(styles.getPropertyValue('--motion-normal')) || 200;
    const slow = parseFloat(styles.getPropertyValue('--motion-slow')) || 280;
    const easing = styles.getPropertyValue('--ease-soft').trim() || 'ease-out';
    function animate(el, frames, duration, delay) {
        if (!el.animate) return Promise.resolve();
        const animation = el.animate(frames, {duration, delay, easing, fill:'backwards'});
        animations.add(animation);
        return animation.finished.catch(() => {}).then(() => { animations.delete(animation); animation.cancel(); });
    }
    function start() {
        introObserver?.disconnect();
        if (stopped || !root.isConnected) { stop(); return; }
        root.dataset.metricsMotion = 'running';
        const jobs = [];
        root.querySelectorAll('[data-metric-surface]').forEach((el, i) =>
            jobs.push(animate(el, [{opacity:.65, transform:'translateY(6px)'}, {opacity:1, transform:'none'}], slow, Math.min(i, 7) * 18)));
        root.querySelectorAll('[data-metric-bar]').forEach((el, i) =>
            jobs.push(animate(el, [{transform:'scaleX(0)'}, {transform:'scaleX(1)'}], slow * 2, 150 + Math.min(i, 8) * 12)));
        root.querySelectorAll('[data-metric-ring]').forEach(el =>
            jobs.push(animate(el, [{strokeDasharray:'0 100'}, {strokeDasharray:el.getAttribute('stroke-dasharray')}], normal * 3, 150)));
        const values = counters.map(el => ({el, value:Number(el.dataset.value), format:el.dataset.format}))
            .filter(item => item.el.dataset.value && Number.isFinite(item.value) && Math.abs(item.value) <= Number.MAX_SAFE_INTEGER);
        const integer = new Intl.NumberFormat('es-AR', {maximumFractionDigits:0});
        const decimal = new Intl.NumberFormat('es-AR', {minimumFractionDigits:2, maximumFractionDigits:2});
        const begun = performance.now(), duration = normal * 3;
        const count = new Promise(resolve => {
            resolveCount = resolve;
            function tick(now) {
                if (stopped || !root.isConnected) { stop(); resolve(); return; }
                const t = Math.min(1, Math.max(0, (now - begun - 100) / duration));
                const progress = 1 - Math.pow(1 - t, 3);
                for (const {el, value, format} of values)
                    el.textContent = t === 1 ? el.dataset.final : format === 'count' ? integer.format(Math.round(value * progress))
                        : (format === 'money' ? '$' : '') + decimal.format(value * progress) + (format === 'percent' ? '%' : '');
                if (t < 1) frame = requestAnimationFrame(tick); else { restore(); resolve(); }
            }
            frame = requestAnimationFrame(tick);
        });
        jobs.push(count);
        Promise.all(jobs).then(stop);
    }
    // Coordinate with the existing brand intro; no new loading barrier or screen.
    if (document.documentElement.hasAttribute('data-brand-intro')) {
        root.dataset.metricsMotion = 'waiting-intro';
        introObserver = new MutationObserver(() => {
            if (!document.documentElement.hasAttribute('data-brand-intro')) start();
        });
        introObserver.observe(document.documentElement, {attributes:true, attributeFilter:['data-brand-intro']});
    } else start();
}
