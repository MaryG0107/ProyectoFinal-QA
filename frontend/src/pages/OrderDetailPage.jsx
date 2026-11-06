import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client.js';
import { useConnectivity } from '../context/ConnectivityContext.jsx';
import { formatDate, formatMoney, shortId } from '../lib/format.js';
import StatusBadge from '../components/StatusBadge.jsx';

const POLL_INTERVAL_MS = 2000;
const POLL_MAX_ATTEMPTS = 15;

export default function OrderDetailPage() {
  const { id } = useParams();
  const { online } = useConnectivity();
  const [order, setOrder] = useState(null);
  const [error, setError] = useState(null);
  const [cardNumber, setCardNumber] = useState('');
  const [paying, setPaying] = useState(false);
  const [payment, setPayment] = useState(null);
  const pollTimer = useRef(null);

  const load = useCallback(async () => {
    try {
      const { data } = await api.order(id);
      setOrder(data);
      setError(null);
      return data;
    } catch (err) {
      setError(err.status === 403 ? 'No tiene permiso para ver este pedido.' : err.message);
      return null;
    }
  }, [id]);

  useEffect(() => {
    let cancelled = false;
    api.order(id)
      .then(({ data }) => {
        if (cancelled) return;
        setOrder(data);
        setError(null);
      })
      .catch((err) => {
        if (!cancelled) setError(err.status === 403 ? 'No tiene permiso para ver este pedido.' : err.message);
      });

    return () => {
      cancelled = true;
      clearTimeout(pollTimer.current);
    };
  }, [id]);

  // El Worker actualiza el estado de forma asíncrona: se consulta cada 2 s hasta que cambie.
  const pollUntilProcessed = useCallback(() => {
    let attempt = 0;
    const tick = async () => {
      const current = await load();
      attempt += 1;
      if (current?.status === 'PENDING_PAYMENT' && attempt < POLL_MAX_ATTEMPTS) {
        pollTimer.current = setTimeout(tick, POLL_INTERVAL_MS);
      }
    };
    clearTimeout(pollTimer.current);
    pollTimer.current = setTimeout(tick, POLL_INTERVAL_MS);
  }, [load]);

  async function handlePay(event) {
    event.preventDefault();
    setPaying(true);
    setError(null);
    try {
      const { data } = await api.pay(id, cardNumber);
      setPayment(data);
      setCardNumber('');
      pollUntilProcessed();
    } catch (err) {
      setError(err.name === 'NetworkError' ? 'El pago requiere conexión a internet.' : err.message);
    } finally {
      setPaying(false);
    }
  }

  async function handleCancel() {
    try {
      await api.cancelOrder(id);
      pollUntilProcessed();
      await load();
    } catch (err) {
      setError(err.message);
    }
  }

  if (!order) {
    return (
      <section>
        {error ? <div className="alert alert-danger" role="alert">{error}</div> : <p className="muted">Cargando pedido…</p>}
        <Link to="/pedidos">← Volver a mis pedidos</Link>
      </section>
    );
  }

  const canPay = order.status === 'PENDING_PAYMENT' && !payment;

  return (
    <section aria-labelledby="titulo-pedido" data-testid="order-detail">
      <Link to="/pedidos">← Mis pedidos</Link>
      <div className="order-header">
        <h1 id="titulo-pedido">Pedido #{shortId(order.id)}</h1>
        <StatusBadge status={order.status} />
      </div>
      <p className="muted">Creado {formatDate(order.createdAt)}</p>

      {error && <div className="alert alert-danger" role="alert" data-testid="order-error">{error}</div>}

      <ul className="card line-list">
        {order.items.map((item) => (
          <li key={item.productId}>
            <span>{item.quantity} × {item.productName}</span>
            <span>{formatMoney(item.lineTotal)}</span>
          </li>
        ))}
        <li className="line-total">
          <span>Total</span>
          <strong data-testid="order-total">{formatMoney(order.total)}</strong>
        </li>
      </ul>

      {payment && (
        <div className={`alert alert-${payment.status === 'APPROVED' ? 'success' : 'danger'}`} role="status" data-testid="payment-result">
          Pago {payment.status === 'APPROVED' ? 'aprobado' : `rechazado: ${payment.reason}`} (tarjeta ****{payment.cardLast4}).
          {order.status === 'PENDING_PAYMENT' && ' Actualizando el estado del pedido…'}
        </div>
      )}

      {canPay && (
        <form className="card pay-form" onSubmit={handlePay}>
          <h2>Pagar</h2>
          <label className="field">
            <span>Número de tarjeta (simulada)</span>
            <input
              inputMode="numeric"
              autoComplete="cc-number"
              placeholder="4111 1111 1111 1111"
              value={cardNumber}
              maxLength={23}
              onChange={(event) => setCardNumber(event.target.value)}
              required
              data-testid="card-number"
            />
          </label>
          <div className="actions">
            <button type="submit" className="btn" disabled={paying || !online} data-testid="pay-button">
              {paying ? 'Procesando…' : `Pagar ${formatMoney(order.total)}`}
            </button>
            <button type="button" className="btn btn-ghost" onClick={handleCancel} disabled={!online} data-testid="cancel-order">
              Cancelar pedido
            </button>
          </div>
          {!online && <p className="muted small">El pago necesita conexión a internet.</p>}
        </form>
      )}
    </section>
  );
}
