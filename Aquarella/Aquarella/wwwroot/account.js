document.querySelectorAll('.password-toggle').forEach(button => button.addEventListener('click', () => {
    const field = document.getElementById(button.dataset.for); const visible = field.type === 'password';
    field.type = visible ? 'text' : 'password'; button.textContent = visible ? 'Ocultar' : 'Mostrar'; button.setAttribute('aria-label', visible ? 'Ocultar contraseña' : 'Mostrar contraseña'); button.setAttribute('aria-pressed', String(visible));
}));
document.querySelector('[data-strength="true"]')?.addEventListener('input', event => {
    const length = event.target.value.length;
    document.getElementById('password-strength').textContent = length < 12 ? 'Débil' : length < 20 ? 'Aceptable' : 'Fuerte';
});
document.querySelectorAll('form').forEach(form => form.addEventListener('submit', event => {
    if (form.dataset.submitting) { event.preventDefault(); return; }
    form.dataset.submitting = 'true'; form.querySelectorAll('[type="submit"]').forEach(b => { b.disabled = true; b.textContent = 'Procesando…'; });
}));
window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
