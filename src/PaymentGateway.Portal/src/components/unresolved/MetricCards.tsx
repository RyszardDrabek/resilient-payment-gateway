import React from 'react';
import { UnresolvedPaymentSummaryDto } from '../../types/api';
import { AlertTriangle, Clock, Layers, ShieldQuestion } from 'lucide-react';

interface MetricCardsProps {
  items: UnresolvedPaymentSummaryDto[];
  totalCount: number;
}

export const MetricCards: React.FC<MetricCardsProps> = ({ items, totalCount }) => {
  const pendingCount = items.filter((x) => x.state.toLowerCase() === 'pending').length;
  const unknownCount = items.filter((x) => x.state.toLowerCase() === 'unknown').length;
  const adyenCount = items.filter((x) => x.settlementChannel.toUpperCase().includes('ADYEN')).length;
  const web3Count = items.filter((x) => x.settlementChannel.toUpperCase().includes('WEB3')).length;
  const maxAgeMinutes = items.reduce((max, cur) => Math.max(max, cur.durationElapsedMinutes), 0);

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
      {/* Total Unresolved */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 shadow-sm">
        <div className="flex items-center justify-between text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">
          <span>Unresolved Total</span>
          <AlertTriangle className="w-4 h-4 text-amber-400" />
        </div>
        <div className="text-2xl font-bold text-white mb-1">
          {totalCount}
        </div>
        <div className="text-xs text-amber-400 font-medium">
          Cutoff threshold exceeded (&gt; 15m)
        </div>
      </div>

      {/* State Breakdown */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 shadow-sm">
        <div className="flex items-center justify-between text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">
          <span>Status Breakdown <span className="text-[10px] text-gray-500 font-normal lowercase">(page)</span></span>
          <ShieldQuestion className="w-4 h-4 text-blue-400" />
        </div>
        <div className="flex items-baseline gap-3 mb-1">
          <div>
            <span className="text-lg font-bold text-amber-400">{pendingCount}</span>
            <span className="text-xs text-gray-400 ml-1">Pending</span>
          </div>
          <div>
            <span className="text-lg font-bold text-gray-300">{unknownCount}</span>
            <span className="text-xs text-gray-400 ml-1">Unknown</span>
          </div>
        </div>
        <div className="text-xs text-gray-400">
          Current page view ({items.length} items)
        </div>
      </div>

      {/* Channel Distribution */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 shadow-sm">
        <div className="flex items-center justify-between text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">
          <span>Channel Split <span className="text-[10px] text-gray-500 font-normal lowercase">(page)</span></span>
          <Layers className="w-4 h-4 text-purple-400" />
        </div>
        <div className="flex items-baseline gap-3 mb-1">
          <div>
            <span className="text-lg font-bold text-white">{adyenCount}</span>
            <span className="text-xs text-gray-400 ml-1">Adyen</span>
          </div>
          <div>
            <span className="text-lg font-bold text-purple-400">{web3Count}</span>
            <span className="text-xs text-gray-400 ml-1">Web3</span>
          </div>
        </div>
        <div className="text-xs text-gray-400">
          Current page view ({items.length} items)
        </div>
      </div>

      {/* Oldest Duration */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 shadow-sm">
        <div className="flex items-center justify-between text-gray-400 text-xs font-semibold uppercase tracking-wider mb-2">
          <span>Oldest In-Flight <span className="text-[10px] text-gray-500 font-normal lowercase">(page)</span></span>
          <Clock className="w-4 h-4 text-rose-400" />
        </div>
        <div className="text-2xl font-bold text-white mb-1">
          {maxAgeMinutes > 0 ? `${maxAgeMinutes}m` : '0m'}
        </div>
        <div className="text-xs text-gray-400">
          {maxAgeMinutes > 60 ? 'Requires operator investigation' : 'Current page oldest item'}
        </div>
      </div>
    </div>
  );
};
