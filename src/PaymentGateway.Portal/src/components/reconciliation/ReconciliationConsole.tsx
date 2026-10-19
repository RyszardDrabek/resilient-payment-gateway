import React, { useState, useEffect, useCallback } from 'react';
import {
  DollarSign,
  TrendingUp,
  AlertOctagon,
  Download,
  Play,
  CheckCircle,
  RefreshCw,
  FileSpreadsheet,
  Layers,
  ArrowRight,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import {
  fetchReconciliationSummary,
  fetchDiscrepancies,
  runReconciliation,
  resolveDiscrepancy,
  fetchPayoutBatches,
  generatePayoutBatch,
  downloadPayoutCsv,
} from '../../services/apiClient';
import {
  ReconciliationSummary,
  DiscrepancyItem,
  MerchantPayoutBatch,
} from '../../types/reconciliation';

export const ReconciliationConsole: React.FC = () => {
  const { token, user } = useAuth();
  const partyId = user?.merchantId;
  const [activeTab, setActiveTab] = useState<'discrepancies' | 'payouts'>('payouts');

  const [summary, setSummary] = useState<ReconciliationSummary | null>(null);
  const [discrepancies, setDiscrepancies] = useState<DiscrepancyItem[]>([]);
  const [payouts, setPayouts] = useState<MerchantPayoutBatch[]>([]);

  const [isLoading, setIsLoading] = useState(false);
  const [isProcessing, setIsProcessing] = useState(false);
  const [message, setMessage] = useState<{ text: string; type: 'success' | 'error' } | null>(null);

  // New Payout Form state
  const [showGenerateModal, setShowGenerateModal] = useState(false);
  const [payoutCurrency, setPayoutCurrency] = useState('EUR');
  const [payoutDaysBack, setPayoutDaysBack] = useState('7');

  // Resolve Discrepancy Modal state
  const [resolvingItem, setResolvingItem] = useState<DiscrepancyItem | null>(null);
  const [resolutionNotes, setResolutionNotes] = useState('');

  const loadData = useCallback(async () => {
    if (!token) return;
    setIsLoading(true);
    setMessage(null);
    try {
      const merchant = partyId || 'acme-corp';
      const [sumData, discData, payoutData] = await Promise.all([
        fetchReconciliationSummary(token),
        fetchDiscrepancies(token),
        fetchPayoutBatches(token, merchant),
      ]);
      setSummary(sumData);
      setDiscrepancies(discData);
      setPayouts(payoutData);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to load reconciliation data';
      setMessage({ text: errorMessage, type: 'error' });
    } finally {
      setIsLoading(false);
    }
  }, [token, partyId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleRunReconciliation = async () => {
    if (!token) return;
    setIsProcessing(true);
    setMessage(null);
    try {
      const res = await runReconciliation(token);
      setMessage({
        text: `Reconciliation cycle completed: ${res.totalProcessed} transactions evaluated, ${res.matchedCount} matched, ${res.discrepancyCount} discrepancies flagged.`,
        type: 'success',
      });
      await loadData();
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Reconciliation run failed';
      setMessage({ text: errorMessage, type: 'error' });
    } finally {
      setIsProcessing(false);
    }
  };

  const handleGeneratePayout = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token) return;
    setIsProcessing(true);
    setMessage(null);
    try {
      const merchant = partyId || 'acme-corp';
      const end = new Date();
      const start = new Date();
      start.setDate(start.getDate() - parseInt(payoutDaysBack, 10));

      const batch = await generatePayoutBatch(token, {
        merchantId: merchant,
        periodStart: start.toISOString(),
        periodEnd: end.toISOString(),
        currency: payoutCurrency,
      });

      setMessage({
        text: `Payout batch '${batch.id}' generated successfully with ${batch.items.length} items. Total net: ${(batch.netAmount / 100).toFixed(2)} ${batch.currency}`,
        type: 'success',
      });
      setShowGenerateModal(false);
      await loadData();
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to generate payout batch';
      setMessage({ text: errorMessage, type: 'error' });
    } finally {
      setIsProcessing(false);
    }
  };

  const handleDownloadCsv = async (batchId: string) => {
    if (!token) return;
    try {
      const csvContent = await downloadPayoutCsv(token, batchId);
      const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.setAttribute('href', url);
      link.setAttribute('download', `${batchId}.csv`);
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'CSV download failed';
      setMessage({ text: errorMessage, type: 'error' });
    }
  };

  const handleResolveDiscrepancy = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token || !resolvingItem) return;
    setIsProcessing(true);
    try {
      await resolveDiscrepancy(token, resolvingItem.id, resolutionNotes);
      setMessage({ text: `Discrepancy '${resolvingItem.id}' resolved.`, type: 'success' });
      setResolvingItem(null);
      setResolutionNotes('');
      await loadData();
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to resolve discrepancy';
      setMessage({ text: errorMessage, type: 'error' });
    } finally {
      setIsProcessing(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Title & Actions */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight flex items-center gap-2">
            <DollarSign className="w-6 h-6 text-emerald-400" />
            Reconciliation & Merchant Payouts
          </h1>
          <p className="text-sm text-gray-400 mt-1">
            Automated settlement matching, transaction fee deduction, and payout batch rollup.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <button
            onClick={loadData}
            disabled={isLoading || isProcessing}
            className="flex items-center gap-2 px-3 py-2 bg-gray-800 hover:bg-gray-700 text-gray-200 text-sm font-medium rounded-lg border border-gray-700 transition"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            Refresh
          </button>

          <button
            onClick={handleRunReconciliation}
            disabled={isProcessing}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium rounded-lg shadow-sm transition"
          >
            <Play className="w-4 h-4" />
            Run Reconciliation
          </button>

          <button
            onClick={() => setShowGenerateModal(true)}
            disabled={isProcessing}
            className="flex items-center gap-2 px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white text-sm font-medium rounded-lg shadow-sm transition"
          >
            <Layers className="w-4 h-4" />
            Generate Payout Batch
          </button>
        </div>
      </div>

      {/* Alert Notification */}
      {message && (
        <div
          className={`p-4 rounded-lg border text-sm flex items-center justify-between ${
            message.type === 'success'
              ? 'bg-emerald-950/40 border-emerald-800/60 text-emerald-300'
              : 'bg-rose-950/40 border-rose-800/60 text-rose-300'
          }`}
        >
          <span>{message.text}</span>
          <button
            onClick={() => setMessage(null)}
            className="text-xs underline hover:no-underline ml-4"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Top Metrics Row */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <div className="bg-[#111827] border border-gray-800 rounded-xl p-4">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-gray-400">Match Rate</span>
            <TrendingUp className="w-4 h-4 text-emerald-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">
              {summary ? `${summary.matchRatePercentage}%` : '100%'}
            </span>
            <span className="text-xs text-gray-400">
              {summary ? `${summary.matchedCount} of ${summary.totalRecords}` : '0 records'}
            </span>
          </div>
        </div>

        <div className="bg-[#111827] border border-gray-800 rounded-xl p-4">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-gray-400">Matched Volume</span>
            <CheckCircle className="w-4 h-4 text-emerald-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-emerald-400">
              {summary ? `€${(summary.matchedVolume / 100).toFixed(2)}` : '€0.00'}
            </span>
            <span className="text-xs text-gray-400">reconciled</span>
          </div>
        </div>

        <div className="bg-[#111827] border border-gray-800 rounded-xl p-4">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-gray-400">Discrepancies</span>
            <AlertOctagon className="w-4 h-4 text-amber-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-amber-400">
              {summary ? summary.discrepancyCount : 0}
            </span>
            <span className="text-xs text-gray-400">
              {summary ? `(€${(summary.discrepancyVolume / 100).toFixed(2)} delta)` : '€0.00 delta'}
            </span>
          </div>
        </div>

        <div className="bg-[#111827] border border-gray-800 rounded-xl p-4">
          <div className="flex items-center justify-between">
            <span className="text-xs font-medium uppercase tracking-wider text-gray-400">Payout Batches</span>
            <FileSpreadsheet className="w-4 h-4 text-blue-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">{payouts.length}</span>
            <span className="text-xs text-gray-400">batches generated</span>
          </div>
        </div>
      </div>

      {/* Navigation Sub-Tabs */}
      <div className="flex border-b border-gray-800 gap-4">
        <button
          onClick={() => setActiveTab('payouts')}
          className={`pb-3 text-sm font-medium transition border-b-2 flex items-center gap-2 ${
            activeTab === 'payouts'
              ? 'border-emerald-500 text-emerald-400'
              : 'border-transparent text-gray-400 hover:text-gray-200'
          }`}
        >
          <Layers className="w-4 h-4" />
          Payout Batches & Ledgers ({payouts.length})
        </button>

        <button
          onClick={() => setActiveTab('discrepancies')}
          className={`pb-3 text-sm font-medium transition border-b-2 flex items-center gap-2 ${
            activeTab === 'discrepancies'
              ? 'border-amber-500 text-amber-400'
              : 'border-transparent text-gray-400 hover:text-gray-200'
          }`}
        >
          <AlertOctagon className="w-4 h-4" />
          Flagged Discrepancies ({discrepancies.length})
        </button>
      </div>

      {/* Tab 1: Payout Batches */}
      {activeTab === 'payouts' && (
        <div className="bg-[#111827] border border-gray-800 rounded-xl overflow-hidden shadow-sm">
          <div className="p-4 border-b border-gray-800 flex justify-between items-center">
            <h2 className="text-sm font-bold text-gray-200 uppercase tracking-wider">
              Settlement & Payout Batches
            </h2>
            <span className="text-xs text-gray-400">Net funds distributed to merchant bank accounts</span>
          </div>

          {payouts.length === 0 ? (
            <div className="p-12 text-center text-gray-400 text-sm">
              <Layers className="w-8 h-8 mx-auto text-gray-500 mb-2 opacity-50" />
              No payout batches have been generated yet for merchant '{partyId || 'acme-corp'}'.
              <br />
              Click &quot;Generate Payout Batch&quot; or wait for the daily worker run.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm text-gray-300">
                <thead className="bg-[#1a2234] text-xs uppercase text-gray-400 border-b border-gray-800">
                  <tr>
                    <th className="px-4 py-3">Batch ID</th>
                    <th className="px-4 py-3">Period</th>
                    <th className="px-4 py-3 text-right">Gross Amount</th>
                    <th className="px-4 py-3 text-right">Gateway Fees</th>
                    <th className="px-4 py-3 text-right">Net Payout</th>
                    <th className="px-4 py-3 text-center">Status</th>
                    <th className="px-4 py-3 text-center">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800">
                  {payouts.map((batch) => (
                    <tr key={batch.id} className="hover:bg-[#1f2937]/50 transition">
                      <td className="px-4 py-3 font-mono text-xs text-blue-400">
                        {batch.id}
                        <div className="text-[10px] text-gray-500">{batch.items.length} items</div>
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-300">
                        {new Date(batch.periodStart).toLocaleDateString()} &rarr;{' '}
                        {new Date(batch.periodEnd).toLocaleDateString()}
                      </td>
                      <td className="px-4 py-3 text-right font-mono">
                        {(batch.grossAmount / 100).toFixed(2)} {batch.currency}
                      </td>
                      <td className="px-4 py-3 text-right font-mono text-rose-400">
                        -{(batch.feeAmount / 100).toFixed(2)} {batch.currency}
                      </td>
                      <td className="px-4 py-3 text-right font-mono font-bold text-emerald-400">
                        {(batch.netAmount / 100).toFixed(2)} {batch.currency}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <span
                          className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                            batch.status === 'Completed'
                              ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                              : batch.status === 'Processing'
                              ? 'bg-blue-500/10 text-blue-400 border border-blue-500/20'
                              : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
                          }`}
                        >
                          {batch.status}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-center">
                        <button
                          onClick={() => handleDownloadCsv(batch.id)}
                          className="inline-flex items-center gap-1.5 px-2.5 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-200 text-xs font-medium rounded border border-gray-700 transition"
                          title="Download Accounting CSV"
                        >
                          <Download className="w-3.5 h-3.5 text-emerald-400" />
                          CSV
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Discrepancies */}
      {activeTab === 'discrepancies' && (
        <div className="bg-[#111827] border border-gray-800 rounded-xl overflow-hidden shadow-sm">
          <div className="p-4 border-b border-gray-800 flex justify-between items-center">
            <h2 className="text-sm font-bold text-gray-200 uppercase tracking-wider">
              Flagged Reconciliation Discrepancies
            </h2>
            <span className="text-xs text-gray-400">Mismatches flagged for manual review & resolution</span>
          </div>

          {discrepancies.length === 0 ? (
            <div className="p-12 text-center text-gray-400 text-sm">
              <CheckCircle className="w-8 h-8 mx-auto text-emerald-400 mb-2 opacity-50" />
              All transactions reconciled successfully! No open discrepancies found.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm text-gray-300">
                <thead className="bg-[#1a2234] text-xs uppercase text-gray-400 border-b border-gray-800">
                  <tr>
                    <th className="px-4 py-3">Record ID</th>
                    <th className="px-4 py-3">Payment ID</th>
                    <th className="px-4 py-3">Channel & Ref</th>
                    <th className="px-4 py-3">Type</th>
                    <th className="px-4 py-3 text-right">Gateway vs Acquirer</th>
                    <th className="px-4 py-3">Notes</th>
                    <th className="px-4 py-3 text-center">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800">
                  {discrepancies.map((d) => (
                    <tr key={d.id} className="hover:bg-[#1f2937]/50 transition">
                      <td className="px-4 py-3 font-mono text-xs text-gray-400">{d.id}</td>
                      <td className="px-4 py-3 font-mono text-xs text-blue-400">
                        {d.paymentId || '—'}
                      </td>
                      <td className="px-4 py-3 text-xs">
                        <span className="font-semibold text-gray-300">{d.channel}</span>
                        <div className="font-mono text-gray-500 text-[10px]">{d.channelReference}</div>
                      </td>
                      <td className="px-4 py-3">
                        <span className="text-xs px-2 py-0.5 rounded-full font-medium bg-amber-500/10 text-amber-400 border border-amber-500/20">
                          {d.discrepancyType}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-right font-mono text-xs">
                        <span className="text-white">€{(d.gatewayAmount / 100).toFixed(2)}</span>
                        <span className="text-gray-500 mx-1">vs</span>
                        <span className="text-amber-400">€{(d.acquirerAmount / 100).toFixed(2)}</span>
                      </td>
                      <td className="px-4 py-3 text-xs text-gray-400 max-w-xs truncate">
                        {d.notes || '—'}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <button
                          onClick={() => {
                            setResolvingItem(d);
                            setResolutionNotes('');
                          }}
                          className="px-2.5 py-1 bg-amber-600/20 hover:bg-amber-600/40 text-amber-300 text-xs font-medium rounded border border-amber-600/40 transition"
                        >
                          Resolve
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Generate Payout Modal */}
      {showGenerateModal && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-[#111827] border border-gray-800 rounded-xl max-w-md w-full p-6 shadow-xl">
            <h3 className="text-lg font-bold text-white mb-2 flex items-center gap-2">
              <Layers className="w-5 h-5 text-emerald-400" />
              Generate Payout Batch
            </h3>
            <p className="text-xs text-gray-400 mb-4">
              Aggregate settled payments into a merchant payout ledger with gateway fee deductions.
            </p>

            <form onSubmit={handleGeneratePayout} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1">
                  Merchant Party ID
                </label>
                <input
                  type="text"
                  readOnly
                  value={partyId || 'acme-corp'}
                  className="w-full bg-[#1a2234] border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-300 font-mono"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1">
                  Settlement Window (Past Days)
                </label>
                <select
                  value={payoutDaysBack}
                  onChange={(e) => setPayoutDaysBack(e.target.value)}
                  className="w-full bg-[#1a2234] border border-gray-700 rounded-lg px-3 py-2 text-sm text-white"
                >
                  <option value="1">Past 24 Hours</option>
                  <option value="7">Past 7 Days</option>
                  <option value="14">Past 14 Days</option>
                  <option value="30">Past 30 Days</option>
                </select>
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1">
                  Currency
                </label>
                <select
                  value={payoutCurrency}
                  onChange={(e) => setPayoutCurrency(e.target.value)}
                  className="w-full bg-[#1a2234] border border-gray-700 rounded-lg px-3 py-2 text-sm text-white font-mono"
                >
                  <option value="EUR">EUR - Euro</option>
                  <option value="USD">USD - US Dollar</option>
                  <option value="GBP">GBP - British Pound</option>
                  <option value="PLN">PLN - Polish Zloty</option>
                </select>
              </div>

              <div className="pt-4 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setShowGenerateModal(false)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 text-sm font-medium rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isProcessing}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white text-sm font-medium rounded-lg flex items-center gap-2"
                >
                  {isProcessing ? <RefreshCw className="w-4 h-4 animate-spin" /> : <ArrowRight className="w-4 h-4" />}
                  Generate Batch
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Resolve Discrepancy Modal */}
      {resolvingItem && (
        <div className="fixed inset-0 bg-black/70 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <div className="bg-[#111827] border border-gray-800 rounded-xl max-w-md w-full p-6 shadow-xl">
            <h3 className="text-lg font-bold text-white mb-2 flex items-center gap-2">
              <AlertOctagon className="w-5 h-5 text-amber-400" />
              Resolve Discrepancy
            </h3>
            <p className="text-xs text-gray-400 mb-4">
              Enter audit justification notes to manually clear discrepancy &apos;{resolvingItem.id}&apos;.
            </p>

            <form onSubmit={handleResolveDiscrepancy} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1">
                  Resolution Notes
                </label>
                <textarea
                  rows={3}
                  required
                  placeholder="e.g. Manually verified acquirer interchange adjustment voucher #4891"
                  value={resolutionNotes}
                  onChange={(e) => setResolutionNotes(e.target.value)}
                  className="w-full bg-[#1a2234] border border-gray-700 rounded-lg px-3 py-2 text-sm text-white"
                />
              </div>

              <div className="pt-4 flex justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setResolvingItem(null)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-gray-300 text-sm font-medium rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isProcessing}
                  className="px-4 py-2 bg-amber-600 hover:bg-amber-500 text-white text-sm font-medium rounded-lg flex items-center gap-2"
                >
                  {isProcessing ? <RefreshCw className="w-4 h-4 animate-spin" /> : <CheckCircle className="w-4 h-4" />}
                  Confirm Resolution
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
