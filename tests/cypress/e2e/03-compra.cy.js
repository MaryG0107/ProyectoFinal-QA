// Smoke: flujo completo de compra (carrito → pedido → pago).
// Usa el producto 2 (stock alto) para NO gastar las 5 unidades de la consola (producto 1),
// que se reservan para la prueba de concurrencia con k6.
describe('Compra', () => {
  beforeEach(() => {
    cy.loginByApi('cliente2@flashstore.test');
    cy.clearCart();
  });

  function crearPedido() {
    cy.visit('/');
    cy.get('[data-testid="add-2"]').click();
    cy.get('[data-testid="catalog-notice"]').should('be.visible');
    cy.get('[data-testid="cart-count"]').should('not.have.text', '0');

    cy.get('[data-testid="cart-link"]').click();
    cy.get('[data-testid="cart-item-2"]').should('be.visible');
    cy.get('[data-testid="cart-total"]').invoke('text').should('match', /Q/);
    cy.get('[data-testid="checkout-button"]').click();

    cy.location('pathname').should('match', /^\/pedidos\/[0-9a-f-]+$/);
    cy.get('[data-testid="order-status"]').should('have.text', 'Pendiente de pago');
  }

  it('paga con tarjeta aprobada y el pedido queda Pagado', () => {
    crearPedido();
    cy.get('[data-testid="card-number"]').type('4111 1111 1111 1111');
    cy.get('[data-testid="pay-button"]').click();
    cy.get('[data-testid="payment-result"]').should('contain.text', 'aprobado');
    // El Worker procesa el pago por RabbitMQ: el estado cambia de forma asíncrona.
    cy.get('[data-testid="order-status"]', { timeout: 20000 }).should('have.text', 'Pagado');
  });

  it('muestra el rechazo con tarjeta sin fondos', () => {
    crearPedido();
    cy.get('[data-testid="card-number"]').type('4000 0000 0000 0002');
    cy.get('[data-testid="pay-button"]').click();
    cy.get('[data-testid="payment-result"]').should('contain.text', 'rechazado');
    cy.get('[data-testid="order-status"]', { timeout: 20000 }).should('have.text', 'Pago rechazado');
  });
});
