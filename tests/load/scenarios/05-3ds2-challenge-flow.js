import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, authHeaders } from '../helpers.js';

const challengeInitDuration = new Trend('challenge_init_duration', true);
const challengeResolveDuration = new Trend('challenge_resolve_duration', true);
const completedThreeDsCounter = new Counter('threeds_challenges_completed_total');

export const options = {
  stages: [
    { duration: '10s', target: 15 }, // Ramp up to 15 VUs
    { duration: '20s', target: 15 }, // Sustained 3DS challenge load
    { duration: '5s', target: 0 },   // Ramp down
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],              // <1% failure rate
    http_req_duration: ['p(95)<2000'],           // p95 < 2s
    challenge_init_duration: ['p(95)<1500'],     // Initial authorization p95 < 1.5s
    challenge_resolve_duration: ['p(95)<1500'],  // 3DS result submission p95 < 1.5s
    threeds_challenges_completed_total: ['count>50'], // At least 50 completed full flows
  },
};

export default function () {
  const merchantPartition = ((__VU * 1000 + __ITER) % 500) + 1;
  const merchantId = `merch_3ds_${merchantPartition}`;
  const token = generateJwt('merchant', merchantId);
  const idempotencyKey = generateIdempotencyKey(`3ds_vu${__VU}_it${__ITER}`);
  const headers = authHeaders(token, idempotencyKey);

  // Step 1: Initiate payment requesting 3DS2 Challenge (partyId ending in 0002)
  const initPayload = JSON.stringify({
    partyId: `cardholder_${merchantPartition}_challenge_0002`,
    amount: 5500,
    currency: 'EUR',
    settlementChannel: 'ADYEN',
  });

  const initStart = Date.now();
  const initRes = http.post(`${GATEWAY_URL}/payments`, initPayload, { headers });
  challengeInitDuration.add(Date.now() - initStart);

  let paymentId = null;
  const initSuccess = check(initRes, {
    'step1: status is 201': (r) => r.status === 201,
    'step1: state is RequiresAction': (r) => {
      try {
        const body = JSON.parse(r.body);
        paymentId = body.paymentId;
        return body.state === 'RequiresAction' && body.action !== null && body.action !== undefined;
      } catch {
        return false;
      }
    },
    'step1: action has targetUrl and token': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.action?.targetUrl !== undefined && body.action?.token !== undefined;
      } catch {
        return false;
      }
    },
  });

  if (!initSuccess || !paymentId) {
    sleep(0.2);
    return;
  }

  // Brief pause simulating cardholder completing challenge in authentication frame
  sleep(0.1);

  // Step 2: Submit 3DS challenge resolution payload
  const resolvePayload = JSON.stringify({
    threeDsResult: 'completed',
    details: {
      threeDSResult: 'challenge_success',
    },
  });

  const resolveHeaders = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
    Accept: 'application/json',
    'X-Correlation-Id': `k6-3ds-${paymentId}`,
  };

  const resolveStart = Date.now();
  const resolveRes = http.post(`${GATEWAY_URL}/payments/${paymentId}/3ds-result`, resolvePayload, { headers: resolveHeaders });
  challengeResolveDuration.add(Date.now() - resolveStart);

  const resolveSuccess = check(resolveRes, {
    'step2: status is 200': (r) => r.status === 200,
    'step2: state is Authorized': (r) => {
      try {
        const body = JSON.parse(r.body);
        return body.state === 'Authorized';
      } catch {
        return false;
      }
    },
    'step2: correlation id present': (r) => (r.headers['X-Correlation-Id'] || r.headers['x-correlation-id']) !== undefined,
  });

  if (resolveSuccess) {
    completedThreeDsCounter.add(1);
  }

  sleep(Math.random() * 0.2 + 0.1);
}
