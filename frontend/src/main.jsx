import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { registerSW } from 'virtual:pwa-register';
import App from './App.jsx';
import { AuthProvider } from './context/AuthContext.jsx';
import { CartProvider } from './context/CartContext.jsx';
import { ConnectivityProvider } from './context/ConnectivityContext.jsx';
import './styles.css';

// Service Worker: guarda la app y el catálogo para que funcionen sin internet.
registerSW({ immediate: true });

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <ConnectivityProvider>
          <CartProvider>
            <App />
          </CartProvider>
        </ConnectivityProvider>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>
);
