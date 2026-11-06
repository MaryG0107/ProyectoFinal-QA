// Smoke: inicio de sesión con credenciales correctas e incorrectas (por la interfaz).
describe('Inicio de sesión', () => {
  beforeEach(() => {
    cy.visit('/login');
  });

  it('rechaza una contraseña incorrecta', () => {
    cy.get('[data-testid="login-email"]').type('cliente1@flashstore.test');
    cy.get('[data-testid="login-password"]').type('ClaveMala123');
    cy.get('[data-testid="login-submit"]').click();
    cy.get('[data-testid="login-error"]').should('contain.text', 'incorrectos');
    cy.location('pathname').should('eq', '/login');
  });

  it('inicia sesión y muestra el nombre del cliente', () => {
    cy.get('[data-testid="login-email"]').type('cliente1@flashstore.test');
    cy.get('[data-testid="login-password"]').type('Test1234!');
    cy.get('[data-testid="login-submit"]').click();
    cy.location('pathname').should('eq', '/');
    cy.get('[data-testid="user-name"]').should('be.visible');
    cy.get('[data-testid="logout"]').click();
    cy.get('[data-testid="login-link"]').should('be.visible');
  });
});
