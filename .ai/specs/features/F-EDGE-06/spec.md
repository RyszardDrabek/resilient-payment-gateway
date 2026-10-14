---
id: F-EDGE-06
topic: EDGE
phase: merchant-api-keys-and-webhook-dlq
status: done
---

# F-EDGE-06: Merchant API Key authentication & scoped permissions

- **Topic:** [EDGE](../../topics/edge.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** Phase 7 intake: merchant-api-keys-and-webhook-dlq

## Description

Enables merchants to authenticate API requests using scoped API keys (`X-Api-Key` header) mapped to merchant identity and fine-grained permission scopes (`payments:write`, `payments:read`), with salted SHA-256 hash storage and key revocation lifecycle.

## Business context

Allows merchant servers and backend automations to interact securely with the gateway API without relying on human JWT tokens, enforcing principle of least privilege through scoped permissions.

## Acceptance criteria

- AC-1 THE system SHALL authenticate incoming HTTP requests bearing an `X-Api-Key` header by comparing the hashed token against active merchant API keys in the database.
- AC-2 WHEN a merchant creates an API key with specified scopes (`payments:write`, `payments:read`), THE system SHALL generate a secure key with a distinct prefix (`rpg_live_` or `rpg_test_`), return the raw key exactly once, and persist only the salted SHA-256 hash alongside metadata (prefix, name, merchantId, scopes, createdAt, isRevoked).
- AC-3 IF an API key is revoked or does not possess the required scope for the requested endpoint, THEN THE system SHALL reject the request with a 401 Unauthorized or 403 Forbidden RFC 7807 problem document.
- AC-4 THE system SHALL provide management endpoints (`GET /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys/{keyId}/revoke`) restricted to authorized operators or matching merchant principals.

## Interface contract

- **Resource / surface:** `POST /api/v1/merchants/{merchantId}/api-keys`, `GET /api/v1/merchants/{merchantId}/api-keys`, `POST /api/v1/merchants/{merchantId}/api-keys/{keyId}/revoke`, `ApiKeyAuthenticationHandler`
- **Operation:** REST commands & queries, ASP.NET Core Authentication scheme
- **Response shape category:** resource | problem
- **Terminal status classes:** 200 OK | 201 Created | 400 Bad Request | 401 Unauthorized | 403 Forbidden | 404 Not Found
- **Load-bearing fields:** `id`, `name`, `prefix`, `scopes`, `createdAt`, `isRevoked`, `rawKey` (creation only)

## Out of scope

- Single Sign-On (SSO) / OAuth2 authorization code flows.
- IP allowlisting per API key (future enhancement).

## Dependencies

F-PAY-01, F-EDGE-01
