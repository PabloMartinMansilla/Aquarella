// Legacy read-only adapter, used only by the one-time database import.
import { load as loadStock } from './stock-storage.js';
const key = 'aquarella.prices.v1';
export function load() {
    const products = loadStock();
    const prices = JSON.parse(localStorage.getItem(key) ?? '[]');
    if (!Array.isArray(prices) || prices.some(p => !p || !p.productId || !Number.isFinite(p.cost) || p.cost < 0 || p.cost > 1e9 || !Number.isFinite(p.desiredProfitPercent) || p.desiredProfitPercent < 0 || p.desiredProfitPercent > 10000 || !Number.isFinite(p.salePrice) || p.salePrice < 0 || p.salePrice > 1e12 || typeof p.manualSalePrice !== 'boolean') || new Set(prices.map(p => p.productId)).size !== prices.length) throw new Error('Precios guardados inválidos.');
    return { products, prices };
}


