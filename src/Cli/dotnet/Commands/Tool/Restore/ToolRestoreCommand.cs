// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.CommandLine;
using Microsoft.DotNet.Cli.Extensions;
using Microsoft.DotNet.Cli.NuGetPackageDownloader;
using Microsoft.DotNet.Cli.ToolManifest;
using Microsoft.DotNet.Cli.ToolPackage;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Cli.Utils.Extensions;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Microsoft.DotNet.Cli.Commands.Tool.Restore;

internal class ToolRestoreCommand : CommandBase
{
    private readonly string _configFilePath;
    private readonly IReporter _errorReporter;
    private readonly ILocalToolsResolverCache _localToolsResolverCache;
    private readonly IToolManifestFinder _toolManifestFinder;
    private readonly IFileSystem _fileSystem;
    private readonly IReporter _reporter;
    private readonly string[] _sources;
    private readonly IToolPackageDownloader _toolPackageDownloader;
    private readonly VerbosityOptions _verbosity;
    private readonly RestoreActionConfig _restoreActionConfig;

    public ToolRestoreCommand(
        ParseResult result,
        IToolPackageDownloader toolPackageDownloader = null,
        IToolManifestFinder toolManifestFinder = null,
        ILocalToolsResolverCache localToolsResolverCache = null,
        IFileSystem fileSystem = null,
        IReporter reporter = null)
        : base(result)
    {
        if (toolPackageDownloader == null)
        {
            (IToolPackageStore,
                IToolPackageStoreQuery,
                IToolPackageDownloader downloader) toolPackageStoresAndInstaller
                    = ToolPackageFactory.CreateToolPackageStoresAndDownloader(
                        additionalRestoreArguments: result.OptionValuesToBeForwarded(ToolRestoreCommandParser.GetCommand()));
            _toolPackageDownloader = toolPackageStoresAndInstaller.downloader;
        }
        else
        {
            _toolPackageDownloader = toolPackageDownloader;
        }

        _toolManifestFinder
            = toolManifestFinder
              ?? new ToolManifestFinder(new DirectoryPath(Directory.GetCurrentDirectory()));

        _localToolsResolverCache = localToolsResolverCache ?? new LocalToolsResolverCache();
        _fileSystem = fileSystem ?? new FileSystemWrapper();

        _reporter = reporter ?? Reporter.Output;
        _errorReporter = reporter ?? Reporter.Error;

        _configFilePath = result.GetValue(ToolRestoreCommandParser.ConfigOption);
        _sources = result.GetValue(ToolRestoreCommandParser.AddSourceOption);
        _verbosity = result.GetValue(ToolRestoreCommandParser.VerbosityOption);
        if (!result.HasOption(ToolRestoreCommandParser.VerbosityOption) && result.GetValue(ToolCommandRestorePassThroughOptions.InteractiveRestoreOption))
        {
            _verbosity = VerbosityOptions.minimal;
        }

        _restoreActionConfig = new RestoreActionConfig(DisableParallel: result.GetValue(ToolCommandRestorePassThroughOptions.DisableParallelOption),
            NoCache: result.GetValue(ToolCommandRestorePassThroughOptions.NoCacheOption) || result.GetValue(ToolCommandRestorePassThroughOptions.NoHttpCacheOption),
            IgnoreFailedSources: result.GetValue(ToolCommandRestorePassThroughOptions.IgnoreFailedSourcesOption),
            Interactive: result.GetValue(ToolCommandRestorePassThroughOptions.InteractiveRestoreOption));
    }

