#requires -Version 5.1
<#
.SYNOPSIS
  One-file deployment for Babylon Pro 12 + IIS and/or Apache on Windows workstations.
.DESCRIPTION
  Run ONCE from an elevated 64-bit Windows PowerShell on each workstation.
  - If IIS is installed, installs Microsoft IIS URL Rewrite 2.1 (x64) if missing.
  - IIS is optional. If Apache is installed instead, configures Apache 2.4 native conditions.
  - If neither IIS nor Apache is installed, does absolutely nothing.
  - Installs neither IIS nor Apache; no changes to port/bindings or DHCP.
  - Apache uses a conditional 403 response, NOT an equivalent of IIS AbortRequest.
  - Apache needs no mod_rewrite or extra MSI.
  - Apache detection treats equivalent executable paths as equal regardless of letter case.
  - Apache changes are validated with httpd -t and backed up before restart.
  - Running XAMPP Apache without a Windows service is restarted automatically
    using its own apache_stop.bat and apache_start.bat (not httpd -k restart).
    A running Apache outside XAMPP is left untouched with a warning.
  - The restart runs only for the identified Apache executable and verifies
    that it is listening again. A stopped Apache is never started by this script.
    -SkipApacheRestart opts out. When already configured, reload is needed only
    if the configuration files changed after Apache last started.
  - Apache-only PCs do not need Microsoft IIS URL Rewrite MSI.
  - Retains the exact IIS rule used by Babylon-IIS-Rewrite-v3.ps1.
  - Clears IE/WinINet *temporary web files* for every non-service local user profile.
  - If Babylon.exe was running at script start, closes it before cache cleanup,
    then restarts it at the end in its original interactive user's context.
    Temporary per-user task is removed immediately after launch.
  - A restart is skipped (and Babylon left running) if a safe interactive
    launch cannot be prepared. -SkipBabylonRestart opts out.
  - Only with -CleanupAtNextLogon: registers a one-time per-user native WinINet
    cleanup on NEXT interactive logon. Default: NO next-logon registration.
  - Install/CacheOnly without that switch remove only this script's old Active Setup
    registration, if a previous deployment created it.
  - Does NOT delete cookies, saved credentials, browser profiles or WebCache DBs.
  - Does NOT change IIS port/bindings, site files or Babylon files.
  - Does not install or start IIS/W3SVC.
  - Logging is OPTIONAL: off by default. Use -EnableLog to append status,
    warnings and handled errors to a local log file.
  - Default log location (only with -EnableLog):
    C:\ProgramData\Babylon-IIS-Apache\Deploy.log
  - If neither IIS nor Apache is found, no log or directory is created.

  WARNING: Apache's 403 is not a connection abort. Test Babylon with Apache
  and IIS independently before deploying to the entire lab.

.PARAMETER Action
  Install (default), Status, CacheOnly, Remove, RestartApache. All mutating actions
  skip automatically when neither IIS nor Apache is installed.
  RestartApache: restart the detected Apache only (no rewrite/cache/IIS changes).
  Remove removes the managed IIS/Apache rules and script's Active Setup entry; it does
  not uninstall URL Rewrite or restore already-deleted temporary files.
.PARAMETER SiteName
  Existing IIS site where the tested rule is applied.
.PARAMETER MsiPath
  Optional local/UNC path to the official x64 Rewrite MSI. An MSI next to this
  script is preferred automatically; otherwise Microsoft download is used.
.PARAMETER CacheVersion
  Active Setup version in comma format, e.g. 1,0,0,0. Used only with
  -CleanupAtNextLogon. Increase for a later per-user cleanup deployment.
.PARAMETER EnableLog
  OPTIONAL, OFF BY DEFAULT. Write console status and caught errors to a file.
.PARAMETER LogPath
  Optional path to log file; requires -EnableLog. Log file and parent folder
  are not created when -EnableLog is absent.
.PARAMETER CleanupAtNextLogon
  OPTIONAL, OFF BY DEFAULT. Register the one-time native per-user cache cleanup
  for the next interactive logon. Install and CacheOnly support this switch.
.PARAMETER SkipCache
  With Install: skip cache cleanup and leave any Active Setup entry untouched.
.PARAMETER SkipBabylonRestart
  OPTIONAL, OFF BY DEFAULT. Do not stop/restart an already running Babylon.
.PARAMETER SkipApacheRestart
  OPTIONAL, OFF BY DEFAULT. Do not restart Apache after changing its rule.
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install -MsiPath '\\server\share\rewrite_amd64_en-US.msi'
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install -CleanupAtNextLogon
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install -EnableLog
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install -EnableLog -LogPath 'C:\Temp\Babylon-Deploy.log'
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Install -SkipApacheRestart
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Status
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action RestartApache
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action CacheOnly
.EXAMPLE
  .\Babylon-IIS-Apache-AllUsers-v10.ps1 -Action Remove

.PARAMETER ApacheHttpdPath
  Optional full path to Apache httpd.exe (required if discovery is ambiguous).
.PARAMETER ApacheConfigPath
  Optional full path to the Apache httpd.conf (for nonstandard installs).
#>
[CmdletBinding()]
param(
    [ValidateSet('Install','Status','CacheOnly','Remove','RestartApache')]
    [string]$Action = 'Install',
    [ValidateNotNullOrEmpty()]
    [string]$SiteName = 'Default Web Site',
    [string]$MsiPath = '',
    [ValidatePattern('^\d{1,5},\d{1,5},\d{1,5},\d{1,5}$')]
    [string]$CacheVersion = '1,0,0,0',
    [switch]$SkipCache,
    [switch]$SkipBabylonRestart,
    [switch]$SkipApacheRestart,
    [switch]$CleanupAtNextLogon,
    [switch]$EnableLog,
    [string]$LogPath = '',
    [string]$ApacheHttpdPath = '',
    [string]$ApacheConfigPath = ''
)

$ErrorActionPreference = 'Stop'
$RuleName = 'Babylon12-Localhost-Compatibility'
$HostPattern = '^localhost(:80)?$'
$QueryPattern = '^uid=[0-9]+&ver=12000&bld=13&iev=11(&|$)'
$ActiveSetupId = '{DBFA967A-B72E-416D-A674-1E16C25B4D32}'
$ActiveSetupPath = 'HKLM:\SOFTWARE\Microsoft\Active Setup\Installed Components\' + $ActiveSetupId
$OfficialMsiUrl = 'https://download.microsoft.com/download/1/2/8/128E2E22-C1B9-44A4-BE2A-5859ED1D4592/rewrite_amd64_en-US.msi'
$script:LoggingEnabled = $false
$script:EffectiveLogPath = $null
$script:BabylonAtStart = @()

function Initialize-Logging {
    if (-not $EnableLog) { return }

    $destination = if ([string]::IsNullOrWhiteSpace($LogPath)) {
        Join-Path $env:ProgramData 'Babylon-IIS-Apache\Deploy.log'
    } else {
        $LogPath
    }
    $fullPath = [System.IO.Path]::GetFullPath($destination)
    $folder = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        New-Item -ItemType Directory -Path $folder -Force -ErrorAction Stop | Out-Null
    }
    # A log file is created only when -EnableLog was explicitly supplied.
    $script:EffectiveLogPath = $fullPath
    $script:LoggingEnabled = $true
}

