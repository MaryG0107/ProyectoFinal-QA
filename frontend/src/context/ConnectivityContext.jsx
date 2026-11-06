import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../api/client.js';
import { countPendingOrders, flushQueue, listPendingOrders } from '../lib/offlineQueue.js';
import { useAuth } from './AuthContext.jsx';

const ConnectivityContext = createContext(null);
const RETRY_INTERVAL_MS = 15000;

/**
 * Estado de conexión + cola offline.
 * Al volver internet (evento "online"), al abrir la app y cada 15 s, reenvía
 * los pedidos pendientes con su misma Idempotency-Key.
 */
export function ConnectivityProvider({ children }) {
  const { isAuthenticated } = useAuth();
  const [online, setOnline] = useState(() => navigator.onLine);
  const [pending, setPending] = useState([]);
  const [lastSync, setLastSync] = useState(null);
  const syncing = useRef(false);

  const refreshPending = useCallback(async () => {
    setPending(await listPendingOrders());
  }, []);

  const sync = useCallback(async () => {
    if (syncing.current || !isAuthenticated || !navigator.onLine) return null;
    if ((await countPendingOrders()) === 0) return null;

    syncing.current = true;
    try {
      const result = await flushQueue(async (items, key) => (await api.createOrder(items, key)).data);
      setLastSync({ ...result, at: new Date().toISOString() });
      if (result.sent.length > 0) {
        window.dispatchEvent(new CustomEvent('flashstore:orders-synced', { detail: result }));
      }
      return result;
    } finally {
      syncing.current = false;
      await refreshPending();
    }
  }, [isAuthenticated, refreshPending]);

  useEffect(() => {
    const goOnline = () => {
      setOnline(true);
      sync();
    };
    const goOffline = () => setOnline(false);

    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    window.addEventListener('flashstore:queue-changed', refreshPending);

    listPendingOrders().then(setPending);
    sync();
    const timer = setInterval(sync, RETRY_INTERVAL_MS);

    return () => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
      window.removeEventListener('flashstore:queue-changed', refreshPending);
      clearInterval(timer);
    };
  }, [sync, refreshPending]);

  const value = useMemo(
    () => ({ online, pending, pendingCount: pending.length, lastSync, sync, refreshPending }),
    [online, pending, lastSync, sync, refreshPending]
  );

  return <ConnectivityContext.Provider value={value}>{children}</ConnectivityContext.Provider>;
}

export function useConnectivity() {
  const context = useContext(ConnectivityContext);
  if (!context) throw new Error('useConnectivity debe usarse dentro de ConnectivityProvider');
  return context;
}
