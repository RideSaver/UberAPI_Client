#tool nuget:?package=NuGet.CommandLine&version=5.9.1
#addin nuget:?package=Cake.Git&version=2.0.0
#addin nuget:?package=Cake.CodeGen.OpenAPI&version=1.0.2
using Cake.CodeGen.OpenApi;
using Cake.Common.Tools.NuGet.NuGetAliases;
using System.Text.RegularExpressions;

var currentRuntime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier ;
var currentArchitecture = currentRuntime.Substring(currentRuntime.LastIndexOf("-") + 1);
currentRuntime = currentRuntime.Substring(0, currentRuntime.LastIndexOf("-"));


var target = Argument("target", "Build");
var configuration = Argument("configuration", "Release");
var generator = Argument("generator", "csharp-netcore");
var output_dir = Argument("output_dir", $"./build/{generator}");
var packageName = Argument("package_name", "UberAPI.Client");
var architecture = Argument("architecture", currentArchitecture);
var runtime = Argument("runtime", currentRuntime);

// Summary: The runtime identifier to compile for
var Runtime = $"{runtime}-{architecture}";
//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .WithCriteria(c => HasArgument("rebuild"))
    .Does(() =>
{
    CleanDirectory($"{output_dir}");
});

Task("Generate:OpenAPI")
    .IsDependentOn("Clean")
    .Does(() =>
{
    OpenApiGenerator.Generate("UberAPIClient/openapi.yaml", generator, $"{output_dir}", new OpenApiGenerateSettings()
    {
        ConfigurationFile = "UberAPIClient/openapi-codegen.json",
        PackageName = packageName,
        TemplateDirectory = "./UberAPIClient/template",
    });
});

Task("Build")
    .IsDependentOn("Generate:OpenAPI")
    .Does(() =>
{
    DotNetBuild($"Server/UberClient.csproj", new DotNetBuildSettings
    {
        Configuration = configuration,
        Framework = "net6.0",
        OutputDirectory = $"./build/UberClient",
        Runtime = Runtime,
    });
});

Task("Publish")
    .IsDependentOn("Build")
    .Does(()=>
{
    DotNetPublish("Server/UberClient.csproj", new DotNetPublishSettings {
        Framework = "net6.0",
        Configuration = "Release",
        OutputDirectory = "./publish/",
        SelfContained = true,
        PublishTrimmed = true,
        Runtime = Runtime
    });
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    // DotNetTest($"{output_dir}/{packageName}", new DotNetTestSettings
    // {
    //     Configuration = configuration,
    //     NoBuild = true,
    // });
});


//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
