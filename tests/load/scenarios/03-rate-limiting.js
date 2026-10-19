import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';
import { GATEWAY_URL, generateJwt, generateIdempotencyKey, buildPaymentPayload, authHeaders } from '../helpers.js';

const rateLimitHits = new Counter('rate_limit_429_total');
const rateLimitExpected = new Rate('rate_limit_behavior_correct');

export const options = {
  // Rapid burst from single merchant identity to trigger rate limit partition
  scenarios: {
    flood_single_tenant: {
      executor: 'constant-arrival-rate',
      rate: 80, // 80 req/s will quickly breach the 100 req/min bucket
      timeUnit: '1s',
      duration: '10s',
      preAllocatedVUs: 20,
      maxVUs: 50,
    },
  },
  thresholds: {
    // Assert that the rate limiter engaged and protected the system
    rate_limit_429_total: ['count>10'],
    rate_limit_behavior_correct: ['rate>0.95'],
  },
};

export default function () {
  // Shared merchant identity: all requests share the same rate-limit bucket
  const singleMerchantId = 'merch_rate_limit_target';
  const token = generateJwt('merchant', singleMerchantId);
  const idempotencyKey = generateIdempotencyKey('ratelimit');
  const payload = JSON.stringify(buildPaymentPayload({ partyId: singleMerchantId }));
  const headers = authHeaders(token, idempotencyKey);

  const res = http.post(`${GATEWAY_URL}/payments`, payload, { headers });

  if (res.status === 429) {
    rateLimitHits.add(1);

    const isProblemJson = check(res, {
      '429 returned when over limit': (r) => r.status === 429,
      '429 content-type is problem+json': (r) => r.headers['Content-Type'] && r.headers['Content-Type'].includes('problem+json'),
      '429 has RFC 7807 structure': (r) => {
        try {
          const body = JSON.parse(r.body);
          return body.status === 429 && body.type && body.title;
        } catch {
          return false;
        }
      },
    });

    rateLimitExpected.add(isProblemJson);
  } else if (res.status === 200) {
    const isOk = check(res, {
      'initial requests return 200 OK': (r) => r.status === 200,
    });
    rateLimitExpected.add(isOk);
  } else {
    rateLimitExpected.add(false);
  }
}
