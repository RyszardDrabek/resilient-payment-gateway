import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, buildPaymentPayload, authHeaders } from '../helpers.js';

const paymentDuration = new Trend('payment_authorize_duration', true);
const paymentCount = new Counter('payments_authorized_total');

export const options = {
  stages: [
    { duration: '15s', target: 25 },  // Ramp up to 25 VUs
    { duration: '30s', target: 25 },  // Sustained load at 25 VUs
    { duration: '10s', target: 0 },   // Ramp down
  ],
  thresholds: {
    // SLA Contract Thresholds (ADR-007, ADR-016)
    http_req_failed: ['rate<0.01'],              // Less than 1% failed HTTP requests
    http_req_duration: ['p(95)<2000', 'p(99)<3000'], // Latency SLA: p95 < 2.0s, p99 < 3.0s
    payment_authorize_duration: ['p(95)<1500'],  // Core payment processing p95 < 1.5s
  },
};

export default function () {
  // Use unique merchant partition per Virtual User to test multi-tenancy distribution
  const merchantId = `merch_load_${__VU}`;
  const token = generateJwt('merchant', merchantId);
  const idempotencyKey = generateIdempotencyKey(`vu${__VU}`);
  const payload = JSON.stringify(buildPaymentPayload({ partyId: merchantId }));
  const headers = authHeaders(token, idempotencyKey);

  const start = Date.now();
  const res = http.post(`${GATEWAY_URL}/payments`, payload, { headers });
  const duration = Date.now() - start;

  paymentDuration.add(duration);

  const success = check(res, {
    'status is 200': (r) => r.status === 200,
    'has payment id': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.id !== undefined && body.id !== null;
      } catch {
        return false;
      }
    },
    'correlation id header present': (r) => r.headers['X-Correlation-Id'] !== undefined,
  });

  if (success) {
    paymentCount.add(1);
  }

  // Paced user think-time between 100ms and 400ms
  sleep(Math.random() * 0.3 + 0.1);
}
