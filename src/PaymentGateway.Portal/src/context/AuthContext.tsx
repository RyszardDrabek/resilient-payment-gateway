import React, { createContext, useContext, useEffect, useState } from 'react';
import { AuthContextType, AuthUser, UserRole } from '../types/auth';
import { createDevJwt } from '../services/tokenService';

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
  const [role, setRoleState] = useState<UserRole>('ops');
  const [token, setToken] = useState<string>('');

  useEffect(() => {
    let isCurrent = true;
    const user = ROLE_PROFILES[role];
    createDevJwt(role, user.subject).then((jwt) => {
      if (isCurrent) {
        setToken(jwt);
      }
    });
    return () => {
      isCurrent = false;
    };
  }, [role]);

  const setRole = (newRole: UserRole) => {
    setRoleState(newRole);
  };

  const user = ROLE_PROFILES[role];

  return (
    <AuthContext.Provider value={{ user, token, setRole }}>
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
