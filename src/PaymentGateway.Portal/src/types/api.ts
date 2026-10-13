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
