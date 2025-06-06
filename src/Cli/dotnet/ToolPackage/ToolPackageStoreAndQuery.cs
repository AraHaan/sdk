// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Versioning;

namespace Microsoft.DotNet.Cli.ToolPackage;

internal class ToolPackageStoreAndQuery(DirectoryPath root, IFileSystem fileSystem = null) : IToolPackageStoreQuery, IToolPackageStore
{
    public const string StagingDirectory = ".stage";

    public DirectoryPath Root { get; private set; } = new DirectoryPath(Path.GetFullPath(root.Value));

    private IFileSystem _fileSystem = fileSystem ?? new FileSystemWrapper();

    public DirectoryPath GetRandomStagingDirectory()
    {
        return Root.WithSubDirectories(StagingDirectory, Path.GetRandomFileName());
    }

    public NuGetVersion GetStagedPackageVersion(DirectoryPath stagingDirectory, PackageId packageId)
    {
        if (NuGetVersion.TryParse(
            Path.GetFileName(
                _fileSystem.Directory.EnumerateDirectories(
                    stagingDirectory.WithSubDirectories(packageId.ToString()).Value).FirstOrDefault()),
            out var version))
        {
            return version;
        }

        throw new ToolPackageException(
            string.Format(
                CliStrings.FailedToFindStagedToolPackage,
                packageId));
    }

    public DirectoryPath GetRootPackageDirectory(PackageId packageId)
    {
        return Root.WithSubDirectories(packageId.ToString());
    }

    public DirectoryPath GetPackageDirectory(PackageId packageId, NuGetVersion version)
    {
        if (version == null)
        {
            throw new ArgumentNullException(nameof(version));
        }

        return GetRootPackageDirectory(packageId)
            .WithSubDirectories(version.ToNormalizedString().ToLowerInvariant());
    }

    public IEnumerable<IToolPackage> EnumeratePackages()
    {
        if (!_fileSystem.Directory.Exists(Root.Value))
        {
            yield break;
        }

        foreach (var subdirectory in _fileSystem.Directory.EnumerateDirectories(Root.Value))
        {
            var name = Path.GetFileName(subdirectory);
            var packageId = new PackageId(name);

            // Ignore the staging directory and any directory that isn't the same as the package id
            if (name == StagingDirectory || name != packageId.ToString())
            {
                continue;
            }

            foreach (var package in EnumeratePackageVersions(packageId))
            {
                yield return package;
            }
        }
    }

    public IEnumerable<IToolPackage> EnumeratePackageVersions(PackageId packageId)
    {
        var packageRootDirectory = Root.WithSubDirectories(packageId.ToString());
        if (!_fileSystem.Directory.Exists(packageRootDirectory.Value))
        {
            yield break;
        }

        foreach (var subdirectory in _fileSystem.Directory.EnumerateDirectories(packageRootDirectory.Value))
        {
            yield return new ToolPackageInstance(id: packageId,
                version: NuGetVersion.Parse(Path.GetFileName(subdirectory)),
                packageDirectory: new DirectoryPath(subdirectory),
                assetsJsonParentDirectory: new DirectoryPath(subdirectory), _fileSystem);
        }
    }

    public IToolPackage GetPackage(PackageId packageId, NuGetVersion version)
    {
        if (version == null)
        {
            throw new ArgumentNullException(nameof(version));
        }

        var directory = GetPackageDirectory(packageId, version);
        if (!_fileSystem.Directory.Exists(directory.Value))
        {
            return null;
        }

        return new ToolPackageInstance(id: packageId,
            version: version,
            packageDirectory: directory,
            assetsJsonParentDirectory: directory,
            fileSystem: _fileSystem);
    }
}
