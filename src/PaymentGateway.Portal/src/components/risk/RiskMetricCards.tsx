import React from 'react';
import { ShieldAlert, AlertTriangle, Clock, CheckCircle2 } from 'lucide-react';
import { RiskFlagDto } from '../../types/api';

interface RiskMetricCardsProps {
  flags: RiskFlagDto[];
  totalCount: number;
}

export const RiskMetricCards: React.FC<RiskMetricCardsProps> = ({ flags, totalCount }) => {
  const highSeverityCount = flags.filter((f) => {
    const r = (f.reason || '').toLowerCase();
    return r.includes('high') || r.includes('spike') || r.includes('velocity') || r.includes('multiplier') || r.includes('exceed');
  }).length;

  const oldestMinutes = flags.reduce((max, f) => {
    const diff = Math.max(0, Math.floor((Date.now() - new Date(f.createdAt).getTime()) / 60000));
    return diff > max ? diff : max;
  }, 0);

  const formatAge = (minutes: number) => {
    if (minutes === 0) return '< 1m';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    const remainingMins = minutes % 60;
    return `${hours}h ${remainingMins}m ago`;
  };

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
      {/* Total Open Flags */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 flex items-center gap-4">
        <div className="w-10 h-10 rounded-lg bg-rose-500/15 border border-rose-500/30 flex items-center justify-center text-rose-400 shrink-0">
          <ShieldAlert className="w-5 h-5" />
        </div>
        <div>
          <p className="text-xs font-medium text-gray-400 uppercase tracking-wider">Open Risk Flags</p>
          <div className="flex items-baseline gap-2 mt-0.5">
            <span className="text-2xl font-bold text-white tracking-tight">{totalCount}</span>
            <span className="text-xs text-rose-400 font-medium">Pending Review</span>
          </div>
        </div>
      </div>

      {/* High Severity / Spike Anomalies */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 flex items-center gap-4">
        <div className="w-10 h-10 rounded-lg bg-amber-500/15 border border-amber-500/30 flex items-center justify-center text-amber-400 shrink-0">
          <AlertTriangle className="w-5 h-5" />
        </div>
        <div>
          <p className="text-xs font-medium text-gray-400 uppercase tracking-wider">High Severity</p>
          <div className="flex items-baseline gap-2 mt-0.5">
            <span className="text-2xl font-bold text-white tracking-tight">{highSeverityCount}</span>
            <span className="text-xs text-amber-400 font-medium">Velocity / Spike</span>
          </div>
        </div>
      </div>

      {/* Oldest Pending Flag */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 flex items-center gap-4">
        <div className="w-10 h-10 rounded-lg bg-blue-500/15 border border-blue-500/30 flex items-center justify-center text-blue-400 shrink-0">
          <Clock className="w-5 h-5" />
        </div>
        <div>
          <p className="text-xs font-medium text-gray-400 uppercase tracking-wider">Oldest In Queue</p>
          <div className="flex items-baseline gap-2 mt-0.5">
            <span className="text-2xl font-bold text-white tracking-tight">
              {flags.length > 0 ? formatAge(oldestMinutes) : '0m'}
            </span>
            <span className="text-xs text-gray-400">Aging</span>
          </div>
        </div>
      </div>

      {/* Queue Status */}
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-4 flex items-center gap-4">
        <div className={`w-10 h-10 rounded-lg flex items-center justify-center shrink-0 ${
          totalCount === 0
            ? 'bg-emerald-500/15 border border-emerald-500/30 text-emerald-400'
            : 'bg-purple-500/15 border border-purple-500/30 text-purple-400'
        }`}>
          <CheckCircle2 className="w-5 h-5" />
        </div>
        <div>
          <p className="text-xs font-medium text-gray-400 uppercase tracking-wider">Queue Health</p>
          <div className="flex items-baseline gap-2 mt-0.5">
            <span className="text-base font-bold text-white">
              {totalCount === 0 ? 'Clear' : 'Active Triage'}
            </span>
            <span className="text-xs text-gray-400">
              {totalCount === 0 ? 'Compliant' : 'SLA Target < 4h'}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};
