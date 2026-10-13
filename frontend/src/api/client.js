// Cliente HTTP de la API de FlashStore.
// Las rutas son relativas (/api/...): en Docker, Nginx las envía a la API;
// en desarrollo lo hace el proxy de Vite.

const SESSION_KEY = 'flashstore.session';
const API_BASE = import.meta.env?.VITE_API_URL ?? '';

/** Error de red: no hubo respuesta del servidor (sin internet o API caída). */
export class NetworkError extends Error {
  constructor(cause) {
    super('No hay conexión con el servidor.');
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/** Error HTTP: el servidor respondió con un código 4xx o 5xx. */
export class ApiError extends Error {
  constructor(status, message, data) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.data = data;
  }
}

export function getSession() {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const session = JSON.parse(raw);
    if (!session?.token || new Date(session.expiresAt) <= new Date()) {
      localStorage.removeItem(SESSION_KEY);
      return null;
    }
    return session;
  } catch {
    return null;
  }
}

export function saveSession(session) {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session));
}

export function clearSession() {
  localStorage.removeItem(SESSION_KEY);
}

/**
 * Llama a la API y devuelve { data, status, headers }.
 * Lanza NetworkError si no hay conexión y ApiError si la respuesta no es 2xx.
 */
export async function apiFetch(path, { method = 'GET', body, headers = {}, auth = true, signal } = {}) {
  const finalHeaders = { Accept: 'application/json', ...headers };

  if (body !== undefined) {
    finalHeaders['Content-Type'] = 'application/json';
  }

  const session = auth ? getSession() : null;
  if (session) {
    finalHeaders.Authorization = `Bearer ${session.token}`;
  }

  let response;
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: finalHeaders,
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal
    });
  } catch (error) {
    if (error?.name === 'AbortError') throw error;
    throw new NetworkError(error);
  }

  const contentType = response.headers.get('content-type') || '';
  const data = contentType.includes('application/json') ? await response.json() : null;

  if (response.status === 401 && session) {
    clearSession();
    window.dispatchEvent(new CustomEvent('flashstore:session-expired'));
  }

  if (!response.ok) {
    const message = data?.error || data?.title || `Error ${response.status}`;
    throw new ApiError(response.status, message, data);
  }

  return { data, status: response.status, headers: response.headers };
}

export const api = {
  login: (email, password) =>
    apiFetch('/api/auth/login', { method: 'POST', body: { email, password }, auth: false }),
  register: (email, password, fullName) =>
    apiFetch('/api/auth/register', { method: 'POST', body: { email, password, fullName }, auth: false }),
  logout: () => apiFetch('/api/auth/logout', { method: 'POST' }),

  categories: () => apiFetch('/api/categories', { auth: false }),
  products: ({ search, categoryId } = {}) => {
    const params = new URLSearchParams();
    if (search) params.set('search', search);
    if (categoryId) params.set('categoryId', categoryId);
    const query = params.toString();
    return apiFetch(`/api/products${query ? `?${query}` : ''}`, { auth: false });
  },
  featured: () => apiFetch('/api/products/featured', { auth: false }),

  cart: () => apiFetch('/api/cart'),
  addToCart: (productId, quantity) =>
    apiFetch('/api/cart/items', { method: 'POST', body: { productId, quantity } }),
  removeFromCart: (productId) => apiFetch(`/api/cart/items/${productId}`, { method: 'DELETE' }),
  clearCart: () => apiFetch('/api/cart', { method: 'DELETE' }),

  createOrder: (items, idempotencyKey) =>
    apiFetch('/api/orders', {
      method: 'POST',
      body: { items },
      headers: { 'Idempotency-Key': idempotencyKey }
    }),
  orders: () => apiFetch('/api/orders'),
  order: (id) => apiFetch(`/api/orders/${id}`),
  cancelOrder: (id) => apiFetch(`/api/orders/${id}/cancel`, { method: 'POST' }),
  pay: (orderId, cardNumber) =>
    apiFetch('/api/payments', { method: 'POST', body: { orderId, cardNumber } }),

  notifications: () => apiFetch('/api/notifications')
};
