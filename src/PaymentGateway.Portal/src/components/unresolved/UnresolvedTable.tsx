import React from 'react';
import { UnresolvedPaymentSummaryDto } from '../../types/api';
import { StateBadge, ChannelBadge } from '../common/Badge';
import { ChevronLeft, ChevronRight, Eye } from 'lucide-react';

interface UnresolvedTableProps {
  items: UnresolvedPaymentSummaryDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (newPage: number) => void;
  onSelectPayment: (paymentId: string) => void;
}

export const UnresolvedTable: React.FC<UnresolvedTableProps> = ({
  items,
  page,
  pageSize,
  totalCount,
  onPageChange,
  onSelectPayment,
}) => {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div className="bg-[#111827] border border-[#374151] rounded-lg overflow-hidden shadow-sm flex flex-col">
      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="border-b border-[#374151] bg-[#1f2937]/50 text-gray-400 text-xs uppercase tracking-wider">
              <th className="py-3 px-4 font-semibold">Payment ID</th>
              <th className="py-3 px-4 font-semibold">State</th>
              <th className="py-3 px-4 font-semibold">Channel</th>
              <th className="py-3 px-4 font-semibold">Channel Reference</th>
              <th className="py-3 px-4 font-semibold">Created At (UTC)</th>
              <th className="py-3 px-4 font-semibold text-right">Elapsed</th>
              <th className="py-3 px-4 font-semibold text-center">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[#374151] text-xs">
            {items.map((row) => (
              <tr
                key={row.paymentId}
                className="hover:bg-[#1f2937]/70 transition-colors group cursor-pointer"
                onClick={() => onSelectPayment(row.paymentId)}
              >
                <td className="py-3.5 px-4 font-mono font-medium text-blue-400">
                  {row.paymentId}
                </td>
                <td className="py-3.5 px-4">
                  <StateBadge state={row.state} />
                </td>
                <td className="py-3.5 px-4">
                  <ChannelBadge channel={row.settlementChannel} />
                </td>
                <td className="py-3.5 px-4 font-mono text-gray-300">
                  {row.channelReference || <span className="text-gray-500 italic">None</span>}
                </td>
                <td className="py-3.5 px-4 text-gray-400">
                  {new Date(row.createdAt).toISOString().replace('T', ' ').substring(0, 19)}
                </td>
                <td className="py-3.5 px-4 text-right font-medium">
                  <span className={row.durationElapsedMinutes >= 30 ? 'text-rose-400' : 'text-amber-400'}>
                    {row.durationElapsedMinutes}m ago
                  </span>
                </td>
                <td className="py-3.5 px-4 text-center">
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      onSelectPayment(row.paymentId);
                    }}
                    className="inline-flex items-center gap-1 px-2.5 py-1 rounded bg-[#1f2937] hover:bg-blue-600 text-gray-300 hover:text-white border border-[#374151] hover:border-blue-500 transition text-xs font-semibold"
                  >
                    <Eye className="w-3.5 h-3.5" />
                    Inspect
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination Footer */}
      <div className="border-t border-[#374151] px-4 py-3 flex items-center justify-between text-xs text-gray-400 bg-[#111827]">
        <div>
          Showing <span className="text-white font-medium">{items.length > 0 ? (page - 1) * pageSize + 1 : 0}</span> to{' '}
          <span className="text-white font-medium">{Math.min(page * pageSize, totalCount)}</span> of{' '}
          <span className="text-white font-medium">{totalCount}</span> unresolved
        </div>

        <div className="flex items-center gap-2">
          <button
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            className="p-1 rounded bg-[#1f2937] border border-[#374151] text-gray-300 hover:bg-[#374151] disabled:opacity-40 disabled:cursor-not-allowed transition"
            aria-label="Previous Page"
          >
            <ChevronLeft className="w-4 h-4" />
          </button>

          <span className="px-2 font-medium text-gray-300">
            Page {page} of {totalPages}
          </span>

          <button
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            className="p-1 rounded bg-[#1f2937] border border-[#374151] text-gray-300 hover:bg-[#374151] disabled:opacity-40 disabled:cursor-not-allowed transition"
            aria-label="Next Page"
          >
            <ChevronRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
};
