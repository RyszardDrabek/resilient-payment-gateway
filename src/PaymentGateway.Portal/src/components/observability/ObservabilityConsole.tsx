import React, { useState, useEffect, useCallback } from 'react';
import {
  Activity,
  AlertTriangle,
  Clock,
  Database,
  ExternalLink,
  Flame,
  Gauge,
  Layers,
  Radio,
  RefreshCw,
  Server,
  ShieldCheck,
  Zap,
  ArrowRight,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { fetchObservabilitySummary } from '../../services/apiClient';
import { ObservabilitySummaryDto, AlertmanagerAlertDto } from '../../types/api';

interface ObservabilityConsoleProps {
  onNavigate?: (tab: 'unresolved' | 'webhooks' | 'risk' | 'api-keys' | 'webhook-dlq' | 'observability') => void;
}

const GRAFANA_URL = (import.meta.env.VITE_GRAFANA_URL as string) || 'http://localhost:3001';
const PROMETHEUS_URL = (import.meta.env.VITE_PROMETHEUS_URL as string) || 'http://localhost:9090';
const ALERTMANAGER_URL = (import.meta.env.VITE_ALERTMANAGER_URL as string) || 'http://localhost:9093';
const JAEGER_URL = (import.meta.env.VITE_JAEGER_URL as string) || 'http://localhost:16686';

export const ObservabilityConsole: React.FC<ObservabilityConsoleProps> = ({ onNavigate }) => {
  const { token } = useAuth();
  const [data, setData] = useState<ObservabilitySummaryDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [autoRefreshInterval, setAutoRefreshInterval] = useState<number>(10); // seconds, 0 = off
  const [severityFilter, setSeverityFilter] = useState<'all' | 'critical' | 'warning'>('all');
  const [lastRefreshedAt, setLastRefreshedAt] = useState<Date | null>(null);

  const loadSummary = useCallback(async (isSilent = false) => {
    if (!token) {
      setIsLoading(false);
      return;
    }

    if (!isSilent) setIsLoading(true);
    setError(null);

    try {
      const summary = await fetchObservabilitySummary(token);
      setData(summary);
      setLastRefreshedAt(new Date());
    } catch (err: unknown) {
      if (err instanceof Error) {
        setError(err.message);
      } else {
        setError('Failed to load system observability metrics.');
      }
    } finally {
      if (!isSilent) setIsLoading(false);
    }
  }, [token]);

  useEffect(() => {
    loadSummary();
  }, [loadSummary]);

  // Auto-refresh timer
  useEffect(() => {
    if (autoRefreshInterval <= 0) return;

    const intervalId = setInterval(() => {
      loadSummary(true);
    }, autoRefreshInterval * 1000);

    return () => clearInterval(intervalId);
  }, [autoRefreshInterval, loadSummary]);

  const activeAlerts: AlertmanagerAlertDto[] = data?.activeAlerts || [];
  const filteredAlerts = activeAlerts.filter((a: AlertmanagerAlertDto) => {
    if (severityFilter === 'all') return true;
    return a.severity.toLowerCase() === severityFilter;
  });

  const criticalCount = activeAlerts.filter((a: AlertmanagerAlertDto) => a.severity.toLowerCase() === 'critical').length;
  const warningCount = activeAlerts.filter((a: AlertmanagerAlertDto) => a.severity.toLowerCase() === 'warning').length;

  const isSystemHealthy =
    data?.api.status === 'Healthy' &&
    data?.database.status === 'Connected' &&
    criticalCount === 0;

  return (
    <div className="space-y-6">
      {/* Top Banner / Header */}
      <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4 bg-[#111827] border border-[#374151] rounded-xl p-6 shadow-lg">
        <div className="flex items-center gap-4">
          <div
            className={`p-3 rounded-xl border ${
              isSystemHealthy
                ? 'bg-emerald-500/10 border-emerald-500/30 text-emerald-400'
                : 'bg-rose-500/10 border-rose-500/30 text-rose-400'
            }`}
          >
            <Activity className="w-8 h-8 animate-pulse" />
          </div>
          <div>
            <div className="flex items-center gap-3">
              <h1 className="text-xl font-bold text-white tracking-wide">
                System Observability & Reliability
              </h1>
              <span
                className={`px-2.5 py-0.5 text-xs font-semibold rounded-full border ${
                  isSystemHealthy
                    ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                    : 'bg-rose-500/20 text-rose-300 border-rose-500/40'
                }`}
              >
                {isSystemHealthy ? 'CLUSTER NOMINAL' : 'ATTENTION REQUIRED'}
              </span>
            </div>
            <p className="text-sm text-gray-400 mt-1">
              Live service health, Prometheus p95 latency percentiles, Alertmanager alerts, and transactional queues.
            </p>
          </div>
        </div>

        {/* Header Controls */}
        <div className="flex items-center flex-wrap gap-3">
          <div className="flex items-center gap-2 bg-[#1f2937] border border-[#374151] px-3 py-1.5 rounded-lg text-xs text-gray-300">
            <Clock className="w-3.5 h-3.5 text-gray-400" />
            <span>Auto-refresh:</span>
            <select
              value={autoRefreshInterval}
              onChange={(e) => setAutoRefreshInterval(Number(e.target.value))}
              className="bg-[#111827] border border-[#374151] rounded px-2 py-0.5 text-xs text-white focus:outline-none focus:border-blue-500"
            >
              <option value={0}>Off</option>
              <option value={5}>5s</option>
              <option value={10}>10s</option>
              <option value={30}>30s</option>
            </select>
          </div>

          <button
            onClick={() => loadSummary(false)}
            disabled={isLoading}
            className="flex items-center gap-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white px-4 py-2 rounded-lg text-sm font-medium transition shadow-sm"
          >
            <RefreshCw className={`w-4 h-4 ${isLoading ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* Global Error Banner */}
      {error && (
        <div className="bg-rose-500/10 border border-rose-500/30 text-rose-300 p-4 rounded-xl flex items-center justify-between">
          <div className="flex items-center gap-3">
            <AlertTriangle className="w-5 h-5 text-rose-400 shrink-0" />
            <span className="text-sm">{error}</span>
          </div>
          <button
            onClick={() => loadSummary(false)}
            className="text-xs bg-rose-500/20 hover:bg-rose-500/30 text-rose-200 px-3 py-1 rounded transition"
          >
            Retry
          </button>
        </div>
      )}

      {/* SECTION 1: Service Statuses */}
      <div>
        <h2 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 px-1 flex items-center gap-2">
          <Server className="w-4 h-4 text-blue-400" />
          <span>Core Infrastructure & Services</span>
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {/* API Service */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs text-gray-400 font-mono">SERVICE</span>
              <span
                className={`px-2 py-0.5 text-[11px] font-semibold rounded-md border ${
                  data?.api.status === 'Healthy'
                    ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                    : 'bg-rose-500/20 text-rose-300 border-rose-500/40'
                }`}
              >
                {data?.api.status || 'Checking...'}
              </span>
            </div>
            <div className="text-base font-bold text-white mb-1">
              PaymentGateway.Api
            </div>
            <div className="text-xs text-gray-400 space-y-1">
              <div>Version: <span className="font-mono text-gray-300">{data?.api.version || '1.0.0'}</span></div>
              <div>Env: <span className="font-mono text-gray-300">{data?.api.environment || 'Production'}</span></div>
              <div>Uptime: <span className="font-mono text-gray-300">{data?.api.uptime || 'active'}</span></div>
            </div>
          </div>

          {/* Database */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs text-gray-400 font-mono">DATABASE</span>
              <span
                className={`px-2 py-0.5 text-[11px] font-semibold rounded-md border ${
                  data?.database.status === 'Connected'
                    ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                    : 'bg-rose-500/20 text-rose-300 border-rose-500/40'
                }`}
              >
                {data?.database.status || 'Connecting...'}
              </span>
            </div>
            <div className="text-base font-bold text-white mb-1 flex items-center gap-2">
              <Database className="w-4 h-4 text-purple-400" />
              <span>PostgreSQL Cluster</span>
            </div>
            <div className="text-xs text-gray-400 space-y-1">
              <div className="flex justify-between">
                <span>PaymentDb:</span>
                <span className={data?.database.paymentDbConnected ? 'text-emerald-400 font-mono' : 'text-rose-400 font-mono'}>
                  {data?.database.paymentDbConnected ? 'Connected' : 'Offline'}
                </span>
              </div>
              <div className="flex justify-between">
                <span>RiskDb:</span>
                <span className={data?.database.riskDbConnected ? 'text-emerald-400 font-mono' : 'text-rose-400 font-mono'}>
                  {data?.database.riskDbConnected ? 'Connected' : 'Offline'}
                </span>
              </div>
              <div className="flex justify-between">
                <span>Latency:</span>
                <span className="font-mono text-gray-300">{data?.database.responseTimeMs ?? '--'} ms</span>
              </div>
            </div>
          </div>

          {/* Message Broker */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs text-gray-400 font-mono">MESSAGING</span>
              <span className="px-2 py-0.5 text-[11px] font-semibold rounded-md border bg-emerald-500/20 text-emerald-300 border-emerald-500/40">
                {data?.messageBroker.status || 'Active'}
              </span>
            </div>
            <div className="text-base font-bold text-white mb-1 flex items-center gap-2">
              <Layers className="w-4 h-4 text-amber-400" />
              <span>{data?.messageBroker.provider || 'MassTransit'}</span>
            </div>
            <div className="text-xs text-gray-400 space-y-1">
              <div>Host: <span className="font-mono text-gray-300">{data?.messageBroker.host || 'localhost'}</span></div>
              <div>Bus: <span className="font-mono text-emerald-400">Online</span></div>
              <div>Transport: <span className="font-mono text-gray-300">{data?.messageBroker.provider}</span></div>
            </div>
          </div>

          {/* Monitoring Services */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs text-gray-400 font-mono">TELEMETRY</span>
              <span className="px-2 py-0.5 text-[11px] font-semibold rounded-md border bg-blue-500/20 text-blue-300 border-blue-500/40">
                OpenTelemetry
              </span>
            </div>
            <div className="text-base font-bold text-white mb-1 flex items-center gap-2">
              <Zap className="w-4 h-4 text-cyan-400" />
              <span>Prometheus & Alerts</span>
            </div>
            <div className="text-xs text-gray-400 space-y-1">
              <div className="flex justify-between">
                <span>Prometheus:</span>
                <span className={data?.monitoring.prometheusStatus === 'Reachable' ? 'text-emerald-400 font-mono' : 'text-amber-400 font-mono'}>
                  {data?.monitoring.prometheusStatus || 'Checking...'}
                </span>
              </div>
              <div className="flex justify-between">
                <span>Alertmanager:</span>
                <span className={data?.monitoring.alertmanagerStatus === 'Reachable' ? 'text-emerald-400 font-mono' : 'text-amber-400 font-mono'}>
                  {data?.monitoring.alertmanagerStatus || 'Checking...'}
                </span>
              </div>
              <div className="flex justify-between">
                <span>Jaeger Tracing:</span>
                <span className="text-emerald-400 font-mono">Active</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* SECTION 2: Alertmanager Active Alerts */}
      <div className="bg-[#111827] border border-[#374151] rounded-xl p-6 shadow-md">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-4 border-b border-[#374151]">
          <div className="flex items-center gap-3">
            <div className={`p-2 rounded-lg ${criticalCount > 0 ? 'bg-rose-500/20 text-rose-400' : 'bg-blue-500/20 text-blue-400'}`}>
              <Flame className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white">
                Alertmanager Active Alerts
              </h2>
              <p className="text-xs text-gray-400">
                Live alert firing states evaluated against PromQL operational rules.
              </p>
            </div>
          </div>

          {/* Filter Pills */}
          <div className="flex items-center gap-1 bg-[#1f2937] p-1 rounded-lg border border-[#374151] text-xs">
            <button
              onClick={() => setSeverityFilter('all')}
              className={`px-3 py-1 rounded font-medium transition ${
                severityFilter === 'all'
                  ? 'bg-blue-600 text-white'
                  : 'text-gray-400 hover:text-white'
              }`}
            >
              All ({activeAlerts.length})
            </button>
            <button
              onClick={() => setSeverityFilter('critical')}
              className={`px-3 py-1 rounded font-medium transition ${
                severityFilter === 'critical'
                  ? 'bg-rose-600 text-white'
                  : 'text-gray-400 hover:text-rose-400'
              }`}
            >
              Critical ({criticalCount})
            </button>
            <button
              onClick={() => setSeverityFilter('warning')}
              className={`px-3 py-1 rounded font-medium transition ${
                severityFilter === 'warning'
                  ? 'bg-amber-600 text-white'
                  : 'text-gray-400 hover:text-amber-400'
              }`}
            >
              Warning ({warningCount})
            </button>
          </div>
        </div>

        {/* Alerts Content */}
        <div className="mt-4">
          {filteredAlerts.length === 0 ? (
            <div className="py-8 text-center bg-[#1f2937]/40 border border-dashed border-[#374151] rounded-xl flex flex-col items-center justify-center">
              <ShieldCheck className="w-10 h-10 text-emerald-400 mb-2" />
              <div className="text-sm font-semibold text-white">
                All Operational Rules Nominal
              </div>
              <p className="text-xs text-gray-400 max-w-md mt-1">
                No active or firing alerts reported by Alertmanager. Latency SLAs, outbox drain queues, and webhook DLQs are within specified operating limits.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {filteredAlerts.map((alert: AlertmanagerAlertDto) => {
                const isCrit = alert.severity.toLowerCase() === 'critical';
                return (
                  <div
                    key={alert.fingerprint}
                    className={`border rounded-xl p-4 transition ${
                      isCrit
                        ? 'bg-rose-950/20 border-rose-500/40'
                        : 'bg-amber-950/20 border-amber-500/40'
                    }`}
                  >
                    <div className="flex flex-col md:flex-row md:items-center justify-between gap-2 mb-2">
                      <div className="flex items-center gap-2">
                        <span
                          className={`px-2 py-0.5 text-xs font-bold uppercase rounded border ${
                            isCrit
                              ? 'bg-rose-500/20 text-rose-300 border-rose-500/50 animate-pulse'
                              : 'bg-amber-500/20 text-amber-300 border-amber-500/50'
                          }`}
                        >
                          {alert.severity}
                        </span>
                        <span className="font-mono text-sm font-bold text-white">
                          {alert.alertName}
                        </span>
                      </div>
                      <div className="text-xs text-gray-400 font-mono">
                        Started: {new Date(alert.startsAt).toLocaleTimeString()} ({new Date(alert.startsAt).toLocaleDateString()})
                      </div>
                    </div>

                    <div className="text-sm font-medium text-gray-200 mb-1">
                      {alert.summary}
                    </div>

                    {alert.description && (
                      <p className="text-xs text-gray-400 mb-3">
                        {alert.description}
                      </p>
                    )}

                    {/* Labels chip row */}
                    {alert.labels && Object.keys(alert.labels).length > 0 && (
                      <div className="flex flex-wrap gap-1.5 pt-2 border-t border-[#374151]/50">
                        {Object.entries(alert.labels).map(([k, v]) => (
                          <span
                            key={k}
                            className="bg-[#1f2937] text-gray-300 text-[10px] font-mono px-2 py-0.5 rounded border border-[#374151]"
                          >
                            {k}={v}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* SECTION 3: Latency SLAs & Operational Metrics */}
      <div>
        <h2 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 px-1 flex items-center gap-2">
          <Gauge className="w-4 h-4 text-cyan-400" />
          <span>Latency SLA Percentiles & Throughput</span>
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {/* Acquirer Latency SLA */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs text-gray-400 font-mono">SETTLEMENT SLA</span>
                <span
                  className={`px-2 py-0.5 text-[11px] font-semibold rounded-md border ${
                    data?.latencyMetrics.acquirerSlaCompliant
                      ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                      : 'bg-rose-500/20 text-rose-300 border-rose-500/40'
                  }`}
                >
                  {data?.latencyMetrics.acquirerSlaCompliant ? 'SLA Compliant' : 'SLA Breach'}
                </span>
              </div>
              <div className="text-xs text-gray-400">Acquirer Channel p95 Latency</div>
              <div className="text-3xl font-extrabold text-white mt-2 font-mono">
                {data?.latencyMetrics.acquirerP95LatencySeconds != null
                  ? `${data.latencyMetrics.acquirerP95LatencySeconds.toFixed(3)}s`
                  : '--'}
              </div>
            </div>
            <div className="mt-4 pt-3 border-t border-[#374151] flex items-center justify-between text-xs text-gray-400">
              <span>Target: &lt; 2.0s</span>
              <span className="text-cyan-400 font-mono">PromQL: p95(5m)</span>
            </div>
          </div>

          {/* Outbox Dispatch Duration */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs text-gray-400 font-mono">OUTBOX DISPATCH</span>
                <span className="px-2 py-0.5 text-[11px] font-semibold rounded-md border bg-emerald-500/20 text-emerald-300 border-emerald-500/40">
                  Optimal
                </span>
              </div>
              <div className="text-xs text-gray-400">Transactional Outbox p95</div>
              <div className="text-3xl font-extrabold text-white mt-2 font-mono">
                {data?.latencyMetrics.outboxDispatchP95LatencySeconds != null
                  ? `${(data.latencyMetrics.outboxDispatchP95LatencySeconds * 1000).toFixed(0)} ms`
                  : '--'}
              </div>
            </div>
            <div className="mt-4 pt-3 border-t border-[#374151] flex items-center justify-between text-xs text-gray-400">
              <span>Target: &lt; 500 ms</span>
              <span className="text-cyan-400 font-mono">MassTransit Bus</span>
            </div>
          </div>

          {/* Payment Decline Rate */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs text-gray-400 font-mono">QUALITY OF SERVICE</span>
                <span
                  className={`px-2 py-0.5 text-[11px] font-semibold rounded-md border ${
                    data?.latencyMetrics.declineRateCompliant
                      ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40'
                      : 'bg-rose-500/20 text-rose-300 border-rose-500/40'
                  }`}
                >
                  {data?.latencyMetrics.declineRateCompliant ? 'Healthy' : 'High Decline Rate'}
                </span>
              </div>
              <div className="text-xs text-gray-400">Payment Decline Rate</div>
              <div className="text-3xl font-extrabold text-white mt-2 font-mono">
                {data?.latencyMetrics.declineRatePercent != null
                  ? `${data.latencyMetrics.declineRatePercent.toFixed(2)}%`
                  : '--'}
              </div>
            </div>
            <div className="mt-4 pt-3 border-t border-[#374151] flex items-center justify-between text-xs text-gray-400">
              <span>Target: &lt; 5.0%</span>
              <span className="text-gray-300 font-mono">
                {data?.latencyMetrics.totalDeclines ?? 0} / {data?.latencyMetrics.totalTransactions ?? 0}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* SECTION 4: Queue Health & Telemetry */}
      <div>
        <h2 className="text-xs font-bold uppercase tracking-wider text-gray-400 mb-3 px-1 flex items-center gap-2">
          <Layers className="w-4 h-4 text-emerald-400" />
          <span>Transactional Queues & Dead-Letter Backlog</span>
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {/* Outbox Backlog */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-bold text-white flex items-center gap-2">
                <Radio className="w-4 h-4 text-blue-400" />
                <span>Transactional Outbox</span>
              </span>
              <span
                className={`px-2 py-0.5 text-[10px] font-bold rounded ${
                  (data?.queues.outbox.pendingCount ?? 0) > 0
                    ? 'bg-amber-500/20 text-amber-300 border border-amber-500/40'
                    : 'bg-emerald-500/20 text-emerald-300 border border-emerald-500/40'
                }`}
              >
                {(data?.queues.outbox.pendingCount ?? 0) > 0 ? 'Draining' : 'Empty / Idle'}
              </span>
            </div>

            <div className="space-y-2 text-xs">
              <div className="flex justify-between py-1 border-b border-[#374151]/50">
                <span className="text-gray-400">Pending Messages:</span>
                <span className="font-mono font-bold text-white">
                  {data?.queues.outbox.pendingCount ?? 0}
                </span>
              </div>
              <div className="flex justify-between py-1 border-b border-[#374151]/50">
                <span className="text-gray-400">Dispatched Messages:</span>
                <span className="font-mono text-emerald-400">
                  {data?.queues.outbox.dispatchedCount ?? 0}
                </span>
              </div>
              <div className="flex justify-between py-1 border-b border-[#374151]/50">
                <span className="text-gray-400">Failed / Retrying:</span>
                <span className="font-mono text-rose-400 font-bold">
                  {data?.queues.outbox.failedCount ?? 0}
                </span>
              </div>
              <div className="flex justify-between py-1">
                <span className="text-gray-400">Oldest Message Lag:</span>
                <span className="font-mono text-gray-300">
                  {data?.queues.outbox.oldestPendingAgeSeconds != null
                    ? `${data.queues.outbox.oldestPendingAgeSeconds}s`
                    : 'None (0s)'}
                </span>
              </div>
            </div>
          </div>

          {/* Webhook DLQ */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-3">
                <span className="text-xs font-bold text-white flex items-center gap-2">
                  <AlertTriangle className="w-4 h-4 text-amber-400" />
                  <span>Webhook DLQ</span>
                </span>
                <span className="text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                  F-05
                </span>
              </div>

              <div className="space-y-2 text-xs">
                <div className="flex justify-between py-1 border-b border-[#374151]/50">
                  <span className="text-gray-400">Exhausted Deliveries:</span>
                  <span className="font-mono font-bold text-amber-400">
                    {data?.queues.webhookDlq.exhaustedCount ?? 0}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-[#374151]/50">
                  <span className="text-gray-400">Replayed Deliveries:</span>
                  <span className="font-mono text-emerald-400">
                    {data?.queues.webhookDlq.replayedCount ?? 0}
                  </span>
                </div>
                <div className="flex justify-between py-1">
                  <span className="text-gray-400">Total DLQ Entries:</span>
                  <span className="font-mono text-white">
                    {data?.queues.webhookDlq.totalCount ?? 0}
                  </span>
                </div>
              </div>
            </div>

            {onNavigate && (
              <button
                onClick={() => onNavigate('webhook-dlq')}
                className="mt-4 w-full flex items-center justify-center gap-2 bg-[#1f2937] hover:bg-[#374151] text-xs font-medium text-gray-200 py-2 rounded-lg transition border border-[#374151]"
              >
                <span>Manage Webhook DLQ</span>
                <ArrowRight className="w-3.5 h-3.5 text-blue-400" />
              </button>
            )}
          </div>

          {/* Unresolved Payments */}
          <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-3">
                <span className="text-xs font-bold text-white flex items-center gap-2">
                  <Clock className="w-4 h-4 text-cyan-400" />
                  <span>Unresolved Payments</span>
                </span>
                <span className="text-[10px] bg-purple-500/20 text-purple-300 border border-purple-500/30 px-1.5 py-0.2 rounded font-mono">
                  F-01
                </span>
              </div>

              <div className="space-y-2 text-xs">
                <div className="flex justify-between py-1 border-b border-[#374151]/50">
                  <span className="text-gray-400">Pending / Unknown:</span>
                  <span className="font-mono font-bold text-cyan-400">
                    {data?.queues.unresolvedPayments.totalUnresolvedCount ?? 0}
                  </span>
                </div>
                <div className="flex justify-between py-1 border-b border-[#374151]/50">
                  <span className="text-gray-400">Settlement Reconciled:</span>
                  <span className="font-mono text-emerald-400">Active</span>
                </div>
                <div className="flex justify-between py-1">
                  <span className="text-gray-400">Reconciliation Worker:</span>
                  <span className="font-mono text-gray-300">Healthy</span>
                </div>
              </div>
            </div>

            {onNavigate && (
              <button
                onClick={() => onNavigate('unresolved')}
                className="mt-4 w-full flex items-center justify-center gap-2 bg-[#1f2937] hover:bg-[#374151] text-xs font-medium text-gray-200 py-2 rounded-lg transition border border-[#374151]"
              >
                <span>View Unresolved Console</span>
                <ArrowRight className="w-3.5 h-3.5 text-blue-400" />
              </button>
            )}
          </div>
        </div>
      </div>

      {/* SECTION 5: External Monitoring Dashboards Quick Access */}
      <div className="bg-[#111827] border border-[#374151] rounded-xl p-5 shadow-sm">
        <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-gray-400 mb-3">
          <ExternalLink className="w-4 h-4 text-blue-400" />
          <span>Operational Tooling Dashboards</span>
        </div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <a
            href={GRAFANA_URL}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-between p-3 rounded-lg bg-[#1f2937] hover:bg-[#374151] border border-[#374151] text-xs text-white transition group"
          >
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-orange-400"></span>
              <span className="font-medium">Grafana (Cockpits)</span>
            </div>
            <ExternalLink className="w-3.5 h-3.5 text-gray-400 group-hover:text-white" />
          </a>

          <a
            href={PROMETHEUS_URL}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-between p-3 rounded-lg bg-[#1f2937] hover:bg-[#374151] border border-[#374151] text-xs text-white transition group"
          >
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-red-400"></span>
              <span className="font-medium">Prometheus (Meters)</span>
            </div>
            <ExternalLink className="w-3.5 h-3.5 text-gray-400 group-hover:text-white" />
          </a>

          <a
            href={ALERTMANAGER_URL}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-between p-3 rounded-lg bg-[#1f2937] hover:bg-[#374151] border border-[#374151] text-xs text-white transition group"
          >
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-rose-400"></span>
              <span className="font-medium">Alertmanager (Alerts)</span>
            </div>
            <ExternalLink className="w-3.5 h-3.5 text-gray-400 group-hover:text-white" />
          </a>

          <a
            href={JAEGER_URL}
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-between p-3 rounded-lg bg-[#1f2937] hover:bg-[#374151] border border-[#374151] text-xs text-white transition group"
          >
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-cyan-400"></span>
              <span className="font-medium">Jaeger (Distributed Traces)</span>
            </div>
            <ExternalLink className="w-3.5 h-3.5 text-gray-400 group-hover:text-white" />
          </a>
        </div>
        {lastRefreshedAt && (
          <div className="mt-3 text-[11px] text-gray-500 text-right">
            Last updated: {lastRefreshedAt.toLocaleTimeString()}
          </div>
        )}
      </div>
    </div>
  );
};
