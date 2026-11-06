// Comandos reutilizables para las pruebas de FlashStore.

const SESSION_KEY = 'flashstore.session';
const CART_COPY_KEY = 'flashstore.cart';
const PASSWORD = 'Test1234!';

/**
 * Inicia sesión por API (rápido) y guarda la sesión como lo hace la PWA.
 * cy.session la reutiliza dentro del mismo archivo para no gastar el
 * límite de 10 inicios de sesión por minuto de la API.
 */
Cypress.Commands.add('loginByApi', (email) => {
  cy.session(email, () => {
    cy.request('POST', '/api/auth/login', { email, password: PASSWORD }).then(({ body }) => {
      const session = { token: body.token, expiresAt: body.expiresAt, user: body.user };
      window.localStorage.setItem(SESSION_KEY, JSON.stringify(session));
    });
  }, {
    validate() {
      const raw = window.localStorage.getItem(SESSION_KEY);
      expect(raw, 'sesión guardada').to.be.a('string');
    }
  });
});

/** Token JWT de la sesión actual (para llamar a la API desde la prueba). */
Cypress.Commands.add('authToken', () =>
  cy.window().then((win) => JSON.parse(win.localStorage.getItem(SESSION_KEY)).token));

/** Vacía el carrito del usuario en el servidor y la copia local, para que cada prueba empiece limpia. */
Cypress.Commands.add('clearCart', () => {
  cy.visit('/');
  cy.authToken().then((token) => {
    cy.request({ method: 'DELETE', url: '/api/cart', headers: { Authorization: `Bearer ${token}` } });
  });
  cy.window().then((win) => win.localStorage.removeItem(CART_COPY_KEY));
});

/**
 * Simula perder o recuperar internet en la PWA:
 *  - cambia navigator.onLine y dispara el evento 'offline'/'online' del navegador;
 *  - mientras está sin conexión, cualquier llamada a /api se corta con error de red.
 * Requiere haber llamado antes a cy.prepareNetworkToggle().
 */
Cypress.Commands.add('prepareNetworkToggle', () => {
  const state = { online: true };
  cy.wrap(state, { log: false }).as('network');
  cy.intercept('/api/**', (req) => {
    if (!state.online) req.destroy();
  });
});

Cypress.Commands.add('setNetwork', (online) => {
  cy.get('@network', { log: false }).then((state) => {
    state.online = online;
  });
  cy.window().then((win) => {
    Object.defineProperty(win.navigator, 'onLine', { configurable: true, get: () => online });
    win.dispatchEvent(new win.Event(online ? 'online' : 'offline'));
  });
});
