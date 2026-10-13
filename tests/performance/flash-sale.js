// =============================================================
// FlashStore - Escenario crítico: "las últimas 5 unidades"
// 300 clientes distintos intentan comprar el producto 1 al mismo tiempo.
//
// Criterio de salida (declarado ANTES de ejecutar):
//   - orders_created <= 5        → nunca hay sobreventa
//   - p95 de POST /api/orders < 800 ms
//   - 0 errores 5xx
// Si un umbral falla, k6 termina con código distinto de 0 y Jenkins marca NO-GO.
//
// Antes de ejecutar:
//   1. LOGIN_RATE_LIMIT=1000 en .env (300 logins desde una sola IP) y docker compose up -d
//   2. docker exec -i flashstore-postgres psql -U flashstore -d flashstore < tests/performance/reset-flash-sale.sql
//   3. docker exec flashstore-redis redis-cli DEL product:1 products:list products:featured
// Ejecutar:
//   k6 run -e BASE_URL=http://localhost:5000 --summary-export=reports/performance/flash-sale.json tests/performance/flash-sale.js
// =============================================================
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const PRODUCT_ID = Number(__ENV.PRODUCT_ID || 1);
const BUYERS = Number(__ENV.BUYERS || 300);
const PASSWORD = __ENV.PASSWORD || 'Test1234!';
const JSON_HEADERS = { 'Content-Type': 'application/json' };

const ordersCreated = new Counter('orders_created');
const ordersRejected = new Counter('orders_rejected_out_of_stock');
const serverErrors = new Counter('server_errors');

export const options = {
  setupTimeout: '180s',
  scenarios: {
    flash_sale: {
      executor: 'shared-iterations',
      vus: BUYERS,
      iterations: BUYERS,
      maxDuration: '60s',
    },
  },
  thresholds: {
    orders_created: ['count<=5'],
    server_errors: ['count==0'],
    'http_req_duration{endpoint:create_order}': ['p(95)<800'],
  },
};

export function setup() {
  const tokens = [];

  // Inicia sesión con los 300 clientes de prueba en lotes de 50 solicitudes paralelas.
  for (let start = 1; start <= BUYERS; start += 50) {
    const batch = [];
    for (let n = start; n < start + 50 && n <= BUYERS; n++) {
      batch.push([
        'POST',
        `${BASE_URL}/api/auth/login`,
        JSON.stringify({ email: `cliente${n}@flashstore.test`, password: PASSWORD }),
        { headers: JSON_HEADERS, tags: { endpoint: 'login' } },
      ]);
    }

    for (const res of http.batch(batch)) {
      if (res.status !== 200) {
        throw new Error(`Login falló con estado ${res.status}: ${res.body}. ¿Subió LOGIN_RATE_LIMIT a 1000?`);
      }
      tokens.push(res.json('token'));
    }
  }

  const product = http.get(`${BASE_URL}/api/products/${PRODUCT_ID}`).json();
  console.log(`Stock inicial del producto ${PRODUCT_ID}: ${product.stock}`);

  return { tokens };
}

export default function (data) {
  const token = data.tokens[(__VU - 1) % data.tokens.length];

  const res = http.post(
    `${BASE_URL}/api/orders`,
    JSON.stringify({ items: [{ productId: PRODUCT_ID, quantity: 1 }] }),
    {
      headers: {
        ...JSON_HEADERS,
        Authorization: `Bearer ${token}`,
        'Idempotency-Key': uuidv4(),
      },
      tags: { endpoint: 'create_order' },
      responseCallback: http.expectedStatuses(201, 409),
    }
  );

  if (res.status === 201) ordersCreated.add(1);
  if (res.status === 409) ordersRejected.add(1);
  if (res.status >= 500) serverErrors.add(1);

  check(res, {
    'respuesta 201 (comprado) o 409 (sin stock)': (r) => r.status === 201 || r.status === 409,
  });
}

export function teardown() {
  const product = http.get(`${BASE_URL}/api/products/${PRODUCT_ID}`).json();
  console.log(`Stock final del producto ${PRODUCT_ID}: ${product.stock} (debe ser 0, nunca negativo)`);
}