function Write-Status {
    param([string]$Message, [string]$Level = 'INFO')
    switch ($Level) {
        'WARN' { Write-Warning $Message }
        'ERROR' { Write-Host "ERROR: $Message" -ForegroundColor Red }
        default { Write-Host $Message }
    }
    if ($script:LoggingEnabled) {
        try {
            $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            $logLine = "[$timestamp] [$Level] $Message"
            Add-Content -LiteralPath $script:EffectiveLogPath -Value $logLine -Encoding UTF8 -ErrorAction Stop
        } catch {
            # Optional logging must never interrupt the IIS/Apache/cache operation.
            $script:LoggingEnabled = $false
            Write-Warning "Unable to write optional log; continuing without logging: $($_.Exception.Message)"
        }
    }
}

function Assert-Environment {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'This package requires 64-bit Windows.'
    }
    if (-not [Environment]::Is64BitProcess) {
        throw 'Use 64-bit Windows PowerShell (System32, not SysWOW64).'
    }
    if ($CleanupAtNextLogon -and $Action -notin @('Install','CacheOnly')) {
        throw '-CleanupAtNextLogon is supported only with -Action Install or CacheOnly.'
    }
    if ($CleanupAtNextLogon -and $SkipCache) {
        throw '-SkipCache and -CleanupAtNextLogon cannot be used together.'
    }
    if (-not $EnableLog -and -not [string]::IsNullOrWhiteSpace($LogPath)) {
        throw '-LogPath requires -EnableLog.'
    }
    if ($Action -ne 'Status') {
        $userIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $admin = New-Object Security.Principal.WindowsPrincipal($userIdentity)
        if ($userIdentity.User.Value -ne 'S-1-5-18' -and
            -not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Run this PS1 from elevated Windows PowerShell (Run as Administrator).'
        }
    }
}

function Test-IisWebServerInstalled {
    # The W3SVC service is installed with the IIS Web Server feature on
    # workstation Windows. Its status may be Running or Stopped.
    return ($null -ne (Get-Service -Name 'W3SVC' -ErrorAction SilentlyContinue))
}

# Apache-specific routines. No changes are made if Apache cannot be identified
# unambiguously. IIS and Apache can coexist but are configured independently.
function Get-HttpdExeFromCommandLine {
    param([string]$CommandLine)
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $null }
    if ($CommandLine -match '(?i)^\s*"(?<exe>[^\"]*httpd\.exe)"') {
        return $Matches.exe
    }
    if ($CommandLine -match '(?i)(?<exe>[a-z]:\\[^\"]*?\\httpd\.exe)') {
        return $Matches.exe.Trim()
    }
    return $null
}

function Get-HttpdServiceConfigArg {
    param([string]$CommandLine)
    if ([string]::IsNullOrWhiteSpace($CommandLine)) { return $null }
    if ($CommandLine -match '(?i)(?:^|\s)-f\s+(?:"(?<quoted>[^\"]+)"|(?<plain>\S+))') {
        if ($Matches.quoted) { return $Matches.quoted }
        return $Matches.plain
    }
    return $null
}

# Windows paths are case-insensitive. Apache normally runs two httpd.exe
# processes whose ExecutablePath can differ only in the case of the drive letter.
# Resolve and compare paths without case sensitivity before choosing an instance.
function Get-UniqueApacheExecutablePaths {
    param([string[]]$Paths)
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        try {
            $candidate = $path.Trim().Trim('"').Replace('/', '\')
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
            $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).ProviderPath
        } catch { continue }
        if ($seen.Add($resolved)) { Write-Output $resolved }
    }
}

function Get-ApacheTarget {
    $serviceCandidates = @()
    try {
        $serviceCandidates = @(Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object {
            $_.Name -match '(?i)apache|httpd|wampapache' -or
            $_.DisplayName -match '(?i)apache http server' -or
            $_.PathName -match '(?i)httpd\.exe'
        })
    } catch {
        Write-Status "Apache service discovery failed: $($_.Exception.Message)" 'WARN'
    }

    $possible = New-Object 'System.Collections.Generic.List[string]'
    foreach ($service in $serviceCandidates) {
        $path = Get-HttpdExeFromCommandLine -CommandLine ([string]$service.PathName)
        if ($path -and (Test-Path -LiteralPath $path -PathType Leaf)) { $possible.Add($path) }
    }
    try {
        foreach ($proc in @(Get-CimInstance Win32_Process -Filter "Name = 'httpd.exe'" -ErrorAction SilentlyContinue)) {
            if ($proc.ExecutablePath -and (Test-Path -LiteralPath $proc.ExecutablePath -PathType Leaf)) {
                $possible.Add([string]$proc.ExecutablePath)
            }
        }
    } catch { }
    foreach ($knownPath in @(
        'C:\xampp\apache\bin\httpd.exe',
        'C:\Apache24\bin\httpd.exe',
        'C:\Apache2\bin\httpd.exe',
        'C:\Program Files\Apache Software Foundation\Apache2.4\bin\httpd.exe',
        'C:\Program Files\Apache Software Foundation\Apache24\bin\httpd.exe'
    )) {
        if (Test-Path -LiteralPath $knownPath -PathType Leaf) { $possible.Add($knownPath) }
    }
    # Bounded scans for the two common development stacks; do not search all C:\.
    foreach ($stackDir in @('C:\wamp64\bin\apache','C:\wamp\bin\apache','C:\laragon\bin\apache')) {
        if (Test-Path -LiteralPath $stackDir -PathType Container) {
            foreach ($candidate in @(Get-ChildItem -LiteralPath $stackDir -Filter 'httpd.exe' -File -Recurse -ErrorAction SilentlyContinue)) {
                $possible.Add($candidate.FullName)
            }
        }
    }
    if ($ApacheHttpdPath) {
        if (-not (Test-Path -LiteralPath $ApacheHttpdPath -PathType Leaf)) {
            throw "Apache executable not found: $ApacheHttpdPath"
        }
        $exe = (Resolve-Path -LiteralPath $ApacheHttpdPath).ProviderPath
    } else {
        $choices = @(Get-UniqueApacheExecutablePaths -Paths $possible.ToArray())
        # Prefer the running Apache, deduplicated using case-insensitive Windows paths.
        $runningPaths = @()
        $runningProcesses = @()
        try {
            $runningProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'httpd.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $_.ExecutablePath -and (Test-Path -LiteralPath $_.ExecutablePath -PathType Leaf) })
            $runningPaths = @(Get-UniqueApacheExecutablePaths -Paths @($runningProcesses | ForEach-Object { [string]$_.ExecutablePath }))
        } catch { }
        if ($runningPaths.Count -eq 1) {
            $exe = $runningPaths[0]
        } elseif ($runningPaths.Count -gt 1) {
            # If several genuinely different Apache installations are running,
            # pick one only when exactly one of them owns TCP port 80.
            $port80Processes = @()
            try {
                $port80Pids = @(Get-NetTCPConnection -LocalPort 80 -State Listen -ErrorAction SilentlyContinue |
                    Select-Object -ExpandProperty OwningProcess -Unique)
                $port80Processes = @($runningProcesses |
                    Where-Object { $port80Pids -contains [uint32]$_.ProcessId })
            } catch { }
            $port80Paths = @(Get-UniqueApacheExecutablePaths -Paths @($port80Processes | ForEach-Object { [string]$_.ExecutablePath }))
            if ($port80Paths.Count -eq 1) {
                $exe = $port80Paths[0]
                Write-Status "Multiple Apache installations found; selected port 80 owner: $exe"
            } else {
                throw 'Multiple different running Apache installations found and no unique port 80 owner. Specify -ApacheHttpdPath.'
            }
        } elseif ($choices.Count -eq 1) {
            $exe = $choices[0]
        } elseif ($choices.Count -gt 1) {
            throw 'Multiple different Apache installations found. Specify -ApacheHttpdPath.'
        } else {
            return $null
        }
    }
    $exe = (Resolve-Path -LiteralPath $exe -ErrorAction Stop).ProviderPath
    $root = (Split-Path -Parent (Split-Path -Parent $exe))
    $svcMatches = @($serviceCandidates | Where-Object {
        $found = Get-HttpdExeFromCommandLine -CommandLine ([string]$_.PathName)
        $found -and ([string]::Equals($found, $exe, [StringComparison]::OrdinalIgnoreCase))
    })
    if ($svcMatches.Count -gt 1) {
        throw 'More than one Apache service uses this executable. Specify a service-specific Apache configuration manually.'
    }
    $serviceName = $null
    if ($svcMatches.Count -eq 1 -and -not $ApacheConfigPath) { $serviceName = [string]$svcMatches[0].Name }
    if ($ApacheConfigPath) {
        $conf = $ApacheConfigPath
    } elseif ($svcMatches.Count -eq 1) {
        $configFromService = Get-HttpdServiceConfigArg -CommandLine ([string]$svcMatches[0].PathName)
        if ($configFromService) {
            if ([System.IO.Path]::IsPathRooted($configFromService)) {
                $conf = $configFromService
            } else {
                $conf = Join-Path $root $configFromService
            }
        } else {
            $conf = Join-Path $root 'conf\httpd.conf'
        }
    } else {
        $conf = Join-Path $root 'conf\httpd.conf'
    }
    if (-not (Test-Path -LiteralPath $conf -PathType Leaf)) {
        throw "Apache configuration was not found: $conf. Specify -ApacheConfigPath."
    }
    $conf = (Resolve-Path -LiteralPath $conf -ErrorAction Stop).ProviderPath
    [pscustomobject]@{
        Exe = $exe
        Root = $root
        Config = $conf
        ServiceName = $serviceName
        ServiceRunning = [bool]($svcMatches.Count -eq 1 -and -not $ApacheConfigPath -and $svcMatches[0].State -eq 'Running')
    }
}

