// Central activation setting: 0.40 = 40% (try 0.30, 0.35 or 0.45).
const ACTIVATION_VISIBILITY = 0.40;
// Preserve the existing 10-point gap to prevent edge flicker in either direction.
const ENTER_VISIBILITY = ACTIVATION_VISIBILITY;
const EXIT_VISIBILITY = Math.max(0, ACTIVATION_VISIBILITY - 0.10);
const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
const observed = new Map();
let visibilityObserver;

function measure(element, rect, viewportHeight) {
    // Measure the layout position, excluding our animated translation. Otherwise
    // animation itself could cross a threshold and retrigger without any scroll.
    const translate = getComputedStyle(element).translate;
    const offset = translate === 'none' ? 0 : (parseFloat(translate.split(' ')[1]) || 0);
    const top = rect.top - offset;
    const bottom = top + rect.height;
    const height = Math.min(rect.height, viewportHeight);
    const visibleHeight = Math.max(0, Math.min(bottom, viewportHeight) - Math.max(top, 0));
    return { visible: height > 0 ? Math.min(1, visibleHeight / height) : 1, direction: top < 0 ? -1 : 1 };
}

function updateState(element, rect, viewportHeight) {
    const { visible, direction } = measure(element, rect, viewportHeight);
    const previous = observed.get(element);
    let state = previous?.state ?? 'pending';
    if (state !== 'visible' && visible >= ENTER_VISIBILITY) state = 'visible';
    else if (state === 'visible' && visible < EXIT_VISIBILITY) state = 'hidden';
    // Scroll changes targets only when a threshold is crossed. CSS then runs the
    // entire transition on its own, including when the user stops scrolling.
    if (!previous || previous.state !== state) {
        element.style.setProperty('--reveal-direction', String(direction));
        element.setAttribute('data-reveal-state', state);
        observed.set(element, { state });
    }
}

function syncElements() {
    for (const element of observed.keys()) {
        if (!element.isConnected) {
            visibilityObserver?.unobserve(element);
            observed.delete(element);
        }
    }
    if (!visibilityObserver) return;
    for (const element of document.querySelectorAll('.scroll-reveal')) {
        if (observed.has(element)) continue;
        updateState(element, element.getBoundingClientRect(), document.documentElement.clientHeight);
        visibilityObserver.observe(element);
    }
}

function configureMotion() {
    visibilityObserver?.disconnect();
    visibilityObserver = undefined;
    for (const element of observed.keys()) {
        element.removeAttribute('data-reveal-state');
        element.style.removeProperty('--reveal-direction');
    }
    observed.clear();
    if (!motionPreference.matches && 'IntersectionObserver' in window) {
        visibilityObserver = new IntersectionObserver(entries => {
            for (const entry of entries) {
                updateState(entry.target, entry.boundingClientRect, document.documentElement.clientHeight);
            }
        }, { threshold: Array.from({ length: 101 }, (_, index) => index / 100) });
        syncElements();
    }
}

configureMotion();
motionPreference.addEventListener('change', configureMotion);
new MutationObserver(syncElements).observe(document.body, { childList: true, subtree: true });
