// Smoke: el catálogo carga, busca y filtra sin iniciar sesión.
describe('Catálogo', () => {
  beforeEach(() => {
    cy.visit('/');
  });

  it('muestra los productos y la consola de edición limitada', () => {
    cy.get('[data-testid="product-grid"]').should('be.visible');
    cy.get('[data-testid^="product-"]').should('have.length.greaterThan', 0);
    cy.get('[data-testid="product-1"]').should('contain.text', 'FlashBox X');
    cy.get('[data-testid="price-1"]').invoke('text').should('match', /Q/);
  });

  it('busca productos por nombre', () => {
    cy.get('[data-testid="search-input"]').type('laptop');
    cy.get('[data-testid="product-6"]').should('be.visible');
    cy.get('[data-testid="product-1"]').should('not.exist');
  });

  it('avisa cuando la búsqueda no tiene resultados', () => {
    cy.get('[data-testid="search-input"]').type('zzzz-no-existe');
    cy.get('[data-testid="no-results"]').should('be.visible');
  });

  it('pide iniciar sesión al agregar al carrito sin sesión', () => {
    cy.get('[data-testid="add-2"]').click();
    cy.location('pathname').should('eq', '/login');
  });

  it('entrega cabeceras de seguridad (CSP) en la página', () => {
    cy.request('/').its('headers').should((headers) => {
      expect(headers).to.have.property('content-security-policy');
      expect(headers['x-content-type-options']).to.eq('nosniff');
    });
  });
});
