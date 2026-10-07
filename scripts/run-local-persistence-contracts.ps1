[CmdletBinding()]
param(
    [string]$Dotnet,
    [switch]$KeepDatabase
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Dotnet) {
    $bundledSdk = Join-Path (Split-Path $repository -Parent) 'toolchain/dotnet/dotnet.exe'
    $Dotnet = if (Test-Path -LiteralPath $bundledSdk) { $bundledSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
}
$containerName = 'strataai-contract-' + [guid]::NewGuid().ToString('N')
$identity = [guid]::NewGuid().ToString('N')
$containerId = $null
$passed = $false
$previousEnvironment = @{}
$connectionVariables = @('STRATAAI_CONTRACT_ADMIN_CONNECTION', 'STRATAAI_CONTRACT_API_CONNECTION', 'STRATAAI_CONTRACT_WORKER_CONNECTION')
foreach ($variable in $connectionVariables) { $previousEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process') }

function Invoke-CheckedDocker {
    param([string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Disposable PostgreSQL operation failed (exit $LASTEXITCODE)." }
}

Push-Location $repository
try {
    & $Dotnet restore StrataAI2.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    & $Dotnet build StrataAI2.slnx -c Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Release compilation failed.' }
    $databasePath = (Resolve-Path (Join-Path $repository 'db')).Path
    $created = @(Invoke-CheckedDocker -Arguments @('run', '-d', '--name', $containerName,
        '--label', "codex.strataai.contract=$identity", '-e', 'POSTGRES_DB=strataai_ci',
        '-e', 'POSTGRES_USER=postgres', '-e', 'POSTGRES_PASSWORD=postgres', '-p', '127.0.0.1::5432',
        '--mount', "type=bind,source=$databasePath,target=/workspace/db,readonly", 'pgvector/pgvector:pg17'))
    $containerId = $created[-1].Trim()
    if ($containerId -notmatch '^[0-9a-f]{64}$') { throw 'Disposable container identity was not confirmed.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try { & docker exec $containerName pg_isready -U postgres -d strataai_ci *> $null; $readyExit = $LASTEXITCODE }
        catch { $readyExit = 1 }
        if ($readyExit -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Disposable PostgreSQL did not become ready.' }
    Invoke-CheckedDocker -Arguments @('exec', $containerName, 'bash', '-c',
        'set -e; for file in /workspace/db/migrations/*.sql; do psql -X -U postgres -d strataai_ci -v ON_ERROR_STOP=1 -f "$file" >/dev/null; done')
    Invoke-CheckedDocker -Arguments @('exec', '-e', 'STRATAAI_API_DB_PASSWORD=ci-api-runtime-password',
        '-e', 'STRATAAI_WORKER_DB_PASSWORD=ci-worker-runtime-password', $containerName,
        'psql', '-X', '-U', 'postgres', '-d', 'strataai_ci', '-f', '/workspace/db/provision-runtime-roles.sql')
    $mapping = (Invoke-CheckedDocker -Arguments @('port', $containerName, '5432/tcp')).Trim()
    if ($mapping -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Database port is not bound exclusively to loopback.' }
    $port = $Matches[1]
    $env:STRATAAI_CONTRACT_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$port;Database=strataai_ci;Username=postgres;Password=postgres"
    $env:STRATAAI_CONTRACT_API_CONNECTION = "Host=127.0.0.1;Port=$port;Database=strataai_ci;Username=strataai_api_runtime;Password=ci-api-runtime-password"
    $env:STRATAAI_CONTRACT_WORKER_CONNECTION = "Host=127.0.0.1;Port=$port;Database=strataai_ci;Username=strataai_worker_runtime;Password=ci-worker-runtime-password"
    Write-Host "Running the full restricted persistence suite in $containerName (loopback port $port)."
    & $Dotnet run --project tests/StrataAI.Persistence.Contracts/StrataAI.Persistence.Contracts.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Persistence suite failed (exit $LASTEXITCODE)." }
    $passed = $true
}
finally {
    foreach ($variable in $connectionVariables) { [Environment]::SetEnvironmentVariable($variable, $previousEnvironment[$variable], 'Process') }
    Pop-Location
    if ($containerId) {
        if ($passed -and -not $KeepDatabase) {
            # Verify both immutable ID and this run's label before removing only
            # its disposable container and anonymous PostgreSQL volume.
            $currentId = & docker inspect --format '{{.Id}}' $containerName
            $currentLabel = & docker inspect --format '{{index .Config.Labels "codex.strataai.contract"}}' $containerName
            if ($currentId -eq $containerId -and $currentLabel -eq $identity) {
                Invoke-CheckedDocker -Arguments @('rm', '-f', '-v', $containerId)
            } else { Write-Warning 'Container identity changed; cleanup was withheld.' }
        } else { Write-Host "Database retained for inspection: $containerName. No test or container was restarted." }
    }
}
