import React, { useState, useEffect } from 'react';
import { TestWebhookDispatchResultDto } from '../../types/api';
import { SimulatorResultCard } from './SimulatorResultCard';

interface EventSimulatorPanelProps {
  configuredEndpointUrl: string;
  isDispatching: boolean;
  onDispatch: (endpointUrl?: string, eventType?: string) => Promise<TestWebhookDispatchResultDto>;
}

export const EventSimulatorPanel: React.FC<EventSimulatorPanelProps> = ({
  configuredEndpointUrl,
  isDispatching,
  onDispatch,
}) => {
  const [eventType, setEventType] = useState('payment.settled');
  const [customUrl, setCustomUrl] = useState(configuredEndpointUrl);
  const [useCustomUrl, setUseCustomUrl] = useState(false);
  const [dispatchResult, setDispatchResult] = useState<TestWebhookDispatchResultDto | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  useEffect(() => {
    if (!useCustomUrl) {
      setCustomUrl(configuredEndpointUrl);
    }
  }, [configuredEndpointUrl, useCustomUrl]);

  const handleTriggerDispatch = async (e: React.FormEvent) => {
    e.preventDefault();
    setLocalError(null);

    const targetUrl = useCustomUrl ? customUrl.trim() : configuredEndpointUrl.trim();
    if (!targetUrl) {
      setLocalError('No endpoint URL configured. Please set an endpoint above or specify a custom URL.');
      return;
    }

    try {
      const result = await onDispatch(targetUrl, eventType);
      setDispatchResult(result);
    } catch (err: unknown) {
      if (err instanceof Error) {
        setLocalError(err.message);
      } else {
        setLocalError('Webhook test dispatch failed unexpectedly.');
      }
    }
  };

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-6 shadow-sm flex flex-col h-full">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-gray-100">Webhook Event Simulator</h2>
          <p className="text-xs text-gray-400 mt-0.5">
            Deliver signed mock events to your receiver to verify HMAC signature validation and handler logic.
          </p>
        </div>
        <span className="text-xs font-mono text-cyan-400 bg-cyan-500/10 px-2 py-0.5 rounded border border-cyan-500/20">
          Sandbox Simulator
        </span>
      </div>

      <form onSubmit={handleTriggerDispatch} className="space-y-4 mb-6">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label htmlFor="event-type-select" className="block text-xs font-medium text-gray-300 mb-1.5">
              Simulated Event Type
            </label>
            <select
              id="event-type-select"
              value={eventType}
              onChange={(e) => setEventType(e.target.value)}
              disabled={isDispatching}
              className="w-full px-3 py-2 text-xs bg-gray-950 border border-gray-700 rounded-lg text-gray-200 focus:outline-none focus:border-blue-500 font-mono transition-colors disabled:opacity-50"
            >
              <option value="payment.settled">payment.settled (Settlement Complete)</option>
              <option value="payment.authorized">payment.authorized (Auth Captured)</option>
              <option value="payment.failed">payment.failed (Processing Declined)</option>
            </select>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label htmlFor="custom-url-input" className="block text-xs font-medium text-gray-300">
                Destination Target
              </label>
              <button
                type="button"
                onClick={() => setUseCustomUrl(!useCustomUrl)}
                className="text-[11px] text-blue-400 hover:text-blue-300 transition-colors font-mono cursor-pointer"
              >
                {useCustomUrl ? 'Use Configured URL' : 'Custom URL'}
              </button>
            </div>
            {useCustomUrl ? (
              <input
                id="custom-url-input"
                type="url"
                value={customUrl}
                onChange={(e) => setCustomUrl(e.target.value)}
                disabled={isDispatching}
                placeholder="https://webhook.site/..."
                className="w-full px-3 py-2 text-xs bg-gray-950 border border-gray-700 rounded-lg text-gray-100 placeholder-gray-500 font-mono focus:outline-none focus:border-blue-500 disabled:opacity-50"
              />
            ) : (
              <div className="px-3 py-2 text-xs bg-gray-950/70 border border-gray-800 rounded-lg text-gray-400 font-mono truncate">
                {configuredEndpointUrl || '(No endpoint configured)'}
              </div>
            )}
          </div>
        </div>

        {localError && (
          <div className="p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-xs text-rose-400">
            {localError}
          </div>
        )}

        <div className="flex justify-end pt-1">
          <button
            type="submit"
            disabled={isDispatching}
            className="px-5 py-2.5 text-xs font-semibold rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white disabled:opacity-50 transition-colors flex items-center gap-2 cursor-pointer shadow-sm"
          >
            {isDispatching && (
              <span className="inline-block w-3.5 h-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
            )}
            {isDispatching ? 'Transmitting Simulated Event...' : 'Dispatch Test Event'}
          </button>
        </div>
      </form>

      {/* Results Section */}
      <div className="flex-1">
        {dispatchResult ? (
          <div>
            <h3 className="text-xs font-semibold uppercase tracking-wider text-gray-400 mb-3">
              Last Dispatch Diagnostics
            </h3>
            <SimulatorResultCard result={dispatchResult} />
          </div>
        ) : (
          <div className="border border-dashed border-gray-800 rounded-xl p-8 text-center flex flex-col items-center justify-center h-48 bg-gray-950/40">
            <svg className="w-8 h-8 text-gray-600 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
            <p className="text-xs text-gray-400 font-medium">Ready for Simulation</p>
            <p className="text-[11px] text-gray-400 mt-1 max-w-xs">
              Click &ldquo;Dispatch Test Event&rdquo; to send a test payload with calculated HMAC-SHA256 signature and inspect the response.
            </p>
          </div>
        )}
      </div>
    </div>
  );
};
