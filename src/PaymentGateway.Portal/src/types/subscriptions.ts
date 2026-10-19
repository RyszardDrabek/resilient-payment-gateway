export interface PaymentMethodItem {
  id: string;
  partyId: string;
  settlementChannel: string;
  cardBrand: string;
  maskedPan: string;
  expiryMonth: number;
  expiryYear: number;
  status: 'Active' | 'Revoked';
  createdAt: string;
  revokedAt?: string | null;
}

export interface SubscriptionItem {
  id: string;
  partyId: string;
  paymentMethodId: string;
  amount: number;
  currency: string;
  interval: string;
  state: 'Active' | 'Paused' | 'PastDue' | 'Cancelled';
  nextBillingAt: string;
  failureCount: number;
  lastBilledAt?: string | null;
  createdAt: string;
  cancelledAt?: string | null;
}
