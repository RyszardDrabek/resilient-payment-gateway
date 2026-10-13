import React, { useState, useEffect } from 'react';
import { Badge } from '../common/Badge';

interface WebhookConfigCardProps {
  partyId: string;
  initialEndpointUrl: string;
  initialIsActive: boolean;
  isLoading: boolean;
  isSaving: boolean;
  onSave: (endpointUrl: string, isActive: boolean) => Promise<void>;
}

export const WebhookConfigCard: React.FC<WebhookConfigCardProps> = ({
  partyId,
  initialEndpointUrl,
  initialIsActive,
  isLoading,
  isSaving,
  onSave,
}) => {
  const [endpointUrl, setEndpointUrl] = useState(initialEndpointUrl);
  const [isActive, setIsActive] = useState(initialIsActive);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setEndpointUrl(initialEndpointUrl);
    setIsActive(initialIsActive);
  }, [initialEndpointUrl, initialIsActive]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!endpointUrl.trim()) {
      setError('Endpoint URL is required.');
      return;
    }

    try {
      const url = new URL(endpointUrl.trim());
      if (url.protocol !== 'http:' && url.protocol !== 'https:') {
        setError('Endpoint URL must use HTTP or HTTPS protocol.');
        return;
      }
    } catch {
      setError('Please provide a valid, well-formed URL (e.g. https://api.merchant.com/webhooks).');
      return;
    }

    try {
      await onSave(endpointUrl.trim(), isActive);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to save webhook configuration.');
      }
    }
  };

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6 shadow-sm">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-gray-100">Webhook Notification Endpoint</h2>
          <p className="text-xs text-gray-400 mt-0.5">
            Target URL where payment lifecycle notifications are delivered via HTTP POST.
          </p>
        </div>
        <Badge variant={isActive ? 'success' : 'muted'}>
          {isActive ? 'Active Delivery' : 'Inactive'}
        </Badge>
      </div>

      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <div className="flex items-center justify-between mb-1.5">
            <label htmlFor="webhook-url-input" className="block text-xs font-medium text-gray-300">
              Callback Endpoint URL
            </label>
            <span className="text-[11px] text-gray-400 font-mono">Party: {partyId}</span>
          </div>
          <input
            id="webhook-url-input"
            type="url"
            value={endpointUrl}
            onChange={(e) => setEndpointUrl(e.target.value)}
            disabled={isLoading || isSaving}
            placeholder="https://api.yourdomain.com/webhooks/payments"
            className="w-full px-3.5 py-2 text-sm bg-gray-950 border border-gray-700 rounded-lg text-gray-100 placeholder-gray-500 focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 font-mono transition-colors disabled:opacity-50"
          />
        </div>

        <div className="flex items-center justify-between p-3 bg-gray-950/60 border border-gray-800 rounded-lg">
          <div>
            <span className="block text-xs font-medium text-gray-200">Delivery Status</span>
            <span className="text-[11px] text-gray-400">
              When disabled, outbound notification dispatches are suspended for this party.
            </span>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={isActive}
            onClick={() => setIsActive(!isActive)}
            disabled={isLoading || isSaving}
            className={`relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none disabled:opacity-50 ${
              isActive ? 'bg-blue-600' : 'bg-gray-700'
            }`}
          >
            <span
              className={`pointer-events-none inline-block h-5 w-5 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out ${
                isActive ? 'translate-x-5' : 'translate-x-0'
              }`}
            />
          </button>
        </div>

        {error && (
          <div className="p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-xs text-rose-400">
            {error}
          </div>
        )}

        <div className="flex justify-end pt-1">
          <button
            type="submit"
            disabled={isLoading || isSaving}
            className="px-4 py-2 text-xs font-semibold rounded-lg bg-blue-600 hover:bg-blue-500 text-white disabled:opacity-50 transition-colors flex items-center gap-2 cursor-pointer"
          >
            {isSaving && (
              <span className="inline-block w-3.5 h-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
            )}
            {isSaving ? 'Saving Changes...' : 'Save Configuration'}
          </button>
        </div>
      </form>
    </div>
  );
};
