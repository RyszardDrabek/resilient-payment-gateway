import {
  DispositionRiskFlagRequest,
  MerchantWebhookSubscriptionDto,
  OpenRiskFlagsResponse,
  PaymentDto,
  PaymentProblemDetails,
  RiskFlagDto,
  RiskVerdictDto,
  TestWebhookDispatchRequest,
  TestWebhookDispatchResultDto,
  UnresolvedPaymentsResponse,
  UpdateWebhookSubscriptionRequest,
} from '../types/api';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5080';

export class ApiError extends Error {
  public status: number;
  public problem?: PaymentProblemDetails;

  constructor(status: number, message: string, problem?: PaymentProblemDetails) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (response.ok) {
    return (await response.json()) as T;
  }

  let problem: PaymentProblemDetails | undefined;
  let message = `API request failed with status ${response.status}`;

  try {
    const errorJson = await response.json();
    if (errorJson && (errorJson.type || errorJson.title || errorJson.detail)) {
      problem = errorJson as PaymentProblemDetails;
      message = problem.detail || problem.title || message;
    }
  } catch {
    // Non-JSON error body
  }

  throw new ApiError(response.status, message, problem);
}

export interface UnresolvedQueryParams {
  channel?: string;
  ageMinutes?: number;
  page?: number;
  pageSize?: number;
}

export async function fetchUnresolvedPayments(
  token: string,
  params: UnresolvedQueryParams = {}
): Promise<UnresolvedPaymentsResponse> {
  const query = new URLSearchParams();
  if (params.channel) query.set('channel', params.channel);
  if (params.ageMinutes !== undefined && params.ageMinutes > 0) query.set('ageMinutes', params.ageMinutes.toString());
  if (params.page !== undefined) query.set('page', params.page.toString());
  if (params.pageSize !== undefined) query.set('pageSize', params.pageSize.toString());

  const url = `${API_BASE_URL}/api/v1/payments/unresolved?${query.toString()}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<UnresolvedPaymentsResponse>(response);
}

export async function fetchPaymentById(
  token: string,
  paymentId: string
): Promise<PaymentDto> {
  const url = `${API_BASE_URL}/api/v1/payments/${encodeURIComponent(paymentId)}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<PaymentDto>(response);
}

export async function fetchWebhookSubscription(
  token: string,
  partyId?: string
): Promise<MerchantWebhookSubscriptionDto> {
  const query = partyId ? `?partyId=${encodeURIComponent(partyId)}` : '';
  const url = `${API_BASE_URL}/api/v1/webhooks/merchants/subscription${query}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<MerchantWebhookSubscriptionDto>(response);
}

export async function updateWebhookSubscription(
  token: string,
  data: UpdateWebhookSubscriptionRequest,
  partyId?: string
): Promise<MerchantWebhookSubscriptionDto> {
  const query = partyId ? `?partyId=${encodeURIComponent(partyId)}` : '';
  const url = `${API_BASE_URL}/api/v1/webhooks/merchants/subscription${query}`;

  const response = await fetch(url, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
    body: JSON.stringify(data),
  });

  return handleResponse<MerchantWebhookSubscriptionDto>(response);
}

export async function rotateWebhookSecret(
  token: string,
  partyId?: string
): Promise<MerchantWebhookSubscriptionDto> {
  const query = partyId ? `?partyId=${encodeURIComponent(partyId)}` : '';
  const url = `${API_BASE_URL}/api/v1/webhooks/merchants/subscription/rotate-secret${query}`;

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<MerchantWebhookSubscriptionDto>(response);
}

export async function dispatchTestWebhook(
  token: string,
  data: TestWebhookDispatchRequest = {},
  partyId?: string
): Promise<TestWebhookDispatchResultDto> {
  const query = partyId ? `?partyId=${encodeURIComponent(partyId)}` : '';
  const url = `${API_BASE_URL}/api/v1/webhooks/merchants/test-dispatch${query}`;

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
    body: JSON.stringify(data),
  });

  return handleResponse<TestWebhookDispatchResultDto>(response);
}

export async function fetchOpenRiskFlags(
  token: string,
  page = 1,
  pageSize = 50
): Promise<OpenRiskFlagsResponse> {
  const url = `${API_BASE_URL}/api/v1/risk/flags?page=${page}&pageSize=${pageSize}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<OpenRiskFlagsResponse>(response);
}

export async function fetchRiskVerdict(
  token: string,
  identifier: string
): Promise<RiskVerdictDto> {
  const url = `${API_BASE_URL}/api/v1/risk/verdicts/${encodeURIComponent(identifier)}`;

  const response = await fetch(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
  });

  return handleResponse<RiskVerdictDto>(response);
}

export async function submitRiskDisposition(
  token: string,
  flagId: string,
  data: DispositionRiskFlagRequest
): Promise<RiskFlagDto> {
  const url = `${API_BASE_URL}/api/v1/risk/flags/${encodeURIComponent(flagId)}/disposition`;

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      Accept: 'application/json',
      'X-Correlation-Id': `portal-${Date.now()}`,
    },
    body: JSON.stringify(data),
  });

  return handleResponse<RiskFlagDto>(response);
}