    public override int Execute()
    {
        FilePath? customManifestFileLocation = GetCustomManifestFileLocation();

        FilePath? configFile = null;
        if (!string.IsNullOrEmpty(_configFilePath))
        {
            configFile = new FilePath(_configFilePath);
        }

        IReadOnlyCollection<ToolManifestPackage> packagesFromManifest;
        try
        {
            packagesFromManifest = _toolManifestFinder.Find(customManifestFileLocation);
        }
        catch (ToolManifestCannotBeFoundException e)
        {
            if (CommandLoggingContext.IsVerbose)
            {
                _reporter.WriteLine(string.Join(Environment.NewLine, e.VerboseMessage).Yellow());
            }

            _reporter.WriteLine(e.Message.Yellow());
            _reporter.WriteLine(CliCommandStrings.NoToolsWereRestored.Yellow());
            return 0;
        }

        ToolRestoreResult[] toolRestoreResults =
            [.. packagesFromManifest
                .AsEnumerable()
                .Select(package => InstallPackages(package, configFile))];

        Dictionary<RestoredCommandIdentifier, ToolCommand> downloaded =
            toolRestoreResults.SelectMany(result => result.SaveToCache)
                .ToDictionary(pair => pair.Item1, pair => pair.Item2);

        EnsureNoCommandNameCollision(downloaded);

        _localToolsResolverCache.Save(downloaded);

        return PrintConclusionAndReturn(toolRestoreResults);
    }

    private ToolRestoreResult InstallPackages(
        ToolManifestPackage package,
        FilePath? configFile)
    {
        string targetFramework = BundledTargetFramework.GetTargetFrameworkMoniker();

        if (PackageHasBeenRestored(package, targetFramework))
        {
            return ToolRestoreResult.Success(
                saveToCache: [],
                message: string.Format(
                    CliCommandStrings.RestoreSuccessful, package.PackageId,
                    package.Version.ToNormalizedString(), string.Join(", ", package.CommandNames)));
        }

        try
        {
            IToolPackage toolPackage =
                _toolPackageDownloader.InstallPackage(
                    new PackageLocation(
                        nugetConfig: configFile,
                        additionalFeeds: _sources,
                        rootConfigDirectory: package.FirstEffectDirectory),
                    package.PackageId,
                    verbosity: _verbosity,
                    ToVersionRangeWithOnlyOneVersion(package.Version),
                    targetFramework,
                    restoreActionConfig: _restoreActionConfig
                    );

            if (!ManifestCommandMatchesActualInPackage(package.CommandNames, [toolPackage.Command]))
            {
                return ToolRestoreResult.Failure(
                    string.Format(CliCommandStrings.CommandsMismatch,
                        JoinBySpaceWithQuote(package.CommandNames.Select(c => c.Value.ToString())),
                        package.PackageId,
                        toolPackage.Command.Name));
            }

            return ToolRestoreResult.Success(
                saveToCache: 
                    [(new RestoredCommandIdentifier(
                        toolPackage.Id,
                        toolPackage.Version,
                        NuGetFramework.Parse(targetFramework),
                        Constants.AnyRid,
                        toolPackage.Command.Name),
                    toolPackage.Command)],
                message: string.Format(
                    CliCommandStrings.RestoreSuccessful,
                    package.PackageId,
                    package.Version.ToNormalizedString(),
                    string.Join(" ", package.CommandNames)));
        }
        catch (ToolPackageException e)
        {
            return ToolRestoreResult.Failure(package.PackageId, e);
        }
    }

    private int PrintConclusionAndReturn(ToolRestoreResult[] toolRestoreResults)
    {
        if (toolRestoreResults.Any(r => !r.IsSuccess))
        {
            _reporter.WriteLine();
            _errorReporter.WriteLine(string.Join(
                Environment.NewLine,
                toolRestoreResults.Where(r => !r.IsSuccess).Select(r => r.Message)).Red());

            var successMessage = toolRestoreResults.Where(r => r.IsSuccess).Select(r => r.Message);
            if (successMessage.Any())
            {
                _reporter.WriteLine();
                _reporter.WriteLine(string.Join(Environment.NewLine, successMessage));

            }

            _errorReporter.WriteLine(Environment.NewLine +
                                     (toolRestoreResults.Any(r => r.IsSuccess)
                                         ? CliCommandStrings.RestorePartiallyFailed
                                         : CliCommandStrings.RestoreFailed).Red());

            return 1;
        }
        else
        {
            _reporter.WriteLine(string.Join(Environment.NewLine,
                toolRestoreResults.Where(r => r.IsSuccess).Select(r => r.Message)));
            _reporter.WriteLine();
            _reporter.WriteLine(CliCommandStrings.LocalToolsRestoreWasSuccessful.Green());

            return 0;
        }
    }

