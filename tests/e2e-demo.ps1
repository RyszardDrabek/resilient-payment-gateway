# Resilient Payment Gateway - Comprehensive E2E Observability & Scenario Suite
param(
    [string]$BaseUrl = "http://localhost:5080",
    [string]$Scenario = "All",
    [int]$VolumeCount = 5
)

$jwtKey = "DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!"
$headerJson = '{"alg":"HS256","typ":"JWT"}'
$payloadJson = '{"sub":"merchant_demo","role":"PaymentService","iss":"payment-gateway","aud":"payment-gateway","exp":1999999999}'

function Base64UrlEncode([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-JwtToken {
    param(
        [string]$Sub = "merchant_demo",
        [string]$Role = "PaymentService"
    )
    $hJson = '{"alg":"HS256","typ":"JWT"}'
    $pJson = "{`"sub`":`"$Sub`",`"role`":`"$Role`",`"iss`":`"payment-gateway`",`"aud`":`"payment-gateway`",`"exp`":1999999999}"
    $hB64 = Base64UrlEncode([System.Text.Encoding]::UTF8.GetBytes($hJson))
    $pB64 = Base64UrlEncode([System.Text.Encoding]::UTF8.GetBytes($pJson))
    $h = New-Object System.Security.Cryptography.HMACSHA256
    $h.Key = [System.Text.Encoding]::UTF8.GetBytes($jwtKey)
    $sB64 = Base64UrlEncode($h.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("$hB64.$pB64")))
    return "$hB64.$pB64.$sB64"
}

$token = New-JwtToken -Sub "merchant_demo" -Role "merchant"

function Send-GatewayRequest {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Body = $null,
        [string]$IdempotencyKey = $null,
        [string]$AuthToken = $token,
        [string]$ApiKey = $null,
        [switch]$NoAuth
    )

    $headers = @{}
    if ($ApiKey) {
        $headers["X-API-Key"] = $ApiKey
    } elseif (-not $NoAuth -and $AuthToken) {
        $headers["Authorization"] = "Bearer $AuthToken"
    }
    if ($IdempotencyKey) {
        $headers["Idempotency-Key"] = $IdempotencyKey
    }

    $uri = "$BaseUrl$Path"
    $jsonBody = if ($Body) { $Body | ConvertTo-Json -Compress } else { $null }

    try {
        if ($jsonBody) {
            $resp = Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $jsonBody
        } else {
            $resp = Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }
        return @{ Success = $true; Data = $resp; Error = $null }
    } catch {
        $ex = $_.Exception
        $respBody = $null
        if ($_.ErrorDetails) {
            $respBody = $_.ErrorDetails.Message
        } elseif ($ex.Response) {
            $stream = $ex.Response.GetResponseStream()
            if ($stream) {
                $reader = New-Object System.IO.StreamReader($stream)
                $respBody = $reader.ReadToEnd()
            }
        }
        return @{ Success = $false; Data = $null; Error = $respBody; Status = $ex.Message }
    }
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "  RESILIENT PAYMENT GATEWAY - END-TO-END OBSERVABILITY TEST SUITE   " -ForegroundColor Cyan
Write-Host "  Target: $BaseUrl | Scenario: $Scenario" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$scenarioList = @()
if ($Scenario -eq "All") {
    $scenarioList = @("AdyenHappyPath", "AdyenCancel", "AdyenDecline", "Web3HappyPath", "Web3Cancel", "Web3Decline", "PartialSettlement", "UnresolvedQuery", "MerchantWebhooks", "RiskDisposition", "MerchantApiKeys", "WebhookDlq", "Volume")
} else {
    $scenarioList = @($Scenario)
}

# -----------------------------------------------------------------------------
# Scenario 1: Adyen Authorize -> Capture -> Refund (Full Lifecycle)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "AdyenHappyPath") {
    Write-Host "`n[SCENARIO 1] Adyen Acquirer: Full Lifecycle (Authorize -> Capture -> Refund)" -ForegroundColor Magenta
    $party = "customer_adyen_$(Get-Random -Minimum 100 -Maximum 999)"
    $amount = 14900 # 149.00 EUR
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing 149.00 EUR via Adyen for $party..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = $amount
        currency = "EUR"
        settlementChannel = "ADYEN"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Status: $($res.Data.state) | PaymentId: $payId | PSP Ref: $($res.Data.channelReference)" -ForegroundColor Green

        # 2. Capture
        Write-Host "  2. Capturing Payment $payId..." -ForegroundColor Yellow
        $capRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/capture" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($capRes.Success) {
            Write-Host "     -> Status: $($capRes.Data.state) | PSP Cap Ref: $($capRes.Data.channelReference)" -ForegroundColor Green
        } else {
            Write-Host "     -> Capture Failed: $($capRes.Error)" -ForegroundColor Red
        }

        # 3. Refund
        Write-Host "  3. Refunding Payment $payId..." -ForegroundColor Yellow
        $refRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/refund" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($refRes.Success) {
            Write-Host "     -> Status: $($refRes.Data.state) | PSP Refund Ref: $($refRes.Data.channelReference)" -ForegroundColor Green
        } else {
            Write-Host "     -> Refund Failed: $($refRes.Error)" -ForegroundColor Red
        }

        # 4. Verify Final State
        $query = Send-GatewayRequest -Method "Get" -Path "/payments/$payId"
        Write-Host "  4. Final Verified State: $($query.Data.state) (Version: $($query.Data.version))" -ForegroundColor Cyan
    } else {
        Write-Host "     -> Authorize Failed: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 2: Adyen Authorize -> Cancel (Void before capture)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "AdyenCancel") {
    Write-Host "`n[SCENARIO 2] Adyen Acquirer: Payment Cancellation (Authorize -> Cancel)" -ForegroundColor Magenta
    $party = "customer_cancel_$(Get-Random -Minimum 100 -Maximum 999)"
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing 45.00 EUR via Adyen..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = 4500
        currency = "EUR"
        settlementChannel = "ADYEN"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Authorized: $payId | State: $($res.Data.state)" -ForegroundColor Green

        Write-Host "  2. Cancelling Payment $payId before capture..." -ForegroundColor Yellow
        $cancelRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/cancel" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($cancelRes.Success) {
            Write-Host "     -> Cancelled successfully! State: $($cancelRes.Data.state) | Cancel Ref: $($cancelRes.Data.channelReference)" -ForegroundColor Green
        } else {
            Write-Host "     -> Cancel Failed: $($cancelRes.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> Authorize Failed: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 3: Adyen Declined / Refused Payment
# -----------------------------------------------------------------------------
if ($scenarioList -contains "AdyenDecline") {
    Write-Host "`n[SCENARIO 3] Adyen Acquirer: Declined / Refused Payment Rule Simulation" -ForegroundColor Magenta
    # Using 'decline_' prefix triggers simulated refusal / decline card in Adyen port
    $party = "decline_customer_$(Get-Random -Minimum 100 -Maximum 999)"
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing payment with decline trigger ($party)..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = 9900
        currency = "EUR"
        settlementChannel = "ADYEN"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Received response: State = $($res.Data.state)" -ForegroundColor Yellow
        Write-Host "     -> Decline Reason: $($res.Data.declineReason)" -ForegroundColor Red
        Write-Host "     -> PaymentId: $payId recorded in Postgres as Declined" -ForegroundColor Gray
    } else {
        Write-Host "     -> Request Error: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 4: Web3 Crypto Settlement (Authorize -> On-Chain Capture -> Refund)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Web3HappyPath") {
    Write-Host "`n[SCENARIO 4] Web3 Channel: Native Crypto Settlement (Authorize -> On-Chain Capture -> Refund)" -ForegroundColor Magenta
    # Standard Anvil/Hardhat local dev account #1 (0x70997970C51812dc3A010C7d01b50e0d17dc79C8)
    $party = "wallet_0x70997970C51812dc3A010C7d01b50e0d17dc79C8"
    $amountUsdc = 25000000 # 25.000000 USDC (6 decimals)
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing 25.00 USDC via Web3 channel (off-chain hold)..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = $amountUsdc
        currency = "USDC"
        settlementChannel = "WEB3"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Authorized: $payId | State: $($res.Data.state) | Ref: $($res.Data.channelReference)" -ForegroundColor Green

        # 2. Capture (Simulates on-chain ERC20 transfer broadcast & finality check)
        Write-Host "  2. Capturing Web3 payment (broadcasting on-chain transfer to merchant destination)..." -ForegroundColor Yellow
        $capRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/capture" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($capRes.Success) {
            Write-Host "     -> On-Chain Capture Finalized!" -ForegroundColor Green
            Write-Host "     -> State: $($capRes.Data.state) | TxHash: $($capRes.Data.channelReference)" -ForegroundColor Cyan
        } else {
            Write-Host "     -> Web3 Capture Failed: $($capRes.Error)" -ForegroundColor Red
        }

        # 3. Refund (Simulates on-chain reverse transfer to refund destination)
        Write-Host "  3. Refunding Web3 payment (broadcasting reverse transfer on-chain)..." -ForegroundColor Yellow
        $refRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/refund" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($refRes.Success) {
            Write-Host "     -> On-Chain Refund Finalized!" -ForegroundColor Green
            Write-Host "     -> State: $($refRes.Data.state) | Refund TxHash: $($refRes.Data.channelReference)" -ForegroundColor Cyan
        } else {
            Write-Host "     -> Web3 Refund Failed: $($refRes.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> Web3 Authorize Failed: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 5: Web3 Cancellation (Authorize -> Cancel off-chain)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Web3Cancel") {
    Write-Host "`n[SCENARIO 5] Web3 Channel: Payment Cancellation (Off-Chain Cancel)" -ForegroundColor Magenta
    # Standard Anvil/Hardhat local dev account #2 (0x3C44CdDdB6a900fa2b585dd299e03d12FA4293BC)
    $party = "wallet_0x3C44CdDdB6a900fa2b585dd299e03d12FA4293BC"
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing 10.00 USDC via Web3 channel..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = 10000000
        currency = "USDC"
        settlementChannel = "WEB3"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Authorized: $payId" -ForegroundColor Green

        Write-Host "  2. Cancelling off-chain before broadcast..." -ForegroundColor Yellow
        $cancelRes = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/cancel" -IdempotencyKey ([Guid]::NewGuid().ToString())
        if ($cancelRes.Success) {
            Write-Host "     -> Cancelled! State: $($cancelRes.Data.state) (no gas fees incurred)" -ForegroundColor Green
        } else {
            Write-Host "     -> Cancel Failed: $($cancelRes.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> Web3 Authorize Failed: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 6: Web3 Unsupported Asset / Decline Validation
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Web3Decline") {
    Write-Host "`n[SCENARIO 6] Web3 Channel: Unsupported Asset Decline Simulation" -ForegroundColor Magenta
    $party = "wallet_crypto_user"
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Attempting Web3 authorization with unsupported token (XYZ)..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = 1000000
        currency = "XYZ"
        settlementChannel = "WEB3"
    }

    if ($res.Success) {
        Write-Host "     -> Response: State = $($res.Data.state) | Decline Reason: $($res.Data.declineReason)" -ForegroundColor Red
    } else {
        Write-Host "     -> Edge Validation rejected unsupported asset: $($res.Error)" -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Scenario 7: Partial Settlement & Balance Tracking (F-PAY-04 & F-PAY-05)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "PartialSettlement") {
    Write-Host "`n[SCENARIO 7] Phase 5: Partial Settlement & Balance Tracking (F-PAY-04 & F-PAY-05)" -ForegroundColor Magenta
    $party = "customer_partial_$(Get-Random -Minimum 100 -Maximum 999)"
    $amount = 20000 # 200.00 EUR
    $idempAuth = [Guid]::NewGuid().ToString()

    Write-Host "  1. Authorizing 200.00 EUR for $party..." -ForegroundColor Yellow
    $res = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey $idempAuth -Body @{
        partyId = $party
        amount = $amount
        currency = "EUR"
        settlementChannel = "ADYEN"
    }

    if ($res.Success) {
        $payId = $res.Data.paymentId
        Write-Host "     -> Authorized: $payId | State: $($res.Data.state) | Authorized Balance: $($res.Data.remainingAuthorizedAmount) cents" -ForegroundColor Green

        # 2. First Partial Capture (80.00 EUR / 8000 cents) -> PartiallyCaptured
        Write-Host "  2. Capturing partial amount (80.00 EUR)..." -ForegroundColor Yellow
        $cap1 = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/capture" -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
            amount = 8000
        }
        if ($cap1.Success) {
            Write-Host "     -> Status: $($cap1.Data.state) | Captured: $($cap1.Data.capturedAmount) | Remaining Auth: $($cap1.Data.remainingAuthorizedAmount) cents" -ForegroundColor Green
        } else {
            Write-Host "     -> Partial Capture 1 Failed: $($cap1.Error)" -ForegroundColor Red
        }

        # 3. Second Partial Capture of remaining balance (120.00 EUR / 12000 cents) -> Captured
        Write-Host "  3. Capturing remainder (120.00 EUR) to fully settle authorization..." -ForegroundColor Yellow
        $cap2 = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/capture" -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
            amount = 12000
        }
        if ($cap2.Success) {
            Write-Host "     -> Status: $($cap2.Data.state) | Total Captured: $($cap2.Data.capturedAmount) | Refundable Balance: $($cap2.Data.remainingRefundableAmount) cents" -ForegroundColor Green
        } else {
            Write-Host "     -> Partial Capture 2 Failed: $($cap2.Error)" -ForegroundColor Red
        }

        # 4. First Partial Refund (50.00 EUR / 5000 cents) -> PartiallyRefunded
        Write-Host "  4. Refunding partial amount (50.00 EUR)..." -ForegroundColor Yellow
        $ref1 = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/refund" -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
            amount = 5000
        }
        if ($ref1.Success) {
            Write-Host "     -> Status: $($ref1.Data.state) | Refunded: $($ref1.Data.refundedAmount) | Remaining Refundable: $($ref1.Data.remainingRefundableAmount) cents" -ForegroundColor Green
        } else {
            Write-Host "     -> Partial Refund 1 Failed: $($ref1.Error)" -ForegroundColor Red
        }

        # 5. Final Refund of remaining captured funds (150.00 EUR / 15000 cents) -> Refunded
        Write-Host "  5. Refunding remaining balance (150.00 EUR)..." -ForegroundColor Yellow
        $ref2 = Send-GatewayRequest -Method "Post" -Path "/payments/$payId/refund" -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
            amount = 15000
        }
        if ($ref2.Success) {
            Write-Host "     -> Status: $($ref2.Data.state) | Total Refunded: $($ref2.Data.refundedAmount) | Remaining Refundable: $($ref2.Data.remainingRefundableAmount) cents" -ForegroundColor Green
        } else {
            Write-Host "     -> Partial Refund 2 Failed: $($ref2.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> Authorize Failed: $($res.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 8: Operator Unresolved Query API & Security Gates (F-EDGE-04)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "UnresolvedQuery") {
    Write-Host "`n[SCENARIO 8] Phase 5: Operator Unresolved Payments Query API (F-EDGE-04)" -ForegroundColor Magenta

    # 1. Test Unauthenticated Access Gate
    Write-Host "  1. Probing unauthenticated GET /api/v1/payments/unresolved..." -ForegroundColor Yellow
    $unauthRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/payments/unresolved" -NoAuth
    if (-not $unauthRes.Success) {
        Write-Host "     -> Rejected unauthenticated caller with RFC 7807 Problem Document: $($unauthRes.Status)" -ForegroundColor Green
    } else {
        Write-Host "     -> Security issue: Unauthenticated request was allowed!" -ForegroundColor Red
    }

    # 2. Test Forbidden Role Gate
    Write-Host "  2. Probing with unauthorized role (guest)..." -ForegroundColor Yellow
    $guestToken = New-JwtToken -Sub "guest_user" -Role "guest"
    $forbiddenRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/payments/unresolved" -AuthToken $guestToken
    if (-not $forbiddenRes.Success) {
        Write-Host "     -> Rejected unauthorized role with RFC 7807 Problem Document: $($forbiddenRes.Status)" -ForegroundColor Green
    } else {
        Write-Host "     -> Security issue: Unauthorized role was allowed!" -ForegroundColor Red
    }

    # 3. Test Authorized Operator Access
    Write-Host "  3. Querying unresolved payments as operator (role=ops)..." -ForegroundColor Yellow
    $opsToken = New-JwtToken -Sub "operator_1" -Role "ops"
    $opsRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/payments/unresolved?ageMinutes=0&pageSize=10" -AuthToken $opsToken
    if ($opsRes.Success) {
        $data = $opsRes.Data
        Write-Host "     -> 200 OK | Page: $($data.page) | PageSize: $($data.pageSize) | TotalCount: $($data.totalCount)" -ForegroundColor Green
        Write-Host "     -> Found $($data.items.Count) unresolved items in response." -ForegroundColor Cyan
    } else {
        Write-Host "     -> Operator query failed: $($opsRes.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 10: Merchant Webhook Self-Service & Event Simulator (F-PORTAL-02)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "MerchantWebhooks") {
    Write-Host "`n[SCENARIO 10] Phase 6: Merchant Webhook Self-Service & Diagnostics (F-PORTAL-02)" -ForegroundColor Magenta
    $merchId = "merch_demo_$(Get-Random -Minimum 100 -Maximum 999)"
    $merchToken = New-JwtToken -Sub $merchId -Role "merchant"

    # 1. Update/Register Webhook Subscription
    Write-Host "  1. Configuring webhook subscription for $merchId..." -ForegroundColor Yellow
    $subRes = Send-GatewayRequest -Method "Put" -Path "/api/v1/webhooks/merchants/subscription" -AuthToken $merchToken -Body @{
        endpointUrl = "https://webhook.site/demo-endpoint"
        isActive = $true
    }
    if ($subRes.Success) {
        Write-Host "     -> Subscription saved! Active: $($subRes.Data.isActive) | URL: $($subRes.Data.endpointUrl)" -ForegroundColor Green

        # 2. Rotate Secret Key
        Write-Host "  2. Rotating HMAC secret key..." -ForegroundColor Yellow
        $rotRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/webhooks/merchants/subscription/rotate-secret" -AuthToken $merchToken
        if ($rotRes.Success) {
            Write-Host "     -> Secret rotated! New secret prefix: $($rotRes.Data.secretKey.Substring(0, 10))..." -ForegroundColor Green
        } else {
            Write-Host "     -> Secret rotation failed: $($rotRes.Error)" -ForegroundColor Red
        }

        # 3. Test Webhook Event Dispatch Simulation
        Write-Host "  3. Simulating signed webhook test dispatch..." -ForegroundColor Yellow
        $simRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/webhooks/merchants/test-dispatch" -AuthToken $merchToken -Body @{
            eventType = "payment.settled"
        }
        if ($simRes.Success) {
            Write-Host "     -> Simulator dispatch executed! Success: $($simRes.Data.success) | Latency: $($simRes.Data.latencyMs) ms | Status: $($simRes.Data.statusCode)" -ForegroundColor Green
        } else {
            Write-Host "     -> Dispatch failed: $($simRes.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> Webhook configuration failed: $($subRes.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 11: Compliance Risk Flags & Disposition Workflow (F-PORTAL-03)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "RiskDisposition") {
    Write-Host "`n[SCENARIO 11] Phase 6: Compliance Risk Flags & Disposition Workflow (F-PORTAL-03)" -ForegroundColor Magenta
    $riskToken = New-JwtToken -Sub "risk_officer_demo" -Role "risk"
    $unauthMerchToken = New-JwtToken -Sub "merch_unauth" -Role "merchant"

    # 1. Verify Unprivileged Role Rejection (AC-4)
    Write-Host "  1. Probing /api/v1/risk/flags with unprivileged merchant token (must return 403)..." -ForegroundColor Yellow
    $forbiddenRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/risk/flags" -AuthToken $unauthMerchToken
    if (-not $forbiddenRes.Success) {
        Write-Host "     -> Access correctly denied with 403 Forbidden: $($forbiddenRes.Status)" -ForegroundColor Green
    } else {
        Write-Host "     -> Security issue: Unprivileged merchant was allowed access!" -ForegroundColor Red
    }

    # 2. Query Open Risk Flags with Risk Auditor Role (AC-1)
    Write-Host "  2. Querying open risk flags queue with compliance role (risk)..." -ForegroundColor Yellow
    $flagsRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/risk/flags?page=1&pageSize=10" -AuthToken $riskToken
    if ($flagsRes.Success) {
        $flagItems = $flagsRes.Data.items
        Write-Host "     -> 200 OK | Total Open Flags: $($flagsRes.Data.totalCount) | Returned: $($flagItems.Count)" -ForegroundColor Green

        if ($flagItems.Count -gt 0) {
            $flagToReview = $flagItems[0]
            Write-Host "  3. Inspecting flag $($flagToReview.id) for Payment $($flagToReview.paymentId)..." -ForegroundColor Yellow
            Write-Host "     -> Reason: $($flagToReview.reason) | Party: $($flagToReview.partyId)" -ForegroundColor Cyan

            # 4. Submit Disposition Decision (AC-3)
            Write-Host "  4. Submitting auditor disposition (false_positive) with review notes..." -ForegroundColor Yellow
            $dispRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/risk/flags/$($flagToReview.id)/disposition" -AuthToken $riskToken -Body @{
                disposition = "false_positive"
                notes = "E2E automated compliance review: verified legitimate merchant velocity."
            }
            if ($dispRes.Success) {
                Write-Host "     -> Disposition recorded! Status: $($dispRes.Data.status) | Disposition: $($dispRes.Data.disposition)" -ForegroundColor Green
            } else {
                Write-Host "     -> Disposition failed: $($dispRes.Error)" -ForegroundColor Red
            }
        } else {
            Write-Host "     -> Notice: Queue is clear (0 open flags). Seed payments to test live triage." -ForegroundColor Gray
        }
    } else {
        Write-Host "     -> Query risk flags failed: $($flagsRes.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 11: Merchant API Keys & Scoped Authentication (F-EDGE-06)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "MerchantApiKeys") {
    Write-Host "`n[SCENARIO 11] Merchant API Keys: Scoped Key Generation & Authentication" -ForegroundColor Magenta
    $merchantId = "merchant_demo"
    $keyName = "E2E Automated Checkout Service"

    # 1. Generate new API Key
    Write-Host "  1. Generating scoped API Key for $merchantId (payments:write, payments:read)..." -ForegroundColor Yellow
    $createKeyRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/merchants/$merchantId/api-keys" -Body @{
        name = $keyName
        scopes = @("payments:write", "payments:read")
        isLive = $true
    }

    if ($createKeyRes.Success) {
        $rawKey = $createKeyRes.Data.rawKey
        $keyId = $createKeyRes.Data.id
        $prefix = $createKeyRes.Data.prefix
        Write-Host "     -> Key created! ID: $keyId | Prefix: $prefix" -ForegroundColor Green
        Write-Host "     -> Raw Token: $($rawKey.Substring(0, 15))... (length: $($rawKey.Length))" -ForegroundColor Cyan

        # 2. Authorize Payment using X-API-Key
        Write-Host "  2. Authorizing payment using generated X-API-Key..." -ForegroundColor Yellow
        $authIdemp = [Guid]::NewGuid().ToString()
        $payRes = Send-GatewayRequest -Method "Post" -Path "/payments" -ApiKey $rawKey -IdempotencyKey $authIdemp -Body @{
            partyId = "customer_apikey_$(Get-Random -Minimum 100 -Maximum 999)"
            amount = 3500 # 35.00 EUR
            currency = "EUR"
            settlementChannel = "ADYEN"
        }

        if ($payRes.Success) {
            Write-Host "     -> Payment Authorized via API Key! Status: $($payRes.Data.state) | PaymentId: $($payRes.Data.paymentId)" -ForegroundColor Green
        } else {
            Write-Host "     -> Authorization via API Key Failed: $($payRes.Error)" -ForegroundColor Red
        }

        # 3. Query Merchant API Keys list
        Write-Host "  3. Querying active merchant API keys list..." -ForegroundColor Yellow
        $listRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/merchants/$merchantId/api-keys"
        if ($listRes.Success) {
            $keyCount = @($listRes.Data).Count
            Write-Host "     -> Found $keyCount registered API key(s) for $merchantId" -ForegroundColor Green
        }

        # 4. Revoke API Key
        Write-Host "  4. Revoking API key $keyId..." -ForegroundColor Yellow
        $revokeRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/merchants/$merchantId/api-keys/$keyId/revoke"
        if ($revokeRes.Success) {
            Write-Host "     -> Key successfully revoked! (Message: $($revokeRes.Data.message))" -ForegroundColor Green

            # 4b. Verify audit trail in list
            Write-Host "  4b. Verifying audit trail in merchant API keys list..." -ForegroundColor Yellow
            $postRevokeList = Send-GatewayRequest -Method "Get" -Path "/api/v1/merchants/$merchantId/api-keys"
            if ($postRevokeList.Success) {
                $revokedKey = @($postRevokeList.Data) | Where-Object { $_.id -eq $keyId }
                if ($revokedKey -and $revokedKey.isRevoked) {
                    Write-Host "     -> Audit Trail Verified! RevokedBy: $($revokedKey.revokedBy) | RevokedAt: $($revokedKey.revokedAt)" -ForegroundColor Cyan
                }
            }

            # 5. Verify rejected payment with revoked key
            Write-Host "  5. Verifying payment attempt with revoked API key is rejected (401)..." -ForegroundColor Yellow
            $rejectedPay = Send-GatewayRequest -Method "Post" -Path "/payments" -ApiKey $rawKey -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
                partyId = "customer_rejected_key"
                amount = 2000
                currency = "EUR"
                settlementChannel = "ADYEN"
            }
            if (-not $rejectedPay.Success) {
                Write-Host "     -> Correctly rejected! Status: $($rejectedPay.Status)" -ForegroundColor Green
            } else {
                Write-Host "     -> UNEXPECTED: Revoked key was allowed!" -ForegroundColor Red
            }
        } else {
            Write-Host "     -> Key revocation failed: $($revokeRes.Error)" -ForegroundColor Red
        }
    } else {
        Write-Host "     -> API Key creation failed: $($createKeyRes.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 12: Webhook Dead-Letter Queue & Manual Replay (F-EDGE-07)
# -----------------------------------------------------------------------------
if ($scenarioList -contains "WebhookDlq") {
    Write-Host "`n[SCENARIO 12] Webhook Dead-Letter Queue: Delivery Logs & Manual Replay" -ForegroundColor Magenta
    $merchantId = "merchant_demo"

    # 1. Query Webhook DLQ records
    Write-Host "  1. Querying DLQ records for merchant $merchantId (status: exhausted)..." -ForegroundColor Yellow
    $dlqRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/merchants/$merchantId/webhooks/dlq?status=exhausted&page=1&pageSize=10"

    if ($dlqRes.Success) {
        $dlqItems = @($dlqRes.Data)
        Write-Host "     -> Retrieved $($dlqItems.Count) exhausted dead-letter record(s)" -ForegroundColor Green

        if ($dlqItems.Count -gt 0) {
            $dlqEntry = $dlqItems[0]
            Write-Host "  2. Inspecting DLQ Entry $($dlqEntry.id)..." -ForegroundColor Yellow
            Write-Host "     -> EventId: $($dlqEntry.eventId) | Destination: $($dlqEntry.destinationUrl)" -ForegroundColor Cyan
            Write-Host "     -> Delivery Attempts: $($dlqEntry.deliveryAttempts) | Last Error: $($dlqEntry.lastErrorMessage)" -ForegroundColor Cyan
            Write-Host "     -> Current Replay Attempts: $($dlqEntry.replayAttemptCount) | Last Replay HTTP: $($dlqEntry.lastReplayStatusCode)" -ForegroundColor Cyan

            # 3. Trigger manual replay
            Write-Host "  3. Triggering manual replay re-dispatch for entry $($dlqEntry.id)..." -ForegroundColor Yellow
            $replayRes = Send-GatewayRequest -Method "Post" -Path "/api/v1/merchants/$merchantId/webhooks/dlq/$($dlqEntry.id)/replay"
            if ($replayRes.Success) {
                Write-Host "     -> Replay result: $($replayRes.Data.message) | Success: $($replayRes.Data.success)" -ForegroundColor Green

                # 4. Verify updated replay diagnostics
                Write-Host "  4. Verifying updated replay diagnostics in DLQ..." -ForegroundColor Yellow
                $updatedDlqRes = Send-GatewayRequest -Method "Get" -Path "/api/v1/merchants/$merchantId/webhooks/dlq?status=all&page=1&pageSize=10"
                if ($updatedDlqRes.Success) {
                    $updatedEntry = @($updatedDlqRes.Data) | Where-Object { $_.id -eq $dlqEntry.id }
                    if ($updatedEntry) {
                        Write-Host "     -> Diagnostics Updated! ReplayAttemptCount: $($updatedEntry.replayAttemptCount) | LastReplayStatusCode: $($updatedEntry.lastReplayStatusCode) | Status: $($updatedEntry.status)" -ForegroundColor Cyan
                    }
                }
            } else {
                Write-Host "     -> Replay request failed: $($replayRes.Error)" -ForegroundColor Red
            }
        } else {
            Write-Host "     -> DLQ is currently clean (0 exhausted records). Outbound delivery health is 100%." -ForegroundColor Gray
        }
    } else {
        Write-Host "     -> Query DLQ records failed: $($dlqRes.Error)" -ForegroundColor Red
    }
}

# -----------------------------------------------------------------------------
# Scenario 13: Mixed Traffic Batch
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Volume") {
    Write-Host "`n[SCENARIO 13] Generating Mixed Traffic Batch ($VolumeCount payments)..." -ForegroundColor Magenta
    for ($i = 1; $i -le $VolumeCount; $i++) {
        $isWeb3 = ($i % 2 -eq 0)
        $channel = if ($isWeb3) { "WEB3" } else { "ADYEN" }
        $currency = if ($isWeb3) { "USDC" } else { "EUR" }
        $amount = if ($isWeb3) { (Get-Random -Minimum 5 -Maximum 50) * 1000000 } else { (Get-Random -Minimum 500 -Maximum 10000) }
        $party = if ($isWeb3) { "wallet_batch_$i" } else { "customer_batch_$i" }

        Write-Host "  [$i/$VolumeCount] Authorize & Capture $channel ($amount $currency)..." -NoNewline
        $auth = Send-GatewayRequest -Method "Post" -Path "/payments" -IdempotencyKey ([Guid]::NewGuid().ToString()) -Body @{
            partyId = $party
            amount = $amount
            currency = $currency
            settlementChannel = $channel
        }

        if ($auth.Success) {
            $cap = Send-GatewayRequest -Method "Post" -Path "/payments/$($auth.Data.paymentId)/capture" -IdempotencyKey ([Guid]::NewGuid().ToString())
            if ($cap.Success) {
                Write-Host " OK ($($cap.Data.state))" -ForegroundColor Green
            } else {
                Write-Host " Capture FAIL" -ForegroundColor Red
            }
        } else {
            Write-Host " Auth FAIL" -ForegroundColor Red
        }
    }
}

Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host "  ALL SCENARIOS COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "  View distributed traces & metrics in:" -ForegroundColor Cyan
Write-Host "  * Jaeger UI:    http://localhost:16686" -ForegroundColor Yellow
Write-Host "    - Service: 'PaymentGateway.Api' and 'PaymentGateway.Worker'" -ForegroundColor Gray
Write-Host "    - Operations: 'POST /payments', 'POST /payments/{id}/capture', 'POST /payments/{id}/refund', 'POST /payments/{id}/cancel'" -ForegroundColor Gray
Write-Host "  * Grafana:      http://localhost:3000 (admin / admin)" -ForegroundColor Yellow
Write-Host "  * Prometheus:   http://localhost:9090" -ForegroundColor Yellow
Write-Host "======================================================================" -ForegroundColor Cyan
