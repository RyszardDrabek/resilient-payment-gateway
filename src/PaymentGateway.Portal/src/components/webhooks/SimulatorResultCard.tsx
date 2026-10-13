import React, { useState } from 'react';
import { TestWebhookDispatchResultDto } from '../../types/api';
import { Badge } from '../common/Badge';

interface SimulatorResultCardProps {
  result: TestWebhookDispatchResultDto;
}

export const SimulatorResultCard: React.FC<SimulatorResultCardProps> = ({ result }) => {
  const [activeTab, setActiveTab] = useState<'headers' | 'payload' | 'response'>('payload');
  const [copiedSig, setCopiedSig] = useState(false);

  const handleCopySig = async () => {
    try {
      await navigator.clipboard.writeText(result.signature);
      setCopiedSig(true);
      setTimeout(() => setCopiedSig(false), 2000);
    } catch {
      // Fallback
    }
  };

  const formattedPayload = (() => {
    try {
      return JSON.stringify(JSON.parse(result.requestPayload), null, 2);
    } catch {
      return result.requestPayload;
    }
  })();

  const isSuccess = result.success;
  const statusDisplay = result.statusCode ? `${result.statusCode}` : 'Failed / Network Error';

  return (
    <div className="bg-gray-950 border border-gray-800 rounded-xl p-5 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-gray-800/80 pb-3">
        <div className="flex items-center gap-2.5">
          <Badge variant={isSuccess ? 'success' : 'destructive'}>
            {statusDisplay}
          </Badge>
          <span className="text-xs font-mono text-cyan-400 bg-cyan-500/10 px-2 py-0.5 rounded border border-cyan-500/20">
            {result.latencyMs} ms
          </span>
          <span className="text-xs text-gray-400 font-mono truncate max-w-xs">
            {result.endpointUrl}
          </span>
        </div>

        <span className="text-[11px] font-mono text-gray-400">
          Event ID: <strong className="text-gray-200">{result.eventId}</strong>
        </span>
      </div>

      {result.errorMessage && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg text-xs text-rose-300">
          <strong className="font-semibold block mb-0.5">Delivery Diagnostic:</strong>
          {result.errorMessage}
        </div>
      )}

      {/* Signature Bar */}
      <div className="bg-gray-900 border border-gray-800 rounded-lg p-3">
        <div className="flex items-center justify-between mb-1.5">
          <span className="text-xs font-medium text-gray-300">Computed X-Signature-SHA256 Header</span>
          <button
            type="button"
            onClick={handleCopySig}
            className="text-[11px] text-blue-400 hover:text-blue-300 transition-colors font-mono cursor-pointer"
          >
            {copiedSig ? 'Copied Signature!' : 'Copy Hex Signature'}
          </button>
        </div>
        <div className="font-mono text-xs text-purple-300 bg-gray-950 px-3 py-1.5 rounded border border-gray-800/80 break-all select-all">
          {result.signature}
        </div>
      </div>

      {/* Inspector Tabs */}
      <div>
        <div className="flex gap-2 border-b border-gray-800 pb-2 mb-3">
          <button
            type="button"
            onClick={() => setActiveTab('payload')}
            className={`px-3 py-1 text-xs font-medium rounded-md transition-colors cursor-pointer ${
              activeTab === 'payload'
                ? 'bg-blue-600/20 text-blue-400 border border-blue-500/30'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Request Payload (JSON)
          </button>
          <button
            type="button"
            onClick={() => setActiveTab('headers')}
            className={`px-3 py-1 text-xs font-medium rounded-md transition-colors cursor-pointer ${
              activeTab === 'headers'
                ? 'bg-blue-600/20 text-blue-400 border border-blue-500/30'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Request Headers
          </button>
          <button
            type="button"
            onClick={() => setActiveTab('response')}
            className={`px-3 py-1 text-xs font-medium rounded-md transition-colors cursor-pointer ${
              activeTab === 'response'
                ? 'bg-blue-600/20 text-blue-400 border border-blue-500/30'
                : 'text-gray-400 hover:text-gray-200'
            }`}
          >
            Receiver Response Body
          </button>
        </div>

        {activeTab === 'payload' && (
          <pre className="text-xs font-mono bg-gray-900 border border-gray-800 rounded-lg p-3 text-emerald-400 overflow-x-auto max-h-60 leading-relaxed">
            {formattedPayload}
          </pre>
        )}

        {activeTab === 'headers' && (
          <div className="bg-gray-900 border border-gray-800 rounded-lg p-3 text-xs font-mono space-y-1.5 max-h-60 overflow-y-auto">
            {Object.entries(result.requestHeaders).map(([key, value]) => (
              <div key={key} className="flex gap-2 text-gray-300">
                <span className="text-gray-400 font-semibold select-none">{key}:</span>
                <span className="text-gray-200 break-all select-all">{value}</span>
              </div>
            ))}
          </div>
        )}

        {activeTab === 'response' && (
          <pre className="text-xs font-mono bg-gray-900 border border-gray-800 rounded-lg p-3 text-gray-300 overflow-x-auto max-h-60 leading-relaxed">
            {result.responseBody || '(No response body returned by destination endpoint)'}
          </pre>
        )}
      </div>
    </div>
  );
};
