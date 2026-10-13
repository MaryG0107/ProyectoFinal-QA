import { useEffect, useState } from 'react';
import { api } from '../api/client.js';
import { formatDate } from '../lib/format.js';

export default function NotificationsPage() {
  const [notifications, setNotifications] = useState([]);
  const [error, setError] = useState(null);

  useEffect(() => {
    const load = () => api.notifications()
      .then(({ data }) => { setNotifications(data ?? []); setError(null); })
      .catch((err) => setError(err.name === 'NetworkError' ? 'Sin conexión.' : err.message));

    load();
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  }, []);

  return (
    <section aria-labelledby="titulo-notificaciones">
      <h1 id="titulo-notificaciones">Notificaciones</h1>
      <p className="muted">Las genera el Worker al procesar los eventos de RabbitMQ.</p>
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {!error && notifications.length === 0 && <p className="empty" data-testid="notifications-empty">Sin notificaciones.</p>}
      <ul className="notification-list" data-testid="notifications">
        {notifications.map((item) => (
          <li key={item.id} className="card notification">
            <span className="badge badge-muted">{item.eventType}</span>
            <p>{item.message}</p>
            <span className="muted small">{formatDate(item.createdAt)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
