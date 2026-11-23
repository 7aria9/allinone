# Lance AllInOne.exe --smoke-test dans chaque dossier : l'application doit afficher sa fenêtre
# puis se fermer d'elle-même avec le code 0. En cas d'échec, affiche le journal et l'Observateur d'événements.
param([string[]] $Folders)

$failed = $false
foreach ($folder in $Folders) {
    $exe = Join-Path $folder 'AllInOne.exe'
    Write-Host "== Démarrage de $exe"
    $p = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -PassThru -RedirectStandardError "$folder-stderr.txt"
    $null = $p.Handle  # nécessaire pour lire ExitCode ensuite
    if (-not $p.WaitForExit(60000)) {
        Write-Host "::error::$exe ne s'est pas fermé en 60 s (fenêtre bloquée ?)"
        Stop-Process -Id $p.Id -Force
        $failed = $true
        continue
    }
    if ($p.ExitCode -eq 0) {
        Write-Host "OK : la fenêtre principale s'est affichée."
        continue
    }
    $failed = $true
    Write-Host "::error::$exe s'est arrêté avec le code $($p.ExitCode)"
    if (Test-Path "$folder-stderr.txt") { Get-Content "$folder-stderr.txt" }
    $logs = Join-Path $env:LOCALAPPDATA 'AllInOne/logs'
    if (Test-Path $logs) { Get-ChildItem $logs | ForEach-Object { Get-Content $_.FullName } }
    Get-WinEvent -LogName Application -MaxEvents 30 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' } |
        Select-Object -First 4 | Format-List TimeCreated, ProviderName, Message
}
if ($failed) { exit 1 }
