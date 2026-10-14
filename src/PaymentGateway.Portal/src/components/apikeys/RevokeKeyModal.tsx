import React, { useState } from 'react';
import { X, AlertOctagon, Loader2 } from 'lucide-react';
import { MerchantApiKeyDto } from '../../types/api';

interface RevokeKeyModalProps {
  keyToRevoke: MerchantApiKeyDto | null;
  onClose: () => void;
  onConfirm: (keyId: string) => Promise<void>;
}

export const RevokeKeyModal: React.FC<RevokeKeyModalProps> = ({
  keyToRevoke,
  onClose,
  onConfirm,
}) => {
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!keyToRevoke) return null;

  const handleRevoke = async () => {
    setIsSubmitting(true);
    setError(null);
    try {
      await onConfirm(keyToRevoke.id);
      onClose();
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to revoke API key.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
      <div className="bg-[#111827] border border-[#374151] rounded-2xl max-w-md w-full p-6 shadow-2xl relative text-gray-100">
        <button
          onClick={onClose}
          disabled={isSubmitting}
          className="absolute top-4 right-4 text-gray-400 hover:text-white transition p-1 rounded-lg hover:bg-[#1f2937]"
          aria-label="Close modal"
        >
          <X className="w-5 h-5" />
        </button>

        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-xl bg-rose-500/20 border border-rose-500/30 flex items-center justify-center text-rose-400">
            <AlertOctagon className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-white">Revoke API Key</h2>
            <p className="text-xs text-gray-400">This action cannot be undone</p>
          </div>
        </div>

        {error && (
          <div className="mb-4 p-3 rounded-lg bg-rose-500/15 border border-rose-500/30 text-rose-300 text-xs">
            {error}
          </div>
        )}

        <div className="bg-[#1f2937]/50 border border-[#374151] rounded-xl p-3 mb-4 space-y-1.5 text-xs">
          <div className="text-gray-400">
            Key Name:{' '}
            <span className="text-white font-semibold">{keyToRevoke.name}</span>
          </div>
          <div className="text-gray-400">
            Prefix ID:{' '}
            <span className="font-mono text-gray-200">{keyToRevoke.prefix}</span>
          </div>
        </div>

        <p className="text-xs text-gray-300 leading-relaxed mb-5">
          Are you sure you want to revoke this API key? Any backend service, script, or integration currently using this key will immediately be rejected with <span className="text-rose-400 font-semibold font-mono">401 Unauthorized</span> or <span className="text-rose-400 font-semibold font-mono">403 Forbidden</span>.
        </p>

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
            type="button"
            onClick={handleRevoke}
            disabled={isSubmitting}
            className="flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold bg-rose-600 hover:bg-rose-500 text-white shadow-sm transition disabled:opacity-50"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                <span>Revoking...</span>
              </>
            ) : (
              <span>Revoke Key Immediately</span>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};
