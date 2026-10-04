export const steps = [
    { id: 'tour-menu', title: 'Menú principal', text: 'Desde acá podés acceder rápidamente a las distintas secciones de Aquarella.' },
    { id: 'tour-profile', title: 'Tu negocio', text: 'Configurá el nombre, logo, colores y datos principales de tu negocio.' },
    { id: 'tour-search', title: 'Encontrá lo que necesitás', text: 'Buscá rápidamente cualquier sección de Aquarella.' },
    { id: 'tour-panels', title: 'Tus herramientas', text: 'Desde estos paneles accedés a Stock, Precios, Agenda y las demás herramientas.' },
    { id: 'tour-card-stock', title: 'Controlá tu stock', text: 'Registrá tus productos y mantené actualizadas sus cantidades.' },
    { id: 'tour-card-agenda', title: 'Organizá tu negocio', text: 'Usá la agenda para guardar notas, fechas y próximos eventos.' }
];
let active;
export function stop() { active?.dispose(); active = null; }
export function start(userId, force = false) {
    stop();
    if (!userId) return;
    const key = `aquarella.onboarding.v1.${encodeURIComponent(userId)}`;
    if (!force && localStorage.getItem(key)) return;
    const shell = document.querySelector('.app-shell');
    const oldInert = shell?.inert;
    const oldFocus = document.activeElement;
    const layer = document.createElement('div'); layer.className = 'tour-layer';
    const focus = document.createElement('div'); focus.className = 'tour-spotlight';
    const card = document.createElement('section'); card.className = 'tour-card'; card.role = 'dialog';
    card.setAttribute('aria-modal', 'true'); card.setAttribute('aria-labelledby', 'tour-title');
    layer.append(focus, card); document.body.append(layer);
    if (shell) shell.inert = true;
    let index = 0, confirming = false, final = false, frame;
    const text = (tag, value, cls) => { const el = document.createElement(tag); el.textContent = value; if (cls) el.className = cls; card.append(el); return el; };
    const button = (label, action, primary = false) => { const el = document.createElement('button'); el.type = 'button'; el.textContent = label; if (primary) el.className = 'tour-primary'; el.onclick = action; card.append(el); };
    const position = () => {
        const target = document.getElementById(steps[index].id);
        const margin = 12, gap = 14, vw = window.innerWidth, vh = window.innerHeight;
        const r = target?.getBoundingClientRect();
        card.style.maxHeight = `${vh - margin * 2}px`;
        if (r && !final) {
            Object.assign(focus.style, { left: `${r.left - 4}px`, top: `${r.top - 4}px`, width: `${r.width + 8}px`, height: `${r.height + 8}px`, borderRadius: getComputedStyle(target).borderRadius });
            const c = card.getBoundingClientRect();
            let x = r.right + gap, y = r.top;
            if (x + c.width > vw - margin) x = r.left - c.width - gap;
            if (x < margin) { x = r.left; y = r.bottom + gap; if (y + c.height > vh - margin) y = r.top - c.height - gap; }
            card.style.left = `${Math.max(margin, Math.min(x, vw - c.width - margin))}px`;
            card.style.top = `${Math.max(margin, Math.min(y, vh - c.height - margin))}px`;
        } else {
            focus.style.width = '0'; focus.style.height = '0';
            card.style.left = `${Math.max(margin, (vw - card.offsetWidth) / 2)}px`;
            card.style.top = `${Math.max(margin, (vh - card.offsetHeight) / 2)}px`;
        }
    };
    const persist = result => {
        try { localStorage.setItem(key, JSON.stringify({ status: result, at: new Date().toISOString() })); stop(); }
        catch { text('p', 'No se pudo guardar el tutorial. Revisá los permisos del navegador.'); position(); }
    };
    const render = () => {
        card.replaceChildren();
        if (confirming) {
            text('h2', '¿Querés saltar el tutorial?').id = 'tour-title';
            text('p', 'Podrás volver a verlo más adelante.');
            button('Continuar tutorial', () => { confirming = false; render(); });
            button('Saltar', () => persist('skipped'), true);
        } else if (final) {
            text('h2', '¡Listo! Ya conocés lo básico de Aquarella.').id = 'tour-title';
            button('Empezar', () => persist('completed'), true);
        } else {
            text('small', `${index + 1} de ${steps.length}`);
            text('h2', steps[index].title).id = 'tour-title';
            text('p', steps[index].text);
            if (index > 0) button('Atrás', () => { index--; show(); });
            button(index === steps.length - 1 ? 'Finalizar' : 'Siguiente', () => { if (index === steps.length - 1) { final = true; render(); } else { index++; show(); } }, true);
            button('Saltar tutorial', () => { confirming = true; render(); });
        }
        position(); card.querySelector('button')?.focus({ preventScroll: true });
    };
    const show = () => {
        const target = document.getElementById(steps[index].id);
        target?.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'center', inline: 'nearest' });
        render();
    };
    const update = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(position); };
    const keyboard = event => {
        if (event.key === 'Tab') {
            const buttons = [...card.querySelectorAll('button')];
            if (event.shiftKey && document.activeElement === buttons[0]) { event.preventDefault(); buttons.at(-1)?.focus(); }
            else if (!event.shiftKey && document.activeElement === buttons.at(-1)) { event.preventDefault(); buttons[0]?.focus(); }
        }
        if (event.key === 'Escape' && !final) { event.preventDefault(); confirming = true; render(); }
    };
    const observer = new ResizeObserver(update); observer.observe(card);
    window.addEventListener('resize', update); window.addEventListener('scroll', update, true); layer.addEventListener('keydown', keyboard);
    active = { dispose() { cancelAnimationFrame(frame); observer.disconnect(); window.removeEventListener('resize', update); window.removeEventListener('scroll', update, true); layer.remove(); if (shell) shell.inert = oldInert; if (oldFocus?.isConnected) oldFocus.focus({ preventScroll: true }); } };
    show();
}
