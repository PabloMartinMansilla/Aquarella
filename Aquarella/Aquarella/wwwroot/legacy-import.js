import { load as stock } from './stock-storage.js';
import { load as prices } from './prices-storage.js';
import { load as agenda } from './agenda-storage.js';
export function read() {
    return { profile: window.aquarellaIdentity.load(), products: stock(), prices: prices().prices, notes: agenda() };
}
