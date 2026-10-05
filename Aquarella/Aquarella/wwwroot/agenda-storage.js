// Legacy read-only adapter, used only by the one-time database import.
const key = 'aquarella.agenda.v1';
export function load() {
    const notes = JSON.parse(localStorage.getItem(key) ?? '[]');
    if (!Array.isArray(notes) || notes.some(n => !n.id || !/^\d{4}-\d{2}-\d{2}$/.test(n.date) || typeof n.title !== 'string' || n.title.length > 120 || typeof n.content !== 'string' || (!n.title.trim() && !n.content.trim()) || n.content.length > 4000 || (n.time !== null && !/^([01]\d|2[0-3]):[0-5]\d$/.test(n.time))) || new Set(notes.map(n => n.id)).size !== notes.length) throw new Error('Notas guardadas inválidas.');
    return notes;
}



