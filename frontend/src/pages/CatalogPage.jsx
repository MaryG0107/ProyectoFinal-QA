import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client.js';
import { useAuth } from '../context/AuthContext.jsx';
import { useCart } from '../context/CartContext.jsx';
import { useConnectivity } from '../context/ConnectivityContext.jsx';
import { formatMoney } from '../lib/format.js';

function StockBadge({ stock }) {
  if (stock <= 0) return <span className="badge badge-danger">Agotado</span>;
  if (stock <= 5) return <span className="badge badge-warning">¡Últimas {stock} unidades!</span>;
  return <span className="badge badge-muted">{stock} disponibles</span>;
}

function ProductCard({ product, onAdd, busy }) {
  return (
    <article className={`card product ${product.isFeatured ? 'is-featured' : ''}`} data-testid={`product-${product.id}`}>
      {product.isFeatured && <span className="ribbon">Destacado</span>}
      <p className="product-category">{product.categoryName}</p>
      <h3 className="product-name">{product.name}</h3>
      <p className="product-description">{product.description}</p>
      <div className="product-footer">
        <span className="price" data-testid={`price-${product.id}`}>{formatMoney(product.price)}</span>
        <StockBadge stock={product.stock} />
      </div>
      <button
        type="button"
        className="btn btn-block"
        disabled={product.stock <= 0 || busy}
        onClick={() => onAdd(product)}
        data-testid={`add-${product.id}`}
        aria-label={`Agregar ${product.name} al carrito`}
      >
        {busy ? 'Agregando…' : 'Agregar al carrito'}
      </button>
    </article>
  );
}

export default function CatalogPage() {
  const { isAuthenticated } = useAuth();
  const { add } = useCart();
  const { online } = useConnectivity();
  const navigate = useNavigate();

  const [categories, setCategories] = useState([]);
  const [search, setSearch] = useState('');
  const [term, setTerm] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [result, setResult] = useState({ key: null, products: [], cacheStatus: '', error: null });
  const [notice, setNotice] = useState(null);
  const [busyId, setBusyId] = useState(null);

  useEffect(() => {
    api.categories().then(({ data }) => setCategories(data ?? [])).catch(() => setCategories([]));
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => setTerm(search.trim()), 300);
    return () => clearTimeout(timer);
  }, [search]);

  const queryKey = `${term}|${categoryId}`;

  useEffect(() => {
    let cancelled = false;
    api.products({ search: term, categoryId })
      .then(({ data, headers }) => {
        if (!cancelled) setResult({ key: queryKey, products: data ?? [], cacheStatus: headers.get('X-Cache') ?? '', error: null });
      })
      .catch((err) => {
        if (cancelled) return;
        setResult({
          key: queryKey,
          products: [],
          cacheStatus: '',
          error: err.name === 'NetworkError'
            ? 'No se pudo cargar el catálogo y no hay una copia guardada todavía.'
            : err.message
        });
      });
    return () => { cancelled = true; };
  }, [term, categoryId, queryKey]);

  const loading = result.key !== queryKey;
  const { products, cacheStatus, error } = result;

  async function handleAdd(product) {
    if (!isAuthenticated) {
      navigate('/login', { state: { from: '/' } });
      return;
    }
    if (!online) {
      setNotice({ tone: 'warning', text: 'Sin conexión: para agregar productos necesita internet. Puede comprar lo que ya está en su carrito.' });
      return;
    }
    setBusyId(product.id);
    try {
      await add(product.id, 1);
      setNotice({ tone: 'success', text: `${product.name} se agregó al carrito.` });
    } catch (err) {
      setNotice({ tone: 'danger', text: err.message });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section aria-labelledby="titulo-catalogo">
      <div className="hero">
        <div>
          <p className="eyebrow">Campaña flash</p>
          <h1 id="titulo-catalogo">Ofertas que se agotan en segundos</h1>
          <p className="muted">Precios y stock en tiempo real. Si se va el internet, su pedido queda guardado y se envía solo.</p>
        </div>
      </div>

      <form className="filters" role="search" onSubmit={(event) => event.preventDefault()}>
        <label className="field">
          <span>Buscar</span>
          <input
            type="search"
            value={search}
            maxLength={100}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Consola, laptop, audífonos…"
            data-testid="search-input"
          />
        </label>
        <label className="field">
          <span>Categoría</span>
          <select value={categoryId} onChange={(event) => setCategoryId(event.target.value)} data-testid="category-select">
            <option value="">Todas</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>{category.name}</option>
            ))}
          </select>
        </label>
        {cacheStatus && (
          <span className="cache-indicator" title="Respuesta del cache Redis" data-testid="cache-status">
            Cache: {cacheStatus}
          </span>
        )}
      </form>

      {notice && (
        <div className={`alert alert-${notice.tone}`} role="status" data-testid="catalog-notice">
          {notice.text}
        </div>
      )}

      {loading && <p className="muted" aria-live="polite">Cargando catálogo…</p>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      {!loading && !error && products.length === 0 && (
        <p className="empty" data-testid="no-results">No encontramos productos para “{term}”.</p>
      )}

      <div className="grid" data-testid="product-grid">
        {products.map((product) => (
          <ProductCard key={product.id} product={product} onAdd={handleAdd} busy={busyId === product.id} />
        ))}
      </div>
    </section>
  );
}
