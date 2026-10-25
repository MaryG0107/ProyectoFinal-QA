// Configuración de Cypress para FlashStore.
// La PWA debe estar levantada con Docker:  docker compose up -d   →  http://localhost:3000
// Para apuntar a otro ambiente:  $env:CYPRESS_BASE_URL="http://otra-url"; npm run cy:run
const { defineConfig } = require('cypress');

module.exports = defineConfig({
  e2e: {
    baseUrl: process.env.CYPRESS_BASE_URL || 'http://localhost:3000',
    specPattern: 'e2e/**/*.cy.js',
    supportFile: 'support/e2e.js',
    fixturesFolder: false,
    screenshotsFolder: 'screenshots',
    videosFolder: 'videos',
    video: false,
    screenshotOnRunFailure: true,
    viewportWidth: 1280,
    viewportHeight: 860,
    defaultCommandTimeout: 8000,
    retries: { runMode: 1, openMode: 0 }
  }
});
