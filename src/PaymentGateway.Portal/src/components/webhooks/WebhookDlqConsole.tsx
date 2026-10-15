import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  AlertTriangle,
  RotateCw,
  Search,
  CheckCircle2,
  Copy,
  Check,
  RefreshCw,
  Clock,
  Globe,
  Tag,
  ExternalLink,
  ShieldCheck,
  AlertOctagon,
  Sparkles,
  Radio,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  fetchMerchantWebhookDlq,
  replayMerchantWebhookDlqEntry,
} from '../../services/apiClient';
import { MerchantWebhookDeadLetterDto } from '../../types/api';
import { DlqDetailsModal } from './DlqDetailsModal';
import { TableSkeleton } from '../common/LoadingSkeleton';

export const WebhookDlqConsole: React.FC = () => {
  const { user, token } = useAuth();
  const [entries, setEntries] = useState<MerchantWebhookDeadLetterDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'exhausted' | 'replayed'>('all');
  const [copiedId, setCopiedId] = useState<string | null>(null);
  const [replayingIds, setReplayingIds] = useState<Set<string>>(new Set());

  // Modal
  const [selectedEntry, setSelectedEntry] = useState<MerchantWebhookDeadLetterDto | null>(null);

  const merchantId = user.merchantId || user.subject || 'merch_acme';

  const loadEntries = useCallback(async () => {
    if (!token) {
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    setGlobalError(null);

    try {
      const data = await fetchMerchantWebhookDlq(
        token,
        merchantId,
        statusFilter === 'all' ? undefined : statusFilter
      );
      setEntries(data);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to load webhook dead-letter records.');
      }
    } finally {
      setIsLoading(false);
    }
  }, [token, merchantId, statusFilter]);

  useEffect(() => {
    loadEntries();
  }, [loadEntries]);

  const showNotification = (msg: string) => {
    setSuccessNotice(msg);
    setTimeout(() => setSuccessNotice(null), 4000);
  };

  const handleReplay = async (entryId: string) => {
    if (!token) return;
    setReplayingIds((prev) => new Set(prev).add(entryId));
    setGlobalError(null);

    try {
      const result = await replayMerchantWebhookDlqEntry(token, merchantId, entryId);
      if (result.success) {
        showNotification(result.message || 'Webhook replayed successfully.');

        // Update entry state locally and update modal if open
        setEntries((prev) =>
          prev.map((e) =>
            e.id === entryId
              ? { ...e, status: 'replayed', replayedAt: result.replayedAt || new Date().toISOString() }
              : e
          )
        );

        if (selectedEntry && selectedEntry.id === entryId) {
          setSelectedEntry((prev) =>
            prev
              ? { ...prev, status: 'replayed', replayedAt: result.replayedAt || new Date().toISOString() }
              : null
          );
        }
      } else {
        setGlobalError(result.message || 'Replay attempt failed.');
        if (result.statusCode) {
          setEntries((prev) =>
            prev.map((e) =>
              e.id === entryId
                ? { ...e, lastStatusCode: result.statusCode, lastErrorMessage: result.message }
                : e
            )
          );
          if (selectedEntry && selectedEntry.id === entryId) {
            setSelectedEntry((prev) =>
              prev
                ? { ...prev, lastStatusCode: result.statusCode, lastErrorMessage: result.message }
                : null
            );
          }
        }
      }
    } catch (err: unknown) {
      if (err instanceof Error) {
        setGlobalError(err.message);
      } else {
        setGlobalError('Failed to replay webhook.');
      }
    } finally {
      setReplayingIds((prev) => {
        const next = new Set(prev);
        next.delete(entryId);
        return next;
      });
    }
  };

  const handleCopy = async (id: string, text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedId(id);
      setTimeout(() => setCopiedId(null), 2000);
    } catch {
      // ignore
    }
  };

  // Metrics
  const totalCount = entries.length;
  const exhaustedCount = useMemo(
    () => entries.filter((e) => e.status.toLowerCase() === 'exhausted').length,
    [entries]
  );
  const replayedCount = useMemo(
    () => entries.filter((e) => e.status.toLowerCase() === 'replayed').length,
    [entries]
  );

  // Search filter
  const filteredEntries = useMemo(() => {
    if (!searchQuery.trim()) return entries;
    const q = searchQuery.toLowerCase().trim();
    return entries.filter(
      (e) =>
        e.eventId.toLowerCase().includes(q) ||
        e.destinationUrl.toLowerCase().includes(q) ||
        e.paymentId.toLowerCase().includes(q) ||
        e.lastErrorMessage?.toLowerCase().includes(q)
    );
  }, [entries, searchQuery]);

  return (
    <div className="space-y-6 animate-fade-in">
      {/* Top Notifications */}
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
            <div className="w-10 h-10 rounded-xl bg-rose-500/20 border border-rose-500/30 flex items-center justify-center text-rose-400 shadow-sm">
              <AlertTriangle className="w-5 h-5" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-white flex items-center gap-2">
                Webhook Dead-Letter Queue (DLQ)
                <span className="text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-2 py-0.5 rounded font-mono font-medium">
                  F-EDGE-07
                </span>
              </h1>
              <p className="text-xs text-gray-400">
                Inspect delivery failures, view HTTP error diagnostics, and trigger manual re-dispatch
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
            onClick={() => loadEntries()}
            disabled={isLoading}
            className="p-2 rounded-lg bg-[#1f2937] border border-[#374151] text-gray-300 hover:text-white hover:border-gray-500 transition"
            title="Refresh dead-letter records"
            aria-label="Refresh DLQ"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {/* Metrics Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-gray-400 uppercase tracking-wider">
              Total DLQ Entries
            </div>
            <div className="text-2xl font-bold text-white mt-1">{totalCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-400">
            <Radio className="w-5 h-5" />
          </div>
        </div>

        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-rose-400 uppercase tracking-wider">
              Exhausted Failures
            </div>
            <div className="text-2xl font-bold text-rose-300 mt-1">{exhaustedCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center text-rose-400">
            <AlertTriangle className="w-5 h-5" />
          </div>
        </div>

        <div className="bg-[#111827] border border-[#374151] rounded-xl p-4 shadow-sm flex items-center justify-between">
          <div>
            <div className="text-xs font-semibold text-emerald-400 uppercase tracking-wider">
              Replayed & Recovered
            </div>
            <div className="text-2xl font-bold text-emerald-300 mt-1">{replayedCount}</div>
          </div>
          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
            <ShieldCheck className="w-5 h-5" />
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
            placeholder="Search by event ID, URL, or error..."
            className="w-full bg-[#1f2937] border border-[#374151] rounded-lg pl-9 pr-3 py-1.5 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition"
          />
        </div>

        <div className="flex items-center gap-1.5 w-full sm:w-auto">
          {(['all', 'exhausted', 'replayed'] as const).map((filter) => (
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

      {/* Main Table Content */}
      <div className="bg-[#111827] border border-[#374151] rounded-2xl shadow-xl overflow-hidden">
        {isLoading ? (
          <div className="p-6">
            <TableSkeleton rows={4} />
          </div>
        ) : filteredEntries.length === 0 ? (
          <div className="p-12 text-center">
            <div className="w-12 h-12 rounded-2xl bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-500 mx-auto mb-3">
              <ShieldCheck className="w-6 h-6 text-emerald-400" />
            </div>
            <h3 className="text-sm font-bold text-white mb-1">
              {entries.length === 0 ? 'No Failed Webhooks in DLQ' : 'No Records Match Your Filter'}
            </h3>
            <p className="text-xs text-gray-400 max-w-sm mx-auto">
              {entries.length === 0
                ? 'All outbound merchant webhook notifications were dispatched successfully or none have failed retry limits.'
                : 'Try adjusting your search query or status filter.'}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-[#1f2937]/50 border-b border-[#374151] text-[11px] font-bold uppercase tracking-wider text-gray-400">
                  <th className="py-3 px-4">Failed Timestamp</th>
                  <th className="py-3 px-4">Event ID</th>
                  <th className="py-3 px-4">Destination URL</th>
                  <th className="py-3 px-4">HTTP Status / Error</th>
                  <th className="py-3 px-4">Attempts</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-[#374151] text-xs">
                {filteredEntries.map((e) => {
                  const isExhausted = e.status.toLowerCase() === 'exhausted';
                  const isCurrentReplaying = replayingIds.has(e.id);

                  return (
                    <tr
                      key={e.id}
                      className="hover:bg-[#1f2937]/40 transition group"
                    >
                      <td className="py-3.5 px-4 text-gray-400 whitespace-nowrap">
                        <div className="flex items-center gap-1.5">
                          <Clock className="w-3.5 h-3.5 text-gray-500" />
                          <span>{new Date(e.failedAt).toLocaleString()}</span>
                        </div>
                      </td>

                      <td className="py-3.5 px-4 font-mono text-gray-200">
                        <div className="flex items-center gap-2">
                          <Tag className="w-3.5 h-3.5 text-blue-400 shrink-0" />
                          <span className="font-medium">{e.eventId}</span>
                          <button
                            onClick={() => handleCopy(e.id, e.eventId)}
                            className="p-1 rounded hover:bg-gray-800 text-gray-400 hover:text-white transition"
                            title="Copy Event ID"
                            aria-label={`Copy Event ID ${e.eventId}`}
                          >
                            {copiedId === e.id ? (
                              <Check className="w-3 h-3 text-emerald-400" />
                            ) : (
                              <Copy className="w-3 h-3" />
                            )}
                          </button>
                        </div>
                      </td>

                      <td className="py-3.5 px-4 font-mono text-gray-300 max-w-xs truncate" title={e.destinationUrl}>
                        <div className="flex items-center gap-1.5">
                          <Globe className="w-3.5 h-3.5 text-gray-500 shrink-0" />
                          <span className="truncate">{e.destinationUrl}</span>
                        </div>
                      </td>

                      <td className="py-3.5 px-4">
                        <div className="flex items-center gap-2">
                          {e.lastStatusCode && (
                            <span className="px-1.5 py-0.5 rounded text-[10px] font-mono font-bold bg-rose-500/15 border border-rose-500/30 text-rose-300">
                              HTTP {e.lastStatusCode}
                            </span>
                          )}
                          <span className="text-gray-400 truncate max-w-xs" title={e.lastErrorMessage}>
                            {e.lastErrorMessage || 'Exhausted retries'}
                          </span>
                        </div>
                      </td>

                      <td className="py-3.5 px-4 text-gray-300 font-medium">
                        <div>{e.deliveryAttempts}x</div>
                        {(e.replayAttemptCount !== undefined && e.replayAttemptCount > 0) && (
                          <div className="text-[10px] text-blue-400 font-normal">
                            {e.replayAttemptCount} replay{e.replayAttemptCount > 1 ? 's' : ''}
                          </div>
                        )}
                      </td>

                      <td className="py-3.5 px-4">
                        {isExhausted ? (
                          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30">
                            <span className="w-1.5 h-1.5 rounded-full bg-rose-400" />
                            Exhausted
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30">
                            <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
                            Replayed
                          </span>
                        )}
                      </td>

                      <td className="py-3.5 px-4 text-right whitespace-nowrap">
                        <div className="flex items-center justify-end gap-2">
                          <button
                            onClick={() => setSelectedEntry(e)}
                            className="px-2.5 py-1 rounded-lg text-xs font-semibold text-gray-300 hover:text-white bg-[#1f2937] hover:bg-[#374151] border border-[#374151] transition flex items-center gap-1"
                          >
                            <ExternalLink className="w-3 h-3" />
                            <span>Details</span>
                          </button>

                          {isExhausted ? (
                            <button
                              onClick={() => handleReplay(e.id)}
                              disabled={isCurrentReplaying}
                              className="px-3 py-1 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white shadow-sm transition flex items-center gap-1.5 disabled:opacity-50"
                            >
                              <RotateCw className={`w-3 h-3 ${isCurrentReplaying ? 'animate-spin' : ''}`} />
                              <span>{isCurrentReplaying ? 'Replaying...' : 'Replay'}</span>
                            </button>
                          ) : (
                            <span className="text-[11px] text-gray-500 italic px-2">
                              {e.replayedAt ? `Replayed ${new Date(e.replayedAt).toLocaleDateString()}` : 'Replayed'}
                            </span>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Informational Guidance */}
      <div className="p-4 rounded-xl bg-[#111827] border border-[#374151] flex items-start gap-3 text-xs text-gray-400">
        <Sparkles className="w-4 h-4 text-blue-400 shrink-0 mt-0.5" />
        <div>
          <strong className="text-gray-200">Replay Architecture (F-EDGE-07):</strong> Triggering a manual replay immediately re-computes a fresh HMAC-SHA256 signature and fresh Unix timestamp header (<code className="bg-[#1f2937] px-1 py-0.5 rounded text-blue-300 font-mono">X-Signature-SHA256</code>, <code className="bg-[#1f2937] px-1 py-0.5 rounded text-blue-300 font-mono">X-Timestamp</code>) to prevent replay attack rejections on the merchant destination endpoint.
        </div>
      </div>

      {/* Details Modal */}
      <DlqDetailsModal
        entry={selectedEntry}
        onClose={() => setSelectedEntry(null)}
        onReplay={handleReplay}
      />
    </div>
  );
};
