// Cola offline de pedidos (patrón "outbox" en el cliente).
//
// Cuando el usuario confirma una compra sin internet, la solicitud se guarda en
// IndexedDB con su Idempotency-Key. Al volver la conexión se reenvía con la MISMA
// llave: si el servidor ya la había recibido, responde con el pedido original y
// no se crea un duplicado.
//
// Por seguridad, la cola NUNCA guarda datos de tarjeta: el pago solo se hace en línea.

import { openDB } from 'idb';

const DB_NAME = 'flashstore';
const DB_VERSION = 1;
const STORE = 'pending-orders';

let dbPromise;

function getDb() {
  if (!dbPromise) {
    dbPromise = openDB(DB_NAME, DB_VERSION, {
      upgrade(db) {
        if (!db.objectStoreNames.contains(STORE)) {
          const store = db.createObjectStore(STORE, { keyPath: 'idempotencyKey' });
          store.createIndex('createdAt', 'createdAt');
        }
      }
    });
  }
  return dbPromise;
}

/** Solo para pruebas: cierra la conexión para empezar con una base limpia. */
export async function resetQueueForTests() {
  if (dbPromise) {
    (await dbPromise).close();
    dbPromise = undefined;
  }
}

export function newIdempotencyKey() {
  if (globalThis.crypto?.randomUUID) {
    return globalThis.crypto.randomUUID();
  }
  // Respaldo para contextos sin randomUUID (http no seguro): UUID v4 con getRandomValues.
  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** Guarda un pedido pendiente. items = [{ productId, quantity }] (sin precios: el servidor los calcula). */
export async function enqueueOrder({ idempotencyKey, items, summary }) {
  if (!idempotencyKey) throw new Error('Falta la Idempotency-Key.');
  if (!Array.isArray(items) || items.length === 0) throw new Error('El pedido no tiene productos.');

  const entry = {
    idempotencyKey,
    items: items.map(({ productId, quantity }) => ({ productId, quantity })),
    summary: summary ?? null,
    createdAt: new Date().toISOString(),
    attempts: 0,
    lastError: null
  };

  const db = await getDb();
  await db.put(STORE, entry);
  return entry;
}

export async function listPendingOrders() {
  const db = await getDb();
  return db.getAllFromIndex(STORE, 'createdAt');
}

export async function countPendingOrders() {
  const db = await getDb();
  return db.count(STORE);
}

export async function removePendingOrder(idempotencyKey) {
  const db = await getDb();
  await db.delete(STORE, idempotencyKey);
}

async function markAttempt(entry, errorMessage) {
  const db = await getDb();
  await db.put(STORE, { ...entry, attempts: entry.attempts + 1, lastError: errorMessage });
}

/**
 * Envía los pedidos pendientes en orden de creación.
 * sendOrder(items, idempotencyKey) debe devolver el pedido creado o lanzar:
 *   - un error con name === 'NetworkError' → se conserva en la cola y se detiene el envío;
 *   - un error con status 4xx → el pedido es inválido (por ejemplo, sin stock): se retira de la cola;
 *   - cualquier otro error (5xx) → se conserva para reintentar más tarde.
 */
export async function flushQueue(sendOrder) {
  const result = { sent: [], rejected: [], kept: 0 };
  const pending = await listPendingOrders();

  for (const entry of pending) {
    try {
      const order = await sendOrder(entry.items, entry.idempotencyKey);
      await removePendingOrder(entry.idempotencyKey);
      result.sent.push({ idempotencyKey: entry.idempotencyKey, order });
    } catch (error) {
      if (error?.name === 'NetworkError') {
        await markAttempt(entry, error.message);
        result.kept = (await countPendingOrders());
        return result;
      }

      if (error?.status >= 400 && error?.status < 500 && error.status !== 408 && error.status !== 429) {
        await removePendingOrder(entry.idempotencyKey);
        result.rejected.push({ idempotencyKey: entry.idempotencyKey, status: error.status, message: error.message });
      } else {
        await markAttempt(entry, error?.message ?? 'Error desconocido');
      }
    }
  }

  result.kept = await countPendingOrders();
  return result;
}
