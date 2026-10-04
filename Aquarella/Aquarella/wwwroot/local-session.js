export function load() { return sessionStorage.getItem('aquarella.development-session.v1') === 'pablo'; }
export function save() { sessionStorage.setItem('aquarella.development-session.v1', 'pablo'); }
export function clear() { sessionStorage.removeItem('aquarella.development-session.v1'); }
