import http from 'k6/http';
import { check } from 'k6';

const orderBaseUrl = (__ENV.ORDER_BASE_URL || 'http://localhost:8081').replace(/\/$/, '');
const productId = __ENV.PRODUCT_ID;
const runId = __ENV.RUN_ID || 'adhoc';
const orderQty = Number(__ENV.ORDER_QTY || '1');

if (!productId) {
  throw new Error('PRODUCT_ID env var is required');
}

export const options = {
  vus: Number(__ENV.VUS || '50'),
  iterations: Number(__ENV.ITERATIONS || '400'),
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500']
  }
};

export default function () {
  const email = `k6+${runId}-vu${__VU}-iter${__ITER}@example.com`;
  const payload = JSON.stringify({
    customerEmail: email,
    items: [{ productId, quantity: orderQty }]
  });

  const res = http.post(`${orderBaseUrl}/orders`, payload, {
    headers: { 'Content-Type': 'application/json' },
    tags: { scenario: 'hot_product_order_create' }
  });

  check(res, {
    'create order returns 201': (r) => r.status === 201
  });
}