function Invoke-ApacheCommand {
    param([psobject]$Target, [string[]]$Arguments)
    # Avoid WinPS 5.1 NativeCommandError when Apache writes 'Syntax OK' to stderr.
    # Capture both streams in memory; do not write diagnostic files.
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = [string]$Target.Exe
    $start.WorkingDirectory = [string]$Target.Root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $parts = foreach ($arg in $Arguments) {
        $value = [string]$arg
        if ($value -match '[\s"]') { '"' + $value.Replace('"', '\"') + '"' }
        else { $value }
    }
    $start.Arguments = ($parts -join ' ')
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Unable to start Apache httpd.exe.' }
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            try { $process.Kill() } catch { }
            throw 'Apache httpd.exe command timed out after 30 seconds.'
        }
        $outTask.Wait()
        $errTask.Wait()
        $output = @($outTask.Result, $errTask.Result) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = ($output -join "`n").Trim() }
    } finally {
        $process.Dispose()
    }
}

function Get-ApacheCommandArgs {
    param([psobject]$Target, [string[]]$Extra)
    if ($Target.ServiceName) {
        return @('-n', $Target.ServiceName) + @($Extra)
    }
    return @('-d', $Target.Root, '-f', $Target.Config) + @($Extra)
}

function Test-ApacheSyntax {
    param([psobject]$Target)
    $result = Invoke-ApacheCommand -Target $Target -Arguments (Get-ApacheCommandArgs -Target $Target -Extra @('-t'))
    Write-Status "Apache configuration test: exit=$($result.ExitCode); $($result.Output)"
    return ($result.ExitCode -eq 0)
}

# Latin-1 decoding/encoding is lossless for pre-existing arbitrary 8-bit
# Apache configuration bytes. It preserves original UTF-8/ANSI and BOM bytes.
function Read-ApacheConfigRaw {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return [Text.Encoding]::GetEncoding(28591).GetString($bytes)
}
function Write-ApacheConfigRaw {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllBytes($Path, [Text.Encoding]::GetEncoding(28591).GetBytes($Content))
}

