// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Razor.Tasks;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Sdk.Razor.Tests
{
    public class JsModulesIntegrationTest : AspNetSdkBaselineTest
    {
        public JsModulesIntegrationTest(ITestOutputHelper log) : base(log, true)
        {
        }

        [Fact]
        public void Build_NoOps_WhenJsModulesIsDisabled()
        {
            var testAsset = "RazorComponentApp";
            var projectDirectory = CreateAspNetSdkTestAsset(testAsset);

            Directory.CreateDirectory(Path.Combine(projectDirectory.TestRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(projectDirectory.TestRoot, "wwwroot", "ComponentApp.lib.module.js"), "console.log('Hello world!');");

            var build = new BuildCommand(projectDirectory);
            build.WithWorkingDirectory(projectDirectory.TestRoot);
            build.Execute("/p:JsModulesEnabled=false").Should().Pass();

            var intermediateOutputPath = Path.Combine(build.GetBaseIntermediateDirectory().ToString(), "Debug", DefaultTfm);

            new FileInfo(Path.Combine(intermediateOutputPath, "jsmodules", "jsmodules.build.manifest.json")).Should().NotExist();
        }

        [Fact]
        public void Build_GeneratesManifestWhenItFindsALibrary()
        {
            var testAsset = "RazorComponentApp";
            var projectDirectory = CreateAspNetSdkTestAsset(testAsset);

            Directory.CreateDirectory(Path.Combine(projectDirectory.TestRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(projectDirectory.TestRoot, "wwwroot", "ComponentApp.lib.module.js"), "console.log('Hello world!');");

            var build = new BuildCommand(projectDirectory);
            build.WithWorkingDirectory(projectDirectory.TestRoot);
            build.Execute("/bl").Should().Pass();

            var intermediateOutputPath = Path.Combine(build.GetBaseIntermediateDirectory().ToString(), "Debug", DefaultTfm);

            var file = new FileInfo(Path.Combine(intermediateOutputPath, "jsmodules", "jsmodules.build.manifest.json"));
            file.Should().Exist();
            file.Should().Contain("ComponentApp.lib.module.js");
        }

        [Fact]
        public void Publish_PublishesBundleToTheRightLocation()
        {
            var testAsset = "RazorComponentApp";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);
            Directory.CreateDirectory(Path.Combine(ProjectDirectory.TestRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "ComponentApp.lib.module.js"), "console.log('Hello world!');");

            var publish = new PublishCommand(ProjectDirectory);
            publish.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var publishResult = publish.Execute("/bl");
            publishResult.Should().Pass();

            var outputPath = publish.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = publish.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var path = Path.Combine(intermediateOutputPath, "StaticWebAssets.publish.json");
            new FileInfo(path).Should().Exist();
            var manifest = StaticWebAssetsManifest.FromJsonBytes(File.ReadAllBytes(path));
            AssertManifest(manifest, LoadPublishManifest());

            AssertPublishAssets(
                StaticWebAssetsManifest.FromJsonBytes(File.ReadAllBytes(path)),
                outputPath,
                intermediateOutputPath);
        }

        [Fact]
        public void Publish_DoesNotPublishAnyFile_WhenThereAreNoJsModulesFiles()
        {
            var testAsset = "RazorComponentApp";
            var projectDirectory = CreateAspNetSdkTestAsset(testAsset);

            var publish = new PublishCommand(Log, projectDirectory.TestRoot);
            publish.Execute().Should().Pass();

            var publishOutputPath = publish.GetOutputDirectory(DefaultTfm, "Debug").ToString();

            new FileInfo(Path.Combine(publishOutputPath, "wwwroot", "ComponentApp.lib.module.js")).Should().NotExist();
            new FileInfo(Path.Combine(publishOutputPath, "wwwroot", "ComponentApp.modules.json")).Should().NotExist();
        }

        [Fact]
        public void Does_Nothing_WhenThereAreNoJsModulesFiles()
        {
            var testAsset = "RazorComponentApp";
            var projectDirectory = CreateAspNetSdkTestAsset(testAsset);

            var build = new BuildCommand(projectDirectory);
            build.WithWorkingDirectory(projectDirectory.TestRoot);
            build.Execute("/bl").Should().Pass();

            var intermediateOutputPath = Path.Combine(build.GetBaseIntermediateDirectory().ToString(), "Debug", DefaultTfm);

            var file = new FileInfo(Path.Combine(intermediateOutputPath, "jsmodules", "jsmodules.build.manifest.json"));
            file.Should().NotExist();
        }

        [Fact]
        public void Build_JsModules_IsIncremental()
        {
            // Arrange
            var thumbprintLookup = new Dictionary<string, FileThumbPrint>();

            var testAsset = "RazorComponentApp";
            var projectDirectory = CreateAspNetSdkTestAsset(testAsset);

            Directory.CreateDirectory(Path.Combine(projectDirectory.TestRoot, "wwwroot"));
            File.WriteAllText(Path.Combine(projectDirectory.TestRoot, "wwwroot", "ComponentApp.lib.module.js"), "console.log('Hello world!');");

            // Act & Assert 1
            var build = new BuildCommand(projectDirectory);
            build.Execute().Should().Pass();

            var intermediateOutputPath = Path.Combine(build.GetBaseIntermediateDirectory().ToString(), "Debug", DefaultTfm);
            var directoryPath = Path.Combine(intermediateOutputPath, "jsmodules");

            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var thumbprint = FileThumbPrint.Create(file);
                thumbprintLookup[file] = thumbprint;
            }

            // Act & Assert 2
            for (var i = 0; i < 2; i++)
            {
                build = new BuildCommand(projectDirectory);
                build.Execute().Should().Pass();

                foreach (var file in files)
                {
                    var thumbprint = FileThumbPrint.Create(file);
                    Assert.Equal(thumbprintLookup[file], thumbprint);
                }
            }
        }
    }
}
