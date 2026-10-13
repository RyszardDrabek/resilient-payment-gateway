import React from 'react';
import { Search, RefreshCw, AlertCircle, ChevronLeft, ChevronRight, ShieldAlert } from 'lucide-react';
import { RiskFlagDto } from '../../types/api';
import { Badge } from '../common/Badge';

interface RiskFlagListProps {
  flags: RiskFlagDto[];
  selectedFlagId: string | null;
  onSelectFlag: (flag: RiskFlagDto) => void;
  searchQuery: string;
  onSearchChange: (query: string) => void;
  onRefresh: () => void;
  isLoading: boolean;
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (newPage: number) => void;
}

export const RiskFlagList: React.FC<RiskFlagListProps> = ({
  flags,
  selectedFlagId,
  onSelectFlag,
  searchQuery,
  onSearchChange,
  onRefresh,
  isLoading,
  page,
  pageSize,
  totalCount,
  onPageChange,
}) => {
  const getSeverity = (reason: string): { label: string; variant: 'destructive' | 'warning' | 'muted' } => {
    const r = (reason || '').toLowerCase();
    if (r.includes('velocity') || r.includes('spike') || r.includes('high') || r.includes('multiplier')) {
      return { label: 'High', variant: 'destructive' };
    }
    if (r.includes('amount') || r.includes('threshold') || r.includes('anomaly')) {
      return { label: 'Medium', variant: 'warning' };
    }
    return { label: 'Low', variant: 'muted' };
  };

  const formatRelativeTime = (isoString: string) => {
    const diffMins = Math.max(0, Math.floor((Date.now() - new Date(isoString).getTime()) / 60000));
    if (diffMins === 0) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    const hours = Math.floor(diffMins / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    return `${days}d ago`;
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="bg-[#111827] border border-[#374151] rounded-lg flex flex-col h-[740px]">
      {/* Header & Search */}
      <div className="p-4 border-b border-[#374151] space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <ShieldAlert className="w-4 h-4 text-rose-400" />
            <h3 className="font-semibold text-white text-sm">Review Queue</h3>
            <span className="px-2 py-0.5 rounded-full text-xs bg-rose-500/20 text-rose-300 font-mono">
              {totalCount}
            </span>
          </div>
          <button
            onClick={onRefresh}
            disabled={isLoading}
            className="p-1.5 text-gray-400 hover:text-white rounded bg-[#1f2937] hover:bg-[#374151] transition border border-[#374151] disabled:opacity-50"
            title="Refresh flags"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isLoading ? 'animate-spin' : ''}`} />
          </button>
        </div>

        {/* Search Input */}
        <div className="relative">
          <Search className="w-4 h-4 text-gray-400 absolute left-3 top-2.5" />
          <input
            type="text"
            placeholder="Search payment, party or reason..."
            value={searchQuery}
            onChange={(e) => onSearchChange(e.target.value)}
            className="w-full bg-[#1f2937] border border-[#374151] rounded pl-9 pr-3 py-1.5 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-rose-500 transition"
          />
        </div>
      </div>

      {/* Flag Items List */}
      <div className="flex-1 overflow-y-auto divide-y divide-[#1f2937] p-2 space-y-1">
        {flags.length === 0 ? (
          <div className="py-16 text-center text-gray-400">
            <AlertCircle className="w-8 h-8 mx-auto mb-2 text-gray-600" />
            <p className="text-sm font-medium">No open flags found</p>
            <p className="text-xs text-gray-500 mt-1">All cases are dispositioned or match filter.</p>
          </div>
        ) : (
          flags.map((flag) => {
            const isSelected = flag.id === selectedFlagId;
            const severity = getSeverity(flag.reason);

            return (
              <button
                key={flag.id}
                onClick={() => onSelectFlag(flag)}
                className={`w-full text-left p-3 rounded-lg border transition flex flex-col gap-2 ${
                  isSelected
                    ? 'bg-rose-950/30 border-rose-500/60 shadow-sm'
                    : 'bg-[#182234]/40 hover:bg-[#1f2937] border-transparent hover:border-[#374151]'
                }`}
              >
                <div className="flex items-center justify-between w-full">
                  <div className="flex items-center gap-2">
                    <Badge variant={severity.variant}>{severity.label}</Badge>
                    <span className="font-mono text-xs text-gray-300 font-semibold truncate max-w-[150px]">
                      {flag.paymentId}
                    </span>
                  </div>
                  <span className="text-[11px] text-gray-400">
                    {formatRelativeTime(flag.createdAt)}
                  </span>
                </div>

                <p className="text-xs text-gray-300 line-clamp-2 leading-relaxed">
                  {flag.reason}
                </p>

                <div className="flex items-center justify-between text-[11px] text-gray-400 pt-1 border-t border-[#374151]/40">
                  <span className="truncate max-w-[140px] font-mono">
                    Party: <strong className="text-gray-300 font-medium">{flag.partyId}</strong>
                  </span>
                  <span className="font-mono text-[10px] text-gray-500">
                    {flag.id}
                  </span>
                </div>
              </button>
            );
          })
        )}
      </div>

      {/* Pagination Footer */}
      {totalPages > 1 && (
        <div className="p-3 border-t border-[#374151] flex items-center justify-between text-xs text-gray-400 bg-[#111827]">
          <span>
            Page <strong className="text-white">{page}</strong> of <strong className="text-white">{totalPages}</strong>
          </span>
          <div className="flex gap-1">
            <button
              onClick={() => onPageChange(page - 1)}
              disabled={page <= 1}
              className="p-1 rounded bg-[#1f2937] hover:bg-[#374151] text-gray-300 disabled:opacity-40 transition"
            >
              <ChevronLeft className="w-4 h-4" />
            </button>
            <button
              onClick={() => onPageChange(page + 1)}
              disabled={page >= totalPages}
              className="p-1 rounded bg-[#1f2937] hover:bg-[#374151] text-gray-300 disabled:opacity-40 transition"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
