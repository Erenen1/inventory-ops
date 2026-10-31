# Creates .env from .env.example and replaces every <generated> placeholder with a random secret.
# Idempotent: an existing .env is never overwritten (use -Force to regenerate; this also requires
# deleting the Docker volumes, because SQL Server keeps the original sa password).
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root '.env'
$template = Join-Path $root '.env.example'

if ((Test-Path $envFile) -and -not $Force) {
    Write-Host '.env already exists; keeping it. Use -Force to regenerate.'
    exit 0
}

$alphabet = [char[]]'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create() # works on Windows PowerShell 5.1 and PowerShell 7
function New-Secret {
    $chars = New-Object System.Collections.Generic.List[char]
    $buffer = New-Object byte[] 1
    while ($chars.Count -lt 24) {
        $rng.GetBytes($buffer)
        # Rejection sampling (248 = 4 * 62) avoids modulo bias.
        if ($buffer[0] -lt 248) { $chars.Add($alphabet[$buffer[0] % $alphabet.Length]) }
    }
    # Fixed "Aa1" suffix guarantees SQL Server and Identity complexity rules.
    (-join $chars) + 'Aa1'
}

$lines = Get-Content $template | ForEach-Object {
    if ($_ -notmatch '^\s*#' -and $_ -like '*<generated>*') { $_ -replace '<generated>', (New-Secret) } else { $_ }
}
# LF line endings so the file is identical in meaning for Docker on every platform.
[System.IO.File]::WriteAllText($envFile, (($lines -join "`n") + "`n"))

Write-Host 'Created .env with generated secrets.'
Write-Host 'Sign-in credentials are in .env (ADMIN_EMAIL / ADMIN_PASSWORD).'
Write-Host 'Next: docker compose up -d --build   then open http://localhost:3000'
