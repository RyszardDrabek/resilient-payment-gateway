import React, { useState } from 'react';
import { X, Key, Shield, AlertCircle, Loader2 } from 'lucide-react';
import { CreateApiKeyRequest } from '../../types/api';

interface GenerateApiKeyModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (data: CreateApiKeyRequest) => Promise<void>;
}

export const GenerateApiKeyModal: React.FC<GenerateApiKeyModalProps> = ({
  isOpen,
  onClose,
  onSubmit,
}) => {
  const [name, setName] = useState('');
  const [isLive, setIsLive] = useState(true);
  const [scopes, setScopes] = useState<string[]>(['payments:write', 'payments:read']);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleScopeToggle = (scope: string) => {
    setScopes((prev) =>
      prev.includes(scope) ? prev.filter((s) => s !== scope) : [...prev, scope]
    );
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    const trimmedName = name.trim();
    if (!trimmedName) {
      setError('Please provide a name for this API key.');
      return;
    }

    if (scopes.length === 0) {
      setError('At least one permission scope must be selected.');
      return;
    }

    setIsSubmitting(true);
    try {
      await onSubmit({
        name: trimmedName,
        scopes,
        isLive,
      });
      setName('');
      setScopes(['payments:write', 'payments:read']);
      setIsLive(true);
      onClose();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to generate API key.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/75 backdrop-blur-sm animate-fade-in">
      <div className="bg-[#111827] border border-[#374151] rounded-2xl max-w-lg w-full p-6 shadow-2xl relative text-gray-100">
        <button
          onClick={onClose}
          disabled={isSubmitting}
          className="absolute top-4 right-4 text-gray-400 hover:text-white transition p-1 rounded-lg hover:bg-[#1f2937]"
          aria-label="Close modal"
        >
          <X className="w-5 h-5" />
        </button>

        <div className="flex items-center gap-3 mb-5">
          <div className="w-10 h-10 rounded-xl bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400">
            <Key className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-white">Generate New API Key</h2>
            <p className="text-xs text-gray-400">
              Create an authentication key for backend API integration
            </p>
          </div>
        </div>

        {error && (
          <div className="mb-4 p-3 rounded-lg bg-rose-500/15 border border-rose-500/30 text-rose-300 text-xs flex items-center gap-2">
            <AlertCircle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold text-gray-300 uppercase tracking-wider mb-1.5">
              Key Name / Description <span className="text-rose-400">*</span>
            </label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Backend Production Checkout, Reporting Worker"
              className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3.5 py-2.5 text-sm text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition"
              disabled={isSubmitting}
              autoFocus
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-300 uppercase tracking-wider mb-1.5">
              Environment
            </label>
            <div className="grid grid-cols-2 gap-3">
              <button
                type="button"
                onClick={() => setIsLive(true)}
                className={`py-2 px-3 rounded-lg border text-xs font-semibold transition flex items-center justify-center gap-2 ${
                  isLive
                    ? 'bg-blue-600/20 border-blue-500 text-blue-300 shadow-sm'
                    : 'bg-[#1f2937] border-[#374151] text-gray-400 hover:text-white'
                }`}
              >
                <span className="w-2 h-2 rounded-full bg-emerald-400" />
                Live (`rpg_live_...`)
              </button>
              <button
                type="button"
                onClick={() => setIsLive(false)}
                className={`py-2 px-3 rounded-lg border text-xs font-semibold transition flex items-center justify-center gap-2 ${
                  !isLive
                    ? 'bg-blue-600/20 border-blue-500 text-blue-300 shadow-sm'
                    : 'bg-[#1f2937] border-[#374151] text-gray-400 hover:text-white'
                }`}
              >
                <span className="w-2 h-2 rounded-full bg-amber-400" />
                Test (`rpg_test_...`)
              </button>
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-gray-300 uppercase tracking-wider mb-1.5 flex items-center gap-1.5">
              <Shield className="w-3.5 h-3.5 text-blue-400" />
              Permission Scopes <span className="text-rose-400">*</span>
            </label>
            <div className="space-y-2 bg-[#1f2937]/50 border border-[#374151] rounded-xl p-3">
              <label className="flex items-start gap-3 cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={scopes.includes('payments:write')}
                  onChange={() => handleScopeToggle('payments:write')}
                  className="mt-0.5 rounded border-gray-600 bg-gray-800 text-blue-600 focus:ring-0 focus:outline-none"
                  disabled={isSubmitting}
                />
                <div>
                  <div className="text-xs font-semibold text-white">payments:write</div>
                  <div className="text-[11px] text-gray-400">
                    Authorize, capture, refund, and cancel payment transactions
                  </div>
                </div>
              </label>

              <label className="flex items-start gap-3 cursor-pointer select-none pt-2 border-t border-[#374151]/50">
                <input
                  type="checkbox"
                  checked={scopes.includes('payments:read')}
                  onChange={() => handleScopeToggle('payments:read')}
                  className="mt-0.5 rounded border-gray-600 bg-gray-800 text-blue-600 focus:ring-0 focus:outline-none"
                  disabled={isSubmitting}
                />
                <div>
                  <div className="text-xs font-semibold text-white">payments:read</div>
                  <div className="text-[11px] text-gray-400">
                    Query payment status, history, and transaction receipts
                  </div>
                </div>
              </label>
            </div>
          </div>

          <div className="flex items-center justify-end gap-3 pt-3 border-t border-[#374151]">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-4 py-2 rounded-lg text-xs font-medium text-gray-300 hover:bg-[#1f2937] transition"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white shadow-sm transition disabled:opacity-50"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Generating...</span>
                </>
              ) : (
                <>
                  <Key className="w-3.5 h-3.5" />
                  <span>Generate Key</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
