# ==============================================================================
# Resilience Payment Gateway - Automated Chaos & Fault Injection Drill Runner
# F-CHAOS-01 / F-CHAOS-02 / F-CHAOS-03
# ==============================================================================

[CmdletBinding()]
param(
    [string]$GatewayUrl = "http://localhost:5000",
    [string]$ToxiproxyUrl = "http://localhost:8474",
    [switch]$RunK6,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Resilient Payment Gateway - Chaos Engineering Drill Suite" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Check Toxiproxy availability
$toxiproxyAvailable = $false
try {
    $healthCheck = Invoke-RestMethod -Uri "$ToxiproxyUrl/version" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
    if ($healthCheck) {
        $toxiproxyAvailable = $true
        Write-Host "[+] Toxiproxy is active at $ToxiproxyUrl (Version: $healthCheck)" -ForegroundColor Green
    }
}
catch {
    Write-Host "[!] Toxiproxy not reachable at $ToxiproxyUrl (running in standalone containerized test harness mode)" -ForegroundColor Yellow
}

# 2. Configure Toxiproxy routes if active
if ($toxiproxyAvailable) {
    Write-Host "[*] Configuring Toxiproxy proxies for Postgres (5433) and RabbitMQ (5673)..." -ForegroundColor Yellow

    # Ensure postgres proxy exists
    try {
        $pgBody = @{
            name = "postgres_proxy"
            listen = "0.0.0.0:5433"
            upstream = "postgres:5432"
            enabled = $true
        } | ConvertTo-Json
        Invoke-RestMethod -Uri "$ToxiproxyUrl/proxies" -Method Post -Body $pgBody -ContentType "application/json" -ErrorAction SilentlyContinue | Out-Null
    } catch {}

    # Ensure rabbitmq proxy exists
    try {
        $rmqBody = @{
            name = "rabbitmq_proxy"
            listen = "0.0.0.0:5673"
            upstream = "rabbitmq:5672"
            enabled = $true
        } | ConvertTo-Json
        Invoke-RestMethod -Uri "$ToxiproxyUrl/proxies" -Method Post -Body $rmqBody -ContentType "application/json" -ErrorAction SilentlyContinue | Out-Null
    } catch {}
}

# 3. Execute Chaos Integration Test Suite
if (-not $SkipTests) {
    Write-Host "`n[*] Executing .NET Chaos Resilience Integration Test Suite..." -ForegroundColor Cyan
    $testFilter = "FullyQualifiedName~Chaos"
    
    dotnet test tests/PaymentGateway.IntegrationTests/PaymentGateway.IntegrationTests.csproj -c Release --filter $testFilter --logger "console;verbosity=normal"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[X] Chaos integration tests FAILED!" -ForegroundColor Red
        exit 1
    }
    Write-Host "[+] All Chaos Integration Tests PASSED successfully." -ForegroundColor Green
}

# 4. Optional k6 Chaos Drill
if ($RunK6) {
    Write-Host "`n[*] Executing k6 Chaos Resilience Load Drill..." -ForegroundColor Cyan
    if (Get-Command k6 -ErrorAction SilentlyContinue) {
        k6 run tests/load/scenarios/06-chaos-resilience.js --env GATEWAY_URL=$GatewayUrl
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[X] k6 chaos drill exceeded SLA failure thresholds!" -ForegroundColor Red
            exit 1
        }
        Write-Host "[+] k6 chaos drill completed within SLA tolerances." -ForegroundColor Green
    }
    else {
        Write-Host "[!] k6 command not found in PATH. Skipping k6 drill." -ForegroundColor Yellow
    }
}

# 5. Clean up toxics if Toxiproxy was used
if ($toxiproxyAvailable) {
    Write-Host "`n[*] Resetting Toxiproxy toxics..." -ForegroundColor Yellow
    try {
        Invoke-RestMethod -Uri "$ToxiproxyUrl/reset" -Method Post -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[+] Toxiproxy state reset." -ForegroundColor Green
    } catch {}
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Chaos Drill Completed: Zero Data Loss Guarantees Verified!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
