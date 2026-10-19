import React, { createContext, useContext, useEffect, useState } from 'react';
import { AuthContextType, AuthUser, UserRole } from '../types/auth';
import { createDevJwt } from '../services/tokenService';
import {
  clearSession,
  getOidcConfig,
  getSavedSession,
  handleOidcCallback,
  initiateLogin,
  initiateLogout,
} from '../services/oidcService';

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const ROLE_PROFILES: Record<UserRole, AuthUser> = {
  ops: {
    role: 'ops',
    subject: 'ops-user-1',
    displayName: 'Operations Specialist (Ops)',
  },
  merchant: {
    role: 'merchant',
    subject: 'merch_acme',
    merchantId: 'merch_acme',
    displayName: 'Acme Store Admin (Merchant)',
  },
  risk: {
    role: 'risk',
    subject: 'risk-officer-1',
    displayName: 'Compliance Auditor (Risk)',
  },
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const oidcConfig = getOidcConfig();
  const [authMode, setAuthMode] = useState<'oidc' | 'dev'>('dev');
  const [devRole, setDevRoleState] = useState<UserRole>('ops');
  const [oidcUser, setOidcUser] = useState<AuthUser | null>(null);
  const [token, setToken] = useState<string>('');

  useEffect(() => {
    let isMounted = true;

    async function initAuth() {
      if (typeof window !== 'undefined' && window.location.search.includes('code=')) {
        try {
          const session = await handleOidcCallback();
          if (session && isMounted) {
            setAuthMode('oidc');
            setOidcUser(session.user);
            setToken(session.accessToken);
            return;
          }
        } catch (err) {
          console.error('OIDC callback processing failed:', err);
        }
      }

      const existingSession = getSavedSession();
      if (existingSession && isMounted) {
        setAuthMode('oidc');
        setOidcUser(existingSession.user);
        setToken(existingSession.accessToken);
        return;
      }

      // Default to dev mode
      const devUser = ROLE_PROFILES[devRole];
      createDevJwt(devRole, devUser.subject).then((jwt) => {
        if (isMounted) {
          setToken(jwt);
        }
      });
    }

    initAuth();

    return () => {
      isMounted = false;
    };
  }, []);

  // When devRole changes in dev mode, regenerate dev JWT
  useEffect(() => {
    if (authMode !== 'dev') return;
    let isCurrent = true;
    const user = ROLE_PROFILES[devRole];
    createDevJwt(devRole, user.subject).then((jwt) => {
      if (isCurrent) {
        setToken(jwt);
      }
    });
    return () => {
      isCurrent = false;
    };
  }, [devRole, authMode]);

  const setRole = (newRole: UserRole) => {
    setAuthMode('dev');
    setDevRoleState(newRole);
  };

  const login = () => {
    initiateLogin();
  };

  const logout = () => {
    clearSession();
    setAuthMode('dev');
    setOidcUser(null);
    initiateLogout();
  };

  const activeUser = authMode === 'oidc' && oidcUser ? oidcUser : ROLE_PROFILES[devRole];

  return (
    <AuthContext.Provider
      value={{
        user: activeUser,
        token,
        setRole,
        authMode,
        isAuthenticated: authMode === 'oidc' && Boolean(oidcUser),
        isOidcEnabled: oidcConfig.isConfigured,
        login,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
