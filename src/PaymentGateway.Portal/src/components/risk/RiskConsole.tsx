import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { ShieldAlert, CheckCircle2, ShieldOff } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { fetchOpenRiskFlags, submitRiskDisposition, ApiError } from '../../services/apiClient';
import { RiskDisposition, RiskFlagDto } from '../../types/api';
import { RiskMetricCards } from './RiskMetricCards';
import { RiskFlagList } from './RiskFlagList';
import { RiskFlagDetailCard } from './RiskFlagDetailCard';
import { DispositionModal } from './DispositionModal';
import { ErrorAlert } from '../common/ErrorAlert';
import { CardSkeleton } from '../common/LoadingSkeleton';

export const RiskConsole: React.FC = () => {
  const { user, token, setRole } = useAuth();
  const role = user.role;
  const isAuthorized = role === 'risk' || role === 'ops';

  const [flags, setFlags] = useState<RiskFlagDto[]>([]);
  const [totalCount, setTotalCount] = useState<number>(0);
  const [page, setPage] = useState<number>(1);
  const pageSize = 50;

  const [selectedFlagId, setSelectedFlagId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<Error | null>(null);
  const [successNotice, setSuccessNotice] = useState<string | null>(null);

  // Disposition modal state
  const [activeDisposition, setActiveDisposition] = useState<RiskDisposition | null>(null);
  const [isDispositioning, setIsDispositioning] = useState<boolean>(false);

  const loadFlags = useCallback(async () => {
    if (!token || !isAuthorized) {
      setIsLoading(false);
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const response = await fetchOpenRiskFlags(token, page, pageSize);
      setFlags(response.items);
      setTotalCount(response.totalCount);

      // Auto-select first item if none selected or previous selection no longer exists
      if (response.items.length > 0) {
        setSelectedFlagId((prev) => {
          if (prev && response.items.some((f) => f.id === prev)) {
            return prev;
          }
          return response.items[0].id;
        });
      } else {
        setSelectedFlagId(null);
      }
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err);
      } else {
        setError(new Error('Failed to load risk flags queue.'));
      }
    } finally {
      setIsLoading(false);
    }
  }, [token, isAuthorized, page, pageSize]);

  useEffect(() => {
    loadFlags();
  }, [loadFlags]);

  // Filter flags based on search query
  const filteredFlags = useMemo(() => {
    if (!searchQuery.trim()) return flags;
    const query = searchQuery.toLowerCase().trim();
    return flags.filter(
      (f) =>
        f.id.toLowerCase().includes(query) ||
        f.paymentId.toLowerCase().includes(query) ||
        f.partyId.toLowerCase().includes(query) ||
        f.reason.toLowerCase().includes(query)
    );
  }, [flags, searchQuery]);

  const selectedFlag = useMemo(() => {
    return flags.find((f) => f.id === selectedFlagId) || null;
  }, [flags, selectedFlagId]);

  const handleOpenDisposition = (disp: RiskDisposition) => {
    setActiveDisposition(disp);
  };

  const handleCloseDisposition = () => {
    setActiveDisposition(null);
  };

  const handleSubmitDisposition = async (disposition: RiskDisposition, notes: string) => {
    if (!token || !selectedFlag) return;

    setIsDispositioning(true);
    setSuccessNotice(null);

    try {
      const updatedFlag = await submitRiskDisposition(token, selectedFlag.id, {
        disposition,
        notes: notes || null,
      });

      // Optimistically remove from open queue (AC-3)
      const remainingFlags = flags.filter((f) => f.id !== selectedFlag.id);
      setFlags(remainingFlags);
      setTotalCount((prev) => Math.max(0, prev - 1));

      // Select next flag if available
      if (remainingFlags.length > 0) {
        setSelectedFlagId(remainingFlags[0].id);
      } else {
        setSelectedFlagId(null);
      }

      const dispLabels: Record<RiskDisposition, string> = {
        confirmed: 'confirmed fraud',
        false_positive: 'false positive',
        escalated: 'escalated to senior review',
      };

      setSuccessNotice(
        `Case ${updatedFlag.paymentId} has been successfully recorded as ${dispLabels[disposition]} and removed from the active triage queue.`
      );
    } catch (err: unknown) {
      if (err instanceof ApiError) {
        throw new Error(err.message);
      } else if (err instanceof Error) {
        throw err;
      } else {
        throw new Error('Failed to apply disposition.');
      }
    } finally {
      setIsDispositioning(false);
    }
  };

  // Role guard for merchant / unprivileged callers (AC-4)
  if (!isAuthorized) {
    return (
      <div className="bg-[#111827] border border-[#374151] rounded-xl p-8 max-w-2xl mx-auto my-8 text-center shadow-lg">
        <div className="w-14 h-14 rounded-full bg-amber-500/15 border border-amber-500/30 text-amber-400 mx-auto flex items-center justify-center mb-4">
          <ShieldOff className="w-7 h-7" />
        </div>
        <h2 className="text-xl font-bold text-white mb-2">Compliance Access Restricted</h2>
        <p className="text-sm text-gray-400 mb-6 leading-relaxed">
          The Risk & Compliance Console is restricted to authenticated <span className="text-white font-medium">Compliance Auditors (Risk)</span> and <span className="text-white font-medium">Operations Specialists (Ops)</span>. Unprivileged merchant accounts are denied access (HTTP 403 Forbidden).
        </p>
        <div className="p-4 bg-[#1f2937] border border-[#374151] rounded-lg mb-6 text-xs text-gray-300 text-left space-y-1">
          <div className="flex justify-between">
            <span className="text-gray-400">Current Role:</span>
            <span className="font-mono text-amber-300 font-semibold uppercase">{role}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-400">Required Roles:</span>
            <span className="font-mono text-white">risk, ops</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-400">Policy:</span>
            <span className="font-mono text-white">RiskPolicy (AC-4)</span>
          </div>
        </div>
        <button
          onClick={() => setRole('risk')}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-rose-600 hover:bg-rose-500 text-white font-semibold text-sm transition shadow-lg shadow-rose-950/40"
        >
          <ShieldAlert className="w-4 h-4" />
          Switch to Compliance Auditor (Risk)
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Console Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2.5">
            <h1 className="text-2xl font-bold text-white tracking-tight">Compliance & Risk Console</h1>
            <span className="px-2.5 py-0.5 rounded-full text-xs font-semibold bg-rose-500/15 border border-rose-500/30 text-rose-300">
              Live AML Queue
            </span>
          </div>
          <p className="text-sm text-gray-400 mt-1">
            Prioritized review queue for anomalous payments, pgvector velocity alerts, and auditor dispositions.
          </p>
        </div>
      </div>

      {/* Metric Cards */}
      <RiskMetricCards flags={flags} totalCount={totalCount} />

      {/* Success Notification Banner */}
      {successNotice && (
        <div className="p-4 rounded-lg bg-emerald-950/40 border border-emerald-500/40 text-emerald-200 flex items-start justify-between gap-3 animate-in fade-in">
          <div className="flex items-center gap-2.5 text-xs font-medium">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
            <span>{successNotice}</span>
          </div>
          <button
            onClick={() => setSuccessNotice(null)}
            className="text-gray-400 hover:text-white text-xs underline"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Error Alert */}
      {error && (
        <ErrorAlert
          error={error}
          onRetry={loadFlags}
          onSwitchToOps={() => setRole('risk')}
        />
      )}

      {/* Main Workspace Layout */}
      {isLoading && flags.length === 0 ? (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
          <div className="lg:col-span-5">
            <CardSkeleton />
          </div>
          <div className="lg:col-span-7">
            <CardSkeleton />
          </div>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
          {/* Left / Master Pane: Flags Queue */}
          <div className="lg:col-span-5">
            <RiskFlagList
              flags={filteredFlags}
              selectedFlagId={selectedFlagId}
              onSelectFlag={(f) => setSelectedFlagId(f.id)}
              searchQuery={searchQuery}
              onSearchChange={setSearchQuery}
              onRefresh={loadFlags}
              isLoading={isLoading}
              page={page}
              pageSize={pageSize}
              totalCount={totalCount}
              onPageChange={setPage}
            />
          </div>

          {/* Right / Detail Pane: Selected Case Inspector */}
          <div className="lg:col-span-7">
            <RiskFlagDetailCard
              flag={selectedFlag}
              token={token}
              onOpenDisposition={handleOpenDisposition}
              isDispositioning={isDispositioning}
            />
          </div>
        </div>
      )}

      {/* Disposition Modal */}
      <DispositionModal
        isOpen={activeDisposition !== null}
        onClose={handleCloseDisposition}
        flag={selectedFlag}
        disposition={activeDisposition}
        onSubmit={handleSubmitDisposition}
        isSubmitting={isDispositioning}
      />
    </div>
  );
};
