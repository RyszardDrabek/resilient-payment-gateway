---
id: F-PORTAL-07
topic: PORTAL
phase: three-ds-secure-and-sca-flow
status: done
---

# F-PORTAL-07: Merchant portal 3DS2 challenge simulator & action inspector

- **Topic:** [PORTAL](../../topics/portal.md)
- **Type:** Explicit
- **Value class:** USER-VISIBLE
- **Source:** ADR-019, ADR-013

## Description

Adds a 3DS2 Challenge Simulator console to the Merchant & Operator Web Portal, enabling developers and operations staff to simulate 3DS challenge triggers, inspect the `action` payload, and complete or fail simulated challenge verifications interactively.

## Business context

Empowers merchants during integration and QA to test their 3DS2 redirect/challenge handling against the sandbox gateway without needing to configure physical mobile devices or real banking apps.

## Acceptance criteria

- AC-1 THE Web Portal SHALL provide a 3DS2 Simulator view accessible from the operations console.
- AC-2 WHEN an operator/merchant initiates a test payment configured for 3DS2 Challenge, THE portal SHALL display the resulting `RequiresAction` state, the redirect URL, and the challenge parameters.
- AC-3 THE portal SHALL provide interactive controls to simulate either "Complete Challenge (Approve)" or "Fail Challenge (Reject)" by calling `POST /payments/{id}/3ds-result`.
- AC-4 UPON challenge resolution, THE portal SHALL refresh and display the terminal state (`Authorized` or `Declined`) along with live lifecycle event diagnostics.

## Interface contract

- **Resource / surface:** React Web Portal view (`ThreeDsSimulator.tsx` / console integration)
- **Operation:** interactive payment simulation and challenge submission
- **Response shape category:** interactive UI console

## Dependencies

F-PORTAL-01, F-EDGE-10
