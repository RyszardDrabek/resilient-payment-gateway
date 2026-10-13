import React, { useState, useEffect } from 'react';
import {
  ShieldAlert,
  CreditCard,
  History,
  Activity,
  CheckCircle2,
  AlertOctagon,
  ArrowUpRight,
  Loader2,
  Info,
} from 'lucide-react';
import { RiskDisposition, RiskFlagDto, PaymentDto, RiskVerdictDto } from '../../types/api';
import { fetchPaymentById, fetchRiskVerdict } from '../../services/apiClient';
import { Badge, StateBadge, ChannelBadge } from '../common/Badge';

interface RiskFlagDetailCardProps {
  flag: RiskFlagDto | null;
  token: string;
  onOpenDisposition: (disposition: RiskDisposition) => void;
  isDispositioning: boolean;
}

export const RiskFlagDetailCard: React.FC<RiskFlagDetailCardProps> = ({
  flag,
  token,
  onOpenDisposition,
  isDispositioning,
}) => {
  const [payment, setPayment] = useState<PaymentDto | null>(null);
  const [verdict, setVerdict] = useState<RiskVerdictDto | null>(null);
  const [isLoadingDetails, setIsLoadingDetails] = useState(false);

  useEffect(() => {
    let isCurrent = true;
    if (!flag || !token) {
      setPayment(null);
      setVerdict(null);
      return;
    }

    setIsLoadingDetails(true);

    Promise.allSettled([
      fetchPaymentById(token, flag.paymentId),
      fetchRiskVerdict(token, flag.id).catch(() => fetchRiskVerdict(token, flag.paymentId)),
    ]).then(([paymentResult, verdictResult]) => {
      if (!isCurrent) return;

      if (paymentResult.status === 'fulfilled') {
        setPayment(paymentResult.value);
      } else {
        setPayment(null);
      }

      if (verdictResult.status === 'fulfilled') {
        setVerdict(verdictResult.value);
      } else {
        setVerdict(null);
      }

      setIsLoadingDetails(false);
    });

    return () => {
      isCurrent = false;
    };
  }, [flag, token]);

  if (!flag) {
    return (
      <div className="bg-[#111827] border border-[#374151] rounded-lg p-12 text-center h-[740px] flex flex-col items-center justify-center text-gray-500">
        <ShieldAlert className="w-12 h-12 mb-3 text-gray-600" />
        <h4 className="text-base font-semibold text-gray-300">Select a Flagged Case</h4>
        <p className="text-xs text-gray-500 max-w-sm mt-1">
          Pick an item from the review queue on the left to inspect anomaly factors, payment metadata, and record compliance disposition.
        </p>
      </div>
    );
  }

  const formatAmount = (minor: number, currency: string) => {
    return `${(minor / 100).toFixed(2)} ${currency}`;
  };

  return (
    <div className="bg-[#111827] border border-[#374151] rounded-lg h-[740px] flex flex-col overflow-hidden">
      {/* Top Header Bar */}
      <div className="p-4 border-b border-[#374151] flex items-center justify-between bg-[#182234]/60">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-rose-500/15 border border-rose-500/30 text-rose-400">
            <ShieldAlert className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h3 className="font-bold text-white text-base">Case Inspection</h3>
              <Badge variant="destructive">Open Flag</Badge>
            </div>
            <p className="text-xs font-mono text-gray-400 mt-0.5">
              Flag ID: <span className="text-gray-200">{flag.id}</span>
            </p>
          </div>
        </div>

        <div className="text-right text-xs text-gray-400">
          <span className="block font-medium text-gray-300">Flagged At</span>
          <span className="font-mono">{new Date(flag.createdAt).toLocaleString()}</span>
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 overflow-y-auto p-5 space-y-5">
        {isLoadingDetails && (
          <div className="flex items-center gap-2 text-xs text-gray-400 p-2 bg-[#1f2937]/50 rounded border border-[#374151]/50">
            <Loader2 className="w-3.5 h-3.5 animate-spin text-rose-400" />
            Loading transaction context and AI anomaly scores...
          </div>
        )}

        {/* Anomaly Reasoning Banner */}
        <div className="p-4 rounded-lg bg-rose-950/30 border border-rose-500/40 text-rose-200">
          <div className="flex items-center gap-2 text-xs font-bold text-rose-400 uppercase tracking-wider mb-1">
            <Activity className="w-4 h-4" />
            Anomaly Trigger Reason
          </div>
          <p className="text-sm font-medium text-white leading-relaxed">
            {flag.reason}
          </p>
          {verdict && (
            <div className="mt-3 pt-3 border-t border-rose-500/20 flex flex-wrap items-center gap-4 text-xs font-mono">
              <span className="text-gray-300">
                AI Verdict: <strong className="text-rose-400">{verdict.status}</strong>
              </span>
              <span className="text-gray-400">
                Model Timestamp: {new Date(verdict.scoredAt).toLocaleTimeString()}
              </span>
              <span className="text-gray-400">
                Verdict ID: {verdict.id}
              </span>
            </div>
          )}
        </div>

        {/* Transaction Metadata Section */}
        <div className="bg-[#182234]/70 border border-[#374151] rounded-lg p-4">
          <div className="flex items-center gap-2 text-xs font-bold text-gray-300 uppercase tracking-wider mb-3">
            <CreditCard className="w-4 h-4 text-indigo-400" />
            Transaction Metadata
          </div>

          <div className="grid grid-cols-2 sm:grid-cols-3 gap-4 text-xs">
            <div>
              <span className="text-gray-400 block mb-1">Payment ID</span>
              <span className="font-mono text-white font-semibold text-sm break-all">
                {flag.paymentId}
              </span>
            </div>

            <div>
              <span className="text-gray-400 block mb-1">Transaction Value</span>
              <span className="text-emerald-400 font-bold text-sm">
                {payment ? formatAmount(payment.amount, payment.currency) : 'N/A'}
              </span>
            </div>

            <div>
              <span className="text-gray-400 block mb-1">Payment State</span>
              {payment ? <StateBadge state={payment.state} /> : <span className="text-gray-500">—</span>}
            </div>

            <div>
              <span className="text-gray-400 block mb-1">Channel</span>
              {payment ? <ChannelBadge channel={payment.settlementChannel} /> : <span className="text-gray-500">—</span>}
            </div>

            <div>
              <span className="text-gray-400 block mb-1">Channel Reference</span>
              <span className="font-mono text-gray-300">
                {payment?.channelReference || 'None'}
              </span>
            </div>

            <div>
              <span className="text-gray-400 block mb-1">Event Identifier</span>
              <span className="font-mono text-gray-300">
                {flag.eventId}
              </span>
            </div>
          </div>
        </div>

        {/* Historical Party Context */}
        <div className="bg-[#182234]/70 border border-[#374151] rounded-lg p-4">
          <div className="flex items-center gap-2 text-xs font-bold text-gray-300 uppercase tracking-wider mb-3">
            <History className="w-4 h-4 text-blue-400" />
            Historical User & Party Risk Profile
          </div>

          <div className="space-y-3 text-xs">
            <div className="flex justify-between items-center py-1.5 border-b border-[#374151]/40">
              <span className="text-gray-400">Merchant / Party Identifier:</span>
              <span className="font-mono text-white font-semibold">{flag.partyId}</span>
            </div>

            <div className="flex justify-between items-center py-1.5 border-b border-[#374151]/40">
              <span className="text-gray-400">Baseline Velocity Profile:</span>
              <span className="text-gray-200">Standard Tier 1 Merchant</span>
            </div>

            <div className="flex justify-between items-center py-1.5 border-b border-[#374151]/40">
              <span className="text-gray-400">pgvector Anomaly Multiplier:</span>
              <span className="font-mono text-amber-400">5.0x Rolling Median</span>
            </div>

            <div className="p-3 bg-[#111827] rounded border border-[#374151]/60 flex items-start gap-2 text-gray-400 text-xs">
              <Info className="w-4 h-4 text-gray-400 shrink-0 mt-0.5" />
              <span>
                Historical user context evaluates frequency of sudden cross-border surges, burst velocity deviations from the party's 30-day baseline, and vector embedding similarity against known fraud vectors.
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Bottom Action Footer: 3 Actionable Disposition Buttons */}
      <div className="p-4 border-t border-[#374151] bg-[#182234]/90 flex items-center justify-between gap-3">
        <span className="text-xs text-gray-400 font-medium hidden sm:inline">
          Auditor Action:
        </span>

        <div className="flex items-center gap-2.5 w-full sm:w-auto justify-end">
          {/* False Positive Button */}
          <button
            onClick={() => onOpenDisposition('false_positive')}
            disabled={isDispositioning}
            className="flex-1 sm:flex-initial inline-flex items-center justify-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-emerald-600 hover:bg-emerald-500 text-white transition disabled:opacity-50 shadow-sm"
          >
            <CheckCircle2 className="w-4 h-4" />
            False Positive
          </button>

          {/* Escalate Case Button */}
          <button
            onClick={() => onOpenDisposition('escalated')}
            disabled={isDispositioning}
            className="flex-1 sm:flex-initial inline-flex items-center justify-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-amber-600 hover:bg-amber-500 text-white transition disabled:opacity-50 shadow-sm"
          >
            <ArrowUpRight className="w-4 h-4" />
            Escalate Case
          </button>

          {/* Confirm Fraud Button */}
          <button
            onClick={() => onOpenDisposition('confirmed')}
            disabled={isDispositioning}
            className="flex-1 sm:flex-initial inline-flex items-center justify-center gap-1.5 px-3.5 py-2 rounded-lg text-xs font-semibold bg-rose-600 hover:bg-rose-500 text-white transition disabled:opacity-50 shadow-sm"
          >
            <AlertOctagon className="w-4 h-4" />
            Confirm Fraud
          </button>
        </div>
      </div>
    </div>
  );
};
