param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$providerPath = [IO.Path]::GetFullPath((Join-Path $projectRoot '../../src/Pervaxis.Genesis.Caching.AWS/Providers/ElastiCache/ElastiCacheProvider.cs'))
$originalBytes = [IO.File]::ReadAllBytes($providerPath)
$source = [IO.File]::ReadAllText($providerPath)
$methods = @('GetAsync', 'SetAsync', 'RemoveAsync', 'ExistsAsync', 'GetManyAsync', 'SetManyAsync', 'RefreshAsync')
$results = @()

try {
    foreach ($method in $methods) {
        $pattern = '(?s)(public async Task[^\r\n]*?\b' + $method + '\b.*?)(?=    /// <inheritdoc/>|    private void ValidateOptions)'
        $match = [regex]::Match($source, $pattern)
        if (-not $match.Success) { throw "Cannot locate $method" }
        foreach ($mutation in @('unscoped-key', 'missing-tenant-guard')) {
            $body = $match.Value
            if ($mutation -eq 'missing-tenant-guard') {
                $changed = $body.Replace('EnsureTenantUnchanged();', '')
            } elseif ($method -eq 'GetManyAsync') {
                $changed = $body.Replace('keyList.Select(GetFullKey)', 'keyList.Select(key => key)')
            } elseif ($method -eq 'SetManyAsync') {
                $changed = $body.Replace('GetFullKey(item.Key)', 'item.Key')
            } else {
                $changed = $body.Replace('GetFullKey(key)', 'key')
            }
            if ($changed -eq $body) { throw "Mutation did not change $method" }
            $mutated = $source.Substring(0, $match.Index) + $changed + $source.Substring($match.Index + $match.Length)
            [IO.File]::WriteAllText($providerPath, $mutated)
            # Windows PowerShell treats the expected xUnit stderr as an ErrorRecord.
            $ErrorActionPreference = 'Continue'
            $output = & dotnet test $projectRoot --no-restore --verbosity minimal --filter 'FullyQualifiedName~ProviderIsolationTests' 2>&1
            $code = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            $log = $output -join "`n"
            # A compiler error is not a killed mutant: require an actual failed test run.
            if ($code -ne 1 -or $log -notmatch 'Failed!') {
                Write-Output $log
                throw "SURVIVED or invalid run: $method / $mutation (exit $code)"
            }
            $expectedTest = if ($mutation -eq 'unscoped-key') { 'EveryOperation_UsesResolver' } else { 'EveryOperation_RejectsChangedAmbientTenantBeforeRedis' }
            if ($log -notmatch $expectedTest) {
                Write-Output $log
                throw "Expected assertion did not fail: $method / $mutation"
            }
            $results += "$method / ${mutation}: KILLED"
            Write-Output $results[-1]
        }
    }
} finally {
    [IO.File]::WriteAllBytes($providerPath, $originalBytes)
}

Write-Output "Killed $($results.Count)/14 provider mutations; original source restored. Run the unmodified suite before committing."
