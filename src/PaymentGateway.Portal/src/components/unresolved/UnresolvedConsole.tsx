import React, { useEffect, useState, useCallback } from 'react';
import { useAuth } from '../../context/AuthContext';
import { UnresolvedPaymentSummaryDto, UnresolvedPaymentsResponse } from '../../types/api';
import { fetchUnresolvedPayments } from '../../services/apiClient';
import { MetricCards } from './MetricCards';
import { FilterToolbar } from './FilterToolbar';
import { UnresolvedTable } from './UnresolvedTable';
import { PaymentDetailModal } from './PaymentDetailModal';
import { CardSkeleton, TableSkeleton } from '../common/LoadingSkeleton';
import { ErrorAlert } from '../common/ErrorAlert';
import { EmptyState } from '../common/EmptyState';

export const UnresolvedConsole: React.FC = () => {
  const { token, setRole } = useAuth();

  const [data, setData] = useState<UnresolvedPaymentsResponse | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<Error | null>(null);

  // Filters state
  const [channel, setChannel] = useState<string>('');
  const [ageMinutes, setAgeMinutes] = useState<number>(15);
  const [page, setPage] = useState<number>(1);
  const pageSize = 10;

  // Detail Modal
  const [selectedPaymentId, setSelectedPaymentId] = useState<string | null>(null);

  const loadData = useCallback(() => {
    if (!token) return;

    setLoading(true);
    setError(null);

    fetchUnresolvedPayments(token, {
      channel: channel || undefined,
      ageMinutes: ageMinutes > 0 ? ageMinutes : undefined,
      page,
      pageSize,
    })
      .then((res) => {
        setData(res);
      })
      .catch((err) => {
        setError(err);
      })
      .finally(() => {
        setLoading(false);
      });
  }, [token, channel, ageMinutes, page, pageSize]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleChannelChange = (newChannel: string) => {
    setChannel(newChannel);
    setPage(1);
  };

  const handleAgeChange = (newAge: number) => {
    setAgeMinutes(newAge);
    setPage(1);
  };

  const items: UnresolvedPaymentSummaryDto[] = data?.items || [];
  const totalCount = data?.totalCount || 0;

  return (
    <div>
      <div className="mb-6">
        <h1 className="text-xl sm:text-2xl font-bold text-white tracking-tight mb-1">
          Unresolved Payments Console
        </h1>
        <p className="text-xs sm:text-sm text-gray-400">
          Real-time investigation console for in-flight transactions exceeding settlement reconciliation thresholds.
        </p>
      </div>

      {/* Error / Unauthorized Alert */}
      {error && (
        <ErrorAlert
          error={error}
          onRetry={loadData}
          onSwitchToOps={() => setRole('ops')}
        />
      )}

      {/* Metric KPI Summary */}
      {loading && !data ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
        </div>
      ) : (
        <MetricCards items={items} totalCount={totalCount} />
      )}

      {/* Filter Toolbar */}
      <FilterToolbar
        channel={channel}
        onChannelChange={handleChannelChange}
        ageMinutes={ageMinutes}
        onAgeMinutesChange={handleAgeChange}
        onRefresh={loadData}
        loading={loading}
      />

      {/* Main Table / States */}
      {loading && !data ? (
        <TableSkeleton rows={6} />
      ) : items.length === 0 && !error ? (
        <EmptyState
          title="No Unresolved Payments"
          description={
            channel || ageMinutes > 0
              ? 'No payments match the specified channel or age threshold.'
              : 'All gateway payments have been reconciled with acquirers and blockchain networks.'
          }
        />
      ) : (
        <UnresolvedTable
          items={items}
          page={page}
          pageSize={pageSize}
          totalCount={totalCount}
          onPageChange={setPage}
          onSelectPayment={(id) => setSelectedPaymentId(id)}
        />
      )}

      {/* Detail Inspection Modal */}
      <PaymentDetailModal
        paymentId={selectedPaymentId}
        token={token}
        onClose={() => setSelectedPaymentId(null)}
      />
    </div>
  );
};
