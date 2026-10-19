import http from 'k6/http';
import { check, sleep } from 'k6';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, buildPaymentPayload, authHeaders } from '../helpers.js';

export const options = {
  vus: 2,
  duration: '10s',
  thresholds: {
    http_req_failed: ['rate<0.01'], // < 1% error rate
    http_req_duration: ['p(95)<500'], // 95% of requests should be below 500ms
  },
};

export default function () {
  const merchantToken = generateJwt('merchant', 'merch_smoke');
  const opsToken = generateJwt('ops', 'ops_smoke');

  // 1. Probe Liveness
  const livenessRes = http.get(`${GATEWAY_URL}/healthz`);
  check(livenessRes, {
    'liveness status is 200': (r) => r.status === 200,
    'liveness payload contains Healthy': (r) => r.body && r.body.includes('Healthy'),
  });

  // 2. Probe Readiness
  const readinessRes = http.get(`${GATEWAY_URL}/health`);
  check(readinessRes, {
    'readiness status is 200': (r) => r.status === 200,
    'readiness payload contains Healthy': (r) => r.body && r.body.includes('Healthy'),
  });

  // 3. Authorize Payment
  const idempotencyKey = generateIdempotencyKey('smoke');
  const payload = JSON.stringify(buildPaymentPayload({ partyId: 'merch_smoke' }));
  const headers = authHeaders(merchantToken, idempotencyKey);

  const authRes = http.post(`${GATEWAY_URL}/payments`, payload, { headers });
  check(authRes, {
    'payment authorize status is 200': (r) => r.status === 200,
    'payment returns paymentId': (r) => {
      try {
        const json = JSON.parse(r.body);
        return json.id && json.status;
      } catch {
        return false;
      }
    },
  });

  // 4. Ops Observability Telemetry
  const obsHeaders = authHeaders(opsToken);
  const obsRes = http.get(`${GATEWAY_URL}/api/v1/ops/observability`, { headers: obsHeaders });
  check(obsRes, {
    'observability status is 200': (r) => r.status === 200,
    'observability returns system health': (r) => {
      try {
        const json = JSON.parse(r.body);
        return json.api && json.database;
      } catch {
        return false;
      }
    },
  });

  // 5. Prometheus Metrics Scrape
  const metricsRes = http.get(`${GATEWAY_URL}/metrics`);
  check(metricsRes, {
    'metrics status is 200': (r) => r.status === 200,
    'metrics content has target_info or payment meters': (r) => r.body && (r.body.includes('target_info') || r.body.includes('payment_gateway')),
  });

  sleep(1);
}
