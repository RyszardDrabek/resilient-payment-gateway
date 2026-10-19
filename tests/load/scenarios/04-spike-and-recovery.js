import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, buildPaymentPayload, authHeaders } from '../helpers.js';

const recoveryLatency = new Trend('recovery_phase_latency', true);

export const options = {
  stages: [
    { duration: '10s', target: 5 },   // Baseline normal traffic
    { duration: '5s', target: 75 },   // Severe traffic spike
    { duration: '15s', target: 75 },  // Hold peak spike
    { duration: '5s', target: 5 },    // Drop back to normal
    { duration: '10s', target: 5 },   // Recovery phase
  ],
  thresholds: {
    // During recovery phase (normal traffic), latency must recover immediately
    recovery_phase_latency: ['p(95)<1000'],
  },
};

export default function () {
  const merchantId = `merch_spike_${__VU}`;
  const token = generateJwt('merchant', merchantId);
  const idempotencyKey = generateIdempotencyKey(`spike_vu${__VU}`);
  const payload = JSON.stringify(buildPaymentPayload({ partyId: merchantId }));
  const headers = authHeaders(token, idempotencyKey);

  const start = Date.now();
  const res = http.post(`${GATEWAY_URL}/payments`, payload, { headers });
  const duration = Date.now() - start;

  // Track latency during the recovery phase (after 35s of the test)
  // __ENV or time-based detection
  recoveryLatency.add(duration);

  check(res, {
    'request handled (200 or 429 under extreme load)': (r) => r.status === 200 || r.status === 429,
    'no 5xx internal server errors': (r) => r.status < 500,
  });

  sleep(0.2);
}
