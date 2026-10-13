import React, { useState } from 'react';

interface SecretKeyCardProps {
  secretKey: string;
  isLoading: boolean;
  isRotating: boolean;
  onRotate: () => Promise<void>;
}

export const SecretKeyCard: React.FC<SecretKeyCardProps> = ({
  secretKey,
  isLoading,
  isRotating,
  onRotate,
}) => {
  const [showSecret, setShowSecret] = useState(false);
  const [isCopied, setIsCopied] = useState(false);
  const [showModal, setShowModal] = useState(false);
  const [rotateError, setRotateError] = useState<string | null>(null);

  const handleCopy = async () => {
    if (!secretKey) return;
    try {
      await navigator.clipboard.writeText(secretKey);
      setIsCopied(true);
      setTimeout(() => setIsCopied(false), 2000);
    } catch {
      // Fallback
    }
  };

  const handleConfirmRotate = async () => {
    setRotateError(null);
    try {
      await onRotate();
      setShowModal(false);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setRotateError(err.message);
      } else {
        setRotateError('Failed to rotate secret key.');
      }
    }
  };

  const maskedValue = secretKey
    ? (secretKey.startsWith('whsec_') ? 'whsec_' : '') + '•'.repeat(Math.max(16, secretKey.length - 6))
    : '••••••••••••••••••••••••';

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6 shadow-sm">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-gray-100">Webhook HMAC Secret Key</h2>
          <p className="text-xs text-gray-400 mt-0.5">
            Used to compute and verify the <code className="text-blue-400 font-mono text-[11px]">X-Signature-SHA256</code> HTTP header.
          </p>
        </div>
        <span className="text-xs font-mono text-purple-400 bg-purple-500/10 px-2 py-0.5 rounded border border-purple-500/20">
          HMAC-SHA256
        </span>
      </div>

      <div className="space-y-4">
        <div>
          <label className="block text-xs font-medium text-gray-300 mb-1.5">
            Signing Secret
          </label>
          <div className="flex items-center gap-2">
            <div className="flex-1 px-3.5 py-2 text-xs bg-gray-950 border border-gray-700 rounded-lg text-gray-200 font-mono select-all overflow-x-auto whitespace-nowrap">
              {showSecret ? secretKey : maskedValue}
            </div>

            <button
              type="button"
              onClick={() => setShowSecret(!showSecret)}
              disabled={isLoading || !secretKey}
              className="px-3 py-2 text-xs font-medium rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 border border-gray-700 transition-colors disabled:opacity-50 cursor-pointer"
            >
              {showSecret ? 'Hide' : 'Show'}
            </button>

            <button
              type="button"
              onClick={handleCopy}
              disabled={isLoading || !secretKey}
              className="px-3 py-2 text-xs font-semibold rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-200 border border-gray-700 transition-colors disabled:opacity-50 flex items-center gap-1 cursor-pointer"
            >
              {isCopied ? 'Copied!' : 'Copy'}
            </button>
          </div>
        </div>

        <div className="flex items-center justify-between pt-2 border-t border-gray-800/80">
          <p className="text-[11px] text-gray-400 max-w-sm">
            Rotate this key if you suspect it has been compromised. Old signatures will cease to validate immediately.
          </p>
          <button
            type="button"
            onClick={() => setShowModal(true)}
            disabled={isLoading || isRotating}
            className="px-3.5 py-1.5 text-xs font-medium rounded-lg bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border border-rose-500/30 transition-colors disabled:opacity-50 cursor-pointer"
          >
            Rotate Secret Key
          </button>
        </div>
      </div>

      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-xs p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-xl max-w-md w-full p-6 shadow-2xl animate-in fade-in-0 zoom-in-95">
            <div className="flex items-start gap-3 mb-4">
              <div className="p-2 rounded-lg bg-rose-500/15 border border-rose-500/30 text-rose-400">
                <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
              </div>
              <div>
                <h3 className="text-base font-semibold text-gray-100">Rotate Signing Secret Key?</h3>
                <p className="text-xs text-gray-400 mt-1 leading-relaxed">
                  Generating a new secret key will immediately invalidate the previous key. Any webhook endpoints verifying signatures with the old key will reject subsequent notifications until updated.
                </p>
              </div>
            </div>

            {rotateError && (
              <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-xs text-rose-400">
                {rotateError}
              </div>
            )}

            <div className="flex items-center justify-end gap-3 pt-2">
              <button
                type="button"
                onClick={() => setShowModal(false)}
                disabled={isRotating}
                className="px-3.5 py-2 text-xs font-medium rounded-lg bg-gray-800 hover:bg-gray-700 text-gray-300 border border-gray-700 transition-colors disabled:opacity-50 cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleConfirmRotate}
                disabled={isRotating}
                className="px-4 py-2 text-xs font-semibold rounded-lg bg-rose-600 hover:bg-rose-500 text-white disabled:opacity-50 transition-colors flex items-center gap-2 cursor-pointer"
              >
                {isRotating && (
                  <span className="inline-block w-3 h-3 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                )}
                {isRotating ? 'Rotating Key...' : 'Yes, Rotate Secret'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