function Get-ApacheManagedPaths {
    param([psobject]$Target)
    $snippet = Join-Path (Split-Path -Parent $Target.Config) 'babylon12-localhost-compat.conf'
    $includePath = $snippet.Replace('\', '/')
    [pscustomobject]@{
        Snippet = $snippet
        IncludeBlock = "# BEGIN Babylon12-Localhost-Compatibility`r`nInclude `"$includePath`"`r`n# END Babylon12-Localhost-Compatibility"
        BeginMarker = '# BEGIN Babylon12-Localhost-Compatibility'
        EndMarker = '# END Babylon12-Localhost-Compatibility'
    }
}

function New-ApacheSnippet {
    param([psobject]$Target)
    # Apache core <If> conditions plus authorization work in a global
    # <LocationMatch> even with multiple VirtualHosts. Unlike IIS AbortRequest,
    # this returns HTTP 403. No external Apache module is required.
    return (@(
        '# Managed by Babylon-IIS-Apache; do not edit manually',
        '<LocationMatch "^/$">',
        '    <If "req(''Host'') =~ m#^localhost(:80)?$# && %{QUERY_STRING} =~ m#^uid=[0-9]+&ver=12000&bld=13&iev=11(&|$)#">',
        '        Require all denied',
        '    </If>',
        '</LocationMatch>',
        ''
    ) -join "`r`n")
}

# Return running Apache processes with the *same executable path*, ignoring
# differences in the letter case used by Win32_Process.ExecutablePath.
function Get-TargetApacheProcesses {
    param([psobject]$Target)
    return @(Get-CimInstance Win32_Process -Filter "Name = 'httpd.exe'" -ErrorAction Stop |
        Where-Object { $_.ExecutablePath -and
            [string]::Equals([string]$_.ExecutablePath, [string]$Target.Exe,
                [StringComparison]::OrdinalIgnoreCase) })
}

function Test-ApacheNeedsReload {
    param([psobject]$Target, [string]$Snippet)
    $running = @(Get-TargetApacheProcesses -Target $Target)
    if ($running.Count -eq 0) { return $false }
    # v9 may already have written the rule, but not reloaded XAMPP.
    # Reload only if the configuration is newer than the running process.
    $latestConfigUtc = ([IO.FileInfo]$Target.Config).LastWriteTimeUtc
    if (Test-Path -LiteralPath $Snippet -PathType Leaf) {
        $snippetUtc = ([IO.FileInfo]$Snippet).LastWriteTimeUtc
        if ($snippetUtc -gt $latestConfigUtc) { $latestConfigUtc = $snippetUtc }
    }
    $starts = @($running | Where-Object { $_.CreationDate } |
        ForEach-Object { ([DateTime]$_.CreationDate).ToUniversalTime() } | Sort-Object)
    if ($starts.Count -eq 0) {
        Write-Status 'Unable to read Apache start time; attempting reload of existing rule.' 'WARN'
        return $true
    }
    return ($latestConfigUtc -gt $starts[0].AddSeconds(2))
}

function Invoke-XamppBatch {
    param([string]$BatchFile, [bool]$WaitForExit)
    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = Join-Path $env:windir 'System32\cmd.exe'
    # CMD double quoting handles paths with spaces. Never invoke arbitrary
    # batch files outside the detected XAMPP installation folder.
    $si.Arguments = '/d /c ""' + $BatchFile + '""'
    $si.WorkingDirectory = Split-Path -Parent $BatchFile
    $si.UseShellExecute = $false
    $si.CreateNoWindow = $true
    $si.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $si
    if (-not $process.Start()) { throw "Unable to start $BatchFile" }
    if ($WaitForExit) {
        try {
            if (-not $process.WaitForExit(20000)) {
                try { $process.Kill() } catch { }
                throw "Timed out waiting for XAMPP stop script: $BatchFile"
            }
        } finally {
            $process.Dispose()
        }
    } else {
        # apache_start.bat typically stays running while the console Apache
        # runs. Do NOT -Wait: that would block the deployment indefinitely.
        $process.Dispose()
    }
}

function Restart-XamppApache {
    param([psobject]$Target)
    $running = @(Get-TargetApacheProcesses -Target $Target)
    if ($running.Count -eq 0) {
        Write-Status 'XAMPP Apache is stopped. Leaving it stopped; rule activates at its next start.'
        return
    }
    $xamppRoot = Split-Path -Parent $Target.Root
    $expectedHttpd = Join-Path $xamppRoot 'apache\bin\httpd.exe'
    $stopBatch = Join-Path $xamppRoot 'apache_stop.bat'
    $startBatch = Join-Path $xamppRoot 'apache_start.bat'
    if (-not [string]::Equals($expectedHttpd, $Target.Exe,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $stopBatch -PathType Leaf) -or
        -not (Test-Path -LiteralPath $startBatch -PathType Leaf)) {
        Write-Status 'No matching XAMPP stop/start scripts for this Apache; leaving it running. Restart manually.' 'WARN'
        return
    }
    # XAMPP stop scripts vary by version and sometimes stop httpd.exe by NAME.
    # Do not run one if ANY other Apache installation is currently active.
    $otherApache = @(Get-CimInstance Win32_Process -Filter "Name = 'httpd.exe'" -ErrorAction Stop |
        Where-Object { -not $_.ExecutablePath -or
            -not [string]::Equals([string]$_.ExecutablePath, [string]$Target.Exe,
                [StringComparison]::OrdinalIgnoreCase) })
    if ($otherApache.Count -gt 0) {
        Write-Status 'Another Apache binary is running; XAMPP batch restart skipped to avoid stopping it.' 'WARN'
        return
    }
    # A SYSTEM GPO task in session 0 cannot reliably recreate an interactive
    # XAMPP console. Leave it running, and use a per-machine Apache service
    # for unattended reloads if required.
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.User.Value -eq 'S-1-5-18' -or (Get-Process -Id $PID).SessionId -eq 0) {
        Write-Status 'XAMPP console restart skipped in Session 0 / SYSTEM; run interactively as administrator.' 'WARN'
        return
    }
    Write-Status "Restarting XAMPP Apache using $stopBatch and $startBatch"
    Invoke-XamppBatch -BatchFile $stopBatch -WaitForExit $true
    $stopped = $false
    for ($i = 0; $i -lt 30; $i++) {
        $remaining = @(Get-TargetApacheProcesses -Target $Target)
        if ($remaining.Count -eq 0) { $stopped = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $stopped) {
        Write-Status 'XAMPP Apache did not stop. Not starting a second instance; restart it manually.' 'WARN'
        return
    }
    Invoke-XamppBatch -BatchFile $startBatch -WaitForExit $false
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $active = @(Get-TargetApacheProcesses -Target $Target)
        if ($active.Count -eq 0) { continue }
        $pids = @($active | ForEach-Object { [int]$_.ProcessId })
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $pids -contains [int]$_.OwningProcess })
        if ($listeners.Count -gt 0) { $ready = $true; break }
    }
    if ($ready) {
        Write-Status "XAMPP Apache restarted and is listening again (PID: $($pids -join ', '))."
    } else {
        Write-Status 'XAMPP restart was requested, but no listening Apache was confirmed. Check XAMPP Control Panel.' 'WARN'
    }
}

function Restart-ApacheWhenPossible {
    param([psobject]$Target)
    if ($SkipApacheRestart) {
        Write-Status 'Apache restart skipped by -SkipApacheRestart. Reload the server manually.'
        return
    }
    if ($Target.ServiceName -and $Target.ServiceRunning) {
        Write-Status "Reloading running Apache service: $($Target.ServiceName)"
        $result = Invoke-ApacheCommand -Target $Target -Arguments @('-k','restart','-n',$Target.ServiceName)
        if ($result.ExitCode -ne 0) {
            throw "Apache reload failed (exit=$($result.ExitCode)): $($result.Output)"
        }
        Write-Status 'Apache service restarted with updated configuration.'
    } elseif ($Target.ServiceName) {
        Write-Status "Apache service $($Target.ServiceName) is stopped. Rule will become active on its next start."
    } else {
        Restart-XamppApache -Target $Target
    }
}

function Apply-ApacheRule {
    param([psobject]$Target, [ValidateSet('Install','Status','Remove')][string]$Mode)
    $managed = Get-ApacheManagedPaths -Target $Target
    $configText = Read-ApacheConfigRaw -Path $Target.Config
    $ownInclude = $configText.Contains($managed.BeginMarker) -and $configText.Contains($managed.EndMarker)
    $snippetExists = Test-Path -LiteralPath $managed.Snippet -PathType Leaf
    $ownSnippet = $false
    if ($snippetExists) {
        $snippetContents = [System.IO.File]::ReadAllText($managed.Snippet)
        # Recognize previously deployed managed snippets by their exact rule body,
        # even when their first-line management label used an older folder name.
        # This prevents duplicate includes and supports clean upgrades/removal.
        $expectedSnippet = New-ApacheSnippet -Target $Target
        $expectedBodyStart = $expectedSnippet.IndexOf("`n")
        $actualBodyStart = $snippetContents.IndexOf("`n")
        if ($expectedBodyStart -ge 0 -and $actualBodyStart -ge 0 -and
            $snippetContents.StartsWith('# Managed by Babylon-') -and
            $snippetContents.Substring($actualBodyStart + 1) -ceq
                $expectedSnippet.Substring($expectedBodyStart + 1)) {
            $ownSnippet = $true
        }
    }
    Write-Status "Apache: $($Target.Exe); config=$($Target.Config); service=$($Target.ServiceName)"
    if ($Mode -eq 'Status') {
        Write-Status "Apache managed include=$ownInclude; managed snippet=$ownSnippet; service running=$($Target.ServiceRunning)"
        return
    }
    if ($Mode -eq 'Remove' -and -not $ownInclude) {
        Write-Status 'Apache Babylon rule is not registered; nothing to remove.'
        return
    }
    if ($Mode -eq 'Install' -and $ownInclude -and $ownSnippet) {
        $currentSnippet = [System.IO.File]::ReadAllText($managed.Snippet)
        if ($currentSnippet -eq (New-ApacheSnippet -Target $Target)) {
            Write-Status 'Apache Babylon rule already installed; leaving configuration unchanged.'
            if (Test-ApacheNeedsReload -Target $Target -Snippet $managed.Snippet) {
                Write-Status 'Apache configuration is newer than its running process; reloading it now.'
                Restart-ApacheWhenPossible -Target $Target
            } else {
                Write-Status 'Apache configuration appears loaded; no restart needed.'
            }
            return
        }
        Write-Status 'Apache Babylon snippet requires an update.'
    }
    if ($snippetExists -and -not $ownSnippet) {
        throw "Apache config snippet already exists and is not ours: $($managed.Snippet)"
    }
    if ($Mode -eq 'Install') {
        $version = Invoke-ApacheCommand -Target $Target -Arguments @('-v')
        if ($version.ExitCode -ne 0 -or $version.Output -notmatch 'Apache/2\.4\.') {
            throw 'Only Apache HTTP Server 2.4 is supported. No changes were made.'
        }
    }
    if (-not (Test-ApacheSyntax -Target $Target)) {
        throw 'The existing Apache configuration has syntax errors; no changes were made.'
    }
    $originalBytes = [IO.File]::ReadAllBytes($Target.Config)
    $oldSnippet = $null
    if ($snippetExists) { $oldSnippet = [IO.File]::ReadAllBytes($managed.Snippet) }
    $backupPath = $Target.Config + '.BabylonBackup.' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff')
    [IO.File]::Copy($Target.Config, $backupPath, $false)
    Write-Status "Apache config backup: $backupPath"
    try {
        if ($Mode -eq 'Install') {
            $newSnippet = New-ApacheSnippet -Target $Target
            [IO.File]::WriteAllText($managed.Snippet, $newSnippet, [Text.Encoding]::ASCII)
            if (-not $ownInclude) {
                $prefix = if ($configText.EndsWith("`n")) { '' } else { "`r`n" }
                $configText += $prefix + $managed.IncludeBlock + "`r`n"
                Write-ApacheConfigRaw -Path $Target.Config -Content $configText
            }
        } else {
            $pattern = '(?ms)^# BEGIN Babylon12-Localhost-Compatibility\r?\n.*?^# END Babylon12-Localhost-Compatibility\r?\n?'
            $configText = [regex]::Replace($configText, $pattern, '', [Text.RegularExpressions.RegexOptions]::Multiline)
            Write-ApacheConfigRaw -Path $Target.Config -Content $configText
            if ($ownSnippet) { [IO.File]::Delete($managed.Snippet) }
        }
        if (-not (Test-ApacheSyntax -Target $Target)) {
            throw 'Modified Apache configuration failed syntax validation.'
        }
        Restart-ApacheWhenPossible -Target $Target
        Write-Status "Apache Babylon rule: $Mode completed."
    } catch {
        # Restore exact bytes rather than writing through a potentially lossy
        # text encoding; keep the backup file for administrator reference.
        [IO.File]::WriteAllBytes($Target.Config, $originalBytes)
        if ($null -ne $oldSnippet) {
            [IO.File]::WriteAllBytes($managed.Snippet, $oldSnippet)
        } elseif (Test-Path -LiteralPath $managed.Snippet) {
            [IO.File]::Delete($managed.Snippet)
        }
        Write-Status 'Apache configuration restored after failed modification.' 'WARN'
        try { Restart-ApacheWhenPossible -Target $Target } catch { }
        throw
    }
}

function Test-IisManagementToolsPresent {
    return (@(Get-Module -ListAvailable -Name 'WebAdministration').Count -gt 0)
}

function Ensure-IisManagementTools {
    if (Test-IisManagementToolsPresent) { return $true }
    Write-Status 'Enabling IIS Management Scripting Tools.'
    $feature = Get-WindowsOptionalFeature -Online -FeatureName 'IIS-ManagementScriptingTools' -ErrorAction Stop
    if ([string]$feature.State -match 'Pending') {
        Write-Status 'IIS management feature installation is pending reboot. Rerun after restarting Windows.' 'WARN'
        return $false
    }
    if ([string]$feature.State -ne 'Enabled') {
        $result = Enable-WindowsOptionalFeature -Online -FeatureName 'IIS-ManagementScriptingTools' -All -NoRestart -ErrorAction Stop
        if ($result.RestartNeeded) {
            Write-Status 'Reboot required to complete IIS Management Scripting Tools installation.' 'WARN'
            return $false
        }
    }
    if (-not (Test-IisManagementToolsPresent)) {
        Write-Status 'IIS scripting tools not yet available; reboot Windows and rerun.' 'WARN'
        return $false
    }
    return $true
}

function Get-RewriteModulePresent {
    Import-Module WebAdministration -ErrorAction Stop
    return (@(Get-WebGlobalModule | Where-Object { $_.Name -eq 'RewriteModule' }).Count -gt 0)
}

function Ensure-UrlRewrite {
    if (Get-RewriteModulePresent) {
        Write-Status 'URL Rewrite module already installed; skipping MSI.'
        return
    }

    $package = $MsiPath
    $downloaded = $false
    if ([string]::IsNullOrWhiteSpace($package)) {
        $besideScript = Join-Path $PSScriptRoot 'rewrite_amd64_en-US.msi'
        if (Test-Path -LiteralPath $besideScript -PathType Leaf) {
            $package = $besideScript
            Write-Status 'Using MSI next to the PS1.'
        } else {
            $package = Join-Path $env:TEMP 'rewrite_amd64_en-US.msi'
            Write-Status 'Downloading Microsoft URL Rewrite 2.1 x64 MSI.'
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $OfficialMsiUrl -UseBasicParsing -OutFile $package -ErrorAction Stop
            $downloaded = $true
        }
    }
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "MSI not found: $package"
    }
    $package = (Resolve-Path -LiteralPath $package -ErrorAction Stop).ProviderPath

    # Never install an unsigned or incorrectly signed MSI in the lab.
    $sig = Get-AuthenticodeSignature -LiteralPath $package
    if ($sig.Status -ne 'Valid' -or $null -eq $sig.SignerCertificate -or
        $sig.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "The Rewrite MSI signature is not valid Microsoft Authenticode: $package; status=$($sig.Status)"
    }
    Write-Status "Installing signed URL Rewrite MSI: $package"
    $arguments = '/i "' + $package + '" /qn /norestart'
    $installer = Start-Process -FilePath (Join-Path $env:windir 'System32\msiexec.exe') -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -notin @(0,3010)) {
        throw "URL Rewrite MSI installation failed. Exit code: $($installer.ExitCode)"
    }
    Write-Status "MSI completed: exit code $($installer.ExitCode)"
    if ($installer.ExitCode -eq 3010) {
        Write-Status 'MSI requested a reboot. Reboot, then run the PS1 again.' 'WARN'
    }
    if (-not (Get-RewriteModulePresent)) {
        throw 'RewriteModule not registered after MSI. Reboot and run the script again.'
    }
    if ($downloaded) {
        try { Remove-Item -LiteralPath $package -Force -ErrorAction Stop }
        catch { Write-Status "Downloaded MSI left at $package" 'WARN' }
    }
}

