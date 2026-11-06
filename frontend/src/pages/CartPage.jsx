import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client.js';
import { useCart } from '../context/CartContext.jsx';
import { useConnectivity } from '../context/ConnectivityContext.jsx';
import { formatMoney } from '../lib/format.js';
import { enqueueOrder, newIdempotencyKey } from '../lib/offlineQueue.js';

export default function CartPage() {
  const { cart, remove, clear } = useCart();
  const { online, refreshPending } = useConnectivity();
  const navigate = useNavigate();
  const [status, setStatus] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  const items = cart.items ?? [];

  async function queueOffline(idempotencyKey, payload) {
    await enqueueOrder({
      idempotencyKey,
      items: payload,
      summary: { total: cart.subtotal, lines: items.map((i) => `${i.quantity} × ${i.name}`) }
    });
    window.dispatchEvent(new Event('flashstore:queue-changed'));
    await refreshPending();
    await clear();
    setStatus({
      tone: 'warning',
      text: 'Sin conexión: su pedido quedó guardado en este dispositivo y se enviará automáticamente al volver internet.'
    });
  }

  async function handleCheckout() {
    if (items.length === 0) return;
    setSubmitting(true);
    setStatus(null);

    // La llave se genera UNA vez por intento de compra. Si la red falla a mitad del envío,
    // el reintento usa la misma llave y el servidor no duplica el pedido.
    const idempotencyKey = newIdempotencyKey();
    const payload = items.map(({ productId, quantity }) => ({ productId, quantity }));

    try {
      if (!online) {
        await queueOffline(idempotencyKey, payload);
        return;
      }
      const { data: order } = await api.createOrder(payload, idempotencyKey);
      await clear();
      navigate(`/pedidos/${order.id}`, { state: { justCreated: true } });
    } catch (err) {
      if (err.name === 'NetworkError') {
        await queueOffline(idempotencyKey, payload);
      } else if (err.status === 409) {
        setStatus({ tone: 'danger', text: 'Lo sentimos: uno de los productos se agotó mientras compraba.' });
      } else {
        setStatus({ tone: 'danger', text: err.message });
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section aria-labelledby="titulo-carrito">
      <h1 id="titulo-carrito">Mi carrito</h1>
      {cart.expiresInSeconds != null && items.length > 0 && (
        <p className="muted">El carrito se reserva por {Math.ceil(cart.expiresInSeconds / 60)} minuto(s) más.</p>
      )}

      {status && <div className={`alert alert-${status.tone}`} role="status" data-testid="checkout-status">{status.text}</div>}

      {items.length === 0 ? (
        <div className="empty card" data-testid="cart-empty">
          <p>Su carrito está vacío.</p>
          <Link to="/" className="btn">Ver catálogo</Link>
        </div>
      ) : (
        <div className="cart-layout">
          <ul className="cart-list" data-testid="cart-items">
            {items.map((item) => (
              <li key={item.productId} className="card cart-item" data-testid={`cart-item-${item.productId}`}>
                <div>
                  <p className="product-name">{item.name}</p>
                  <p className="muted">{item.quantity} × {formatMoney(item.unitPrice)}</p>
                </div>
                <div className="cart-item-side">
                  <strong>{formatMoney(item.lineTotal)}</strong>
                  <button
                    type="button"
                    className="btn btn-ghost btn-small"
                    disabled={!online}
                    onClick={() => remove(item.productId)}
                    aria-label={`Quitar ${item.name} del carrito`}
                    data-testid={`remove-${item.productId}`}
                  >
                    Quitar
                  </button>
                </div>
              </li>
            ))}
          </ul>

          <aside className="card summary" aria-label="Resumen de compra">
            <div className="summary-row">
              <span>Total</span>
              <strong data-testid="cart-total">{formatMoney(cart.subtotal)}</strong>
            </div>
            <p className="muted small">El precio final lo calcula el servidor con los precios vigentes.</p>
            <button
              type="button"
              className="btn btn-block"
              onClick={handleCheckout}
              disabled={submitting}
              data-testid="checkout-button"
            >
              {submitting ? 'Procesando…' : online ? 'Confirmar pedido' : 'Guardar pedido sin conexión'}
            </button>
          </aside>
        </div>
      )}
    </section>
  );
}