    private static bool ManifestCommandMatchesActualInPackage(
        ToolCommandName[] commandsFromManifest,
        IReadOnlyList<ToolCommand> toolPackageCommands)
    {
        ToolCommandName[] commandsFromPackage = [.. toolPackageCommands.Select(t => t.Name)];
        foreach (var command in commandsFromManifest)
        {
            if (!commandsFromPackage.Contains(command))
            {
                return false;
            }
        }

        foreach (var command in commandsFromPackage)
        {
            if (!commandsFromManifest.Contains(command))
            {
                return false;
            }
        }

        return true;
    }

    private bool PackageHasBeenRestored(
        ToolManifestPackage package,
        string targetFramework)
    {
        var sampleRestoredCommandIdentifierOfThePackage = new RestoredCommandIdentifier(
            package.PackageId,
            package.Version,
            NuGetFramework.Parse(targetFramework),
            Constants.AnyRid,
            package.CommandNames.First());

        return _localToolsResolverCache.TryLoad(
                   sampleRestoredCommandIdentifierOfThePackage,
                   out var toolCommand)
               && _fileSystem.File.Exists(toolCommand.Executable.Value);
    }

    private FilePath? GetCustomManifestFileLocation()
    {
        string customFile = _parseResult.GetValue(ToolRestoreCommandParser.ToolManifestOption);
        FilePath? customManifestFileLocation;
        if (!string.IsNullOrEmpty(customFile))
        {
            customManifestFileLocation = new FilePath(customFile);
        }
        else
        {
            customManifestFileLocation = null;
        }

        return customManifestFileLocation;
    }

    private static void EnsureNoCommandNameCollision(Dictionary<RestoredCommandIdentifier, ToolCommand> dictionary)
    {
        string[] errors = [.. dictionary
            .Select(pair => (pair.Key.PackageId, pair.Key.CommandName))
            .GroupBy(packageIdAndCommandName => packageIdAndCommandName.CommandName)
            .Where(grouped => grouped.Count() > 1)
            .Select(nonUniquePackageIdAndCommandNames =>
                string.Format(CliCommandStrings.PackagesCommandNameCollisionConclusion,
                    string.Join(Environment.NewLine,
                        nonUniquePackageIdAndCommandNames.Select(
                            p => "\t" + string.Format(
                                CliCommandStrings.PackagesCommandNameCollisionForOnePackage,
                                p.CommandName.Value,
                                p.PackageId.ToString())))))];

        if (errors.Any())
        {
            throw new ToolPackageException(string.Join(Environment.NewLine, errors));
        }
    }

    private static string JoinBySpaceWithQuote(IEnumerable<object> objects)
    {
        return string.Join(" ", objects.Select(o => $"\"{o.ToString()}\""));
    }

    private static VersionRange ToVersionRangeWithOnlyOneVersion(NuGetVersion version)
    {
        return new VersionRange(
            version,
            includeMinVersion: true,
            maxVersion: version,
            includeMaxVersion: true);
    }

    private struct ToolRestoreResult
    {
        public (RestoredCommandIdentifier, ToolCommand)[] SaveToCache { get; }
        public bool IsSuccess { get; }
        public string Message { get; }

        private ToolRestoreResult(
            (RestoredCommandIdentifier, ToolCommand)[] saveToCache,
            bool isSuccess, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("message", nameof(message));
            }

            SaveToCache = saveToCache ?? [];
            IsSuccess = isSuccess;
            Message = message;
        }

        public static ToolRestoreResult Success(
            (RestoredCommandIdentifier, ToolCommand)[] saveToCache,
            string message)
        {
            return new ToolRestoreResult(saveToCache, true, message);
        }

        public static ToolRestoreResult Failure(string message)
        {
            return new ToolRestoreResult(null, false, message);
        }

        public static ToolRestoreResult Failure(
            PackageId packageId,
            ToolPackageException toolPackageException)
        {
            return new ToolRestoreResult(null, false,
                string.Format(CliCommandStrings.PackageFailedToRestore,
                    packageId.ToString(), toolPackageException.ToString()));
        }
    }
}
