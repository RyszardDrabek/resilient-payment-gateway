import React from 'react';
import { ShieldCheck, UserCheck, ChevronDown, LogIn, LogOut, KeyRound } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { UserRole } from '../../types/auth';

export const Header: React.FC = () => {
  const { user, setRole, authMode, isAuthenticated, isOidcEnabled, login, logout } = useAuth();

  const handleRoleChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setRole(e.target.value as UserRole);
  };

  return (
    <header className="h-14 bg-[#111827] border-b border-[#374151] sticky top-0 z-30 px-4 lg:px-6 flex items-center justify-between">
      <div className="flex items-center gap-3">
        <div className="w-8 h-8 rounded-lg bg-blue-600 flex items-center justify-center font-bold text-white shadow-md">
          RPG
        </div>
        <div className="flex items-center gap-2">
          <span className="font-bold text-base tracking-tight text-white">Payment Gateway Portal</span>
          {authMode === 'oidc' && isAuthenticated ? (
            <span className="px-2 py-0.5 rounded text-[10px] font-mono font-bold bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 flex items-center gap-1">
              <KeyRound className="w-3 h-3" />
              KEYCLOAK SSO
            </span>
          ) : (
            <span className="px-2 py-0.5 rounded text-[10px] font-mono font-bold bg-blue-500/20 text-blue-400 border border-blue-500/30">
              DEV
            </span>
          )}
        </div>
      </div>

      <div className="flex items-center gap-3">
        {/* Role Indicator / Switcher */}
        <div className="flex items-center gap-2 bg-[#1f2937] border border-[#374151] rounded-lg px-2.5 py-1 text-xs">
          <ShieldCheck className="w-4 h-4 text-blue-400" />
          <span className="text-gray-400 font-medium">Role:</span>
          {authMode === 'oidc' && isAuthenticated ? (
            <span className="text-white font-semibold capitalize px-1">{user.role}</span>
          ) : (
            <div className="relative">
              <select
                value={user.role}
                onChange={handleRoleChange}
                className="bg-transparent text-white font-semibold cursor-pointer focus:outline-none pr-5 appearance-none"
              >
                <option value="ops" className="bg-[#111827] text-white">Ops Specialist (Authorized)</option>
                <option value="merchant" className="bg-[#111827] text-white">Merchant Admin (Restricted)</option>
                <option value="risk" className="bg-[#111827] text-white">Compliance Risk (Authorized)</option>
              </select>
              <ChevronDown className="w-3.5 h-3.5 text-gray-400 pointer-events-none absolute right-0 top-1/2 -translate-y-1/2" />
            </div>
          )}
        </div>

        {/* User Identity */}
        <div className="hidden sm:flex items-center gap-2 text-xs text-gray-300">
          <div className="w-7 h-7 rounded-full bg-[#1f2937] border border-[#374151] flex items-center justify-center text-blue-400">
            <UserCheck className="w-3.5 h-3.5" />
          </div>
          <span className="font-mono text-gray-300 font-medium truncate max-w-[140px]">{user.displayName || user.subject}</span>
        </div>

        {/* SSO Action Button */}
        {isOidcEnabled && (
          authMode === 'oidc' && isAuthenticated ? (
            <button
              onClick={logout}
              title="Sign Out from Keycloak SSO"
              className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium text-gray-300 hover:text-white bg-[#1f2937] hover:bg-[#374151] border border-[#374151] rounded-lg transition-colors"
            >
              <LogOut className="w-3.5 h-3.5 text-red-400" />
              <span className="hidden md:inline">Sign Out</span>
            </button>
          ) : (
            <button
              onClick={login}
              title="Sign In with Keycloak SSO"
              className="flex items-center gap-1.5 px-2.5 py-1 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-500 rounded-lg shadow transition-colors"
            >
              <LogIn className="w-3.5 h-3.5" />
              <span>SSO Login</span>
            </button>
          )
        )}
      </div>
    </header>
  );
};
