import { useEffect, useMemo, useState } from 'react';
import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext.jsx';
import { useCart } from '../context/CartContext.jsx';
import { useConnectivity } from '../context/ConnectivityContext.jsx';

function InstallButton() {
  const [installEvent, setInstallEvent] = useState(null);

  useEffect(() => {
    const onPrompt = (event) => {
      event.preventDefault();
      setInstallEvent(event);
    };
    const onInstalled = () => setInstallEvent(null);
    window.addEventListener('beforeinstallprompt', onPrompt);
    window.addEventListener('appinstalled', onInstalled);
    return () => {
      window.removeEventListener('beforeinstallprompt', onPrompt);
      window.removeEventListener('appinstalled', onInstalled);
    };
  }, []);

  if (!installEvent) return null;

  return (
    <button
      type="button"
      className="btn btn-ghost btn-small"
      data-testid="install-app"
      onClick={async () => {
        installEvent.prompt();
        await installEvent.userChoice;
        setInstallEvent(null);
      }}
    >
      Instalar app
    </button>
  );
}

function ConnectivityBanner() {
  const { online, pendingCount, sync, lastSync } = useConnectivity();
  const [dismissedAt, setDismissedAt] = useState(null);

  const message = useMemo(() => {
    if (!lastSync || lastSync.at === dismissedAt) return null;
    const parts = [];
    if (lastSync.sent.length) parts.push(`${lastSync.sent.length} pedido(s) pendiente(s) enviado(s)`);
    if (lastSync.rejected.length) parts.push(`${lastSync.rejected.length} rechazado(s): ${lastSync.rejected[0].message}`);
    return parts.length ? parts.join(' · ') : null;
  }, [lastSync, dismissedAt]);

  if (!online) {
    return (
      <div className="banner banner-offline" role="status" data-testid="offline-banner">
        <strong>Sin conexión.</strong> Puede ver el catálogo guardado y confirmar pedidos: se enviarán al volver internet.
        {pendingCount > 0 && <span className="pill">{pendingCount} pendiente(s)</span>}
      </div>
    );
  }

  if (pendingCount > 0) {
    return (
      <div className="banner banner-pending" role="status" data-testid="pending-banner">
        Hay {pendingCount} pedido(s) esperando envío.
        <button type="button" className="btn btn-small" onClick={sync}>Enviar ahora</button>
      </div>
    );
  }

  if (message) {
    return (
      <div className="banner banner-synced" role="status" data-testid="synced-banner">
        {message}
        <button type="button" className="btn btn-ghost btn-small" aria-label="Cerrar aviso" onClick={() => setDismissedAt(lastSync.at)}>✕</button>
      </div>
    );
  }

  return null;
}

export default function Layout() {
  const { isAuthenticated, user, logout } = useAuth();
  const { itemCount } = useCart();
  const { online } = useConnectivity();
  const navigate = useNavigate();

  return (
    <div className="app">
      <a href="#contenido" className="skip-link">Saltar al contenido</a>
      <header className="topbar">
        <Link to="/" className="brand" aria-label="FlashStore, ir al catálogo">
          <span className="brand-mark" aria-hidden="true">⚡</span>
          <span>FlashStore</span>
        </Link>

        <nav className="nav" aria-label="Principal">
          <NavLink to="/" end>Catálogo</NavLink>
          {isAuthenticated && <NavLink to="/pedidos">Mis pedidos</NavLink>}
          {isAuthenticated && <NavLink to="/notificaciones">Notificaciones</NavLink>}
        </nav>

        <div className="topbar-actions">
          <span className={`status-dot ${online ? 'is-online' : 'is-offline'}`} data-testid="connection-status">
            {online ? 'En línea' : 'Sin conexión'}
          </span>
          <InstallButton />
          <NavLink to="/carrito" className="cart-link" data-testid="cart-link" aria-label={`Carrito, ${itemCount} productos`}>
            🛒 <span className="cart-count" data-testid="cart-count">{itemCount}</span>
          </NavLink>
          {isAuthenticated ? (
            <div className="user-box">
              <span className="user-name" data-testid="user-name">{user?.fullName}</span>
              <button
                type="button"
                className="btn btn-ghost btn-small"
                data-testid="logout"
                onClick={async () => {
                  await logout();
                  navigate('/login');
                }}
              >
                Salir
              </button>
            </div>
          ) : (
            <Link to="/login" className="btn btn-small" data-testid="login-link">Ingresar</Link>
          )}
        </div>
      </header>

      <ConnectivityBanner />

      <main id="contenido" className="content" tabIndex={-1}>
        <Outlet />
      </main>

      <footer className="footer">
        FlashStore · Proyecto Final QA · UMG · Tarjetas de prueba: 4111 1111 1111 1111 (aprobada) y 4000 0000 0000 0002 (rechazada)
      </footer>
    </div>
  );
}
