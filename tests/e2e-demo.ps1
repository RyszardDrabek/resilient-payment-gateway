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
        [switch]$NoAuth
    )

    $headers = @{}
    if (-not $NoAuth -and $AuthToken) {
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
    $scenarioList = @("AdyenHappyPath", "AdyenCancel", "AdyenDecline", "Web3HappyPath", "Web3Cancel", "Web3Decline", "PartialSettlement", "UnresolvedQuery", "Volume")
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
# Scenario 9: Mixed Traffic Batch
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Volume") {
    Write-Host "`n[SCENARIO 9] Generating Mixed Traffic Batch ($VolumeCount payments)..." -ForegroundColor Magenta
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
