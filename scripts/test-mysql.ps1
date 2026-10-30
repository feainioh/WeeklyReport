param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^weeklyreport_test_[A-Za-z0-9_]+$')]
    [string]$Database,

    [ValidateRange(1, 65535)]
    [int]$Port = 3306,

    [string]$UserName = 'root'
)

$ErrorActionPreference = 'Stop'
$credential = Get-Credential -UserName $UserName -Message 'Enter credentials for the isolated local MySQL test database.'
if ($null -eq $credential) {
    throw 'No test database credentials were provided.'
}

$builder = New-Object System.Data.Common.DbConnectionStringBuilder
$builder['Server'] = '127.0.0.1'
$builder['Port'] = $Port
$builder['Database'] = $Database
$builder['User ID'] = $credential.UserName
$builder['Password'] = $credential.GetNetworkCredential().Password
$previous = $env:WEEKLYREPORT_TEST_CONNECTION

try {
    $env:WEEKLYREPORT_TEST_CONNECTION = $builder.psbase.ConnectionString
    $project = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\WeeklyReport.IntegrationTests\WeeklyReport.IntegrationTests.csproj'
    & dotnet test $project --filter 'Category=MySql' --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw "MySQL integration tests failed (exit code: $LASTEXITCODE)."
    }
}
finally {
    $null = $builder.Remove('Password')
    if ($null -eq $previous) {
        Remove-Item Env:\WEEKLYREPORT_TEST_CONNECTION -ErrorAction SilentlyContinue
    }
    else {
        $env:WEEKLYREPORT_TEST_CONNECTION = $previous
    }
}
