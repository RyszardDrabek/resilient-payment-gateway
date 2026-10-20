import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, buildPaymentPayload, authHeaders } from '../helpers.js';

const chaosLatency = new Trend('chaos_payment_latency', true);
const serverErrors = new Counter('server_errors');
const pendingOutcomes = new Counter('pending_degraded_outcomes');

export const options = {
  stages: [
    { duration: '10s', target: 5 },   // Phase 1: Clean baseline
    { duration: '20s', target: 20 },  // Phase 2: Active chaos & fault injection (latency, broker drops)
    { duration: '15s', target: 20 },  // Phase 3: Hold chaos load
    { duration: '15s', target: 5 },   // Phase 4: Recovery and cool down
  ],
  thresholds: {
    // Under chaos, healthy requests must complete within SLA; degraded requests must not crash
    chaos_payment_latency: ['p(95)<3000'],
    server_errors: ['count==0'],       // Zero unhandled 5xx errors permitted during fault injection
    checks: ['rate>0.98'],
  },
};

export default function () {
  const merchantId = `merch_chaos_${__VU}`;
  const token = generateJwt('merchant', merchantId);
  const idempotencyKey = generateIdempotencyKey(`chaos_vu${__VU}`);
  
  // Every 5th request intentionally triggers simulated acquirer latency
  const isChaosTarget = (__ITER % 5 === 0);
  const partyId = isChaosTarget ? `party_chaos_timeout_${__VU}` : `party_chaos_steady_${__VU}`;

  const payload = JSON.stringify(buildPaymentPayload({
    partyId: partyId,
    amount: 3500 + (__ITER * 10),
    currency: 'EUR',
  }));

  const headers = authHeaders(token, idempotencyKey);

  const start = Date.now();
  const res = http.post(`${GATEWAY_URL}/payments`, payload, { headers });
  const duration = Date.now() - start;

  chaosLatency.add(duration);

  if (res.status >= 500) {
    serverErrors.add(1);
  }

  if (res.status === 201) {
    try {
      const body = JSON.parse(res.body);
      if (body.state === 'Pending') {
        pendingOutcomes.add(1);
      }
    } catch (e) {
      // Ignore JSON parse errors in metric counter
    }
  }

  check(res, {
    'status is 201 Created or acceptable handled response': (r) => r.status === 201 || r.status === 200 || r.status === 429,
    'zero unhandled 500 server crashes': (r) => r.status < 500,
    'correlation id present in response headers': (r) => r.headers['X-Correlation-Id'] !== undefined || r.headers['x-correlation-id'] !== undefined,
  });

  sleep(0.3);
}
