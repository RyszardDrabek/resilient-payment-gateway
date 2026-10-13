import React from 'react';
import { Filter, RefreshCw, Clock } from 'lucide-react';

interface FilterToolbarProps {
  channel: string;
  onChannelChange: (channel: string) => void;
  ageMinutes: number;
  onAgeMinutesChange: (age: number) => void;
  onRefresh: () => void;
  loading: boolean;
}

export const FilterToolbar: React.FC<FilterToolbarProps> = ({
  channel,
  onChannelChange,
  ageMinutes,
  onAgeMinutesChange,
  onRefresh,
  loading,
}) => {
  return (
    <div className="bg-[#111827] border border-[#374151] rounded-lg p-3 sm:p-4 mb-4 flex flex-wrap items-center justify-between gap-3 shadow-sm">
      <div className="flex flex-wrap items-center gap-3">
        {/* Channel Filter */}
        <div className="flex items-center gap-2 text-xs">
          <Filter className="w-4 h-4 text-gray-400 shrink-0" />
          <span className="text-gray-400 font-medium">Channel:</span>
          <select
            value={channel}
            onChange={(e) => onChannelChange(e.target.value)}
            className="bg-[#1f2937] border border-[#374151] rounded-md px-2.5 py-1.5 text-xs text-white focus:outline-none focus:ring-1 focus:ring-blue-500 cursor-pointer"
          >
            <option value="">All Channels</option>
            <option value="adyen-wiremock">Adyen (WireMock/Sandbox)</option>
            <option value="web3-anvil">Web3 (Anvil/Mock USDC)</option>
          </select>
        </div>

        {/* Age Filter */}
        <div className="flex items-center gap-2 text-xs">
          <Clock className="w-4 h-4 text-gray-400 shrink-0" />
          <span className="text-gray-400 font-medium">Min Age (Minutes):</span>
          <input
            type="number"
            min={0}
            max={1440}
            value={ageMinutes}
            onChange={(e) => onAgeMinutesChange(Math.max(0, parseInt(e.target.value) || 0))}
            className="bg-[#1f2937] border border-[#374151] rounded-md px-2.5 py-1.5 text-xs text-white w-20 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>
      </div>

      {/* Refresh Button */}
      <button
        onClick={onRefresh}
        disabled={loading}
        className="inline-flex items-center gap-2 px-3.5 py-1.5 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-md text-xs font-semibold transition shadow-sm ml-auto"
      >
        <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
        <span>Refresh</span>
      </button>
    </div>
  );
};
