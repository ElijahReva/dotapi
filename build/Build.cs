using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nuke.Common;
using Nuke.Common.BuildServers;
using Nuke.Common.ChangeLog;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.Tooling.ProcessTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.ControlFlow;

class Build : NukeBuild
{
    readonly string ProductName = "dotapi";
    readonly string MasterBranch = "master";
    readonly string DevelopBranch = "develop";
    
    public static int Main () => Execute<Build>(x => x.Test);
    
    [Parameter("ApiKey for the specified source.")] readonly string ApiKey;
    [Parameter] string Source = "https://api.nuget.org/v3/index.json";
    [Parameter] string SymbolSource = "https://nuget.smbsrc.net/";
    
    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    
    string ChangelogFile => RootDirectory / "CHANGELOG.md";
    string ReleaseVersion => "0.4.1";
    string FileVersion => $"{ReleaseVersion}.0";
    string NugetVersion => GetNugetVersion();
    string InformationalVersion => $"{NugetVersion}.{GetCommitId()}";
    string ReleaseNotes => string.Join(NewLine, ExtractChangelogSectionNotes(ChangelogFile));
    
    Target PrintVersion => _ => _
        .Executes(() =>
        {
            Logger.Info("Release Version: {0}", ReleaseVersion);
            Logger.Info("File Version: {0}", FileVersion);
            Logger.Info("Informational Version: {0}", InformationalVersion);
            Logger.Info("NuGet Version: {0}", NugetVersion);
            Logger.Info("Release Notes: {0}", ReleaseNotes);
        });    
    
    Target Clean => _ => _
        .DependsOn(PrintVersion)
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
                        .EnableNoBuild()
                        .SetProjectFile(project)
                        .SetArgumentConfigurator(args => args
                            .Add("/p:CollectCoverage=true")
                            .Add("/p:CoverletOutputFormat=opencover"))));
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
        .OnlyWhen(    
            () => GitRepository.Branch.EqualsOrdinalIgnoreCase(MasterBranch), 
            () => GitRepository.Branch.EqualsOrdinalIgnoreCase(DevelopBranch)
        )
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(DotApiProject.Path)
                .SetVersion(NugetVersion)
                .SetOutputDirectory(OutputDirectory)
                .SetConfiguration(Configuration)
                .SetPackageReleaseNotes(ReleaseNotes)
                .EnableNoBuild());
        });
    
    Target Push => _ => _
        .DependsOn(Pack, Test)
        .OnlyWhen(    
            () => GitRepository.Branch.EqualsOrdinalIgnoreCase(MasterBranch), 
            () => GitRepository.Branch.EqualsOrdinalIgnoreCase(DevelopBranch)
        )
        .Requires(() => ApiKey) 
        .Requires(() => GitStatus())
        .Requires(() => Configuration.EqualsOrdinalIgnoreCase("release"))
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
            DotNet($"tool install -g {ProductName} --add-source {OutputDirectory} --version {NugetVersion}");
        });

    string GetNugetVersion()
    {
        var sb = new StringBuilder(ReleaseVersion);
        if (!GitRepository.Branch.EqualsOrdinalIgnoreCase(MasterBranch))
        {
            if (!GitRepository.Branch.EqualsOrdinalIgnoreCase(DevelopBranch))
            {
                sb.Append($"-{GitRepository.Branch?.Replace('/', '-')}");
            }
            else
            {
                sb.Append("-dev");
            }
            if (IsServerBuild && Travis.Instance != null && Travis.Instance.Ci)
            {
                sb.Append(Travis.Instance.BuildNumber);
            }
            else
            {
                sb.Append("-local");
            }
        }
        else
        {
            if (IsServerBuild && Travis.Instance != null && Travis.Instance.Ci)
            {
                sb.Append($".{Travis.Instance.BuildNumber}");
            }
            else
            {
                sb.Append("-local");
            }    
        }

        return sb.ToString();
    }

    string GetCommitId()
    {
        var process = StartProcess(GitPath, "rev-parse --short HEAD");
        process.AssertZeroExitCode();
        if (process.Output.Count == 1)
        {
           return process.Output.First().Text;
        }
        throw new InvalidOperationException("Not a git repo");
    }

    bool GitStatus()
    {
        // Fix chmod +x build.sh
        return Git("status --short").All(o => o.Text.Contains("build.sh"));
    }

}
