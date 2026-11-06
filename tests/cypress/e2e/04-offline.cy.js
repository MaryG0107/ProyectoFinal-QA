// Cola offline: sin internet el pedido se guarda en el dispositivo
// y se envía solo (una vez, sin duplicar) al volver la conexión.
describe('Pedido sin conexión', () => {
  beforeEach(() => {
    cy.loginByApi('cliente3@flashstore.test');
    cy.clearCart();
    cy.prepareNetworkToggle();
  });

  it('guarda el pedido offline y lo envía al reconectar', () => {
    cy.visit('/');
    cy.get('[data-testid="add-2"]').click();
    cy.get('[data-testid="catalog-notice"]').should('be.visible');
    cy.get('[data-testid="cart-link"]').click();
    cy.get('[data-testid="cart-item-2"]').should('be.visible');

    cy.setNetwork(false);
    cy.get('[data-testid="offline-banner"]').should('be.visible');
    cy.get('[data-testid="connection-status"]').should('have.class', 'is-offline');

    cy.get('[data-testid="checkout-button"]').click();
    cy.get('[data-testid="checkout-status"]').should('contain.text', 'Sin conexión');
    cy.get('[data-testid="offline-banner"]').should('contain.text', '1 pendiente');

    cy.get('a[href="/pedidos"]').first().click();
    cy.get('[data-testid="pending-orders"]').should('be.visible');

    // Cuenta cuántas veces se envía el pedido al volver internet.
    cy.intercept('POST', '/api/orders').as('crearPedido');
    cy.setNetwork(true);

    cy.wait('@crearPedido').its('response.statusCode').should('be.oneOf', [200, 201]);
    cy.get('[data-testid="synced-banner"]').should('contain.text', '1 pedido(s) pendiente(s) enviado(s)');
    cy.get('[data-testid="pending-orders"]').should('not.exist');
    cy.get('@crearPedido.all').should('have.length', 1);
  });
});
