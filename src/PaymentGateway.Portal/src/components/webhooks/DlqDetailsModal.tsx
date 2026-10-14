import React, { useState } from 'react';
import {
  X,
  AlertTriangle,
  Check,
  Copy,
  RotateCw,
  Clock,
  Globe,
  Tag,
  Loader2,
  FileCode,
  ShieldCheck,
} from 'lucide-react';
import { MerchantWebhookDeadLetterDto } from '../../types/api';

interface DlqDetailsModalProps {
  entry: MerchantWebhookDeadLetterDto | null;
  onClose: () => void;
  onReplay: (entryId: string) => Promise<void>;
}

export const DlqDetailsModal: React.FC<DlqDetailsModalProps> = ({
  entry,
  onClose,
  onReplay,
}) => {
  const [copiedPayload, setCopiedPayload] = useState(false);
  const [isReplaying, setIsReplaying] = useState(false);

  if (!entry) return null;

  let prettyPayload = entry.payloadJson;
  try {
    prettyPayload = JSON.stringify(JSON.parse(entry.payloadJson), null, 2);
  } catch {
    // raw format
  }

  const handleCopyPayload = async () => {
    try {
      await navigator.clipboard.writeText(prettyPayload);
      setCopiedPayload(true);
      setTimeout(() => setCopiedPayload(false), 2000);
    } catch {
      // ignore
    }
  };

  const handleReplayClick = async () => {
    setIsReplaying(true);
    try {
      await onReplay(entry.id);
    } finally {
      setIsReplaying(false);
    }
  };

  const isExhausted = entry.status.toLowerCase() === 'exhausted';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
      <div className="bg-[#111827] border border-[#374151] rounded-2xl max-w-2xl w-full p-6 shadow-2xl relative text-gray-100 max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-start justify-between pb-4 border-b border-[#374151]">
          <div className="flex items-center gap-3">
            <div className={`w-10 h-10 rounded-xl flex items-center justify-center border ${
              isExhausted
                ? 'bg-rose-500/20 border-rose-500/30 text-rose-400'
                : 'bg-emerald-500/20 border-emerald-500/30 text-emerald-400'
            }`}>
              {isExhausted ? <AlertTriangle className="w-5 h-5" /> : <ShieldCheck className="w-5 h-5" />}
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-bold text-white">Webhook Delivery Diagnostics</h2>
                <span
                  className={`text-[10px] px-2 py-0.5 rounded-full font-semibold uppercase tracking-wider border ${
                    isExhausted
                      ? 'bg-rose-500/15 text-rose-400 border-rose-500/30'
                      : 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30'
                  }`}
                >
                  {entry.status}
                </span>
              </div>
              <p className="text-xs text-gray-400 font-mono mt-0.5">
                Entry ID: {entry.id}
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="text-gray-400 hover:text-white transition p-1.5 rounded-lg hover:bg-[#1f2937]"
            aria-label="Close modal"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Area */}
        <div className="flex-1 overflow-y-auto py-4 space-y-4">
          {/* Failure Alert Box */}
          <div className="p-3.5 rounded-xl bg-[#1f2937] border border-rose-500/30">
            <div className="flex items-center justify-between mb-1.5">
              <span className="text-xs font-semibold text-rose-400 uppercase tracking-wider flex items-center gap-1.5">
                <AlertTriangle className="w-3.5 h-3.5" />
                Failure Reason / Last Error
              </span>
              {entry.lastStatusCode && (
                <span className="text-xs font-mono font-bold px-2 py-0.5 rounded bg-rose-500/20 border border-rose-500/40 text-rose-300">
                  HTTP {entry.lastStatusCode}
                </span>
              )}
            </div>
            <p className="text-xs text-gray-200 font-mono break-all leading-relaxed">
              {entry.lastErrorMessage || 'Exhausted maximum retry delivery attempts.'}
            </p>
          </div>

          {/* Metadata Grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs bg-[#1f2937]/50 border border-[#374151] rounded-xl p-3.5">
            <div>
              <span className="text-gray-400 block mb-0.5 flex items-center gap-1">
                <Tag className="w-3.5 h-3.5 text-blue-400" />
                Event ID:
              </span>
              <span className="font-mono text-white font-medium">{entry.eventId}</span>
            </div>

            <div>
              <span className="text-gray-400 block mb-0.5 flex items-center gap-1">
                <Tag className="w-3.5 h-3.5 text-purple-400" />
                Payment ID:
              </span>
              <span className="font-mono text-white font-medium">{entry.paymentId}</span>
            </div>

            <div>
              <span className="text-gray-400 block mb-0.5 flex items-center gap-1">
                <Globe className="w-3.5 h-3.5 text-emerald-400" />
                Destination URL:
              </span>
              <span className="font-mono text-gray-200 break-all">{entry.destinationUrl}</span>
            </div>

            <div>
              <span className="text-gray-400 block mb-0.5 flex items-center gap-1">
                <Clock className="w-3.5 h-3.5 text-amber-400" />
                Delivery Attempts / Failed At:
              </span>
              <span className="text-gray-200">
                <strong className="text-white">{entry.deliveryAttempts} attempts</strong> ({new Date(entry.failedAt).toLocaleString()})
              </span>
            </div>

            {entry.replayedAt && (
              <div className="sm:col-span-2 pt-2 border-t border-[#374151]/50 text-emerald-400 flex items-center gap-1.5">
                <Check className="w-4 h-4 shrink-0" />
                <span>Replayed on: {new Date(entry.replayedAt).toLocaleString()}</span>
              </div>
            )}
          </div>

          {/* Payload JSON Section */}
          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs font-semibold text-gray-300 uppercase tracking-wider flex items-center gap-1.5">
                <FileCode className="w-3.5 h-3.5 text-blue-400" />
                Dispatched JSON Payload
              </label>
              <button
                onClick={handleCopyPayload}
                className="flex items-center gap-1 text-xs text-gray-400 hover:text-white transition px-2 py-0.5 rounded hover:bg-[#1f2937]"
              >
                {copiedPayload ? (
                  <>
                    <Check className="w-3 h-3 text-emerald-400" />
                    <span className="text-emerald-400">Copied!</span>
                  </>
                ) : (
                  <>
                    <Copy className="w-3 h-3" />
                    <span>Copy JSON</span>
                  </>
                )}
              </button>
            </div>
            <pre className="bg-[#0b0f19] border border-[#374151] rounded-xl p-3 text-xs font-mono text-blue-300 overflow-x-auto max-h-56 leading-relaxed select-all">
              {prettyPayload}
            </pre>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between pt-4 border-t border-[#374151]">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 rounded-lg text-xs font-medium text-gray-300 hover:bg-[#1f2937] transition"
          >
            Close
          </button>

          {isExhausted && (
            <button
              type="button"
              onClick={handleReplayClick}
              disabled={isReplaying}
              className="flex items-center gap-2 px-5 py-2 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white shadow-sm transition disabled:opacity-50"
            >
              {isReplaying ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Replaying...</span>
                </>
              ) : (
                <>
                  <RotateCw className="w-3.5 h-3.5" />
                  <span>Replay Webhook Now</span>
                </>
              )}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
