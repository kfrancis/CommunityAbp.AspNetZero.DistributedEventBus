#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Prints the dependency groups of every .nupkg in a directory and asserts the expected minimum versions.
.DESCRIPTION
    Guards against floating (x.y.*) PackageReference versions silently raising the published dependency floors.
    Fails if any expected dependency is missing, has a different minimum version, or if any dependency uses a wildcard.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# package id -> target framework -> dependency id -> expected minimum version
$expected = @{
    'CommunityAbp.AspNetZero.DistributedEventBus.Core' = @{
        'net10.0' = @{
            'Abp'                                                   = '11.3.0'
            'Microsoft.Extensions.Caching.Memory'                   = '10.0.11'
            'Microsoft.Extensions.Configuration'                    = '10.0.11'
            'Microsoft.Extensions.Configuration.Abstractions'       = '10.0.11'
            'Microsoft.Extensions.Configuration.Binder'             = '10.0.11'
            'Microsoft.Extensions.Configuration.EnvironmentVariables' = '10.0.11'
            'Microsoft.Extensions.Configuration.Json'               = '10.0.11'
            'Microsoft.Extensions.Configuration.UserSecrets'        = '10.0.11'
            'Microsoft.Extensions.DependencyInjection.Abstractions' = '10.0.11'
            'Microsoft.Extensions.Logging.Abstractions'             = '10.0.11'
            'Microsoft.Extensions.Options'                          = '10.0.11'
            'System.Linq.Dynamic.Core'                              = '1.7.3'
        }
        '.NETStandard2.0' = @{
            'Abp'                                                   = '9.0.0'
            'Microsoft.Extensions.Options'                          = '9.0.19'
            'System.Linq.Dynamic.Core'                              = '1.7.3'
        }
    }
    'CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus' = @{
        'net10.0'         = @{ 'Azure.Messaging.ServiceBus' = '7.20.2' }
        '.NETStandard2.0' = @{ 'Azure.Messaging.ServiceBus' = '7.20.2' }
    }
    'CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore' = @{
        'net10.0' = @{
            'Abp.EntityFrameworkCore'                   = '11.3.0'
            'Microsoft.EntityFrameworkCore'             = '10.0.11'
            'Microsoft.EntityFrameworkCore.Relational'  = '10.0.11'
            'Microsoft.EntityFrameworkCore.SqlServer'   = '10.0.11'
            # System.Text.Json is framework-provided on net10.0; NuGet prunes it from the nuspec (NU1510), so it is not asserted here.
        }
    }
}

# Any Microsoft.Extensions.* dependency in these groups must sit exactly on the stated floor.
$prefixFloors = @{
    'net10.0'         = @{ 'Microsoft.Extensions.' = '10.0.11'; 'Microsoft.EntityFrameworkCore' = '10.0.11' }
    '.NETStandard2.0' = @{ 'Microsoft.Extensions.' = '9.0.19' }
}

$packages = Get-ChildItem -Path $PackageDirectory -Filter *.nupkg -Recurse | Where-Object { $_.Name -notlike '*.snupkg' }
if (-not $packages) { throw "No .nupkg files found under '$PackageDirectory'." }

$failures = New-Object System.Collections.Generic.List[string]
$checked = 0

foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $entry) { $failures.Add("$($package.Name): no .nuspec entry found."); continue }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    $metadata = $nuspec.package.metadata
    $packageId = [string] $metadata.id
    Write-Host ""
    Write-Host "== $packageId $($metadata.version)"

    $groups = @($metadata.dependencies.group)
    if ($groups.Count -eq 0) { $failures.Add("${packageId}: nuspec has no dependency groups."); continue }

    foreach ($group in $groups) {
        $tfm = [string] $group.targetFramework
        Write-Host "  [$tfm]"
        $deps = @{}
        foreach ($dependency in @($group.dependency)) {
            if ($null -eq $dependency) { continue }
            $id = [string] $dependency.id
            $version = [string] $dependency.version
            $deps[$id] = $version
            Write-Host ("    {0,-60} {1}" -f $id, $version)
            if ($version -like '*[*]*') { $failures.Add("${packageId} [$tfm]: $id uses a wildcard version '$version'.") }
            if ($prefixFloors.ContainsKey($tfm)) {
                foreach ($prefix in $prefixFloors[$tfm].Keys) {
                    if ($id.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
                        $floor = $prefixFloors[$tfm][$prefix]
                        if ($version -ne $floor) { $failures.Add("${packageId} [$tfm]: $id is '$version', expected floor '$floor'.") }
                    }
                }
            }
        }

        if ($expected.ContainsKey($packageId) -and $expected[$packageId].ContainsKey($tfm)) {
            foreach ($id in $expected[$packageId][$tfm].Keys) {
                $floor = $expected[$packageId][$tfm][$id]
                if (-not $deps.ContainsKey($id)) { $failures.Add("${packageId} [$tfm]: expected dependency '$id' ($floor) is missing."); continue }
                if ($deps[$id] -ne $floor) { $failures.Add("${packageId} [$tfm]: $id is '$($deps[$id])', expected floor '$floor'.") }
                $checked++
            }
        }
    }
}

Write-Host ""
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "::error::$_" }
    throw "Nuspec dependency floor assertion failed with $($failures.Count) problem(s)."
}
Write-Host "Nuspec dependency floors OK ($checked expected dependencies verified across $($packages.Count) package(s))."
