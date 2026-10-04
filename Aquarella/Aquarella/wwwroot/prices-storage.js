import { load as loadStock } from './stock-storage.js';
const key = 'aquarella.prices.v1';
export function load() {
    const products = loadStock();
    const prices = JSON.parse(localStorage.getItem(key) ?? '[]');
    if (!Array.isArray(prices) || prices.some(p => !p.productId || !Number.isFinite(p.cost) || p.cost < 0 || p.cost > 1e9 || !Number.isFinite(p.desiredProfitPercent) || p.desiredProfitPercent < 0 || p.desiredProfitPercent > 10000 || !Number.isFinite(p.salePrice) || p.salePrice < 0 || p.salePrice > 1e12 || typeof p.manualSalePrice !== 'boolean') || new Set(prices.map(p => p.productId)).size !== prices.length) throw new Error('Precios guardados inválidos.');
    return { products, prices };
}
export function save(prices) { localStorage.setItem(key, JSON.stringify(prices)); }
export function watch(receiver) {
    const refresh = event => { if (event.type === 'aquarella-stock-changed' || event.key === 'aquarella.stock.v1') receiver.invokeMethodAsync('RefreshStock').catch(() => {}); };
    window.addEventListener('storage', refresh);
    window.addEventListener('aquarella-stock-changed', refresh);
    return { dispose() { window.removeEventListener('storage', refresh); window.removeEventListener('aquarella-stock-changed', refresh); } };
}
