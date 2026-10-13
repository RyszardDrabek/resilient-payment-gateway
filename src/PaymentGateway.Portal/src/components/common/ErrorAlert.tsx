import React from 'react';
import { AlertCircle, RotateCcw, ShieldAlert } from 'lucide-react';
import { PaymentProblemDetails } from '../../types/api';

interface ErrorAlertProps {
  error: Error | null;
  problem?: PaymentProblemDetails;
  onRetry?: () => void;
  onSwitchToOps?: () => void;
}

export const ErrorAlert: React.FC<ErrorAlertProps> = ({
  error,
  problem,
  onRetry,
  onSwitchToOps,
}) => {
  if (!error) return null;

  const status = problem?.status || (error as { status?: number }).status;
  const isAuthError = status === 401 || status === 403;

  return (
    <div className={`p-4 rounded-lg border mb-6 ${
      isAuthError
        ? 'bg-amber-950/40 border-amber-500/50 text-amber-200'
        : 'bg-rose-950/40 border-rose-500/50 text-rose-200'
    }`}>
      <div className="flex items-start gap-3">
        {isAuthError ? (
          <ShieldAlert className="w-5 h-5 text-amber-400 mt-0.5 shrink-0" />
        ) : (
          <AlertCircle className="w-5 h-5 text-rose-400 mt-0.5 shrink-0" />
        )}

        <div className="flex-1 text-sm">
          <div className="font-semibold text-base mb-1">
            {problem?.title || (isAuthError ? 'Access Denied' : 'Request Failed')}
            {status ? ` (${status})` : ''}
          </div>

          <p className="mb-2 text-gray-300">
            {problem?.detail || error.message}
          </p>

          {problem?.correlationId && (
            <p className="font-mono text-xs text-gray-400 mb-2">
              Correlation ID: {problem.correlationId}
            </p>
          )}

          {isAuthError && (
            <div className="mt-2 p-2.5 bg-black/30 rounded border border-amber-500/30 text-xs">
              <span className="font-medium text-amber-300">Operator Access Required: </span>
              This console requires the <code className="font-mono text-white">ops</code> or <code className="font-mono text-white">risk</code> role.
              {onSwitchToOps && (
                <button
                  onClick={onSwitchToOps}
                  className="ml-3 px-2 py-0.5 bg-amber-500 hover:bg-amber-600 text-black font-semibold rounded text-xs transition"
                >
                  Switch to Ops Role
                </button>
              )}
            </div>
          )}
        </div>

        {onRetry && (
          <button
            onClick={onRetry}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded bg-[#1f2937] hover:bg-[#374151] text-xs font-semibold text-white border border-[#374151] transition shrink-0"
          >
            <RotateCcw className="w-3.5 h-3.5" />
            Retry
          </button>
        )}
      </div>
    </div>
  );
};
