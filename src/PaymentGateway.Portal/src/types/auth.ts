export type UserRole = 'ops' | 'merchant' | 'risk';

export interface AuthUser {
  role: UserRole;
  subject: string;
  displayName: string;
  merchantId?: string;
  email?: string;
}

export interface AuthContextType {
  user: AuthUser;
  token: string;
  setRole: (role: UserRole) => void;
  authMode: 'oidc' | 'dev';
  isAuthenticated: boolean;
  isOidcEnabled: boolean;
  login: () => void;
  logout: () => void;
}
