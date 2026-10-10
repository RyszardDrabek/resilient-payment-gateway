# E2E traffic generator for Resilient Payment Gateway
param(
    [string]$BaseUrl = "http://localhost:5080",
    [int]$Count = 3
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

Write-Host "=== Running E2E Payment Journeys against $BaseUrl ===" -ForegroundColor Cyan

for ($i = 1; $i -le $Count; $i++) {
    $amount = Get-Random -Minimum 1000 -Maximum 50000
    $partyId = "customer_$(Get-Random -Minimum 100 -Maximum 999)"
    $idempotencyKey = [Guid]::NewGuid().ToString()

    Write-Host "`n[$i/$Count] Authorizing payment for $partyId (Amount: $amount EUR)..." -ForegroundColor Yellow
    $authBody = @{
        partyId = $partyId
        amount = $amount
        currency = "EUR"
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $token"
        "Idempotency-Key" = $idempotencyKey
    }

    $auth = Invoke-RestMethod -Method Post -Uri "$BaseUrl/payments" -Headers $headers -ContentType "application/json" -Body $authBody
    Write-Host "  -> Authorized: $($auth.paymentId) (Channel ref: $($auth.channelReference))" -ForegroundColor Green

    # Query
    $query = Invoke-RestMethod -Method Get -Uri "$BaseUrl/payments/$($auth.paymentId)" -Headers $headers
    Write-Host "  -> Query status: $($query.state)" -ForegroundColor Gray

    # Capture
    $captureHeaders = @{
        "Authorization" = "Bearer $token"
        "Idempotency-Key" = [Guid]::NewGuid().ToString()
    }
    $capture = Invoke-RestMethod -Method Post -Uri "$BaseUrl/payments/$($auth.paymentId)/capture" -Headers $captureHeaders
    Write-Host "  -> Captured: $($capture.state)" -ForegroundColor Green
}

Write-Host "`nAll journeys completed! Check Jaeger at http://localhost:16686 to see the full distributed traces." -ForegroundColor Cyan
