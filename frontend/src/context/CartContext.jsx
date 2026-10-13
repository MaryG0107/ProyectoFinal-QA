import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api } from '../api/client.js';
import { useAuth } from './AuthContext.jsx';

const CartContext = createContext(null);
const LOCAL_COPY_KEY = 'flashstore.cart';

const emptyCart = { items: [], subtotal: 0, expiresInSeconds: null };

function readLocalCopy() {
  try {
    return JSON.parse(localStorage.getItem(LOCAL_COPY_KEY)) ?? emptyCart;
  } catch {
    return emptyCart;
  }
}

/**
 * Carrito: la fuente oficial es el carrito temporal de Redis (API /api/cart).
 * Se guarda una copia local para poder mostrarlo y comprar aun sin conexión.
 */
export function CartProvider({ children }) {
  const { isAuthenticated } = useAuth();
  const [cart, setCart] = useState(() => (isAuthenticated ? readLocalCopy() : emptyCart));
  const [error, setError] = useState(null);

  const store = useCallback((next) => {
    setCart(next);
    localStorage.setItem(LOCAL_COPY_KEY, JSON.stringify(next));
  }, []);

  const refresh = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const { data } = await api.cart();
      store(data);
      setError(null);
    } catch (err) {
      if (err.name === 'NetworkError') {
        setCart(readLocalCopy());
      } else {
        setError(err.message);
      }
    }
  }, [isAuthenticated, store]);

  useEffect(() => {
    if (!isAuthenticated) {
      localStorage.removeItem(LOCAL_COPY_KEY);
      return undefined;
    }

    let cancelled = false;
    api.cart()
      .then(({ data }) => {
        if (cancelled) return;
        store(data);
        setError(null);
      })
      .catch((err) => {
        if (cancelled) return;
        if (err.name === 'NetworkError') setCart(readLocalCopy());
        else setError(err.message);
      });

    return () => { cancelled = true; };
  }, [isAuthenticated, store]);

  const add = useCallback(async (productId, quantity = 1) => {
    const { data } = await api.addToCart(productId, quantity);
    store(data);
    return data;
  }, [store]);

  const remove = useCallback(async (productId) => {
    const { data } = await api.removeFromCart(productId);
    store(data);
  }, [store]);

  const clear = useCallback(async () => {
    try {
      await api.clearCart();
    } catch {
      // Si no hay conexión, el carrito de Redis expira solo por su TTL.
    }
    store(emptyCart);
  }, [store]);

  // Un pedido guardado sin conexión ya se envió: el carrito de Redis (que se quedó
  // con esos productos mientras no había internet) se vacía para no comprarlos dos veces.
  useEffect(() => {
    const onSynced = () => clear();
    window.addEventListener('flashstore:orders-synced', onSynced);
    return () => window.removeEventListener('flashstore:orders-synced', onSynced);
  }, [clear]);

  // Sin sesión el carrito siempre se muestra vacío.
  const visibleCart = isAuthenticated ? cart : emptyCart;

  const value = useMemo(() => ({
    cart: visibleCart,
    error,
    itemCount: visibleCart.items.reduce((sum, item) => sum + item.quantity, 0),
    refresh,
    add,
    remove,
    clear
  }), [visibleCart, error, refresh, add, remove, clear]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart() {
  const context = useContext(CartContext);
  if (!context) throw new Error('useCart debe usarse dentro de CartProvider');
  return context;
}
