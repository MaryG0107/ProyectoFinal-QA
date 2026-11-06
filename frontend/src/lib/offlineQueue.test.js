import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  countPendingOrders,
  enqueueOrder,
  flushQueue,
  listPendingOrders,
  newIdempotencyKey,
  resetQueueForTests
} from './offlineQueue.js';

const networkError = () => Object.assign(new Error('Sin conexión'), { name: 'NetworkError' });
const httpError = (status, message = `Error ${status}`) => Object.assign(new Error(message), { name: 'ApiError', status });

beforeEach(async () => {
  await resetQueueForTests();
  globalThis.indexedDB = new IDBFactory();
});

describe('newIdempotencyKey', () => {
  it('genera UUID v4 distintos', () => {
    const a = newIdempotencyKey();
    const b = newIdempotencyKey();
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(a).not.toBe(b);
  });
});

describe('enqueueOrder', () => {
  it('guarda solo productId y quantity (nunca precios ni tarjetas)', async () => {
    await enqueueOrder({
      idempotencyKey: 'k1',
      items: [{ productId: 1, quantity: 2, unitPrice: 0.01, cardNumber: '4111111111111111' }]
    });

    const [entry] = await listPendingOrders();
    expect(entry.items).toEqual([{ productId: 1, quantity: 2 }]);
    expect(JSON.stringify(entry)).not.toContain('4111');
    expect(entry.attempts).toBe(0);
  });

  it('rechaza pedidos sin llave o sin productos', async () => {
    await expect(enqueueOrder({ items: [{ productId: 1, quantity: 1 }] })).rejects.toThrow('Idempotency-Key');
    await expect(enqueueOrder({ idempotencyKey: 'k', items: [] })).rejects.toThrow('no tiene productos');
  });

  it('la misma llave no crea dos entradas', async () => {
    await enqueueOrder({ idempotencyKey: 'repetida', items: [{ productId: 1, quantity: 1 }] });
    await enqueueOrder({ idempotencyKey: 'repetida', items: [{ productId: 1, quantity: 1 }] });
    expect(await countPendingOrders()).toBe(1);
  });
});

describe('flushQueue', () => {
  it('envía los pendientes con su misma Idempotency-Key y los retira de la cola', async () => {
    await enqueueOrder({ idempotencyKey: 'a', items: [{ productId: 1, quantity: 1 }] });
    await enqueueOrder({ idempotencyKey: 'b', items: [{ productId: 2, quantity: 3 }] });
    const send = vi.fn(async (items, key) => ({ id: `pedido-${key}`, items }));

    const result = await flushQueue(send);

    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenNthCalledWith(1, [{ productId: 1, quantity: 1 }], 'a');
    expect(send).toHaveBeenNthCalledWith(2, [{ productId: 2, quantity: 3 }], 'b');
    expect(result.sent.map((s) => s.idempotencyKey)).toEqual(['a', 'b']);
    expect(result.kept).toBe(0);
  });

  it('si sigue sin conexión conserva el pedido, cuenta el intento y se detiene', async () => {
    await enqueueOrder({ idempotencyKey: 'a', items: [{ productId: 1, quantity: 1 }] });
    await enqueueOrder({ idempotencyKey: 'b', items: [{ productId: 2, quantity: 1 }] });
    const send = vi.fn(async () => { throw networkError(); });

    const result = await flushQueue(send);

    expect(send).toHaveBeenCalledTimes(1);
    expect(result.kept).toBe(2);
    const [first] = await listPendingOrders();
    expect(first.attempts).toBe(1);
    expect(first.lastError).toBe('Sin conexión');
  });

  it('retira de la cola los pedidos rechazados por el servidor (409 sin stock)', async () => {
    await enqueueOrder({ idempotencyKey: 'agotado', items: [{ productId: 1, quantity: 1 }] });
    const send = vi.fn(async () => { throw httpError(409, 'Stock insuficiente para completar el pedido.'); });

    const result = await flushQueue(send);

    expect(result.rejected).toEqual([
      { idempotencyKey: 'agotado', status: 409, message: 'Stock insuficiente para completar el pedido.' }
    ]);
    expect(await countPendingOrders()).toBe(0);
  });

  it('conserva los pedidos ante errores del servidor (5xx) para reintentar', async () => {
    await enqueueOrder({ idempotencyKey: 'a', items: [{ productId: 1, quantity: 1 }] });
    const send = vi.fn(async () => { throw httpError(503, 'Servicio no disponible'); });

    const result = await flushQueue(send);

    expect(result.kept).toBe(1);
    expect((await listPendingOrders())[0].attempts).toBe(1);
  });

  it('un reenvío repetido usa siempre la misma llave (el servidor responde con el pedido original)', async () => {
    await enqueueOrder({ idempotencyKey: 'misma', items: [{ productId: 1, quantity: 1 }] });
    const keysSeen = [];
    let calls = 0;
    const send = async (_items, key) => {
      keysSeen.push(key);
      calls += 1;
      if (calls === 1) throw networkError();
      return { id: 'pedido-1' };
    };

    await flushQueue(send);
    await flushQueue(send);

    expect(keysSeen).toEqual(['misma', 'misma']);
    expect(await countPendingOrders()).toBe(0);
  });
});
