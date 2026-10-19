import React, { useState, useEffect, useCallback } from 'react';
import {
  Repeat,
  CreditCard,
  Plus,
  RefreshCw,
  Pause,
  Play,
  XCircle,
  AlertCircle,
  Calendar,
  CheckCircle2,
  Trash2,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  fetchSubscriptions,
  fetchPaymentMethods,
  createSubscription,
  pauseSubscription,
  resumeSubscription,
  cancelSubscription,
  revokePaymentMethod,
} from '../../services/apiClient';
import { SubscriptionItem, PaymentMethodItem } from '../../types/subscriptions';

export const SubscriptionsConsole: React.FC = () => {
  const { user, token } = useAuth();
  const partyId = user.merchantId || user.subject || 'merch_acme';

  const [subscriptions, setSubscriptions] = useState<SubscriptionItem[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<PaymentMethodItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // New Subscription Form State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedMethodId, setSelectedMethodId] = useState('');
  const [amount, setAmount] = useState(2500);
  const [currency, setCurrency] = useState('EUR');
  const [interval, setInterval] = useState('Monthly');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const loadData = useCallback(async () => {
    if (!token) return;
    setIsLoading(true);
    setError(null);

    try {
      const [subs, methods] = await Promise.all([
        fetchSubscriptions(token, partyId).catch(() => []),
        fetchPaymentMethods(token, partyId).catch(() => []),
      ]);
      setSubscriptions(subs);
      setPaymentMethods(methods);
      if (methods.length > 0 && !selectedMethodId) {
        setSelectedMethodId(methods[0].id);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load subscription data');
    } finally {
      setIsLoading(false);
    }
  }, [token, partyId, selectedMethodId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handlePause = async (id: string) => {
    if (!token) return;
    try {
      await pauseSubscription(token, id);
      setNotice(`Subscription ${id} paused.`);
      loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to pause subscription');
    }
  };

  const handleResume = async (id: string) => {
    if (!token) return;
    try {
      await resumeSubscription(token, id);
      setNotice(`Subscription ${id} resumed.`);
      loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to resume subscription');
    }
  };

  const handleCancel = async (id: string) => {
    if (!token) return;
    if (!window.confirm(`Are you sure you want to cancel subscription ${id}?`)) return;
    try {
      await cancelSubscription(token, id);
      setNotice(`Subscription ${id} cancelled.`);
      loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to cancel subscription');
    }
  };

  const handleRevokeMethod = async (id: string) => {
    if (!token) return;
    if (!window.confirm(`Revoke card token ${id}? This cannot be undone.`)) return;
    try {
      await revokePaymentMethod(token, id);
      setNotice(`Payment method revoked.`);
      loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to revoke payment method');
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token || !selectedMethodId) return;
    setIsSubmitting(true);
    setError(null);

    try {
      await createSubscription(token, {
        partyId,
        paymentMethodId: selectedMethodId,
        amount: Number(amount),
        currency,
        interval,
      });
      setIsModalOpen(false);
      setNotice('New recurring subscription created.');
      loadData();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to create subscription');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-8">
      {/* Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-3">
            <Repeat className="w-7 h-7 text-indigo-400" />
            Recurring Subscriptions & Cards
          </h1>
          <p className="text-gray-400 text-sm mt-1">
            Automated recurring billing contracts, tokenized card-on-file entries, and dunning lifecycle.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={() => loadData()}
            className="flex items-center gap-2 px-3 py-2 bg-[#1f2937] hover:bg-[#374151] rounded-lg text-sm text-gray-200 transition border border-[#374151]"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
          <button
            onClick={() => setIsModalOpen(true)}
            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg text-sm font-medium transition shadow-sm"
          >
            <Plus className="w-4 h-4" />
            New Subscription
          </button>
        </div>
      </div>

      {notice && (
        <div className="p-4 bg-emerald-950/40 border border-emerald-500/30 text-emerald-300 rounded-lg flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CheckCircle2 className="w-5 h-5 text-emerald-400" />
            <span>{notice}</span>
          </div>
          <button onClick={() => setNotice(null)} className="text-emerald-400 hover:text-white">✕</button>
        </div>
      )}

      {error && (
        <div className="p-4 bg-red-950/40 border border-red-500/30 text-red-300 rounded-lg flex items-center justify-between">
          <div className="flex items-center gap-2">
            <AlertCircle className="w-5 h-5 text-red-400" />
            <span>{error}</span>
          </div>
          <button onClick={() => setError(null)} className="text-red-400 hover:text-white">✕</button>
        </div>
      )}

      {/* Subscriptions Section */}
      <div className="bg-[#111827] border border-[#374151] rounded-xl overflow-hidden shadow-sm">
        <div className="p-4 border-b border-[#374151] flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Calendar className="w-5 h-5 text-indigo-400" />
            <h2 className="text-lg font-semibold text-white">Active Contracts</h2>
          </div>
          <span className="text-xs bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 px-2.5 py-1 rounded-full font-mono">
            {subscriptions.length} Subscriptions
          </span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm text-gray-300">
            <thead className="bg-[#1f2937]/50 text-xs uppercase tracking-wider text-gray-400 border-b border-[#374151]">
              <tr>
                <th className="p-4">Subscription ID</th>
                <th className="p-4">Party</th>
                <th className="p-4">Amount</th>
                <th className="p-4">Interval</th>
                <th className="p-4">State</th>
                <th className="p-4">Next Billing</th>
                <th className="p-4">Failures</th>
                <th className="p-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#374151]">
              {subscriptions.length === 0 ? (
                <tr>
                  <td colSpan={8} className="p-8 text-center text-gray-500">
                    No subscriptions registered for this party.
                  </td>
                </tr>
              ) : (
                subscriptions.map((sub) => (
                  <tr key={sub.id} className="hover:bg-[#1f2937]/30 transition">
                    <td className="p-4 font-mono text-xs text-white font-medium">{sub.id}</td>
                    <td className="p-4 text-gray-300">{sub.partyId}</td>
                    <td className="p-4 font-medium text-white">
                      {(sub.amount / 100).toFixed(2)} {sub.currency}
                    </td>
                    <td className="p-4 text-gray-300">{sub.interval}</td>
                    <td className="p-4">
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-semibold ${
                          sub.state === 'Active'
                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                            : sub.state === 'Paused'
                            ? 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
                            : sub.state === 'PastDue'
                            ? 'bg-red-500/10 text-red-400 border border-red-500/20'
                            : 'bg-gray-700/50 text-gray-400 border border-gray-600'
                        }`}
                      >
                        {sub.state}
                      </span>
                    </td>
                    <td className="p-4 font-mono text-xs text-gray-400">
                      {new Date(sub.nextBillingAt).toLocaleString()}
                    </td>
                    <td className="p-4 font-mono text-xs">
                      {sub.failureCount > 0 ? (
                        <span className="text-red-400 font-bold">{sub.failureCount} retry</span>
                      ) : (
                        <span className="text-gray-500">0</span>
                      )}
                    </td>
                    <td className="p-4 text-right space-x-2">
                      {sub.state === 'Active' && (
                        <button
                          onClick={() => handlePause(sub.id)}
                          className="px-2.5 py-1 bg-[#1f2937] hover:bg-amber-950/40 text-amber-300 border border-[#374151] rounded text-xs inline-flex items-center gap-1 transition"
                          title="Pause subscription"
                        >
                          <Pause className="w-3 h-3" /> Pause
                        </button>
                      )}
                      {sub.state === 'Paused' && (
                        <button
                          onClick={() => handleResume(sub.id)}
                          className="px-2.5 py-1 bg-[#1f2937] hover:bg-emerald-950/40 text-emerald-300 border border-[#374151] rounded text-xs inline-flex items-center gap-1 transition"
                          title="Resume subscription"
                        >
                          <Play className="w-3 h-3" /> Resume
                        </button>
                      )}
                      {sub.state !== 'Cancelled' && (
                        <button
                          onClick={() => handleCancel(sub.id)}
                          className="px-2.5 py-1 bg-[#1f2937] hover:bg-red-950/40 text-red-400 border border-[#374151] rounded text-xs inline-flex items-center gap-1 transition"
                          title="Cancel subscription"
                        >
                          <XCircle className="w-3 h-3" /> Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Stored Cards / Payment Methods Section */}
      <div className="bg-[#111827] border border-[#374151] rounded-xl overflow-hidden shadow-sm">
        <div className="p-4 border-b border-[#374151] flex items-center justify-between">
          <div className="flex items-center gap-2">
            <CreditCard className="w-5 h-5 text-purple-400" />
            <h2 className="text-lg font-semibold text-white">Stored Cards (Card-on-File)</h2>
          </div>
          <span className="text-xs bg-purple-500/20 text-purple-300 border border-purple-500/30 px-2.5 py-1 rounded-full font-mono">
            {paymentMethods.length} Stored Cards
          </span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm text-gray-300">
            <thead className="bg-[#1f2937]/50 text-xs uppercase tracking-wider text-gray-400 border-b border-[#374151]">
              <tr>
                <th className="p-4">Token ID</th>
                <th className="p-4">Brand</th>
                <th className="p-4">Masked PAN</th>
                <th className="p-4">Expiry</th>
                <th className="p-4">Channel</th>
                <th className="p-4">Status</th>
                <th className="p-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#374151]">
              {paymentMethods.length === 0 ? (
                <tr>
                  <td colSpan={7} className="p-8 text-center text-gray-500">
                    No tokenized cards stored yet. Complete a checkout with "storePaymentMethod: true".
                  </td>
                </tr>
              ) : (
                paymentMethods.map((pm) => (
                  <tr key={pm.id} className="hover:bg-[#1f2937]/30 transition">
                    <td className="p-4 font-mono text-xs text-white font-medium">{pm.id}</td>
                    <td className="p-4 font-semibold text-white uppercase">{pm.cardBrand}</td>
                    <td className="p-4 font-mono text-gray-300">{pm.maskedPan}</td>
                    <td className="p-4 font-mono text-gray-400">
                      {pm.expiryMonth.toString().padStart(2, '0')}/{pm.expiryYear}
                    </td>
                    <td className="p-4 text-gray-400">{pm.settlementChannel}</td>
                    <td className="p-4">
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-semibold ${
                          pm.status === 'Active'
                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                            : 'bg-red-500/10 text-red-400 border border-red-500/20'
                        }`}
                      >
                        {pm.status}
                      </span>
                    </td>
                    <td className="p-4 text-right">
                      {pm.status === 'Active' && (
                        <button
                          onClick={() => handleRevokeMethod(pm.id)}
                          className="px-2.5 py-1 bg-[#1f2937] hover:bg-red-950/40 text-red-400 border border-[#374151] rounded text-xs inline-flex items-center gap-1 transition"
                          title="Revoke card token"
                        >
                          <Trash2 className="w-3 h-3" /> Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* New Subscription Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center p-4 z-50">
          <div className="bg-[#111827] border border-[#374151] rounded-xl max-w-md w-full p-6 shadow-2xl">
            <h3 className="text-lg font-bold text-white mb-4 flex items-center gap-2">
              <Plus className="w-5 h-5 text-indigo-400" />
              Create Recurring Subscription
            </h3>

            <form onSubmit={handleCreate} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-gray-400 mb-1">Stored Card (Token)</label>
                {paymentMethods.filter((m) => m.status === 'Active').length === 0 ? (
                  <p className="text-xs text-amber-400">
                    No active card token available. A tokenized card must be saved first.
                  </p>
                ) : (
                  <select
                    value={selectedMethodId}
                    onChange={(e) => setSelectedMethodId(e.target.value)}
                    className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500"
                  >
                    {paymentMethods
                      .filter((m) => m.status === 'Active')
                      .map((m) => (
                        <option key={m.id} value={m.id}>
                          {m.cardBrand.toUpperCase()} {m.maskedPan} (Exp: {m.expiryMonth}/{m.expiryYear})
                        </option>
                      ))}
                  </select>
                )}
              </div>

              <div>
                <label className="block text-xs font-semibold text-gray-400 mb-1">Amount (minor units, e.g. 2500 = €25.00)</label>
                <input
                  type="number"
                  min="100"
                  step="50"
                  value={amount}
                  onChange={(e) => setAmount(Number(e.target.value))}
                  className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500"
                  required
                />
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-semibold text-gray-400 mb-1">Currency</label>
                  <select
                    value={currency}
                    onChange={(e) => setCurrency(e.target.value)}
                    className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500"
                  >
                    <option value="EUR">EUR (€)</option>
                    <option value="USD">USD ($)</option>
                    <option value="GBP">GBP (£)</option>
                    <option value="PLN">PLN (zł)</option>
                  </select>
                </div>

                <div>
                  <label className="block text-xs font-semibold text-gray-400 mb-1">Interval</label>
                  <select
                    value={interval}
                    onChange={(e) => setInterval(e.target.value)}
                    className="w-full bg-[#1f2937] border border-[#374151] rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-indigo-500"
                  >
                    <option value="Daily">Daily</option>
                    <option value="Weekly">Weekly</option>
                    <option value="Monthly">Monthly</option>
                    <option value="Yearly">Yearly</option>
                  </select>
                </div>
              </div>

              <div className="pt-4 flex justify-end gap-3 border-t border-[#374151]">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 bg-[#1f2937] hover:bg-[#374151] rounded-lg text-sm text-gray-300 transition"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isSubmitting || !selectedMethodId}
                  className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-50 text-white rounded-lg text-sm font-medium transition"
                >
                  {isSubmitting ? 'Creating...' : 'Create Subscription'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
