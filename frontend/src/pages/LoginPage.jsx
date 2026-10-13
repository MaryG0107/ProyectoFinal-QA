import { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext.jsx';

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email.trim(), password);
      navigate(location.state?.from ?? '/', { replace: true });
    } catch (err) {
      if (err.name === 'NetworkError') setError('Sin conexión: el inicio de sesión requiere internet.');
      else if (err.status === 429) setError('Demasiados intentos. Espere un minuto e intente de nuevo.');
      else setError('Correo o contraseña incorrectos.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="auth" aria-labelledby="titulo-login">
      <form className="card auth-card" onSubmit={handleSubmit} noValidate>
        <h1 id="titulo-login">Iniciar sesión</h1>
        <p className="muted">Usuarios de prueba: <code>cliente1@flashstore.test</code> · <code>Test1234!</code></p>

        <label className="field">
          <span>Correo electrónico</span>
          <input
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            data-testid="login-email"
          />
        </label>

        <label className="field">
          <span>Contraseña</span>
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            data-testid="login-password"
          />
        </label>

        {error && <div className="alert alert-danger" role="alert" data-testid="login-error">{error}</div>}

        <button type="submit" className="btn btn-block" disabled={submitting} data-testid="login-submit">
          {submitting ? 'Ingresando…' : 'Ingresar'}
        </button>
      </form>
    </section>
  );
}
