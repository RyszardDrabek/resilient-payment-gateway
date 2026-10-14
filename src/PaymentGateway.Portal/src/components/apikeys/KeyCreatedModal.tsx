import React, { useState } from 'react';
import { Check, Copy, AlertTriangle, ShieldCheck } from 'lucide-react';
import { MerchantApiKeyCreatedDto } from '../../types/api';

interface KeyCreatedModalProps {
  createdKey: MerchantApiKeyCreatedDto | null;
  onClose: () => void;
}

export const KeyCreatedModal: React.FC<KeyCreatedModalProps> = ({ createdKey, onClose }) => {
  const [copied, setCopied] = useState(false);

  if (!createdKey) return null;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(createdKey.rawKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 3000);
    } catch {
      // fallback
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
      <div className="bg-[#111827] border border-[#374151] rounded-2xl max-w-lg w-full p-6 shadow-2xl relative text-gray-100">
        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-xl bg-emerald-500/20 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
            <ShieldCheck className="w-5 h-5" />
          </div>
          <div>
            <h2 className="text-lg font-bold text-white">API Key Created Successfully</h2>
            <p className="text-xs text-gray-400">
              Key name: <span className="text-white font-medium">{createdKey.name}</span>
            </p>
          </div>
        </div>

        {/* Warning Banner */}
        <div className="mb-4 p-3.5 rounded-xl bg-amber-500/15 border border-amber-500/30 text-amber-200 text-xs flex items-start gap-2.5">
          <AlertTriangle className="w-4 h-4 shrink-0 text-amber-400 mt-0.5" />
          <div>
            <span className="font-bold">Save this key now!</span> For security reasons, this raw API key will{' '}
            <span className="font-semibold underline">never be shown again</span>. If lost, you will need to revoke it and generate a new key.
          </div>
        </div>

        {/* Raw Key Display & Copy */}
        <div className="mb-4">
          <label className="block text-xs font-semibold text-gray-300 uppercase tracking-wider mb-1.5">
            Secret API Key
          </label>
          <div className="flex items-center gap-2 bg-[#1f2937] border border-[#374151] rounded-xl p-2.5">
            <input
              type="text"
              readOnly
              value={createdKey.rawKey}
              className="bg-transparent text-emerald-400 font-mono text-xs flex-1 outline-none select-all"
            />
            <button
              onClick={handleCopy}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition ${
                copied
                  ? 'bg-emerald-600 text-white'
                  : 'bg-blue-600 hover:bg-blue-500 text-white'
              }`}
            >
              {copied ? (
                <>
                  <Check className="w-3.5 h-3.5" />
                  <span>Copied!</span>
                </>
              ) : (
                <>
                  <Copy className="w-3.5 h-3.5" />
                  <span>Copy Key</span>
                </>
              )}
            </button>
          </div>
        </div>

        {/* Details Summary */}
        <div className="bg-[#1f2937]/50 border border-[#374151] rounded-xl p-3 mb-5 space-y-2 text-xs">
          <div className="flex justify-between items-center text-gray-400">
            <span>Prefix ID:</span>
            <span className="font-mono text-gray-200 font-semibold">{createdKey.prefix}</span>
          </div>
          <div className="flex justify-between items-center text-gray-400">
            <span>Granted Scopes:</span>
            <div className="flex gap-1.5 flex-wrap justify-end">
              {createdKey.scopes.map((scope) => (
                <span
                  key={scope}
                  className="px-2 py-0.5 rounded bg-blue-500/15 border border-blue-500/30 text-blue-300 text-[10px] font-mono font-medium"
                >
                  {scope}
                </span>
              ))}
            </div>
          </div>
        </div>

        <div className="flex justify-end pt-3 border-t border-[#374151]">
          <button
            onClick={onClose}
            className="w-full sm:w-auto px-6 py-2 rounded-lg text-xs font-semibold bg-[#1f2937] hover:bg-[#374151] text-white transition text-center"
          >
            I Have Saved My Secret Key
          </button>
        </div>
      </div>
    </div>
  );
};
