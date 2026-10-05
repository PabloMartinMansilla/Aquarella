// Presentation only: once per tab session, independent of account/authentication state.
(() => {
    const key = 'aquarella.brand-intro.v1';
    try {
        if (sessionStorage.getItem(key)) return;
        sessionStorage.setItem(key, 'seen');
    } catch { return; } // Unavailable storage must not create a recurring entrance barrier.

    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const root = document.documentElement;
    // Set before body is parsed so the interface cannot flash ahead of the intro.
    root.setAttribute('data-brand-intro', 'playing');
    function start() {
        const overlay = document.createElement('div');
        overlay.className = 'brand-intro';
        overlay.setAttribute('aria-hidden', 'true');
        const composition = document.createElement('div');
        composition.className = 'brand-intro-composition';
        const mark = document.createElement('span');
        mark.className = 'brand-intro-mark';
        mark.textContent = 'A';
        const name = document.createElement('strong');
        name.className = 'brand-intro-name';
        [...'Aquarella'].forEach((letter, index) => {
            const piece = document.createElement('span');
            piece.textContent = letter;
            piece.style.setProperty('--piece-index', index);
            piece.style.setProperty('--piece-rotation', `${index % 2 ? 18 : -24}deg`);
            name.append(piece);
        });
        composition.append(mark, name);
        overlay.append(composition);
        document.body.prepend(overlay);

        let closing = false;
        let timer;
        function finish() {
            overlay.remove();
            root.removeAttribute('data-brand-intro');
            root.style.removeProperty('--intro-fade');
        }
        function close() {
            if (closing) return;
            closing = true;
            clearTimeout(timer);
            reduced.removeEventListener('change', close);
            const styles = getComputedStyle(document.documentElement);
            const duration = parseFloat(styles.getPropertyValue(reduced.matches ? '--motion-fast' : '--intro-fade')) || (reduced.matches ? 140 : 280);
            const easing = styles.getPropertyValue('--ease-soft').trim() || 'ease-out';
            root.style.setProperty('--intro-fade', `${duration}ms`);
            // Both layers change in the same frame: no empty intermediate screen.
            root.setAttribute('data-brand-intro', 'revealing');
            if (!overlay.animate) { finish(); return; }
            const fade = overlay.animate([{ opacity: 1 }, { opacity: 0 }], { duration, easing, fill: 'forwards' });
            fade.finished.then(finish, finish);
            // Removal is bounded even if the browser cancels or suspends an animation.
            setTimeout(finish, duration + 80);
        }
        if (reduced.matches && composition.animate) {
            composition.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 140, easing: 'ease-out' });
        }
        const hold = parseFloat(getComputedStyle(root).getPropertyValue('--intro-hold')) || 1100;
        timer = setTimeout(close, reduced.matches ? 160 : hold);
        reduced.addEventListener('change', close, { once: true });
    }
    if (document.body) start();
    else {
        // Mount before first paint, without waiting for Blazor or network-backed state.
        const observer = new MutationObserver(() => {
            if (!document.body) return;
            observer.disconnect();
            start();
        });
        observer.observe(document.documentElement, { childList: true });
    }
})();
