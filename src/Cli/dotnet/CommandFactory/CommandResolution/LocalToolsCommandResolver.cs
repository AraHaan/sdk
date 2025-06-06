﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.DotNet.Cli.ToolManifest;
using Microsoft.DotNet.Cli.ToolPackage;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.DependencyResolver;
using NuGet.Frameworks;

namespace Microsoft.DotNet.Cli.CommandFactory.CommandResolution;

internal class LocalToolsCommandResolver(
    ToolManifestFinder toolManifest = null,
    ILocalToolsResolverCache localToolsResolverCache = null,
    IFileSystem fileSystem = null,
    string currentWorkingDirectory = null) : ICommandResolver
{
    private readonly ToolManifestFinder _toolManifest = toolManifest ?? new ToolManifestFinder(new DirectoryPath(currentWorkingDirectory ?? Directory.GetCurrentDirectory()));
    private readonly ILocalToolsResolverCache _localToolsResolverCache = localToolsResolverCache ?? new LocalToolsResolverCache();
    private readonly IFileSystem _fileSystem = fileSystem ?? new FileSystemWrapper();
    private const string LeadingDotnetPrefix = "dotnet-";

    public CommandSpec ResolveStrict(CommandResolverArguments arguments, bool allowRollForward = false)
    {
        if (arguments == null || string.IsNullOrWhiteSpace(arguments.CommandName))
        {
            return null;
        }

        if (!arguments.CommandName.StartsWith(LeadingDotnetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolveResult = GetPackageCommandSpecUsingMuxer(arguments,
            new ToolCommandName(arguments.CommandName.Substring(LeadingDotnetPrefix.Length)), allowRollForward);

        return resolveResult;
    }

    public CommandSpec Resolve(CommandResolverArguments arguments)
    {
        if (arguments == null || string.IsNullOrWhiteSpace(arguments.CommandName))
        {
            return null;
        }

        if (!arguments.CommandName.StartsWith(LeadingDotnetPrefix, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(arguments.CommandName.Substring(LeadingDotnetPrefix.Length)))
        {
            return null;
        }

        // Try resolving without prefix first
        var result = GetPackageCommandSpecUsingMuxer(
            arguments,
            new ToolCommandName(arguments.CommandName.Substring(LeadingDotnetPrefix.Length)));

        if (result != null)
        {
            return result;
        }

        // Fallback to resolving with the prefix
        return GetPackageCommandSpecUsingMuxer(arguments, new ToolCommandName(arguments.CommandName));
    }

    private CommandSpec GetPackageCommandSpecUsingMuxer(CommandResolverArguments arguments,
        ToolCommandName toolCommandName, bool allowRollForward = false)
    {
        if (!_toolManifest.TryFind(toolCommandName, out var toolManifestPackage))
        {
            return null;
        }

        if (_localToolsResolverCache.TryLoad(
            new RestoredCommandIdentifier(
                toolManifestPackage.PackageId,
                toolManifestPackage.Version,
                NuGetFramework.Parse(BundledTargetFramework.GetTargetFrameworkMoniker()),
                Constants.AnyRid,
                toolCommandName),
            out var toolCommand))
        {
            if (!_fileSystem.File.Exists(toolCommand.Executable.Value))
            {
                throw new GracefulException(string.Format(CliStrings.NeedRunToolRestore,
                    toolCommandName.ToString()));
            }

            if (toolCommand.Runner == "dotnet")
            {
                if (toolManifestPackage.RollForward || allowRollForward)
                {
                    arguments.CommandArguments = ["--allow-roll-forward", .. arguments.CommandArguments];
                }

                return MuxerCommandSpecMaker.CreatePackageCommandSpecUsingMuxer(
                    toolCommand.Executable.Value,
                    arguments.CommandArguments);
            }
            else if (toolCommand.Runner == "executable")
            {
                var escapedArgs = ArgumentEscaper.EscapeAndConcatenateArgArrayForProcessStart(
                    arguments.CommandArguments);

                return new CommandSpec(
                    toolCommand.Executable.Value,
                    escapedArgs);
            }
            else
            {
                throw new GracefulException(string.Format(CliStrings.ToolSettingsUnsupportedRunner,
                    toolCommand.Name, toolCommand.Runner));
            }
        }
        else
        {
            throw new GracefulException(string.Format(CliStrings.NeedRunToolRestore,
                    toolCommandName.ToString()));
        }
    }
}
