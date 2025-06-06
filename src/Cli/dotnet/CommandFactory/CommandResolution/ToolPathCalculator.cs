// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.DotNet.Cli.Utils;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace Microsoft.DotNet.Cli.CommandFactory.CommandResolution;

public class ToolPathCalculator(string packagesDirectory)
{
    private readonly string _packagesDirectory = packagesDirectory;

    public string GetBestLockFilePath(string packageId, VersionRange versionRange, NuGetFramework framework)
    {
        if (versionRange == null)
        {
            throw new ArgumentNullException(nameof(versionRange));
        }

        if (framework == null)
        {
            throw new ArgumentNullException(nameof(framework));
        }

        var availableToolVersions = GetAvailableToolVersions(packageId);

        var bestVersion = versionRange.FindBestMatch(availableToolVersions);
        if (bestVersion == null)
        {
            throw new GracefulException(string.Format(
                CliStrings.VersionForPackageCouldNotBeResolved,
                packageId));
        }

        return GetLockFilePath(packageId, bestVersion, framework);
    }

    public string GetLockFilePath(string packageId, NuGetVersion version, NuGetFramework framework)
    {
        if (version == null)
        {
            throw new ArgumentNullException(nameof(version));
        }

        if (framework == null)
        {
            throw new ArgumentNullException(nameof(framework));
        }

        return Path.Combine(
            GetBaseToolPath(packageId),
            version.ToNormalizedString().ToLowerInvariant(),
            framework.GetShortFolderName(),
            "project.assets.json");
    }

    private string GetBaseToolPath(string packageId)
    {
        return Path.Combine(
            _packagesDirectory,
            ".tools",
            packageId.ToLowerInvariant());
    }

    private IEnumerable<NuGetVersion> GetAvailableToolVersions(string packageId)
    {
        var availableVersions = new List<NuGetVersion>();

        var toolBase = GetBaseToolPath(packageId);
        if (!Directory.Exists(toolBase))
        {
            return [];
        }

        var versionDirectories = Directory.EnumerateDirectories(toolBase);

        foreach (var versionDirectory in versionDirectories)
        {
            var version = Path.GetFileName(versionDirectory);

            NuGetVersion nugetVersion;
            NuGetVersion.TryParse(version, out nugetVersion);

            if (nugetVersion != null)
            {
                availableVersions.Add(nugetVersion);
            }
        }

        return availableVersions;
    }

}