function New-IisBackup {
    $appCmdPath = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
    if (-not (Test-Path -LiteralPath $appCmdPath -PathType Leaf)) {
        throw "IIS appcmd.exe missing: $appCmdPath"
    }
    $name = 'BeforeBabylonRewrite_' + (Get-Date -Format 'yyyyMMdd_HHmmss_fff')
    $output = & $appCmdPath add backup $name 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "IIS backup failed: $($output -join ' ')"
    }
    Write-Status "IIS backup created: $name"
}

function Apply-IisRule {
    param([ValidateSet('Install','Status','Remove')][string]$Mode)

    if (-not (Get-RewriteModulePresent)) {
        if ($Mode -eq 'Status') {
            Write-Status 'RewriteModule: missing' 'WARN'
            return
        }
        throw 'RewriteModule is missing. Install cannot continue.'
    }
    $assembly = Join-Path $env:windir 'System32\inetsrv\Microsoft.Web.Administration.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "IIS management assembly not found: $assembly"
    }
    Add-Type -Path $assembly

    $manager = $null
    try {
        $manager = New-Object Microsoft.Web.Administration.ServerManager
        $site = $manager.Sites[$SiteName]
        if ($null -eq $site) {
            throw "IIS site not found: $SiteName"
        }
        Write-Status "IIS site: $SiteName; state=$($site.State)"
        foreach ($binding in $site.Bindings) {
            Write-Status "Binding: $($binding.Protocol) $($binding.BindingInformation)"
        }

        # Keep the section path and the tested rule exactly as in working IIS v3.
        $appHost = $manager.GetApplicationHostConfiguration()
        $section = $appHost.GetSection('system.webServer/rewrite/rules', $SiteName)
        $rules = $section.GetCollection()
        $existing = $null
        foreach ($item in $rules) {
            if ([string]$item.GetAttributeValue('name') -eq $RuleName) {
                $existing = $item
                break
            }
        }

        if ($Mode -eq 'Status') {
            if ($null -eq $existing) {
                Write-Status "Rewrite rule: absent ($RuleName)"
            } else {
                Write-Status "Rewrite rule: present; enabled=$($existing.GetAttributeValue('enabled')); action=$($existing.GetChildElement('action').GetAttributeValue('type'))"
            }
            return
        }
        if ($Mode -eq 'Remove') {
            if ($null -eq $existing) {
                Write-Status 'Rewrite rule is already absent.'
                return
            }
            New-IisBackup
            $rules.Remove($existing)
            $manager.CommitChanges()
            Write-Status "REMOVED rule: $RuleName"
            return
        }
        if ($null -ne $existing) {
            # Preserve the rule from the successful v3 deployment unchanged.
            Write-Status "Rewrite rule already present: $RuleName. Leaving it unchanged."
            return
        }

        New-IisBackup
        $newRule = $rules.CreateElement('rule')
        $newRule.SetAttributeValue('name', $RuleName)
        $newRule.SetAttributeValue('enabled', $true)
        $newRule.SetAttributeValue('patternSyntax', 'ECMAScript')
        $newRule.SetAttributeValue('stopProcessing', $true)
        $newRule.GetChildElement('match').SetAttributeValue('url', '^$')
        $conditions = $newRule.GetChildElement('conditions')
        $conditions.SetAttributeValue('logicalGrouping', 'MatchAll')
        $conditionCollection = $conditions.GetCollection()
        $hostCondition = $conditionCollection.CreateElement('add')
        $hostCondition.SetAttributeValue('input', '{HTTP_HOST}')
        $hostCondition.SetAttributeValue('pattern', $HostPattern)
        $conditionCollection.Add($hostCondition) | Out-Null
        $queryCondition = $conditionCollection.CreateElement('add')
        $queryCondition.SetAttributeValue('input', '{QUERY_STRING}')
        $queryCondition.SetAttributeValue('pattern', $QueryPattern)
        $conditionCollection.Add($queryCondition) | Out-Null
        $newRule.GetChildElement('action').SetAttributeValue('type', 'AbortRequest')
        $rules.AddAt(0, $newRule) | Out-Null
        $manager.CommitChanges()
        Write-Status "INSTALLED rule: $RuleName"
    } finally {
        if ($null -ne $manager) { $manager.Dispose() }
    }
}

