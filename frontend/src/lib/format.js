const currency = new Intl.NumberFormat('es-GT', { style: 'currency', currency: 'GTQ' });
const dateTime = new Intl.DateTimeFormat('es-GT', { dateStyle: 'medium', timeStyle: 'short' });

export const formatMoney = (value) => currency.format(Number(value ?? 0));

export const formatDate = (value) => (value ? dateTime.format(new Date(value)) : '');

export const shortId = (id) => String(id ?? '').replaceAll('-', '').slice(0, 8).toUpperCase();

export const ORDER_STATUS = {
  PENDING_PAYMENT: { label: 'Pendiente de pago', tone: 'warning' },
  PAID: { label: 'Pagado', tone: 'success' },
  PAYMENT_REJECTED: { label: 'Pago rechazado', tone: 'danger' },
  CANCELLED: { label: 'Cancelado', tone: 'muted' }
};
