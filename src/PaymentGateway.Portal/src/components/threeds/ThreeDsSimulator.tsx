import React, { useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { createTestPayment, submitThreeDsResult } from '../../services/apiClient';
import { PaymentDto } from '../../types/api';
import {
  ShieldAlert,
  ShieldCheck,
  XCircle,
  Play,
  RotateCw,
  ExternalLink,
  CheckCircle2,
  AlertCircle,
  Clock,
  ArrowRight
} from 'lucide-react';

export const ThreeDsSimulator: React.FC = () => {
  const { token } = useAuth();
  const [partyId, setPartyId] = useState('cardholder_challenge_0002');
  const [amount, setAmount] = useState<number>(4500);
  const [currency, setCurrency] = useState('EUR');
  const [settlementChannel, setSettlementChannel] = useState('ADYEN');
  const [loading, setLoading] = useState(false);
  const [resolving, setResolving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currentPayment, setCurrentPayment] = useState<PaymentDto | null>(null);

  const handleCreatePayment = async (customPartyId?: string) => {
    const targetPartyId = customPartyId || partyId;
    setLoading(true);
    setError(null);
    setCurrentPayment(null);

    try {
      const payment = await createTestPayment(token || '', {
        partyId: targetPartyId,
        amount,
        currency,
        settlementChannel: settlementChannel || undefined,
        idempotencyKey: `sim-3ds-${Date.now()}`
      });
      setCurrentPayment(payment);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to initiate payment';
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const handleResolveChallenge = async (approved: boolean) => {
    if (!currentPayment) return;
    setResolving(true);
    setError(null);

    try {
      const result = await submitThreeDsResult(token || '', currentPayment.paymentId, {
        threeDsResult: approved ? 'completed' : 'refused',
        details: {
          threeDSResult: approved ? 'challenge_success' : 'refused'
        }
      });
      setCurrentPayment(result);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to submit 3DS challenge result';
      setError(message);
    } finally {
      setResolving(false);
    }
  };

  const renderStateBadge = (state: string) => {
    switch (state) {
      case 'RequiresAction':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/30">
            <Clock className="w-3.5 h-3.5 animate-pulse" />
            RequiresAction (3DS2 Challenge)
          </span>
        );
      case 'Authorized':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/30">
            <CheckCircle2 className="w-3.5 h-3.5" />
            Authorized
          </span>
        );
      case 'Declined':
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/30">
            <XCircle className="w-3.5 h-3.5" />
            Declined
          </span>
        );
      default:
        return (
          <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-gray-500/10 text-gray-400 border border-gray-500/30">
            {state}
          </span>
        );
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-6 border-b border-[#374151]">
        <div>
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-purple-500/10 border border-purple-500/20 text-purple-400">
              <ShieldAlert className="w-6 h-6" />
            </div>
            <div>
              <h1 className="text-2xl font-bold tracking-tight text-white">3DS2 & SCA Challenge Simulator</h1>
              <p className="text-sm text-gray-400">
                Interactive Strong Customer Authentication console for PSD2 challenge flows & details verification
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <span className="text-xs px-2.5 py-1 rounded-md bg-purple-500/20 text-purple-300 border border-purple-500/30 font-mono">
            ADR-019 / F-PORTAL-07
          </span>
        </div>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/30 text-rose-300 text-sm flex items-start gap-3">
          <AlertCircle className="w-5 h-5 shrink-0 mt-0.5 text-rose-400" />
          <div className="flex-1">
            <p className="font-semibold">Operation Error</p>
            <p className="text-xs text-rose-300/80 mt-0.5">{error}</p>
          </div>
        </div>
      )}

      {/* Simulator Form & Quick Presets */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column: Test Configuration */}
        <div className="lg:col-span-1 bg-[#111827] border border-[#374151] rounded-2xl p-6 space-y-5">
          <h2 className="text-base font-semibold text-white flex items-center gap-2">
            <Play className="w-4 h-4 text-purple-400" />
            Initiate Test Authorization
          </h2>

          <div className="space-y-4">
            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">
                Party ID / Test Card Trigger
              </label>
              <input
                type="text"
                value={partyId}
                onChange={(e) => setPartyId(e.target.value)}
                className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-purple-500 font-mono"
                placeholder="e.g. cardholder_challenge_0002"
              />
              <p className="text-[11px] text-gray-400 mt-1">
                Ends with <code className="text-purple-300 font-mono">0002</code>: 3DS2 Challenge, <code className="text-rose-300 font-mono">0003</code>: Decline
              </p>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1.5">Amount (cents)</label>
                <input
                  type="number"
                  value={amount}
                  onChange={(e) => setAmount(Number(e.target.value))}
                  className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white font-mono"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1.5">Currency</label>
                <input
                  type="text"
                  value={currency}
                  onChange={(e) => setCurrency(e.target.value.toUpperCase())}
                  className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white font-mono"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">Settlement Channel</label>
              <select
                value={settlementChannel}
                onChange={(e) => setSettlementChannel(e.target.value)}
                className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white"
              >
                <option value="ADYEN">Adyen (WireMock / Sandbox)</option>
                <option value="MOCK">Gateway Mock Port</option>
              </select>
            </div>

            <button
              onClick={() => handleCreatePayment()}
              disabled={loading}
              className="w-full py-2.5 px-4 rounded-xl bg-purple-600 hover:bg-purple-500 disabled:opacity-50 text-white font-medium text-sm flex items-center justify-center gap-2 shadow-sm transition"
            >
              {loading ? (
                <>
                  <RotateCw className="w-4 h-4 animate-spin" />
                  <span>Authorizing...</span>
                </>
              ) : (
                <>
                  <Play className="w-4 h-4" />
                  <span>Send Authorization Request</span>
                </>
              )}
            </button>
          </div>

          {/* Quick Presets */}
          <div className="pt-4 border-t border-[#374151] space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wider text-gray-400">Quick Scenarios</p>
            <div className="flex flex-col gap-2">
              <button
                type="button"
                onClick={() => {
                  setPartyId('cardholder_challenge_0002');
                  handleCreatePayment('cardholder_challenge_0002');
                }}
                className="w-full text-left p-2.5 rounded-lg bg-[#1f2937] hover:bg-[#283548] border border-[#374151] text-xs transition"
              >
                <div className="font-semibold text-amber-400 flex items-center justify-between">
                  <span>3DS2 Challenge Flow</span>
                  <span className="font-mono text-[10px] text-gray-400">...0002</span>
                </div>
                <div className="text-gray-400 text-[11px] mt-0.5">Triggers RequiresAction challenge state</div>
              </button>

              <button
                type="button"
                onClick={() => {
                  setPartyId('cardholder_frictionless_1111');
                  handleCreatePayment('cardholder_frictionless_1111');
                }}
                className="w-full text-left p-2.5 rounded-lg bg-[#1f2937] hover:bg-[#283548] border border-[#374151] text-xs transition"
              >
                <div className="font-semibold text-emerald-400 flex items-center justify-between">
                  <span>Frictionless Flow</span>
                  <span className="font-mono text-[10px] text-gray-400">...1111</span>
                </div>
                <div className="text-gray-400 text-[11px] mt-0.5">Direct authorization without challenge</div>
              </button>

              <button
                type="button"
                onClick={() => {
                  setPartyId('decline_card_0003');
                  handleCreatePayment('decline_card_0003');
                }}
                className="w-full text-left p-2.5 rounded-lg bg-[#1f2937] hover:bg-[#283548] border border-[#374151] text-xs transition"
              >
                <div className="font-semibold text-rose-400 flex items-center justify-between">
                  <span>Direct Acquirer Decline</span>
                  <span className="font-mono text-[10px] text-gray-400">...0003</span>
                </div>
                <div className="text-gray-400 text-[11px] mt-0.5">Immediate decline by simulated issuer</div>
              </button>
            </div>
          </div>
        </div>

        {/* Right Column: Interactive Challenge Inspector & Resolution */}
        <div className="lg:col-span-2 space-y-6">
          {currentPayment ? (
            <div className="bg-[#111827] border border-[#374151] rounded-2xl p-6 space-y-6">
              {/* Payment Summary Bar */}
              <div className="flex flex-wrap items-center justify-between gap-4 pb-5 border-b border-[#374151]">
                <div>
                  <div className="text-xs text-gray-400">Payment ID</div>
                  <div className="text-base font-bold text-white font-mono">{currentPayment.paymentId}</div>
                </div>

                <div>
                  <div className="text-xs text-gray-400">Amount</div>
                  <div className="text-base font-bold text-white font-mono">
                    {(currentPayment.amount / 100).toFixed(2)} {currentPayment.currency}
                  </div>
                </div>

                <div>
                  <div className="text-xs text-gray-400">State</div>
                  <div className="mt-0.5">{renderStateBadge(currentPayment.state)}</div>
                </div>

                <div>
                  <div className="text-xs text-gray-400">Channel</div>
                  <div className="text-sm font-semibold text-gray-200">{currentPayment.settlementChannel}</div>
                </div>
              </div>

              {/* 3DS2 Challenge Action Details (when RequiresAction) */}
              {currentPayment.state === 'RequiresAction' && currentPayment.action && (
                <div className="p-5 rounded-xl bg-purple-500/5 border border-purple-500/20 space-y-4">
                  <div className="flex items-center justify-between">
                    <h3 className="text-sm font-bold text-white flex items-center gap-2">
                      <ShieldAlert className="w-4 h-4 text-purple-400" />
                      Issuer Strong Customer Authentication Challenge
                    </h3>
                    <span className="text-xs px-2 py-0.5 rounded bg-amber-500/20 text-amber-300 font-mono">
                      Action: {currentPayment.action.actionType}
                    </span>
                  </div>

                  <div className="space-y-2 text-xs font-mono">
                    <div className="bg-[#1f2937] p-3 rounded-lg border border-[#374151] overflow-x-auto">
                      <span className="text-gray-400">Target URL: </span>
                      <a
                        href={currentPayment.action.targetUrl || '#'}
                        target="_blank"
                        rel="noreferrer"
                        className="text-cyan-400 hover:underline inline-flex items-center gap-1"
                      >
                        {currentPayment.action.targetUrl}
                        <ExternalLink className="w-3 h-3" />
                      </a>
                    </div>

                    <div className="bg-[#1f2937] p-3 rounded-lg border border-[#374151] overflow-x-auto">
                      <span className="text-gray-400">Opaque Payload / Token: </span>
                      <span className="text-purple-300 break-all">{currentPayment.action.token}</span>
                    </div>

                    {currentPayment.action.expiresAt && (
                      <div className="text-[11px] text-gray-400">
                        Expires at: {new Date(currentPayment.action.expiresAt).toLocaleString()}
                      </div>
                    )}
                  </div>

                  {/* Interactive Challenge Controls */}
                  <div className="pt-3 border-t border-purple-500/20">
                    <p className="text-xs text-gray-300 font-medium mb-3">
                      Simulate Cardholder Challenge Outcome:
                    </p>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      <button
                        onClick={() => handleResolveChallenge(true)}
                        disabled={resolving}
                        className="py-3 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-semibold text-sm flex items-center justify-center gap-2 shadow-sm transition"
                      >
                        {resolving ? (
                          <RotateCw className="w-4 h-4 animate-spin" />
                        ) : (
                          <ShieldCheck className="w-4 h-4" />
                        )}
                        <span>Complete Challenge (Approve)</span>
                      </button>

                      <button
                        onClick={() => handleResolveChallenge(false)}
                        disabled={resolving}
                        className="py-3 px-4 rounded-xl bg-rose-600 hover:bg-rose-500 disabled:opacity-50 text-white font-semibold text-sm flex items-center justify-center gap-2 shadow-sm transition"
                      >
                        {resolving ? (
                          <RotateCw className="w-4 h-4 animate-spin" />
                        ) : (
                          <XCircle className="w-4 h-4" />
                        )}
                        <span>Fail Challenge (Reject)</span>
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {/* Terminal Outcome Banner */}
              {currentPayment.state === 'Authorized' && (
                <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/30 flex items-center gap-3 text-emerald-300">
                  <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
                  <div>
                    <p className="font-semibold text-sm">Payment Successfully Authorized</p>
                    <p className="text-xs text-emerald-300/80 mt-0.5">
                      PSP Reference: <span className="font-mono">{currentPayment.channelReference || 'N/A'}</span>
                    </p>
                  </div>
                </div>
              )}

              {currentPayment.state === 'Declined' && (
                <div className="p-4 rounded-xl bg-rose-500/10 border border-rose-500/30 flex items-center gap-3 text-rose-300">
                  <XCircle className="w-5 h-5 text-rose-400 shrink-0" />
                  <div>
                    <p className="font-semibold text-sm">Payment Declined</p>
                    <p className="text-xs text-rose-300/80 mt-0.5">
                      Decline Reason: <span className="font-mono">{currentPayment.declineReason || 'Refused'}</span>
                    </p>
                  </div>
                </div>
              )}

              {/* Full Resource Inspection */}
              <div className="space-y-2">
                <div className="text-xs font-semibold uppercase tracking-wider text-gray-400">Resource Payload</div>
                <pre className="p-4 rounded-xl bg-[#090d16] border border-[#374151] text-xs font-mono text-gray-300 overflow-x-auto max-h-60">
                  {JSON.stringify(currentPayment, null, 2)}
                </pre>
              </div>
            </div>
          ) : (
            <div className="bg-[#111827] border border-[#374151] rounded-2xl p-12 text-center space-y-4">
              <div className="w-12 h-12 rounded-full bg-purple-500/10 border border-purple-500/20 text-purple-400 flex items-center justify-center mx-auto">
                <ShieldAlert className="w-6 h-6" />
              </div>
              <div className="max-w-md mx-auto">
                <h3 className="text-base font-semibold text-white">No Active Payment Simulation</h3>
                <p className="text-xs text-gray-400 mt-1">
                  Configure and send an authorization request on the left or click a quick scenario to inspect the 3DS2 challenge flow.
                </p>
              </div>
              <button
                type="button"
                onClick={() => {
                  setPartyId('cardholder_challenge_0002');
                  handleCreatePayment('cardholder_challenge_0002');
                }}
                className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-purple-600 hover:bg-purple-500 text-white text-xs font-semibold transition"
              >
                <span>Run 3DS2 Challenge Scenario</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
