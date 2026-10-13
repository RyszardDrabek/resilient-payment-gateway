export interface UnresolvedPaymentSummaryDto {
  paymentId: string;
  state: string;
  settlementChannel: string;
  channelReference: string | null;
  createdAt: string;
  durationElapsedMinutes: number;
}

export interface UnresolvedPaymentsResponse {
  items: UnresolvedPaymentSummaryDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  ageMinutes: number;
  channel: string | null;
}

export interface PaymentDto {
  paymentId: string;
  partyId: string;
  amount: number;
  currency: string;
  settlementChannel: string;
  state: string;
  channelReference: string | null;
  declineReason: string | null;
  createdAt: string;
  capturedAmount: number;
  remainingAuthorizedAmount: number;
  refundedAmount: number;
  remainingRefundableAmount: number;
}

export interface PaymentProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  correlationId?: string;
  invalidParams?: Array<{ name: string; reason: string }>;
}

export interface MerchantWebhookSubscriptionDto {
  id: string;
  partyId: string;
  endpointUrl: string;
  secretKey: string;
  isActive: boolean;
}

export interface UpdateWebhookSubscriptionRequest {
  endpointUrl: string;
  isActive: boolean;
}

export interface TestWebhookDispatchRequest {
  endpointUrl?: string;
  eventType?: string;
}

export interface TestWebhookDispatchResultDto {
  success: boolean;
  statusCode: number | null;
  latencyMs: number;
  endpointUrl: string;
  eventId: string;
  timestamp: string;
  signature: string;
  requestHeaders: Record<string, string>;
  requestPayload: string;
  responseBody: string | null;
  errorMessage: string | null;
}

export type RiskDisposition = 'confirmed' | 'false_positive' | 'escalated';

export interface RiskFlagDto {
  id: string;
  paymentId: string;
  partyId: string;
  eventId: string;
  reason: string;
  status: string;
  disposition: string | null;
  createdAt: string;
  dispositionedAt: string | null;
  reviewerNotes: string | null;
}

export interface OpenRiskFlagsResponse {
  items: RiskFlagDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface RiskVerdictDto {
  id: string;
  eventId: string;
  paymentId: string;
  partyId: string;
  status: string;
  reason: string;
  flagId: string | null;
  scoredAt: string;
}

export interface DispositionRiskFlagRequest {
  disposition: RiskDisposition;
  notes?: string | null;
}