function Register-UserCacheCleanup {
    # Active Setup runs once for EACH profile at its NEXT interactive Explorer logon.
    # It runs in the actual USER context; administrator cannot call this per-user
    # native WinINet API for offline/unrelated profiles in one elevated session.
    $cmd = 'rundll32.exe InetCpl.cpl,ClearMyTracksByProcess 8'
    if (-not (Test-Path -LiteralPath $ActiveSetupPath)) {
        $null = New-Item -Path $ActiveSetupPath -Force
    }
    New-ItemProperty -Path $ActiveSetupPath -Name 'Version' -PropertyType String -Value $CacheVersion -Force | Out-Null
    New-ItemProperty -Path $ActiveSetupPath -Name 'StubPath' -PropertyType String -Value $cmd -Force | Out-Null
    New-ItemProperty -Path $ActiveSetupPath -Name 'IsInstalled' -PropertyType DWord -Value 1 -Force | Out-Null
    Write-Status "Per-user native WinINet cleanup registered for NEXT logon; Active Setup version=$CacheVersion"
}

function Remove-UserCacheCleanupRegistration {
    # Clean up only the registration made by this script and its older version.
    # This ensures the default mode does not leave a deferred user cache action.
    if (Test-Path -LiteralPath $ActiveSetupPath) {
        Remove-Item -LiteralPath $ActiveSetupPath -Recurse -Force -ErrorAction Stop
        Write-Status 'Removed previous next-logon cache-cleanup registration.'
    } else {
        Write-Status 'Next-logon cache cleanup is disabled (default).'
    }
}

function Set-UserCacheCleanupRegistration {
    if ($CleanupAtNextLogon) {
        Register-UserCacheCleanup
    } else {
        Remove-UserCacheCleanupRegistration
    }
}

function Invoke-CurrentTokenCacheCleanup {
    # Intentionally runs only in the current token's HKCU (admin/SYSTEM).
    $exe = Join-Path $env:windir 'System32\rundll32.exe'
    try {
        $p = Start-Process -FilePath $exe -ArgumentList 'InetCpl.cpl,ClearMyTracksByProcess 8' -Wait -PassThru
        Write-Status "Native cache cleanup executed under $([Security.Principal.WindowsIdentity]::GetCurrent().Name); process exit=$($p.ExitCode)"
    } catch {
        Write-Status "Native cleanup in current token failed: $($_.Exception.Message)" 'WARN'
    }
}

function Remove-FilesFromCacheDirectory {
    param([string]$CacheDirectory)
    $deleted = 0
    $failed = 0
    $skippedReparse = 0
    $examples = 0
    if (-not (Test-Path -LiteralPath $CacheDirectory -PathType Container)) {
        return [pscustomobject]@{ Deleted=0; Failed=0; Reparse=0 }
    }

    # Avoid junction traversal and *never* remove WebCache/INetCookies databases.
    $stack = New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($CacheDirectory)
    while ($stack.Count -gt 0) {
        $folder = $stack.Pop()
        try {
            $di = New-Object System.IO.DirectoryInfo($folder)
            if (($di.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                $skippedReparse++
                continue
            }
            $children = @($di.GetFileSystemInfos())
        } catch {
            $failed++
            continue
        }
        foreach ($child in $children) {
            try {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    $skippedReparse++
                    continue
                }
                if ($child -is [System.IO.DirectoryInfo]) {
                    $stack.Push($child.FullName)
                } else {
                    Remove-Item -LiteralPath $child.FullName -Force -ErrorAction Stop
                    $deleted++
                }
            } catch {
                $failed++
                if ($examples -lt 3) {
                    Write-Status "Cache file could not be removed (in use/permission): $($child.FullName)" 'WARN'
                    $examples++
                }
            }
        }
    }
    return [pscustomobject]@{ Deleted=$deleted; Failed=$failed; Reparse=$skippedReparse }
}

