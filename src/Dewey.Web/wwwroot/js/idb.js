// Minimal IndexedDB wrapper for Dewey. Two object stores:
//   "outbox" — queued POST bodies awaiting network (key: id)
//   "cache"  — keyed JSON snapshots of GET responses (key: url)
// Plus an online/offline event bridge for Blazor.

const DB_NAME = 'dewey';
const DB_VERSION = 1;

function open() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains('outbox')) {
                db.createObjectStore('outbox', { keyPath: 'id' });
            }
            if (!db.objectStoreNames.contains('cache')) {
                db.createObjectStore('cache', { keyPath: 'url' });
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

async function tx(store, mode, fn) {
    const db = await open();
    return new Promise((resolve, reject) => {
        const t = db.transaction(store, mode);
        const s = t.objectStore(store);
        let result;
        Promise.resolve(fn(s)).then(r => { result = r; });
        t.oncomplete = () => resolve(result);
        t.onerror = () => reject(t.error);
        t.onabort = () => reject(t.error);
    });
}

export async function outboxPut(entry) {
    return tx('outbox', 'readwrite', s => s.put(entry));
}
export async function outboxAll() {
    return tx('outbox', 'readonly', s => new Promise((resolve, reject) => {
        const r = s.getAll();
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
    }));
}
export async function outboxDelete(id) {
    return tx('outbox', 'readwrite', s => s.delete(id));
}

export async function cachePut(url, json) {
    return tx('cache', 'readwrite', s => s.put({ url, json, savedAt: Date.now() }));
}
export async function cacheGet(url) {
    return tx('cache', 'readonly', s => new Promise((resolve, reject) => {
        const r = s.get(url);
        r.onsuccess = () => resolve(r.result?.json ?? null);
        r.onerror = () => reject(r.error);
    }));
}

let dotnetRef = null;
export function registerOnlineListener(ref) {
    dotnetRef = ref;
    window.addEventListener('online', () => ref.invokeMethodAsync('OnOnlineChanged', true));
    window.addEventListener('offline', () => ref.invokeMethodAsync('OnOnlineChanged', false));
}
export function isOnline() {
    return navigator.onLine;
}
