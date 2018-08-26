using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.ChangeLog;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.ControlFlow;

partial class Build : NukeBuild
{
    readonly string ProductName = "dotapi";
    
    public static int Main () => Execute<Build>(x => x.Test);
    
    [Parameter("ApiKey for the specified source.")] readonly string ApiKey;
    [Parameter] string Source = "https://api.nuget.org/v3/index.json";
    [Parameter] string SymbolSource = "https://nuget.smbsrc.net/";
    
    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    
    string ChangelogFile => RootDirectory / "CHANGELOG.md";
    IEnumerable<string> ChangelogSectionNotes => ExtractChangelogSectionNotes(ChangelogFile);
    string ReleaseVersion => GitVersion.GetNormalizedAssemblyVersion();
    string FileVersion => GitVersion.GetNormalizedFileVersion();
    string InformationalVersion => GitVersion.InformationalVersion;
    string NugetVersion => GitVersion.NuGetVersionV2;
    
    
    Target PrintVersion => _ => _
        .Executes(() =>
        {
            Logger.Info("Release Version: {0}", ReleaseVersion);
            Logger.Info("File Version: {0}", FileVersion);
            Logger.Info("Informational Version: {0}", InformationalVersion);
            Logger.Info("NuGet Version: {0}", NugetVersion);
            Logger.Info("Release Notes: {0}", ChangelogSectionNotes);
        });    
    
    Target Clean => _ => _
        .Executes(() =>
        {
            DeleteDirectories(GlobDirectories(SourceDirectory, "*/bin", "*/obj"));
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Project DotApiProject => Solution.GetProject(ProductName).NotNull();

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(SolutionFile)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(ReleaseVersion)
                .SetFileVersion(FileVersion)
                .SetInformationalVersion(InformationalVersion)
                .EnableNoRestore());
        });

    Target Test => _ => _
            .DependsOn(Compile)
            .Executes(() =>
        {
            GlobFiles(TestsDirectory, "**/*.Tests.fsproj")
                .NotEmpty()
                .ForEach(project =>
                    DotNetTest(s => DefaultDotNetTest
                        .SetProjectFile(project)
                        .SetArgumentConfigurator(args => args
                            .Add("/p:CollectCoverage=true")
                            .Add("/p:CoverletOutputFormat=opencover"))));
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(DotApiProject.Path)
                .SetVersion(NugetVersion)
                .SetOutputDirectory(OutputDirectory)
                .SetConfiguration(Configuration)
                .EnableNoBuild());
        });
    
    Target Push => _ => _
        .DependsOn(Pack, Test)
        .Requires(() => ApiKey) //,  () => GitterAuthToken)
        .Requires(() => GitHasCleanWorkingCopy())
        .Requires(() => Configuration.EqualsOrdinalIgnoreCase("release"))
        .Requires(() => GitRepository.Branch.EqualsOrdinalIgnoreCase(MasterBranch) ||
                        GitRepository.Branch.EqualsOrdinalIgnoreCase(DevelopBranch))
        .Executes(() =>
        {
            GlobFiles(OutputDirectory, "*.nupkg").NotEmpty()
                .Where(x => !x.EndsWith(".symbols.nupkg"))
                .ForEach(x => DotNetNuGetPush(s => s
                    .SetTargetPath(x)
                    .SetSource(Source)
                    .SetApiKey(ApiKey)));
        });
    
    Target Install => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            SuppressErrors(() => DotNet($"tool uninstall -g {ProductName}"));
            DotNet($"tool install -g {ProductName} --add-source {OutputDirectory} --version {GitVersion.NuGetVersionV2}");
        });

}
