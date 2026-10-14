# Starts MySQL and Redis without Docker.
# Set MYSQL_HOME to the folder that contains mysqld.exe.
# Set REDIS_HOME to the folder that contains redis-server.exe.
# Safe to run again: if a port is already open, that program is left alone.

$ErrorActionPreference = 'Stop'

function Test-Port([int] $Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($env:REDIS_HOME) -or [string]::IsNullOrWhiteSpace($env:MYSQL_HOME)) {
    throw 'Set REDIS_HOME and MYSQL_HOME before running this script.'
}

$redisDir = $env:REDIS_HOME
$redisExe = Join-Path $redisDir 'redis-server.exe'
if (-not (Test-Path $redisExe)) {
    throw "redis-server.exe was not found in REDIS_HOME."
}

if (Test-Port 6379) {
    Write-Host 'Redis is already running on port 6379.'
} else {
    Start-Process -FilePath $redisExe -ArgumentList @(
        '--bind', '127.0.0.1',
        '--port', '6379',
        '--dir', $redisDir,
        '--dbfilename', 'dump.rdb'
    ) -WindowStyle Hidden
    Write-Host 'Started Redis.'
}

$mysqld = Get-ChildItem $env:MYSQL_HOME -Recurse -Filter 'mysqld.exe' | Select-Object -First 1
if (-not $mysqld) {
    throw 'mysqld.exe was not found in MYSQL_HOME.'
}

$baseDir = $mysqld.Directory.Parent.FullName
$dataDir = Join-Path $env:MYSQL_HOME 'data'
$iniPath = Join-Path $env:MYSQL_HOME 'my.ini'

@"
[mysqld]
basedir=$($baseDir -replace '\\','/')
datadir=$($dataDir -replace '\\','/')
port=3306
bind-address=127.0.0.1
mysqlx=0
"@ | Set-Content -Path $iniPath -Encoding ascii

if (-not (Test-Path (Join-Path $dataDir 'mysql'))) {
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    Write-Host 'Preparing MySQL data folder. This takes a minute.'
    & $mysqld.FullName --defaults-file=$iniPath --initialize-insecure --console
    if ($LASTEXITCODE -ne 0) {
        throw "MySQL initialize failed with exit code $LASTEXITCODE"
    }
}

if (Test-Port 3306) {
    Write-Host 'MySQL is already running on port 3306.'
} else {
    Start-Process -FilePath $mysqld.FullName -ArgumentList "--defaults-file=$iniPath","--console" -WindowStyle Hidden
    Write-Host 'Started MySQL. Waiting for it to accept connections.'
}

$mysql = Join-Path $mysqld.Directory.FullName 'mysql.exe'

function Invoke-Mysql([string[]] $MysqlArgs) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $mysql @MysqlArgs 2>&1 | Out-Null
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    if ((Invoke-Mysql @('--protocol=TCP', '-h', '127.0.0.1', '-P', '3306', '-u', 'root', '--password=root', '-e', 'SELECT 1')) -eq 0) {
        $ready = $true
        break
    }
    if ((Invoke-Mysql @('--protocol=TCP', '-h', '127.0.0.1', '-P', '3306', '-u', 'root', '-e', "ALTER USER 'root'@'localhost' IDENTIFIED BY 'root'; CREATE DATABASE IF NOT EXISTS payment; CREATE DATABASE IF NOT EXISTS payment_test;")) -eq 0) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 2
}

if (-not $ready) {
    throw 'MySQL did not become ready. Look for mysqld in Task Manager and try again.'
}

if ((Invoke-Mysql @('--protocol=TCP', '-h', '127.0.0.1', '-P', '3306', '-u', 'root', '--password=root', '-e', 'CREATE DATABASE IF NOT EXISTS payment; CREATE DATABASE IF NOT EXISTS payment_test;')) -ne 0) {
    throw 'Could not create the payment databases.'
}
Write-Host 'MySQL is ready. Databases: payment, payment_test. User: root. Password: root.'
