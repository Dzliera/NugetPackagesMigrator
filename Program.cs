using CommandLine;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetPackagesMigrator;

await Parser.Default.ParseArguments<CommandLineOptions>(Environment.GetCommandLineArgs()).WithParsedAsync(async (options) =>
{
    var configuration = Settings.LoadDefaultSettings(Directory.GetCurrentDirectory());
    var packageSourceProvider = new PackageSourceProvider(configuration);


    var nugetSourceToCopyFrom = packageSourceProvider.GetPackageSourceByName(options.NugetSourceNameToCopyFrom);

    if (nugetSourceToCopyFrom == null)
        throw new Exception($"Can't find nuget source with name '{options.NugetSourceNameToCopyFrom}'");


    var nugetSourceToCopyTo = packageSourceProvider.GetPackageSourceByName(options.NugetSourceNameToCopyTo);

    if (nugetSourceToCopyTo == null)
        throw new Exception($"Can't find nuget source with name '{options.NugetSourceNameToCopyTo}'");

    var repositoryToCopyFrom = nugetSourceToCopyFrom.ProtocolVersion switch
    {
        3 => Repository.Factory.GetCoreV3(nugetSourceToCopyFrom),
        2 => Repository.Factory.GetCoreV2(nugetSourceToCopyFrom),
        _ => throw new Exception($"Package source '{nugetSourceToCopyFrom.Name}' has Unsupported Nuget protocol version")
    };

    var repositoryToCopyTo = nugetSourceToCopyTo.ProtocolVersion switch
    {
        3 => Repository.Factory.GetCoreV3(nugetSourceToCopyTo),
        2 => Repository.Factory.GetCoreV2(nugetSourceToCopyTo),
        _ => throw new Exception($"Package source '{nugetSourceToCopyTo.Name}' has Unsupported Nuget protocol version")
    };

    var targetRepositoryPackageLocator = repositoryToCopyTo.GetResource<FindPackageByIdResource>();
    var packageDownloader = repositoryToCopyFrom.GetResource<DownloadResource>();
    var packageUploader = repositoryToCopyTo.GetResource<PackageUpdateResource>();
    var cacheContext = new SourceCacheContext();
    var packageDownloadContext = new PackageDownloadContext(cacheContext);

    var totalPackagesDiscoveredCount = 0;
    var totalVersionsDiscoveredCount = 0;
    
    var totalPackagesToTransferCount = 0;
    var totalVersionsToTransferCount = 0;

    var transferredPackagesCount = 0;
    var transferredVersionsCount = 0;

    var skip = 0;
    const int take = 50;
    
    while (true)
    {
        var packagesList = (await repositoryToCopyFrom.GetResource<PackageSearchResource>().SearchAsync(
            searchTerm: string.Empty,
            filters: new SearchFilter(true),
            skip: skip,
            take: take,
            log: new NullLogger(),
            cancellationToken: CancellationToken.None)).ToArray();

        if (packagesList is null || packagesList.Length == 0)
            break;

        skip += take;

        foreach (var packageSearchMetadata in packagesList)
        {
            var packageId = packageSearchMetadata.Identity.Id;
            Console.WriteLine("Checking Package {0}", packageId);
            totalPackagesDiscoveredCount++;

            var doesPackageExistInTargetRepo = await targetRepositoryPackageLocator.DoesPackageExistAsync(id: packageId,
                version: packageSearchMetadata.Identity.Version,
                cacheContext: cacheContext,
                logger: new NullLogger(),
                cancellationToken: CancellationToken.None);

            HashSet<Version> versionsInTargetRepoHashset = null!;

            if (doesPackageExistInTargetRepo)
            {
                var versionsInTargetRepo = await targetRepositoryPackageLocator.GetAllVersionsAsync(packageId,
                    cacheContext,
                    new NullLogger(),
                    CancellationToken.None);

                versionsInTargetRepoHashset = versionsInTargetRepo.Select(x => x.Version).ToHashSet();
            }
            else
            {
                Console.WriteLine("Package {0} does not exists in target repository, transferring...", packageId);
                totalPackagesToTransferCount++;
            }


            var sourceRepoVersions = await packageSearchMetadata.GetVersionsAsync();
            var currentPackageVersionTransferCount = 0;

            foreach (var packageVersion in sourceRepoVersions)
            {
                totalVersionsDiscoveredCount++;
                var versionId = packageVersion.PackageSearchMetadata.Identity;
                if (doesPackageExistInTargetRepo && versionsInTargetRepoHashset.Contains(packageVersion.Version.Version))
                {
                    continue;
                }

                Console.WriteLine("Transferring package version {0}", versionId);

                totalVersionsToTransferCount++;

                var downloadedPackage = await packageDownloader.GetDownloadResourceResultAsync(
                    new PackageIdentity(packageId, packageVersion.Version),
                    packageDownloadContext,
                    "/packages",
                    new NullLogger(),
                    CancellationToken.None);

                if (downloadedPackage.Status != DownloadResourceResultStatus.Available)
                    continue;

                Console.WriteLine("{0} successfully downloaded, uploading...", versionId);

                var tempFilePath = Path.GetTempFileName();
                try
                {
                    await using (var fileStream = File.Create(tempFilePath))
                    {
                        downloadedPackage.PackageStream.CopyTo(fileStream);
                    }

                    await packageUploader.Push(packagePaths: new List<string> { tempFilePath },
                        symbolSource: null,
                        timeoutInSecond: 120,
                        disableBuffering: false,
                        getApiKey: _ => null,
                        getSymbolApiKey: _ => null,
                        noServiceEndpoint: false,
                        skipDuplicate: true,
                        symbolPackageUpdateResource: null,
                        log: new NullLogger());

                    transferredVersionsCount++;
                    currentPackageVersionTransferCount++;

                    Console.WriteLine("{0} successfully uploaded", versionId);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error While transferring package file. Message: {0}", e.Message);
                }
                finally
                {
                    File.Delete(tempFilePath);
                }
            }

            if (currentPackageVersionTransferCount > 0)
            {
                transferredPackagesCount++;
            }
        }
    }

    var leftToTransferVersionsCount = totalVersionsToTransferCount - transferredVersionsCount;
    var statusText = leftToTransferVersionsCount switch
    {
        > 0 => "There were errors during transferring some package versions, check logs above",
        0 when totalVersionsToTransferCount > 0 => "All packages and versions were migrated successfully",
        0 when totalVersionsToTransferCount is 0 => "Nothing to transfer. All packages and versions are already present in target nuget source",
        _ => throw new InvalidOperationException()
    };
    
    Console.WriteLine(Environment.NewLine);
    Console.WriteLine("Migration Completed: {0}.", statusText);
    Console.WriteLine("Total Packages Discovered: {0}", totalPackagesDiscoveredCount);
    Console.WriteLine("Total Versions Discovered: {0}", totalVersionsDiscoveredCount);
    Console.WriteLine("Total Packages Need To Transfer: {0}", totalPackagesToTransferCount);
    Console.WriteLine("Total Package Versions Need To Transfer: {0}", totalVersionsToTransferCount);
    Console.WriteLine("Transferred Packages: {0}", transferredPackagesCount);
    Console.WriteLine("Transferred Versions: {0}", transferredVersionsCount);
    Console.WriteLine("Versions Left To Transfer: {0}", leftToTransferVersionsCount);
    
}).ContinueWith(t =>
{
    if (t.Exception == null) return;
    Console.WriteLine("Error: {0}", t.Exception.InnerException?.Message);
    Environment.Exit(-1);
});