function Clear-AllLocalUserCaches {
    Write-Status 'Enumerating all local, non-special user profiles via Win32_UserProfile.'
    $serviceProfilesRoot = (Join-Path $env:WINDIR 'ServiceProfiles').TrimEnd('\') + '\'
    $profiles = @(Get-CimInstance -ClassName Win32_UserProfile -ErrorAction Stop |
        Where-Object {
            $profilePath = [string]$_.LocalPath
            (-not $_.Special) -and
            (-not [string]::IsNullOrWhiteSpace($profilePath)) -and
            (-not $profilePath.StartsWith($serviceProfilesRoot, [StringComparison]::OrdinalIgnoreCase))
        } | Sort-Object LocalPath -Unique)

    $paths = @(
        'AppData\Local\Microsoft\Windows\INetCache\IE',
        'AppData\Local\Microsoft\Windows\INetCache\Low\IE',
        'AppData\Local\Microsoft\Windows\INetCache\Content.IE5',
        'AppData\Local\Microsoft\Windows\INetCache\Low\Content.IE5',
        'AppData\Local\Microsoft\Windows\Temporary Internet Files\Content.IE5'
    )
    $totalDeleted = 0
    $totalFailed = 0
    $totalUsers = 0
    foreach ($profile in $profiles) {
        $profilePath = [string]$profile.LocalPath
        if ($profilePath -notmatch '^[a-zA-Z]:\\' -or -not (Test-Path -LiteralPath $profilePath -PathType Container)) {
            Write-Status "Skipped missing/non-local user profile: $profilePath" 'WARN'
            continue
        }
        $userRemoved = 0
        $userFailed = 0
        foreach ($relativePath in $paths) {
            $cache = Join-Path $profilePath $relativePath
            $stats = Remove-FilesFromCacheDirectory -CacheDirectory $cache
            $userRemoved += $stats.Deleted
            $userFailed += $stats.Failed
        }
        $totalUsers++
        $totalDeleted += $userRemoved
        $totalFailed += $userFailed
        Write-Status "Profile $profilePath; loaded=$($profile.Loaded); removed=$userRemoved; failures=$userFailed"
    }
    Write-Status "CACHE SUMMARY: profiles=$totalUsers; files_removed=$totalDeleted; failures=$totalFailed"
    if ($totalFailed -gt 0) {
        if ($CleanupAtNextLogon) {
            Write-Status 'Some cache files were locked; native per-user cleanup is registered for next logon.' 'WARN'
        } else {
            Write-Status 'Some cache files were locked. Next-logon cleanup is disabled by default.' 'WARN'
        }
    }
}

# Restart Babylon only if it was already running. A temporary on-demand task
# launches Babylon in the original interactive user's context (not elevated
# through the administrator's or SYSTEM's token). It is removed after launch.
# If task registration is unavailable, the original process is NOT stopped.
function Get-BabylonAtStart {
    $found = New-Object 'System.Collections.Generic.List[object]'
    try { $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Babylon.exe'" -ErrorAction Stop) }
    catch {
        Write-Status "Cannot inspect Babylon processes: $($_.Exception.Message)" 'WARN'
        return @()
    }
    foreach ($process in $processes) {
        $path = [string]$process.ExecutablePath
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf) -or
            [int]$process.SessionId -le 0) { continue }
        try {
            $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid -ErrorAction Stop
            if ($owner.ReturnValue -ne 0 -or [string]::IsNullOrWhiteSpace($owner.Sid)) { continue }
            $found.Add([pscustomobject]@{
                Id = [int]$process.ProcessId
                Session = [int]$process.SessionId
                UserSid = [string]$owner.Sid
                Path = $path
            })
        } catch {
            Write-Status "Cannot identify owner of Babylon PID $($process.ProcessId); leaving it untouched." 'WARN'
        }
    }
    return @($found.ToArray())
}

function Prepare-BabylonRestarts {
    param([object[]]$Snapshots)
    $prepared = New-Object 'System.Collections.Generic.List[object]'
    if (-not $Snapshots -or $Snapshots.Count -eq 0) { return @() }
    try {
        $scheduler = New-Object -ComObject 'Schedule.Service' -ErrorAction Stop
        $scheduler.Connect()
        $folder = $scheduler.GetFolder('\')
    } catch {
        Write-Status "Task Scheduler unavailable; Babylon will remain running: $($_.Exception.Message)" 'WARN'
        return @()
    }
    foreach ($group in @($Snapshots | Group-Object UserSid)) {
        $sessions = @($group.Group | Select-Object -ExpandProperty Session -Unique)
        $paths = @($group.Group | Select-Object -ExpandProperty Path | Sort-Object -Unique)
        if ($sessions.Count -ne 1 -or $paths.Count -ne 1) {
            Write-Status "Babylon has multiple sessions/paths for user SID $($group.Name); skipping restart for safety." 'WARN'
            continue
        }
        $exe = [string]$paths[0]
        $taskName = 'Babylon-Restart-' + [guid]::NewGuid().ToString('N')
        try {
            $definition = $scheduler.NewTask(0)
            $definition.RegistrationInfo.Description = 'Temporary Babylon restart after IIS/Apache compatibility update'
            $definition.Settings.Enabled = $true
            $definition.Settings.AllowDemandStart = $true
            $definition.Settings.Hidden = $true
            $definition.Settings.DisallowStartIfOnBatteries = $false
            $definition.Settings.StopIfGoingOnBatteries = $false
            $definition.Settings.ExecutionTimeLimit = 'PT0S'
            $definition.Principal.UserId = [string]$group.Name
            $definition.Principal.LogonType = 3   # TASK_LOGON_INTERACTIVE_TOKEN
            $definition.Principal.RunLevel = 0    # TASK_RUNLEVEL_LUA
            $action = $definition.Actions.Create(0) # TASK_ACTION_EXEC
            $action.Path = $exe
            $action.WorkingDirectory = Split-Path -Parent $exe
            $registered = $folder.RegisterTaskDefinition($taskName, $definition, 6, [string]$group.Name, $null, 3, $null)
            $prepared.Add([pscustomobject]@{
                TaskName = $taskName
                Task = $registered
                Folder = $folder
                Exe = $exe
                UserSid = [string]$group.Name
                Session = [int]$sessions[0]
                ProcessIds = @($group.Group | Select-Object -ExpandProperty Id)
            })
            Write-Status "Babylon was running; prepared restart for session $($sessions[0])."
        } catch {
            Write-Status "Unable to prepare interactive Babylon restart; leaving process running: $($_.Exception.Message)" 'WARN'
            try { $folder.DeleteTask($taskName, 0) } catch { }
        }
    }
    return @($prepared.ToArray())
}

function Stop-BabylonForCacheCleanup {
    param([object[]]$Plans)
    foreach ($plan in $Plans) {
        foreach ($pidToStop in $plan.ProcessIds) {
            $proc = Get-Process -Id $pidToStop -ErrorAction SilentlyContinue
            if (-not $proc) { continue }
            try {
                $path = $proc.Path
                if (-not [string]::Equals($path, $plan.Exe, [StringComparison]::OrdinalIgnoreCase) -or
                    $proc.SessionId -ne $plan.Session) { continue }
                if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
                    $null = $proc.CloseMainWindow()
                    $null = $proc.WaitForExit(2500)
                }
                $proc.Refresh()
                if (-not $proc.HasExited) {
                    Stop-Process -Id $pidToStop -Force -ErrorAction Stop
                    Write-Status "Stopped Babylon PID $pidToStop for cache cleanup."
                }
            } catch {
                Write-Status "Unable to close Babylon PID $pidToStop: $($_.Exception.Message)" 'WARN'
            }
        }
    }
}

