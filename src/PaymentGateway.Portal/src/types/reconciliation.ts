export interface ReconciliationSummary {
  totalRecords: number;
  matchedCount: number;
  discrepancyCount: number;
  matchedVolume: number;
  discrepancyVolume: number;
  matchRatePercentage: number;
}

export interface DiscrepancyItem {
  id: string;
  paymentId: string | null;
  channel: string;
  channelReference: string;
  gatewayAmount: number;
  acquirerAmount: number;
  discrepancyType: string;
  status: string;
  reconciledAt: string;
  notes?: string;
}

export interface PayoutItem {
  id: string;
  paymentId: string;
  grossAmount: number;
  feeAmount: number;
  netAmount: number;
  currency: string;
  capturedAt: string;
}

export interface MerchantPayoutBatch {
  id: string;
  merchantId: string;
  periodStart: string;
  periodEnd: string;
  grossAmount: number;
  feeAmount: number;
  netAmount: number;
  currency: string;
  status: string;
  createdAt: string;
  executedAt?: string;
  failureReason?: string;
  items: PayoutItem[];
}
