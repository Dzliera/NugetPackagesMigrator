# Nuget Package Source Migration Tool

[![NuGet Version](https://img.shields.io/nuget/v/NugetPackagesMigrator.svg?style=plastic)](https://www.nuget.org/packages/NugetPackagesMigrator)

## Usage :

### 1. Configure Nuget sources (If not already configured).

Both source and target package sources should be configured on machine
where this tool will be run.

### 2. Install [dotnet tool](https://www.nuget.org/packages/NugetPackagesMigrator/)

    dotnet tool install --global NugetPackagesMigrator

### 3. Run Migration

Given we have 2 nuget sources configured,
one named "source-repository" and other "target-repository", run following command:

    dotnet nuget-migrator --from source-repository --to target-repository

## Notes :

May Not work if package source is using custom nuget credential provider plugin