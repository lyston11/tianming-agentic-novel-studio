import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');

// Test configuration
export const options = {
  stages: [
    { duration: '30s', target: 50 },  // Ramp up to 50 users over 30s
    { duration: '1m', target: 100 },  // Ramp up to 100 users over 1 minute
    { duration: '2m', target: 100 },  // Stay at 100 users for 2 minutes
    { duration: '30s', target: 0 },   // Ramp down to 0 users
  ],
  thresholds: {
    http_req_duration: ['p(95)<100'], // 95% of requests should be below 100ms
    http_req_failed: ['rate<0.01'],   // Error rate should be less than 1%
  },
};

// Test data
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const JWT_TOKEN = __ENV.JWT_TOKEN || '';

// Setup: Create test user and get token (run once per VU)
export function setup() {
  // If JWT_TOKEN is provided, use it; otherwise, register/login
  if (JWT_TOKEN) {
    return { token: JWT_TOKEN };
  }

  // Register a test user
  const username = `testuser_${Date.now()}_${__VU}`;
  const registerPayload = JSON.stringify({
    username: username,
    email: `${username}@test.com`,
    password: 'TestPassword123!',
  });

  const registerRes = http.post(`${BASE_URL}/api/auth/register`, registerPayload, {
    headers: { 'Content-Type': 'application/json' },
  });

  if (registerRes.status === 200 || registerRes.status === 201) {
    const data = registerRes.json();
    return { token: data.token };
  }

  // If registration fails (user exists), try login
  const loginPayload = JSON.stringify({
    emailOrUsername: username,
    password: 'TestPassword123!',
  });

  const loginRes = http.post(`${BASE_URL}/api/auth/login`, loginPayload, {
    headers: { 'Content-Type': 'application/json' },
  });

  if (loginRes.status === 200) {
    const data = loginRes.json();
    return { token: data.token };
  }

  console.error('Failed to setup test user');
  return { token: '' };
}

export default function (data) {
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${data.token}`,
  };

  // Test 1: Get user settings (cached after first request)
  const settingsRes = http.get(`${BASE_URL}/api/settings`, { headers });
  check(settingsRes, {
    'settings status is 200': (r) => r.status === 200,
    'settings response time < 100ms': (r) => r.timings.duration < 100,
  }) || errorRate.add(1);

  sleep(0.5);

  // Test 2: Get projects list (with pagination)
  const projectsRes = http.get(`${BASE_URL}/api/projects?pageNumber=1&pageSize=20`, { headers });
  check(projectsRes, {
    'projects status is 200': (r) => r.status === 200,
    'projects response time < 100ms': (r) => r.timings.duration < 100,
  }) || errorRate.add(1);

  sleep(0.5);

  // Test 3: Create a project
  const projectPayload = JSON.stringify({
    title: `Test Novel ${Date.now()}`,
    genre: '玄幻',
    subGenre: '东方玄幻',
    coreHook: 'A young cultivator discovers ancient secrets',
  });

  const createRes = http.post(`${BASE_URL}/api/projects`, projectPayload, { headers });
  check(createRes, {
    'create project status is 200': (r) => r.status === 200 || r.status === 201,
  }) || errorRate.add(1);

  if (createRes.status === 200 || createRes.status === 201) {
    const projectData = createRes.json();
    const projectId = projectData.id;

    sleep(0.5);

    // Test 4: Get single project (cached after first request)
    const singleProjectRes = http.get(`${BASE_URL}/api/projects/${projectId}`, { headers });
    check(singleProjectRes, {
      'get project status is 200': (r) => r.status === 200,
      'get project response time < 100ms': (r) => r.timings.duration < 100,
    }) || errorRate.add(1);

    sleep(0.5);

    // Test 5: Get project again (should be cached now)
    const cachedProjectRes = http.get(`${BASE_URL}/api/projects/${projectId}`, { headers });
    check(cachedProjectRes, {
      'cached project status is 200': (r) => r.status === 200,
      'cached project response time < 50ms': (r) => r.timings.duration < 50,
    }) || errorRate.add(1);

    sleep(0.5);

    // Test 6: Update project (should invalidate cache)
    const updatePayload = JSON.stringify({
      title: `Updated Novel ${Date.now()}`,
    });

    const updateRes = http.put(`${BASE_URL}/api/projects/${projectId}`, updatePayload, { headers });
    check(updateRes, {
      'update project status is 200': (r) => r.status === 200,
    }) || errorRate.add(1);

    sleep(0.5);

    // Test 7: Delete project (cleanup)
    const deleteRes = http.del(`${BASE_URL}/api/projects/${projectId}`, null, { headers });
    check(deleteRes, {
      'delete project status is 200 or 204': (r) => r.status === 200 || r.status === 204,
    }) || errorRate.add(1);
  }

  sleep(1);
}

export function handleSummary(data) {
  return {
    'stress-test-results.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  };
}

function textSummary(data, options) {
  const indent = options.indent || '';
  const enableColors = options.enableColors !== false;

  let summary = '\n';
  summary += `${indent}Stress Test Summary:\n`;
  summary += `${indent}===================\n\n`;

  const metrics = data.metrics;

  if (metrics.http_req_duration) {
    summary += `${indent}Response Times:\n`;
    summary += `${indent}  Min:     ${metrics.http_req_duration.values.min.toFixed(2)}ms\n`;
    summary += `${indent}  Avg:     ${metrics.http_req_duration.values.avg.toFixed(2)}ms\n`;
    summary += `${indent}  P50:     ${metrics.http_req_duration.values['p(50)'].toFixed(2)}ms\n`;
    summary += `${indent}  P95:     ${metrics.http_req_duration.values['p(95)'].toFixed(2)}ms\n`;
    summary += `${indent}  P99:     ${metrics.http_req_duration.values['p(99)'].toFixed(2)}ms\n`;
    summary += `${indent}  Max:     ${metrics.http_req_duration.values.max.toFixed(2)}ms\n\n`;
  }

  if (metrics.http_reqs) {
    summary += `${indent}Request Stats:\n`;
    summary += `${indent}  Total:   ${metrics.http_reqs.values.count}\n`;
    summary += `${indent}  Rate:    ${metrics.http_reqs.values.rate.toFixed(2)} req/s\n\n`;
  }

  if (metrics.http_req_failed) {
    const failRate = metrics.http_req_failed.values.rate * 100;
    summary += `${indent}Error Rate: ${failRate.toFixed(2)}%\n\n`;
  }

  if (metrics.errors) {
    const errorRate = metrics.errors.values.rate * 100;
    summary += `${indent}Custom Errors: ${errorRate.toFixed(2)}%\n\n`;
  }

  return summary;
}
