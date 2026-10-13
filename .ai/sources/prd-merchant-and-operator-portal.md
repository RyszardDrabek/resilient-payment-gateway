# Product Requirements Document (PRD) — Phase 6

## Projekt: Resilient Payment Gateway — Merchant & Operator Portal

### 1. Cel Biznesowy i Koncepcja
Zbudowanie zintegrowanego, responsywnego portalu internetowego (Single Page Application / Web Dashboard) dedykowanego dla dwóch kluczowych grup użytkowników:
1. **Operatorów bramki i oficerów ryzyka (Ops / Compliance):**
   - Podgląd stanu systemu i monitorowanie transakcji w czasie rzeczywistym.
   - Prowadzenie dochodzeń i zarządzanie nierozstrzygniętymi płatnościami (`Pending` / `Unknown`) na podstawie danych z `GET /api/v1/payments/unresolved`.
   - Przeglądanie i dyspozycja oflagowanych zdarzeń ryzyka AML/Fraud (`RISK` flags review & disposition).
2. **Merchantów (Merchant Self-Service):**
   - Rejestracja, podgląd i edycja własnych adresów webhookowych (`MerchantWebhookSubscriptions`).
   - Rotacja klucza podpisu HMAC-SHA256 (`SecretKey`) oraz włączanie/wyłączanie subskrypcji.
   - Testowanie dostarczania powiadomień za pomocą symulatora zdarzeń webhook (test dispatch).

### 2. Główne Wymagania Funkcjonalne

#### Moduł A: Operator Dashboard & Unresolved Payments
- Ekran KPI: wolumen transakcji, podział na kanały (Adyen / Web3), wskaźnik sukcesu.
- Tabela nierozstrzygniętych płatności z filtrowaniem po kanale rozliczenia i minimalnym wieku transakcji (`ageMinutes`).
- Widok szczegółów płatności z informacją o kanale, referencji zewnętrznej i czasie oczekiwania.

#### Moduł B: Merchant Webhook Management & Diagnostics
- Tabela zarejestrowanych subskrypcji webhookowych dla danego identyfikatora merchanta (`PartyId`).
- Formularz dodawania nowego endpointu URL i generowania bezpiecznego klucza HMAC.
- Narzędzie diagnostyczne: wysyłka testowego zdarzenia (ping/simulation) z weryfikacją odpowiedzi HTTP status i nagłówków `X-Signature-SHA256`.

#### Moduł C: Risk & Compliance Console
- Tabela alertów ryzyka (anomalie wykryte przez moduł pgvector / AI Risk Scoring).
- Podgląd szczegółów oceny ryzyka: wynik punktowy (score), powód oflagowania, wektor cech transakcji.
- Akcje dyspozycji: zatwierdzenie (`Approved`), eskalacja (`Escalated`), odrzucenie (`Rejected`).

### 3. Wymagania Techniczne i UI/UX
- Czysta, nowoczesna estetyka (ciemny motyw / dark mode, estetyczna typografia, responsywność).
- Integracja z API Gateway poprzez uwierzytelnianie JWT Bearer (role `merchant`, `ops`, `risk`).
- Spójność z istniejącym ekosystemem backendowym .NET 10.
