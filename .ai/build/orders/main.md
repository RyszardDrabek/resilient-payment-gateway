# Implementation Order

## Sequence

| #   | Feature                                                         | Topic | Phase | Depends On      |
| --- | --------------------------------------------------------------- | ----- | ----- | --------------- |
| 1   | [F-PAY-01](../../specs/features/F-PAY-01/spec.md): Payment authorization | PAY   | 1     | —               |
| 2   | [F-PAY-03](../../specs/features/F-PAY-03/spec.md): Idempotent payment commands | PAY   | 1     | #1              |
| 3   | [F-ADYEN-01](../../specs/features/F-ADYEN-01/spec.md): Adyen WireMock simulation mode | ADYEN | 1     | #1, #2          |
| 4   | [F-EDGE-01](../../specs/features/F-EDGE-01/spec.md): Payment API problem details | EDGE  | 1     | #1, #2          |
| 5   | [F-EDGE-02](../../specs/features/F-EDGE-02/spec.md): Gateway rate limiting and overload protection | EDGE  | 1     | #4              |
| 6   | [F-EVT-01](../../specs/features/F-EVT-01/spec.md): Payment lifecycle event publication | EVT   | 1     | #1, #2, #3      |
| 7   | [F-PAY-02](../../specs/features/F-PAY-02/spec.md): Payment capture, refund, and cancellation | PAY   | 1     | #1, #2, #3      |
| 8   | [F-EDGE-03](../../specs/features/F-EDGE-03/spec.md): Payment-instrument masking in telemetry and logs | EDGE  | 1     | #1, #3          |
| 9   | [F-ADYEN-02](../../specs/features/F-ADYEN-02/spec.md): Live sandbox mode and credential-driven selection | ADYEN | 2     | #3              |
| 10  | [F-ADYEN-03](../../specs/features/F-ADYEN-03/spec.md): Adyen HMAC webhook verification and ingestion | ADYEN | 2     | #9, #1          |
| 11  | [F-ADYEN-04](../../specs/features/F-ADYEN-04/spec.md): Asynchronous payment status reconciliation | ADYEN | 2     | #10, #1, #7, #6 |
| 12  | [F-ADYEN-05](../../specs/features/F-ADYEN-05/spec.md): Provider failure policy for payment authorization | ADYEN | 2     | #9, #2, #11     |
| 13  | [F-WEB3-01](../../specs/features/F-WEB3-01/spec.md): Web3 stablecoin settlement channel | WEB3  | 3     | #1, #2, #7, #6  |
| 14  | [F-WEB3-02](../../specs/features/F-WEB3-02/spec.md): On-chain confirmation and finality | WEB3  | 3     | #13, #1, #7, #6 |
| 15  | [F-RISK-01](../../specs/features/F-RISK-01/spec.md): Off-path risk scoring and flagging | RISK  | 4     | #6, #4, #5      |
| 16  | [F-RISK-02](../../specs/features/F-RISK-02/spec.md): Risk flag review and disposition | RISK  | 4     | #15             |
| 17  | [F-PAY-04](../../specs/features/F-PAY-04/spec.md): Partial capture support | PAY   | 5     | #1, #2, #7      |
| 18  | [F-PAY-05](../../specs/features/F-PAY-05/spec.md): Partial refund support | PAY   | 5     | #17, #7, #2     |
| 19  | [F-EVT-02](../../specs/features/F-EVT-02/spec.md): Transactional outbox retention & pruning | EVT   | 5     | #6              |
| 20  | [F-EDGE-04](../../specs/features/F-EDGE-04/spec.md): Unresolved payments operator query API | EDGE  | 5     | #1, #4, #7      |
| 21  | [F-EDGE-05](../../specs/features/F-EDGE-05/spec.md): Merchant outbound webhook notification dispatch | EDGE  | 5     | #6, #1, #7, #17, #18 |
| 22  | [F-PORTAL-01](../../specs/features/F-PORTAL-01/spec.md): Operator dashboard & unresolved payments console | PORTAL | 6     | #20, #4         |
| 23  | [F-PORTAL-02](../../specs/features/F-PORTAL-02/spec.md): Merchant webhook self-service & event simulator | PORTAL | 6     | #21, #22        |
| 24  | [F-PORTAL-03](../../specs/features/F-PORTAL-03/spec.md): Compliance risk flags review & disposition console | PORTAL | 6     | #15, #16, #22   |
