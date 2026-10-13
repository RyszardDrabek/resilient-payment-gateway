import React, { useState, useEffect } from 'react';
import { AlertOctagon, CheckCircle2, ArrowUpRight, X, Loader2 } from 'lucide-react';
import { RiskDisposition, RiskFlagDto } from '../../types/api';

interface DispositionModalProps {
  isOpen: boolean;
  onClose: () => void;
  flag: RiskFlagDto | null;
  disposition: RiskDisposition | null;
  onSubmit: (disposition: RiskDisposition, notes: string) => Promise<void>;
  isSubmitting: boolean;
}

export const DispositionModal: React.FC<DispositionModalProps> = ({
  isOpen,
  onClose,
  flag,
  disposition,
  onSubmit,
  isSubmitting,
}) => {
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      setNotes('');
      setError(null);
    }
  }, [isOpen]);

  if (!isOpen || !flag || !disposition) return null;

  const getDispositionConfig = () => {
    switch (disposition) {
      case 'confirmed':
        return {
          title: 'Confirm Fraudulent Transaction',
          subtitle: 'The flagged payment will be marked as confirmed fraud in compliance records.',
          buttonLabel: 'Confirm Fraud',
          buttonClass: 'bg-rose-600 hover:bg-rose-700 text-white shadow-rose-950/50',
          icon: <AlertOctagon className="w-5 h-5 text-rose-400" />,
          badgeVariant: 'bg-rose-500/15 border-rose-500/30 text-rose-300',
        };
      case 'false_positive':
        return {
          title: 'Mark as False Positive',
          subtitle: 'The flagged anomaly was verified as legitimate merchant activity.',
          buttonLabel: 'Approve as False Positive',
          buttonClass: 'bg-emerald-600 hover:bg-emerald-700 text-white shadow-emerald-950/50',
          icon: <CheckCircle2 className="w-5 h-5 text-emerald-400" />,
          badgeVariant: 'bg-emerald-500/15 border-emerald-500/30 text-emerald-300',
        };
      case 'escalated':
        return {
          title: 'Escalate to Senior Review',
          subtitle: 'The case requires deeper AML investigation or supervisor sign-off.',
          buttonLabel: 'Escalate Case',
          buttonClass: 'bg-amber-600 hover:bg-amber-700 text-white shadow-amber-950/50',
          icon: <ArrowUpRight className="w-5 h-5 text-amber-400" />,
          badgeVariant: 'bg-amber-500/15 border-amber-500/30 text-amber-300',
        };
    }
  };

  const config = getDispositionConfig();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    try {
      await onSubmit(disposition, notes.trim());
      onClose();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to apply disposition.');
      }
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-in fade-in duration-150">
      <div className="bg-[#111827] border border-[#374151] rounded-xl max-w-lg w-full p-6 shadow-2xl relative text-left">
        {/* Close Button */}
        <button
          onClick={onClose}
          disabled={isSubmitting}
          className="absolute top-4 right-4 text-gray-400 hover:text-white p-1 rounded-md transition"
        >
          <X className="w-5 h-5" />
        </button>

        {/* Modal Header */}
        <div className="flex items-center gap-3 mb-4">
          <div className="p-2.5 rounded-lg bg-[#1f2937] border border-[#374151]">
            {config.icon}
          </div>
          <div>
            <h3 className="text-base font-bold text-white leading-snug">{config.title}</h3>
            <p className="text-xs text-gray-400 mt-0.5">{config.subtitle}</p>
          </div>
        </div>

        {/* Flag Case Summary */}
        <div className="bg-[#182234] border border-[#374151] rounded-lg p-3 mb-4 text-xs space-y-1.5">
          <div className="flex justify-between items-center">
            <span className="text-gray-400">Payment ID:</span>
            <span className="font-mono text-white font-semibold">{flag.paymentId}</span>
          </div>
          <div className="flex justify-between items-center">
            <span className="text-gray-400">Flag ID:</span>
            <span className="font-mono text-gray-300">{flag.id}</span>
          </div>
          <div className="flex justify-between items-center">
            <span className="text-gray-400">Party ID:</span>
            <span className="font-mono text-gray-300">{flag.partyId}</span>
          </div>
          <div className="pt-1 border-t border-[#374151]/50 text-gray-300">
            <span className="text-gray-400">Reason: </span>
            {flag.reason}
          </div>
        </div>

        {error && (
          <div className="p-3 bg-rose-950/50 border border-rose-500/50 text-rose-300 text-xs rounded mb-4">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold text-gray-300 mb-1.5">
              Auditor / Reviewer Notes <span className="text-gray-500 font-normal">(Optional audit trail)</span>
            </label>
            <textarea
              rows={3}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder="e.g. Cross-referenced with customer KYC; verified chargeback claim..."
              className="w-full bg-[#1f2937] border border-[#374151] rounded-lg p-2.5 text-xs text-white placeholder-gray-500 focus:outline-none focus:border-rose-500 transition resize-none"
              disabled={isSubmitting}
            />
          </div>

          <div className="flex items-center justify-end gap-2 pt-2 border-t border-[#374151]">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-3 py-2 rounded-lg text-xs font-medium text-gray-400 hover:text-white bg-[#1f2937] hover:bg-[#374151] transition border border-[#374151]"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className={`px-4 py-2 rounded-lg text-xs font-semibold transition flex items-center gap-1.5 shadow-md ${config.buttonClass} disabled:opacity-50`}
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  Submitting...
                </>
              ) : (
                config.buttonLabel
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
