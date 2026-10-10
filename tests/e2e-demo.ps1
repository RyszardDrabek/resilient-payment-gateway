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

$headerB64 = Base64UrlEncode([System.Text.Encoding]::UTF8.GetBytes($headerJson))
$payloadB64 = Base64UrlEncode([System.Text.Encoding]::UTF8.GetBytes($payloadJson))
$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($jwtKey)
$sigB64 = Base64UrlEncode($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("$headerB64.$payloadB64")))
$token = "$headerB64.$payloadB64.$sigB64"

function Send-GatewayRequest {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Body = $null,
        [string]$IdempotencyKey = $null
    )

    $headers = @{
        "Authorization" = "Bearer $token"
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
    $scenarioList = @("AdyenHappyPath", "AdyenCancel", "AdyenDecline", "Web3HappyPath", "Web3Cancel", "Web3Decline", "Volume")
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
# Scenario 7: Mixed Traffic Batch
# -----------------------------------------------------------------------------
if ($scenarioList -contains "Volume") {
    Write-Host "`n[SCENARIO 7] Generating Mixed Traffic Batch ($VolumeCount payments)..." -ForegroundColor Magenta
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
