// =============================================================
// FlashStore - Pruebas de rendimiento: carga, estrés y pico
//
// Simula el tráfico de navegación de una venta relámpago (catálogo, detalle
// de producto y salud) SIN iniciar sesión, para no chocar con el límite de
// intentos de login. La compra concurrente se prueba aparte en flash-sale.js.
//
//   ESCENARIO=carga  : 50 usuarios constantes durante 2 min (día normal)
//   ESCENARIO=estres : sube por etapas hasta 200 usuarios (busca el punto de quiebre)
//   ESCENARIO=pico   : salta de 10 a 300 usuarios en 10 s (apertura de la venta)
//
// Criterios de salida (declarados ANTES de ejecutar):
//   carga  : p95 < 800 ms  y errores < 1 %
//   estres : p95 < 1500 ms y errores < 5 %
//   pico   : p95 < 2000 ms y errores < 5 %
// Si un umbral falla, k6 termina con código 99 y el pipeline marca Rendimiento = FALLA.
//
// Uso:
//   k6 run -e BASE_URL=http://IP:5000 -e ESCENARIO=carga --summary-export=reports/performance/carga.json tests/performance/carga.js
// =============================================================
import http from 'k6/http';
import { check, sleep, group } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const ESCENARIO = (__ENV.ESCENARIO || 'carga').toLowerCase();

const ESCENARIOS = {
  carga: {
    executor: 'constant-vus',
    vus: 50,
    duration: '2m',
  },
  estres: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '30s', target: 50 },
      { duration: '45s', target: 100 },
      { duration: '45s', target: 200 },
      { duration: '30s', target: 200 },
      { duration: '20s', target: 0 },
    ],
    gracefulRampDown: '10s',
  },
  pico: {
    executor: 'ramping-vus',
    startVUs: 10,
    stages: [
      { duration: '20s', target: 10 },
      { duration: '10s', target: 300 },
      { duration: '40s', target: 300 },
      { duration: '10s', target: 10 },
      { duration: '10s', target: 0 },
    ],
    gracefulRampDown: '10s',
  },
};

const UMBRALES = {
  carga: { p95: 800, errores: 0.01 },
  estres: { p95: 1500, errores: 0.05 },
  pico: { p95: 2000, errores: 0.05 },
};

if (!ESCENARIOS[ESCENARIO]) {
  throw new Error(`ESCENARIO desconocido: ${ESCENARIO}. Use carga, estres o pico.`);
}

export const options = {
  scenarios: { [ESCENARIO]: ESCENARIOS[ESCENARIO] },
  thresholds: {
    http_req_duration: [`p(95)<${UMBRALES[ESCENARIO].p95}`],
    http_req_failed: [`rate<${UMBRALES[ESCENARIO].errores}`],
    checks: ['rate>0.95'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export default function () {
  group('catalogo', () => {
    const res = http.get(`${BASE_URL}/api/products`, { tags: { endpoint: 'catalogo' } });
    check(res, { 'catálogo 200': (r) => r.status === 200 });
  });

  group('detalle', () => {
    const id = 1 + Math.floor(Math.random() * 12);
    const res = http.get(`${BASE_URL}/api/products/${id}`, { tags: { endpoint: 'detalle' } });
    check(res, { 'detalle 200': (r) => r.status === 200 });
  });

  if (Math.random() < 0.1) {
    const res = http.get(`${BASE_URL}/health`, { tags: { endpoint: 'health' } });
    check(res, { 'health 200': (r) => r.status === 200 });
  }

  // Tiempo de "lectura" del usuario entre páginas
  sleep(1 + Math.random() * 2);
}
