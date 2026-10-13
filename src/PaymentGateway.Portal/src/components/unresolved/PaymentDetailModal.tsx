import React, { useEffect, useState } from 'react';
import { X, Clock, DollarSign, Hash, AlertOctagon } from 'lucide-react';
import { PaymentDto } from '../../types/api';
import { fetchPaymentById } from '../../services/apiClient';
import { StateBadge, ChannelBadge } from '../common/Badge';

interface PaymentDetailModalProps {
  paymentId: string | null;
  token: string;
  onClose: () => void;
}

export const PaymentDetailModal: React.FC<PaymentDetailModalProps> = ({
  paymentId,
  token,
  onClose,
}) => {
  const [details, setDetails] = useState<PaymentDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!paymentId) {
      setDetails(null);
      return;
    }

    setLoading(true);
    setError(null);
    fetchPaymentById(token, paymentId)
      .then((data) => {
        setDetails(data);
      })
      .catch((err) => {
        setError(err.message || 'Failed to fetch payment details');
      })
      .finally(() => {
        setLoading(false);
      });
  }, [paymentId, token]);

  if (!paymentId) return null;

  const formatAmount = (amountMinor: number, currency: string) => {
    // Zero-decimal currency handling (e.g. JPY)
    if (currency.toUpperCase() === 'JPY') {
      return `¥${amountMinor.toLocaleString()}`;
    }
    return `${(amountMinor / 100).toFixed(2)} ${currency}`;
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/75 backdrop-blur-sm flex items-center justify-center p-4">
      <div
        className="bg-[#111827] border border-[#374151] rounded-xl max-w-2xl w-full max-h-[90vh] overflow-y-auto shadow-2xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Modal Header */}
        <div className="p-4 sm:p-5 border-b border-[#374151] flex items-center justify-between sticky top-0 bg-[#111827] z-10">
          <div className="flex items-center gap-3">
            <h3 className="text-base font-bold text-white">Payment Details</h3>
            <span className="font-mono text-xs text-blue-400 bg-blue-500/10 border border-blue-500/20 px-2 py-0.5 rounded">
              {paymentId}
            </span>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-gray-400 hover:text-white hover:bg-[#1f2937] transition"
            aria-label="Close Modal"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Modal Body */}
        <div className="p-5 sm:p-6 space-y-6 text-sm">
          {loading && (
            <div className="py-12 text-center text-gray-400 animate-pulse">
              Loading payment aggregate details...
            </div>
          )}

          {error && (
            <div className="p-4 bg-rose-950/40 border border-rose-500/40 rounded-lg text-rose-300 flex items-center gap-3 text-xs">
              <AlertOctagon className="w-5 h-5 text-rose-400 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {details && !loading && (
            <>
              {/* Status and Channel Strip */}
              <div className="flex flex-wrap items-center justify-between gap-4 p-4 bg-[#1f2937]/50 border border-[#374151] rounded-lg">
                <div>
                  <div className="text-xs text-gray-400 mb-1">Current State</div>
                  <StateBadge state={details.state} />
                </div>
                <div>
                  <div className="text-xs text-gray-400 mb-1">Settlement Channel</div>
                  <ChannelBadge channel={details.settlementChannel} />
                </div>
                <div>
                  <div className="text-xs text-gray-400 mb-1">Merchant / Party</div>
                  <span className="font-mono text-xs text-gray-200">{details.partyId}</span>
                </div>
              </div>

              {/* Financial Balances Grid */}
              <div>
                <h4 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 flex items-center gap-1.5">
                  <DollarSign className="w-4 h-4 text-emerald-400" />
                  Financial Ledger
                </h4>
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  <div className="p-3 bg-[#1f2937] border border-[#374151] rounded-lg">
                    <span className="text-[11px] text-gray-400 block mb-1">Authorized</span>
                    <span className="font-semibold text-white">
                      {formatAmount(details.amount, details.currency)}
                    </span>
                  </div>
                  <div className="p-3 bg-[#1f2937] border border-[#374151] rounded-lg">
                    <span className="text-[11px] text-gray-400 block mb-1">Captured</span>
                    <span className="font-semibold text-emerald-400">
                      {formatAmount(details.capturedAmount, details.currency)}
                    </span>
                  </div>
                  <div className="p-3 bg-[#1f2937] border border-[#374151] rounded-lg">
                    <span className="text-[11px] text-gray-400 block mb-1">Refunded</span>
                    <span className="font-semibold text-purple-400">
                      {formatAmount(details.refundedAmount, details.currency)}
                    </span>
                  </div>
                  <div className="p-3 bg-[#1f2937] border border-[#374151] rounded-lg">
                    <span className="text-[11px] text-gray-400 block mb-1">Remaining Auth</span>
                    <span className="font-semibold text-gray-300">
                      {formatAmount(details.remainingAuthorizedAmount, details.currency)}
                    </span>
                  </div>
                </div>
              </div>

              {/* Operational References */}
              <div>
                <h4 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 flex items-center gap-1.5">
                  <Hash className="w-4 h-4 text-blue-400" />
                  Channel & Acquirer Metadata
                </h4>
                <div className="bg-[#1f2937] border border-[#374151] rounded-lg p-3.5 space-y-2 text-xs">
                  <div className="flex justify-between">
                    <span className="text-gray-400">Channel Reference:</span>
                    <span className="font-mono text-gray-200">
                      {details.channelReference || 'None (In-Flight / Not assigned)'}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-gray-400">Created Timestamp (UTC):</span>
                    <span className="font-mono text-gray-200">
                      {new Date(details.createdAt).toISOString()}
                    </span>
                  </div>
                  {details.declineReason && (
                    <div className="flex justify-between text-rose-300">
                      <span>Decline Reason:</span>
                      <span className="font-medium">{details.declineReason}</span>
                    </div>
                  )}
                </div>
              </div>

              {/* Lifecycle Timeline */}
              <div>
                <h4 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 flex items-center gap-1.5">
                  <Clock className="w-4 h-4 text-purple-400" />
                  Reconciliation Status
                </h4>
                <div className="border-l-2 border-[#374151] ml-2 pl-4 space-y-4 text-xs">
                  <div className="relative">
                    <div className="absolute -left-[21px] top-1 w-2.5 h-2.5 rounded-full bg-blue-500"></div>
                    <div className="font-semibold text-white">Payment Authorized</div>
                    <div className="text-gray-400 text-[11px]">{new Date(details.createdAt).toLocaleTimeString()}</div>
                  </div>
                  <div className="relative">
                    <div className="absolute -left-[21px] top-1 w-2.5 h-2.5 rounded-full bg-amber-500"></div>
                    <div className="font-semibold text-amber-300">Awaiting Acquirer / Web3 Settlement</div>
                    <div className="text-gray-400 text-[11px]">In-flight reconciliation window</div>
                  </div>
                </div>
              </div>
            </>
          )}
        </div>

        {/* Modal Footer */}
        <div className="p-4 border-t border-[#374151] bg-[#111827] flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-2 bg-[#1f2937] hover:bg-[#374151] text-white rounded-lg text-xs font-semibold transition"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
