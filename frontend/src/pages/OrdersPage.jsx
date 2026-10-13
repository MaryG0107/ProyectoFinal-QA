import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client.js';
import { useConnectivity } from '../context/ConnectivityContext.jsx';
import StatusBadge from '../components/StatusBadge.jsx';
import { formatDate, formatMoney, shortId } from '../lib/format.js';

export default function OrdersPage() {
  const { pending, online } = useConnectivity();
  const [orders, setOrders] = useState([]);
  const [error, setError] = useState(null);

  const load = useCallback(() => {
    api.orders()
      .then(({ data }) => { setOrders(data ?? []); setError(null); })
      .catch((err) => setError(err.name === 'NetworkError' ? 'Sin conexión: no se pueden consultar los pedidos.' : err.message));
  }, []);

  useEffect(() => {
    load();
    window.addEventListener('flashstore:orders-synced', load);
    return () => window.removeEventListener('flashstore:orders-synced', load);
  }, [load, online]);

  return (
    <section aria-labelledby="titulo-pedidos">
      <h1 id="titulo-pedidos">Mis pedidos</h1>

      {pending.length > 0 && (
        <div className="card pending-list" data-testid="pending-orders">
          <h2>Pendientes de envío (sin conexión)</h2>
          <ul>
            {pending.map((entry) => (
              <li key={entry.idempotencyKey}>
                <span>{entry.summary?.lines?.join(', ') ?? `${entry.items.length} producto(s)`}</span>
                <span className="muted small">
                  Guardado {formatDate(entry.createdAt)} · intentos: {entry.attempts}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      {!error && orders.length === 0 && pending.length === 0 && (
        <p className="empty" data-testid="orders-empty">Todavía no tiene pedidos.</p>
      )}

      <div className="table-wrap">
        {orders.length > 0 && (
          <table className="table" data-testid="orders-table">
            <thead>
              <tr>
                <th scope="col">Pedido</th>
                <th scope="col">Fecha</th>
                <th scope="col">Productos</th>
                <th scope="col">Total</th>
                <th scope="col">Estado</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.id}>
                  <td><Link to={`/pedidos/${order.id}`}>#{shortId(order.id)}</Link></td>
                  <td>{formatDate(order.createdAt)}</td>
                  <td>{order.itemCount}</td>
                  <td>{formatMoney(order.total)}</td>
                  <td><StatusBadge status={order.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </section>
  );
}
