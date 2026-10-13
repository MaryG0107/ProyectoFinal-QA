import { ORDER_STATUS } from '../lib/format.js';

export default function StatusBadge({ status }) {
  const info = ORDER_STATUS[status] ?? { label: status, tone: 'muted' };
  return <span className={`badge badge-${info.tone}`} data-testid="order-status">{info.label}</span>;
}
