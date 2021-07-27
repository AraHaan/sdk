// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Razor.Tasks;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Sdk.BlazorWebAssembly.Tests
{
    public class WasmJsModulesIntegrationTests : BlazorWasmBaselineTests
    {
        public WasmJsModulesIntegrationTests(ITestOutputHelper log) : base(log, GenerateBaselines)
        {
        }

        [Fact]
        public void Build_DoesNotGenerateManifestJson_IncludesJSModulesOnBlazorBootJsonManifest()
        {
            // Arrange
            var testAsset = "BlazorWasmMinimal";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "blazorwasm-minimal.lib.module.js"), "console.log('Hello initializer')");

            var build = new BuildCommand(ProjectDirectory);
            build.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var buildResult = build.Execute("/bl");
            buildResult.Should().Pass();

            var outputPath = build.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = build.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var blazorBootJson = new FileInfo(Path.Combine(intermediateOutputPath, "blazor.boot.json"));
            blazorBootJson.Should().Exist();
            var contents = JsonSerializer.Deserialize<JsonDocument>(blazorBootJson.OpenRead());
            contents.RootElement.TryGetProperty("resources", out var resources).Should().BeTrue();
            resources.TryGetProperty("libraryInitializers", out var initializers).Should().BeTrue();
            initializers.TryGetProperty("blazorwasm-minimal.lib.module.js", out _).Should().BeTrue();

            new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.modules.json")).Should().NotExist();
        }

        [Fact]
        public void JSModules_ManifestIncludesModuleTargetPaths()
        {
            // Arrange
            var testAsset = "BlazorHosted";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "blazorwasm", "wwwroot", "blazorwasm.lib.module.js"), "console.log('Hello initializer')");
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "razorclasslibrary", "wwwroot", "razorclasslibrary.lib.module.js"), "console.log('Hello RCL initializer')");

            var build = new BuildCommand(ProjectDirectory, "blazorhosted");
            build.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var buildResult = build.Execute("/bl");
            buildResult.Should().Pass();

            var outputPath = build.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = build.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var blazorBootJson = new FileInfo(Path.Combine(intermediateOutputPath.Replace("blazorhosted", "blazorwasm"), "blazor.boot.json"));
            blazorBootJson.Should().Exist();
            var contents = JsonSerializer.Deserialize<JsonDocument>(blazorBootJson.OpenRead());
            contents.RootElement.TryGetProperty("resources", out var resources).Should().BeTrue();
            resources.TryGetProperty("libraryInitializers", out var initializers).Should().BeTrue();
            initializers.TryGetProperty("blazorwasm.lib.module.js", out _).Should().BeTrue();
            initializers.TryGetProperty("_content/RazorClassLibrary/razorclasslibrary.lib.module.js", out var hash).Should().BeTrue();
            
            // Do some validation to ensure the hash is included
            Convert.TryFromBase64String(hash.GetString().Substring("SHA256-".Length), new byte[256], out _).Should().BeTrue();

            new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorhosted.modules.json")).Should().NotExist();
        }

        [Fact]
        public void Publish_DoesNotGenerateManifestJson_IncludesJSModulesOnBlazorBootJsonManifest()
        {
            // Arrange
            var testAsset = "BlazorWasmMinimal";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "blazorwasm-minimal.lib.module.js"), "console.log('Hello initializer')");

            var publish = new PublishCommand(ProjectDirectory);
            publish.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var publishResult = publish.Execute("/bl");
            publishResult.Should().Pass();

            var outputPath = publish.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = publish.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var blazorBootJson = new FileInfo(Path.Combine(intermediateOutputPath, "blazor.publish.boot.json"));
            blazorBootJson.Should().Exist();
            var contents = JsonSerializer.Deserialize<JsonDocument>(blazorBootJson.OpenRead());
            contents.RootElement.TryGetProperty("resources", out var resources).Should().BeTrue();
            resources.TryGetProperty("libraryInitializers", out var initializers).Should().BeTrue();
            initializers.TryGetProperty("blazorwasm-minimal.lib.module.js", out var hash).Should().BeTrue();
            Convert.TryFromBase64String(hash.GetString().Substring("SHA256-".Length), new byte[256], out _).Should().BeTrue();

            new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.modules.json")).Should().NotExist();

            var lib = new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.lib.module.js"));
            lib.Should().Exist();
        }

        [Fact]
        public void JsModules_CanHaveDifferentBuildAndPublishModules()
        {
            // Arrange
            var testAsset = "BlazorWasmMinimal";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);

            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "blazorwasm-minimal.lib.module.js"), "console.log('Publish initializer')");
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "blazorwasm-minimal.lib.module.build.js"), "console.log('Build initializer')");
            
            ProjectDirectory.WithProjectChanges(document =>
            {
                document.Root.Add(new XElement("ItemGroup",
                    new XElement("Content",
                        new XAttribute("Update", "wwwroot\\blazorwasm-minimal.lib.module.build.js"),
                        new XAttribute("CopyToPublishDirectory", "Never"),
                        new XAttribute("TargetPath", "wwwroot\\blazorwasm-minimal.lib.module.js"))));
            });


            var publish = new PublishCommand(ProjectDirectory);
            publish.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var publishResult = publish.Execute("/bl");
            publishResult.Should().Pass();

            var outputPath = publish.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = publish.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var buildLibrary = GetLibraryInitializer(Path.Combine(intermediateOutputPath, "blazor.boot.json"));
            var publishLibrary = GetLibraryInitializer(Path.Combine(intermediateOutputPath, "blazor.publish.boot.json"));

            publishLibrary.GetString().Should().NotBe(buildLibrary.GetString());

            new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.modules.json")).Should().NotExist();
            var lib = new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.lib.module.js"));            
            lib.Should().Exist();

            var wwwrootPublishLibrary = GetLibraryInitializer(Path.Combine(outputPath, "wwwroot", "_framework", "blazor.boot.json"));
            publishLibrary.GetString().Should().Be(wwwrootPublishLibrary.GetString());

            static JsonElement GetLibraryInitializer(string path)
            {
                var blazorBootJson = new FileInfo(path);
                blazorBootJson.Should().Exist();
                var contents = JsonSerializer.Deserialize<JsonDocument>(blazorBootJson.OpenRead());
                contents.RootElement.TryGetProperty("resources", out var resources).Should().BeTrue();
                resources.TryGetProperty("libraryInitializers", out var initializers).Should().BeTrue();
                initializers.TryGetProperty("blazorwasm-minimal.lib.module.js", out var buildLibrary).Should().BeTrue();
                return buildLibrary;
            }
        }

        [Fact]
        public void JsModules_CanCustomizeBlazorInitialization()
        {
            // Arrange
            var testAsset = "BlazorWasmMinimal";
            ProjectDirectory = CreateAspNetSdkTestAsset(testAsset);
            File.WriteAllText(Path.Combine(ProjectDirectory.TestRoot, "wwwroot", "blazorwasm-minimal.lib.module.js"), "console.log('Hello initializer')");

            ProjectDirectory.WithProjectChanges(document =>
            {
                document.Root.Add(new XElement("Target",
                    new XAttribute("Name", "_CustomizeBlazorBootProcess"),
                    new XAttribute("AfterTargets", "ProcessPublishFilesForBlazor"),
                    new XElement("ItemGroup",
                        new XElement("_BlazorBootFilesToUpdate",
                            new XAttribute("Include", "@(StaticWebAsset)"),
                            new XAttribute("Condition", "'%(AssetKind)' != 'Build' and (('%(AssetTraitName)' == 'BlazorWebAssemblyResource' and '%(AssetTraitValue)' != 'manifest' and '%(AssetTraitValue)' != 'boot') or '%(AssetTraitName)' == 'Culture')")),

                    new XElement("StaticWebAsset",
                        new XAttribute("Include", "$(IntermediateOutputPath)publish.extension.txt"),
                        new XElement("SourceId", "$(PackageId)"),
                        new XElement("SourceType", "Computed"),
                        new XElement("ContentRoot", "$(PublishDir)wwwroot"),
                        new XElement("BasePath", "$(StaticWebAssetBasePath)"),
                        new XElement("RelativePath", "_framework/publish.extension.txt"),
                        new XElement("AssetKind", "Publish"),
                        new XElement("AssetMode", "All"),
                        new XElement("AssetRole", "Primary"),
                        new XElement("AssetTraitName", "BlazorWebAssemblyResource"),
                        new XElement("AssetTraitValue", "extension:my-custom-extension"),
                        new XElement("CopyToOutputDirectory", "Never"),
                        new XElement("CopyToPublishDirectory", "PreserveNewest"),
                        new XElement("OriginalItemSpec", "$(IntermediateOutputPath)publish.extension.txt")),

                    new XElement("FileWrites",
                        new XAttribute("Include", "$(IntermediateOutputPath)publish.extension.txt"))),

                    new XElement("WriteLinesToFile",
                        new XAttribute("Lines", "@(_BlazorBootFilesToUpdate->'%(FullPath)')"),
                        new XAttribute("File", "$(IntermediateOutputPath)publish.extension.txt"),
                        new XAttribute("WriteOnlyWhenDifferent", "true"))));
            });


            var publish = new PublishCommand(ProjectDirectory);
            publish.WithWorkingDirectory(ProjectDirectory.TestRoot);
            var publishResult = publish.Execute("/bl");
            publishResult.Should().Pass();

            var outputPath = publish.GetOutputDirectory(DefaultTfm).ToString();
            var intermediateOutputPath = publish.GetIntermediateDirectory(DefaultTfm, "Debug").ToString();

            var publishExtension = GetPublishExtension(Path.Combine(intermediateOutputPath, "blazor.publish.boot.json"));

            new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.modules.json")).Should().NotExist();
            var lib = new FileInfo(Path.Combine(outputPath, "wwwroot", "blazorwasm-minimal.lib.module.js"));
            lib.Should().Exist();

            var wwwrootPublishExtension = GetPublishExtension(Path.Combine(outputPath, "wwwroot", "_framework", "blazor.boot.json"));
            publishExtension.GetString().Should().Be(wwwrootPublishExtension.GetString());

            var extension = new FileInfo(Path.Combine(outputPath, "wwwroot", "_framework", "publish.extension.txt"));
            extension.Should().Exist();

            static JsonElement GetPublishExtension(string path)
            {
                var blazorBootJson = new FileInfo(path);
                blazorBootJson.Should().Exist();
                var contents = JsonSerializer.Deserialize<JsonDocument>(blazorBootJson.OpenRead());
                contents.RootElement.TryGetProperty("resources", out var resources).Should().BeTrue();
                resources.TryGetProperty("extensions", out var extensions).Should().BeTrue();
                extensions.TryGetProperty("my-custom-extension", out var extension).Should().BeTrue();
                extension.TryGetProperty("publish.extension.txt", out var file).Should().BeTrue();
                return file;
            }
        }
    }
}
