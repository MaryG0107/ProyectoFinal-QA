import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, clearSession, getSession, saveSession } from '../api/client.js';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [session, setSession] = useState(() => getSession());

  useEffect(() => {
    const onExpired = () => setSession(null);
    window.addEventListener('flashstore:session-expired', onExpired);
    return () => window.removeEventListener('flashstore:session-expired', onExpired);
  }, []);

  const login = useCallback(async (email, password) => {
    const { data } = await api.login(email, password);
    const newSession = { token: data.token, expiresAt: data.expiresAt, user: data.user };
    saveSession(newSession);
    setSession(newSession);
    return newSession;
  }, []);

  const logout = useCallback(async () => {
    try {
      await api.logout();
    } catch {
      // Sin conexión o sesión ya vencida: igual se cierra localmente.
    }
    clearSession();
    setSession(null);
  }, []);

  const value = useMemo(
    () => ({ session, user: session?.user ?? null, isAuthenticated: Boolean(session), login, logout }),
    [session, login, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth debe usarse dentro de AuthProvider');
  return context;
}
