// Legacy read-only adapter, used only by the one-time database import.
const key = 'aquarella.stock.v1';
export function load() {
    const value = localStorage.getItem(key);
    if (value === null) return [];
    const products = JSON.parse(value);
    if (!Array.isArray(products) || products.some(p => !p.id || typeof p.name !== 'string' || !p.name.trim() || p.name.length > 120 || !Number.isInteger(p.quantity) || p.quantity < 0 || p.quantity > 2147483647)
        || new Set(products.map(p => p.id)).size !== products.length) {
        throw new Error('Los datos de stock guardados no son válidos.');
    }
    return products;
}


