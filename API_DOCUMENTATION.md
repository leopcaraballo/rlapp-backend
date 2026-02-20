# 📡 API Documentation — RLAPP Backend

Complete REST API reference for the WaitingRoom service.

**Base URL:** `http://localhost:5000`
**API Version:** v1
**Content-Type:** `application/json`

---

## 📋 Table of Contents

1. [Authentication](#authentication)
2. [Common Headers](#common-headers)
3. [Error Handling](#error-handling)
4. [Command Endpoints](#command-endpoints)
5. [Query Endpoints](#query-endpoints)
6. [Health & Status](#health--status)
7. [Examples (curl)](#examples-curl)

---

## 🔐 Authentication

**Current Status:** No authentication implemented (development mode)

**Future:** OAuth 2.0 / JWT bearer tokens

---

## 📄 Common Headers

All requests should include:

```http
Content-Type: application/json
Accept: application/json
X-Correlation-Id: <optional-uuid>  # For tracing, auto-generated if omitted
```

All responses include:

```http
Content-Type: application/json
X-Correlation-Id: <uuid>
```

---

## ⚠️ Error Handling

### Error Response Format

```json
{
  "error": "Human-readable error message",
  "statusCode": 400,
  "detail": "Optional detailed error description",
  "timestamp": "2026-02-19T10:30:00Z"
}
```

### HTTP Status Codes

| Code | Meaning | When Used |
|------|---------|-----------|
| **200** | OK | Successful query |
| **202** | Accepted | Async operation initiated |
| **400** | Bad Request | Invalid input, missing required fields |
| **404** | Not Found | Queue or resource doesn't exist |
| **409** | Conflict | Concurrency conflict (aggregate version mismatch) |
| **422** | Unprocessable Entity | Business rule violation (e.g., queue full) |
| **500** | Internal Server Error | Unexpected server error |

---

## 📝 Command Endpoints

Commands mutate state. They follow **CQRS write model** principles.

### POST /api/waiting-room/check-in

Register a patient to a waiting queue.

#### Request Body

```json
{
  "queueId": "QUEUE-001",
  "patientId": "PAT-12345",
  "patientName": "John Doe",
  "priority": "High",
  "consultationType": "General",
  "actor": "nurse-001",
  "notes": "Patient complains of headache"
}
```

#### Request Parameters

| Field | Type | Required | Description | Valid Values |
|-------|------|----------|-------------|--------------|
| `queueId` | string | ✅ Yes | Queue identifier | Any non-empty string |
| `patientId` | string | ✅ Yes | Unique patient ID | Any non-empty string |
| `patientName` | string | ✅ Yes | Patient full name | Any non-empty string |
| `priority` | string | ✅ Yes | Triage priority | `High`, `Medium`, `Low` |
| `consultationType` | string | ✅ Yes | Type of consultation | Any non-empty string |
| `actor` | string | ✅ Yes | Who performed check-in | User ID or username |
| `notes` | string | ❌ No | Additional notes | Optional string |

#### Success Response (200 OK)

```json
{
  "success": true,
  "message": "Patient checked in successfully",
  "correlationId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "eventCount": 1
}
```

#### Error Responses

**400 Bad Request** — Invalid input

```json
{
  "error": "Invalid request: Priority must be High, Medium, or Low",
  "statusCode": 400,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

**404 Not Found** — Queue doesn't exist

```json
{
  "error": "Queue 'QUEUE-999' not found",
  "statusCode": 404,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

**422 Unprocessable Entity** — Business rule violation

```json
{
  "error": "Queue is full: Cannot check in more patients",
  "statusCode": 422,
  "detail": "Max capacity: 20, Current: 20",
  "timestamp": "2026-02-19T10:30:00Z"
}
```

**409 Conflict** — Patient already in queue

```json
{
  "error": "Patient 'PAT-12345' already checked in",
  "statusCode": 409,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

#### Example (curl)

```bash
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: 123e4567-e89b-12d3-a456-426614174000" \
  -d '{
    "queueId": "QUEUE-001",
    "patientId": "PAT-12345",
    "patientName": "John Doe",
    "priority": "High",
    "consultationType": "General",
    "actor": "nurse-001",
    "notes": "Headache"
  }'
```

#### Example (JavaScript fetch)

```javascript
const response = await fetch('http://localhost:5000/api/waiting-room/check-in', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'X-Correlation-Id': crypto.randomUUID()
  },
  body: JSON.stringify({
    queueId: 'QUEUE-001',
    patientId: 'PAT-12345',
    patientName: 'John Doe',
    priority: 'High',
    consultationType: 'General',
    actor: 'nurse-001',
    notes: 'Headache'
  })
});

const result = await response.json();
console.log(result.correlationId); // For tracing
```

---

## 🔍 Query Endpoints

Queries return read models (projections). They are **eventually consistent** with commands (typical lag: <100ms).

### GET /api/v1/waiting-room/{queueId}/monitor

Get high-level KPI metrics for a queue. Used for dashboards and monitoring.

#### URL Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `queueId` | string | Queue identifier |

#### Success Response (200 OK)

```json
{
  "queueId": "QUEUE-001",
  "queueName": "Emergency Room",
  "timestamp": "2026-02-19T10:30:00Z",
  "totalPatients": 15,
  "patientsByPriority": {
    "high": 5,
    "medium": 7,
    "low": 3
  },
  "averageWaitTimeMinutes": 32.5,
  "utilizationPercentage": 75.0,
  "maxCapacity": 20,
  "status": "Active"
}
```

#### Error Response

**404 Not Found** — Monitor projection not found

```json
{
  "error": "Queue monitor not found for QUEUE-999",
  "statusCode": 404,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

#### Example (curl)

```bash
curl -X GET http://localhost:5000/api/v1/waiting-room/QUEUE-001/monitor \
  -H "Accept: application/json"
```

---

### GET /api/v1/waiting-room/{queueId}/queue-state

Get detailed queue state with patient list. Used for patient management UI.

#### URL Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `queueId` | string | Queue identifier |

#### Success Response (200 OK)

```json
{
  "queueId": "QUEUE-001",
  "queueName": "Emergency Room",
  "timestamp": "2026-02-19T10:30:00Z",
  "currentPatientCount": 3,
  "maxCapacity": 20,
  "utilizationPercentage": 15.0,
  "patients": [
    {
      "patientId": "PAT-001",
      "patientName": "Alice Smith",
      "priority": "High",
      "consultationType": "Emergency",
      "checkedInAt": "2026-02-19T10:00:00Z",
      "waitTimeMinutes": 30,
      "notes": "Chest pain"
    },
    {
      "patientId": "PAT-002",
      "patientName": "Bob Johnson",
      "priority": "Medium",
      "consultationType": "General",
      "checkedInAt": "2026-02-19T10:15:00Z",
      "waitTimeMinutes": 15,
      "notes": ""
    },
    {
      "patientId": "PAT-003",
      "patientName": "Charlie Brown",
      "priority": "Low",
      "consultationType": "Routine",
      "checkedInAt": "2026-02-19T10:25:00Z",
      "waitTimeMinutes": 5,
      "notes": "Annual checkup"
    }
  ],
  "status": "Active"
}
```

#### Response Fields

| Field | Type | Description |
|-------|------|-------------|
| `queueId` | string | Queue identifier |
| `queueName` | string | Human-readable queue name |
| `timestamp` | ISO 8601 | When projection was updated |
| `currentPatientCount` | int | Number of patients in queue |
| `maxCapacity` | int | Maximum queue capacity |
| `utilizationPercentage` | decimal | (current / max) * 100 |
| `patients` | array | List of patients (see below) |
| `status` | string | Queue status (`Active`, `Closed`, etc.) |

**Patient object:**

| Field | Type | Description |
|-------|------|-------------|
| `patientId` | string | Unique patient identifier |
| `patientName` | string | Patient full name |
| `priority` | string | Triage priority (`High`, `Medium`, `Low`) |
| `consultationType` | string | Type of consultation |
| `checkedInAt` | ISO 8601 | Check-in timestamp |
| `waitTimeMinutes` | int | Minutes since check-in |
| `notes` | string | Optional notes |

#### Error Response

**404 Not Found** — Queue state not found

```json
{
  "error": "Queue state not found for QUEUE-999",
  "statusCode": 404,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

#### Example (curl)

```bash
curl -X GET http://localhost:5000/api/v1/waiting-room/QUEUE-001/queue-state \
  -H "Accept: application/json"
```

---

### POST /api/v1/waiting-room/{queueId}/rebuild

Rebuild projection from event store. Asynchronous operation (returns 202 Accepted).

**Use cases:**

- Recovery from projection corruption
- Schema migration
- Verification (rebuild and compare)

#### URL Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `queueId` | string | Queue identifier |

#### Success Response (202 Accepted)

```json
{
  "message": "Projection rebuild initiated",
  "queueId": "QUEUE-001"
}
```

**Headers:**

```http
Location: /api/v1/waiting-room/QUEUE-001/monitor
```

#### Error Response

**500 Internal Server Error** — Rebuild failed

```json
{
  "error": "Failed to initiate projection rebuild",
  "statusCode": 500,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

#### Example (curl)

```bash
curl -X POST http://localhost:5000/api/v1/waiting-room/QUEUE-001/rebuild \
  -H "Content-Type: application/json"
```

---

## ❤️ Health & Status

### GET /health

Check API health status.

#### Success Response (200 OK)

```json
{
  "status": "Healthy"
}
```

#### Example (curl)

```bash
curl -X GET http://localhost:5000/health
```

---

## 🎯 Examples (curl)

### Complete Workflow Example

```bash
# 1. Check API health
curl http://localhost:5000/health

# 2. Create queue (assuming queue creation endpoint exists)
# ...

# 3. Check in patient
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" \
  -d '{
    "queueId": "ER-001",
    "patientId": "P001",
    "patientName": "Alice",
    "priority": "High",
    "consultationType": "Emergency",
    "actor": "nurse-001"
  }'

# Wait 100ms for projection update (eventual consistency)
sleep 0.1

# 4. Get queue monitor (KPIs)
curl http://localhost:5000/api/v1/waiting-room/ER-001/monitor

# 5. Get detailed queue state
curl http://localhost:5000/api/v1/waiting-room/ER-001/queue-state

# 6. Rebuild projection (if needed)
curl -X POST http://localhost:5000/api/v1/waiting-room/ER-001/rebuild
```

---

## 🔧 Testing with Postman

### Import Collection

1. Create new collection: "RLAPP WaitingRoom API"
2. Set base URL variable: `{{baseUrl}}` = `http://localhost:5000`
3. Add requests:
   - `POST {{baseUrl}}/api/waiting-room/check-in`
   - `GET {{baseUrl}}/api/v1/waiting-room/:queueId/monitor`
   - `GET {{baseUrl}}/api/v1/waiting-room/:queueId/queue-state`
   - `POST {{baseUrl}}/api/v1/waiting-room/:queueId/rebuild`

### Pre-request Script (Auto Correlation ID)

```javascript
pm.collectionVariables.set("correlationId", pm.variables.replaceIn('{{$guid}}'));
```

### Headers

```
Content-Type: application/json
X-Correlation-Id: {{correlationId}}
```

---

## 🌐 Deployment Considerations

### Base URLs by Environment

| Environment | Base URL | Notes |
|-------------|----------|-------|
| **Local** | `http://localhost:5000` | Docker Compose |
| **Dev** | `https://dev-api.rlapp.com` | Azure App Service |
| **Staging** | `https://staging-api.rlapp.com` | Pre-production |
| **Production** | `https://api.rlapp.com` | Production (HTTPS required) |

### CORS

**Allowed Origins (Development):**

```
http://localhost:3000
http://localhost:4200
```

**Production:** Whitelist specific origins only.

---

## 📊 Rate Limiting

**Current Status:** Not implemented

**Planned:**

- 100 requests/minute per IP (commands)
- 1000 requests/minute per IP (queries)
- 429 Too Many Requests response

---

## 🔒 Security Considerations

### Current (Development)

- ❌ No authentication
- ❌ No authorization
- ❌ No rate limiting

### Production Requirements

- ✅ OAuth 2.0 / JWT
- ✅ Role-based access control (RBAC)
- ✅ HTTPS only
- ✅ API key validation
- ✅ Input sanitization
- ✅ SQL injection prevention (Dapper parameterized queries)
- ✅ Rate limiting

---

## 📚 Related Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — System architecture
- [ADR-005: CQRS](.ai/ADR-005-CQRS.md) — Why commands and queries are separated
- [APPLICATION_FLOW.md](APPLICATION_FLOW.md) — Request processing flow
- [DEVELOPER_ONBOARDING.md](DEVELOPER_ONBOARDING.md) — Getting started guide

---

## 🐛 Troubleshooting

### Problem: 404 on all endpoints

**Solution:**

```bash
# Verify API is running
curl http://localhost:5000/health

# Check logs
cd src/Services/WaitingRoom/WaitingRoom.API
dotnet run --verbosity detailed
```

### Problem: 422 "Queue is full"

**Solution:**

- Check queue capacity: `GET /api/v1/waiting-room/{queueId}/queue-state`
- Verify `currentPatientCount < maxCapacity`
- If full, patients must be discharged first

### Problem: Query returns stale data

**Reason:** Eventual consistency (CQRS pattern)

**Solution:**

- Wait 100-200ms after command
- Check projection lag: Grafana dashboard
- If lag > 1s, investigate Outbox Worker

---

## 📞 Support

**Questions?** Contact Architecture Team or open an issue.

**Last updated:** 2026-02-19
**API Version:** 1.0