function Test-BabylonRunningForPlan {
    param([psobject]$Plan)
    # Check the *whole* user session, not only old PIDs: Babylon can relaunch
    # itself through a helper before the scheduled restart is requested.
    try {
        $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Babylon.exe'" -ErrorAction Stop)
        foreach ($process in $processes) {
            if ([int]$process.SessionId -ne $Plan.Session -or
                -not [string]::Equals([string]$process.ExecutablePath, $Plan.Exe, [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwnerSid -ErrorAction Stop
            if ($owner.ReturnValue -eq 0 -and [string]::Equals([string]$owner.Sid, $Plan.UserSid, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        return $false
    } catch {
        Write-Status "Unable to confirm Babylon session status: $($_.Exception.Message)" 'WARN'
        return $null # Unknown: never assume that launch succeeded or is safe.
    }
}

function Complete-BabylonRestarts {
    param([object[]]$Plans)
    foreach ($plan in $Plans) {
        try {
            $before = Test-BabylonRunningForPlan -Plan $plan
            if ($null -eq $before) {
                Write-Status 'Could not check for an existing Babylon instance; skipping restart safely.' 'WARN'
                continue
            }
            if ($before) {
                Write-Status 'Babylon is already running in the original user session; avoiding duplicate launch.'
                continue
            }
            $null = $plan.Task.Run($null)
            $started = $false
            for ($attempt = 0; $attempt -lt 12; $attempt++) {
                Start-Sleep -Milliseconds 500
                $after = Test-BabylonRunningForPlan -Plan $plan
                if ($null -eq $after) { break }
                if ($after) { $started = $true; break }
            }
            if ($started) {
                Write-Status "Babylon restarted in interactive user session $($plan.Session)."
            } else {
                Write-Status 'Babylon restart requested, but its new process was not confirmed.' 'WARN'
            }
        } catch {
            Write-Status "Babylon restart failed: $($_.Exception.Message)" 'WARN'
        } finally {
            # Delete only the uniquely named temporary task; deletion does not
            # terminate an application that was already launched by that task.
            try { $plan.Folder.DeleteTask($plan.TaskName, 0) }
            catch { Write-Status "Could not remove temporary Babylon restart task $($plan.TaskName): $($_.Exception.Message)" 'WARN' }
        }
    }
}

function Invoke-BabylonAwareCacheCleanup {
    param([bool]$DoCache = $true)
    $plans = @()
    if (-not $SkipBabylonRestart -and $script:BabylonAtStart.Count -gt 0) {
        $plans = @(Prepare-BabylonRestarts -Snapshots $script:BabylonAtStart)
    }
    try {
        if ($plans.Count -gt 0) { Stop-BabylonForCacheCleanup -Plans $plans }
        if ($DoCache) {
            Set-UserCacheCleanupRegistration
            Invoke-CurrentTokenCacheCleanup
            Clear-AllLocalUserCaches
        }
    } finally {
        if ($plans.Count -gt 0) { Complete-BabylonRestarts -Plans $plans }
    }
}

try {
    Assert-Environment
    $iisInstalled = Test-IisWebServerInstalled
    $apacheTarget = Get-ApacheTarget
    if (-not $iisInstalled -and $null -eq $apacheTarget) {
        Write-Status 'Neither IIS nor Apache was found. Nothing to do, including cache cleanup.'
        return
    }
    Initialize-Logging
    Write-Status "BEGIN: Action=$Action; Computer=$env:COMPUTERNAME"
    if ($Action -in @('Install','CacheOnly') -and -not $SkipBabylonRestart) {
        $script:BabylonAtStart = @(Get-BabylonAtStart)
    }
    if ($script:LoggingEnabled) {
        Write-Status "Logging enabled: $script:EffectiveLogPath"
    }
    if ($Action -eq 'Status') {
        if ($iisInstalled) {
            if (Test-IisManagementToolsPresent) {
                Apply-IisRule -Mode Status
            } else {
                Write-Status 'IIS management scripting tools missing.' 'WARN'
            }
        } else { Write-Status 'IIS is not installed.' }
        if ($null -ne $apacheTarget) { Apply-ApacheRule -Target $apacheTarget -Mode Status }
        if (Test-Path -LiteralPath $ActiveSetupPath) {
            $active = Get-ItemProperty -LiteralPath $ActiveSetupPath
            Write-Status "Active Setup cache cleaner: present; version=$($active.Version)"
        } else { Write-Status 'Active Setup cache cleaner: absent' }
    } elseif ($Action -eq 'RestartApache') {
        if ($null -ne $apacheTarget) { Restart-ApacheWhenPossible -Target $apacheTarget }
        else { Write-Status 'Apache is not installed; nothing to restart.' }
    } elseif ($Action -eq 'Remove') {
        if ($iisInstalled) {
            if (Test-IisManagementToolsPresent) {
                if (Get-RewriteModulePresent) { Apply-IisRule -Mode Remove }
            } else { Write-Status 'IIS management scripting tools missing; IIS rule not removed.' 'WARN' }
        }
        if ($null -ne $apacheTarget) { Apply-ApacheRule -Target $apacheTarget -Mode Remove }
        Remove-UserCacheCleanupRegistration
    } elseif ($Action -eq 'CacheOnly') {
        Invoke-BabylonAwareCacheCleanup
    } else {
        $configured = $false
        $iisPending = $false
        if ($iisInstalled) {
            $iisReady = Ensure-IisManagementTools
            if ($iisReady) {
                Ensure-UrlRewrite
                Apply-IisRule -Mode Install
                $configured = $true
            } else {
                $iisPending = $true
                Write-Status 'IIS management tools not ready. Reboot if needed, then rerun.' 'WARN'
            }
        }
        if ($null -ne $apacheTarget) {
            Apply-ApacheRule -Target $apacheTarget -Mode Install
            $configured = $true
        }
        if ($configured) {
            if ($SkipCache) { Write-Status 'SkipCache requested; no user cache files were removed.' }
            Invoke-BabylonAwareCacheCleanup -DoCache:(-not $SkipCache)
        } elseif ($iisPending) {
            Write-Status 'No web-server rule was installed; cache cleanup skipped.' 'WARN'
        }
    }
    Write-Status 'DONE. Check Babylon translation and the web server on this PC.'
} catch {
    Write-Status "FAILED: $($_.Exception.Message)" 'ERROR'
    Write-Status "ERROR AT: $($_.InvocationInfo.PositionMessage)" 'ERROR'
    exit 1
}
