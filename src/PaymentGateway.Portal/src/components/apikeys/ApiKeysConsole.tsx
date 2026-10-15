import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  Key,
  Plus,
  RefreshCw,
  Search,
  CheckCircle2,
  Copy,
  Check,
  Shield,
  Activity,
  AlertOctagon,
  Clock,
  Sparkles,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  fetchMerchantApiKeys,
  createMerchantApiKey,
  revokeMerchantApiKey,
} from '../../services/apiClient';
import {
  MerchantApiKeyDto,
  MerchantApiKeyCreatedDto,
  CreateApiKeyRequest,
} from '../../types/api';
import { GenerateApiKeyModal } from './GenerateApiKeyModal';
import { KeyCreatedModal } from './KeyCreatedModal';
import { RevokeKeyModal } from './RevokeKeyModal';
import { TableSkeleton } from '../common/LoadingSkeleton';

export const ApiKeysConsole: React.FC = () => {
  const { user, token } = useAuth();
  const [keys, setKeys] = useState<MerchantApiKeyDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'revoked'>('all');
  const [copiedId, setCopiedId] = useState<string | null>(null);

  // Modal states
  const [isGenerateOpen, setIsGenerateOpen] = useState(false);
  const [createdKey, setCreatedKey] = useState<MerchantApiKeyCreatedDto | null>(null);
  const [keyToRevoke, setKeyToRevoke] = useState<MerchantApiKeyDto | null>(null);

  const merchantId = user.merchantId || user.subject || 'merch_acme';

  const loadKeys = useCallback(async () => {
    if (!token) {
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    setGlobalError(null);

    try {
      const data = await fetchMerchantApiKeys(token, merchantId);
      setKeys(data);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to fetch merchant API keys.');
      }
    } finally {
      setIsLoading(false);
    }
  }, [token, merchantId]);

  useEffect(() => {
    loadKeys();
  }, [loadKeys]);

  const showNotification = (msg: string) => {
    setSuccessNotice(msg);
    setTimeout(() => setSuccessNotice(null), 4000);
  };

  const handleGenerate = async (data: CreateApiKeyRequest) => {
    if (!token) return;
    const result = await createMerchantApiKey(token, merchantId, data);
    setCreatedKey(result);
    showNotification(`API Key '${result.name}' generated successfully.`);
    await loadKeys();
  };

  const handleRevoke = async (keyId: string) => {
    if (!token) return;
    await revokeMerchantApiKey(token, merchantId, keyId);
    showNotification('API key revoked successfully.');
    await loadKeys();
  };

  const handleCopyPrefix = async (id: string, text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedId(id);
      setTimeout(() => setCopiedId(null), 2000);
    } catch {
      // ignore
    }
  };

  // Metrics
  const totalCount = keys.length;
  const activeCount = useMemo(() => keys.filter((k) => !k.isRevoked).length, [keys]);
  const revokedCount = useMemo(() => keys.filter((k) => k.isRevoked).length, [keys]);

  // Filtered keys
  const filteredKeys = useMemo(() => {
    return keys.filter((k) => {
      if (statusFilter === 'active' && k.isRevoked) return false;
      if (statusFilter === 'revoked' && !k.isRevoked) return false;

      if (!searchQuery.trim()) return true;
      const q = searchQuery.toLowerCase().trim();
      return (
        k.name.toLowerCase().includes(q) ||
        k.prefix.toLowerCase().includes(q) ||
        k.scopes.some((s) => s.toLowerCase().includes(q))
      );
    });
  }, [keys, statusFilter, searchQuery]);

  return (
    <div className="space-y-6 animate-fade-in">
      {/* Top Banner / Notifications */}
      {successNotice && (
        <div className="p-4 rounded-xl bg-emerald-500/15 border border-emerald-500/30 text-emerald-300 text-sm flex items-center justify-between shadow-lg">
          <div className="flex items-center gap-3">
            <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
            <span>{successNotice}</span>
          </div>
        </div>
      )}

      {globalError && (
        <div className="p-4 rounded-xl bg-rose-500/15 border border-rose-500/30 text-rose-300 text-sm flex items-center gap-3 shadow-lg">
          <AlertOctagon className="w-5 h-5 text-rose-400 shrink-0" />
          <span>{globalError}</span>
        </div>
      )}

      {/* Header section */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 pb-4 border-b border-[#374151]">
        <div>
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400 shadow-sm">
              <Key className="w-5 h-5" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-white flex items-center gap-2">
                Merchant API Keys
                <span className="text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-2 py-0.5 rounded font-mono font-medium">
                  F-EDGE-06
                </span>
              </h1>
              <p className="text-xs text-gray-400">
                Generate and manage scoped API credentials for backend payment integrations
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-3 self-end sm:self-auto">
          <div className="hidden sm:flex items-center gap-2 px-3 py-1.5 rounded-lg bg-[#1f2937] border border-[#374151] text-xs text-gray-300 font-mono">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
            <span>Merchant: <strong className="text-white">{merchantId}</strong></span>
          </div>

          <button
            onClick={() => loadKeys()}
            disabled={isLoading}
            className="p-2 rounded-lg bg-[#1f2937] border border-[#374151] text-gray-300 hover:text-white hover:border-gray-500 transition"
            title="Refresh keys list"
            aria-label="Refresh keys"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          </button>

          <button
            onClick={() => setIsGenerateOpen(true)}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white shadow-sm transition"
          >
            <Plus className="w-4 h-4" />
            <span>Generate New Key</span>
          </button>
        </div>
      </div>

      {/* Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
              Total Keys
            </div>
            <div className="text-2xl font-bold text-white mt-1">{totalCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-400">
            <Key className="w-5 h-5" />
          </div>
        </div>

        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-emerald-400 uppercase tracking-wider">
              Active Keys
            </div>
            <div className="text-2xl font-bold text-white mt-1">{activeCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
            <Activity className="w-5 h-5" />
          </div>
        </div>

        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
              Revoked Keys
            </div>
            <div className="text-2xl font-bold text-gray-300 mt-1">{revokedCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center text-rose-400">
            <AlertOctagon className="w-5 h-5" />
          </div>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="flex flex-col sm:flex-row items-center justify-between gap-3 bg-[#111827] border border-[#374151] rounded-xl p-3">
        <div className="relative w-full sm:w-80">
          <Search className="w-4 h-4 text-gray-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search by key name, prefix or scope..."
            className="w-full bg-[#1f2937] border border-[#374151] rounded-lg pl-9 pr-3 py-1.5 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition"
          />
        </div>

        <div className="flex items-center gap-1.5 w-full sm:w-auto">
          {(['all', 'active', 'revoked'] as const).map((filter) => (
            <button
              key={filter}
              onClick={() => setStatusFilter(filter)}
              className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition capitalize flex-1 sm:flex-initial ${
                statusFilter === filter
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'bg-[#1f2937] text-gray-400 hover:text-white border border-[#374151]'
              }`}
            >
              {filter}
            </button>
          ))}
        </div>
      </div>

      {/* Main Table / Content */}
      <div className="bg-[#111827] border border-[#374151] rounded-2xl shadow-xl overflow-hidden">
        {isLoading ? (
          <div className="p-6">
            <TableSkeleton rows={4} />
          </div>
        ) : filteredKeys.length === 0 ? (
          <div className="p-12 text-center">
            <div className="w-12 h-12 rounded-2xl bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-500 mx-auto mb-3">
              <Key className="w-6 h-6" />
            </div>
            <h3 className="text-sm font-bold text-white mb-1">
              {keys.length === 0 ? 'No API Keys Generated Yet' : 'No Keys Match Your Filter'}
            </h3>
            <p className="text-xs text-gray-400 max-w-sm mx-auto mb-4">
              {keys.length === 0
                ? 'Generate an API key to securely authenticate backend API requests with scoped privileges.'
                : 'Try adjusting your search query or status filter.'}
            </p>
            {keys.length === 0 && (
              <button
                onClick={() => setIsGenerateOpen(true)}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white transition shadow-sm"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Generate First Key</span>
              </button>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[#1f2937]/50 border-b border-[#374151] text-[11px] font-bold uppercase tracking-wider text-gray-400">
                  <th className="py-3 px-4">Key Name</th>
                  <th className="py-3 px-4">Prefix ID</th>
                  <th className="py-3 px-4">Environment</th>
                  <th className="py-3 px-4">Scopes</th>
                  <th className="py-3 px-4">Created Date</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[#374151] text-xs">
                {filteredKeys.map((k) => {
                  const isLiveKey = k.prefix.startsWith('rpg_live_');
                  return (
                    <tr
                      key={k.id}
                      className="hover:bg-[#1f2937]/40 transition group"
                    >
                      <td className="py-3.5 px-4 font-semibold text-white">
                        <div className="flex items-center gap-2">
                          <Key className="w-3.5 h-3.5 text-gray-400 group-hover:text-blue-400 transition" />
                          <span>{k.name}</span>
                        </div>
                      </td>

                      <td className="py-3.5 px-4 font-mono text-gray-300">
                        <div className="flex items-center gap-2">
                          <span>{k.prefix}...</span>
                          <button
                            onClick={() => handleCopyPrefix(k.id, k.prefix)}
                            className="p-1 rounded hover:bg-gray-800 text-gray-400 hover:text-white transition"
                            title="Copy prefix"
                            aria-label={`Copy prefix ${k.prefix}`}
                          >
                            {copiedId === k.id ? (
                              <Check className="w-3 h-3 text-emerald-400" />
                            ) : (
                              <Copy className="w-3 h-3" />
                            )}
                          </button>
                        </div>
                      </td>

                      <td className="py-3.5 px-4">
                        <span
                          className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${
                            isLiveKey
                              ? 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30'
                              : 'bg-amber-500/15 text-amber-400 border-amber-500/30'
                          }`}
                        >
                          <span
                            className={`w-1.5 h-1.5 rounded-full ${
                              isLiveKey ? 'bg-emerald-400' : 'bg-amber-400'
                            }`}
                          />
                          {isLiveKey ? 'Live' : 'Test'}
                        </span>
                      </td>

                      <td className="py-3.5 px-4">
                        <div className="flex flex-wrap gap-1">
                          {k.scopes.map((scope) => (
                            <span
                              key={scope}
                              className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-blue-500/10 border border-blue-500/20 text-blue-300 font-mono text-[10px]"
                            >
                              <Shield className="w-2.5 h-2.5 text-blue-400" />
                              {scope}
                            </span>
                          ))}
                        </div>
                      </td>

                      <td className="py-3.5 px-4 text-gray-400">
                        <div className="flex items-center gap-1.5">
                          <Clock className="w-3 h-3 text-gray-500" />
                          <span>{new Date(k.createdAt).toLocaleDateString()}</span>
                        </div>
                      </td>

                      <td className="py-3.5 px-4">
                        {k.isRevoked ? (
                          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30">
                            Revoked
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
                            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
                            Active
                          </span>
                        )}
                      </td>

                      <td className="py-3.5 px-4 text-right">
                        {k.isRevoked ? (
                          <div className="flex flex-col items-end">
                            <span className="text-[11px] text-gray-500 italic">
                              {k.revokedAt ? `Revoked ${new Date(k.revokedAt).toLocaleDateString()}` : 'Revoked'}
                            </span>
                            {k.revokedBy && (
                              <span className="text-[10px] text-gray-400 font-mono">
                                by {k.revokedBy}
                              </span>
                            )}
                          </div>
                        ) : (
                          <button
                            onClick={() => setKeyToRevoke(k)}
                            className="px-2.5 py-1 rounded-lg text-xs font-semibold text-rose-400 hover:text-rose-300 hover:bg-rose-500/15 border border-rose-500/30 transition"
                          >
                            Revoke
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Security Guidance Note */}
      <div className="p-4 rounded-xl bg-[#111827] border border-[#374151] flex items-start gap-3 text-xs text-gray-400">
        <Sparkles className="w-4 h-4 text-blue-400 shrink-0 mt-0.5" />
        <div>
          <strong className="text-gray-200">Security Recommendation:</strong> Always use test keys (`rpg_test_...`) in staging environments and rotate production keys periodically. Never commit raw keys to version control. Pass keys via the <code className="bg-[#1f2937] px-1 py-0.5 rounded text-blue-300 font-mono">X-Api-Key</code> request header.
        </div>
      </div>

      {/* Modals */}
      <GenerateApiKeyModal
        isOpen={isGenerateOpen}
        onClose={() => setIsGenerateOpen(false)}
        onSubmit={handleGenerate}
      />

      <KeyCreatedModal
        createdKey={createdKey}
        onClose={() => setCreatedKey(null)}
      />

      <RevokeKeyModal
        keyToRevoke={keyToRevoke}
        onClose={() => setKeyToRevoke(null)}
        onConfirm={handleRevoke}
      />
    </div>
  );
};
