import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

// En desarrollo (npm run dev) las llamadas a /api y /health se envían a la API.
// En Docker, Nginx hace este mismo trabajo (ver nginx.conf).
const apiTarget = process.env.VITE_DEV_API_TARGET || 'http://localhost:5000';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['icons/favicon.png', 'icons/apple-touch-icon.png'],
      manifest: {
        name: 'FlashStore',
        short_name: 'FlashStore',
        description: 'Tienda de ventas flash: catálogo, carrito y compras aun sin conexión.',
        lang: 'es-GT',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'portrait-primary',
        background_color: '#0f1115',
        theme_color: '#ff5a1f',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icons/icon-512-maskable.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      },
      workbox: {
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/health/, /^\/swagger/],
        runtimeCaching: [
          {
            // Catálogo disponible sin internet: primero la red, si falla se usa la copia guardada.
            urlPattern: ({ url }) =>
              url.pathname.startsWith('/api/products') || url.pathname.startsWith('/api/categories'),
            handler: 'NetworkFirst',
            method: 'GET',
            options: {
              cacheName: 'flashstore-catalogo',
              networkTimeoutSeconds: 4,
              expiration: { maxEntries: 60, maxAgeSeconds: 60 * 60 * 24 },
              cacheableResponse: { statuses: [200] }
            }
          }
        ]
      }
    })
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/health': { target: apiTarget, changeOrigin: true }
    }
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.js']
  }
});
