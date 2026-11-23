# Lance LocalConvert.exe --smoke-test dans chaque dossier : l'application doit afficher sa fenêtre
# puis se fermer d'elle-même avec le code 0. En cas d'échec, affiche le journal et l'Observateur d'événements.
param([string[]] $Folders)

# Nombre d'icônes intégrées à un exe (0 = l'Explorateur affiche l'icône générique).
Add-Type -Namespace LocalConvert -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern uint ExtractIconEx(string file, int index, System.IntPtr[] large, System.IntPtr[] small, uint count);
'@

$failed = $false
foreach ($folder in $Folders) {
    $exe = Join-Path $folder 'LocalConvert.exe'
    foreach ($withIcon in @($exe, (Join-Path $folder 'localconvert-cli.exe'))) {
        $icons = [LocalConvert.Shell]::ExtractIconEx((Resolve-Path $withIcon).Path, -1, $null, $null, 0)
        if ($icons -lt 1) { Write-Host "::error::$withIcon ne contient pas d'icône"; $failed = $true }
        else { Write-Host "OK : icône intégrée à $withIcon" }
    }
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
        $cli = Join-Path $folder 'localconvert-cli.exe'
        if (Test-Path $cli) {
            $out = & $cli --help
            $code = $LASTEXITCODE
            $out | Select-Object -First 1
            if ($code -ne 0) { Write-Host "::error::$cli --help a échoué (code $code)"; $failed = $true }
        }
        continue
    }
    $failed = $true
    Write-Host "::error::$exe s'est arrêté avec le code $($p.ExitCode)"
    if (Test-Path "$folder-stderr.txt") { Get-Content "$folder-stderr.txt" }
    $logs = Join-Path $env:LOCALAPPDATA 'LocalConvert/logs'
    if (Test-Path $logs) { Get-ChildItem $logs | ForEach-Object { Get-Content $_.FullName } }
    Get-WinEvent -LogName Application -MaxEvents 30 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' } |
        Select-Object -First 4 | Format-List TimeCreated, ProviderName, Message
}
if ($failed) { exit 1 }
