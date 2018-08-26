// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System.IO;
using Nuke.Common;
using Nuke.Common.Utilities;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.Tools.Git.GitTasks;

partial class Build
{
    readonly string MasterBranch = "master";
    readonly string DevelopBranch = "develop";
    readonly string ReleaseBranchPrefix = "release";
    readonly string HotfixBranchPrefix = "hotfix";
    
    Target Changelog => _ => _
        .Requires(() => GitRepository.Branch.StartsWith("release") ||
                        GitRepository.Branch.StartsWith("hotfix"))
        .Executes(() =>
        {
            FinalizeChangelog(ChangelogFile, GitVersion.MajorMinorPatch, GitRepository);
            Git($"add {ChangelogFile}");
            Git($"commit -m \"Finalize {Path.GetFileName(ChangelogFile)} for {GitVersion.MajorMinorPatch}\"");
        });
    
    Target Release => _ => _
        .DependsOn(Changelog)
        .Requires(() => GitHasCleanWorkingCopy())
        .Executes(() =>
        {
            if (!GitRepository.Branch.StartsWithOrdinalIgnoreCase(ReleaseBranchPrefix))
                Git($"checkout -b {ReleaseBranchPrefix}/{GitVersion.MajorMinorPatch} {DevelopBranch}");
            else
                FinishReleaseOrHotfix();
        });

    void FinishReleaseOrHotfix()
    {
        Git($"checkout {MasterBranch}");
        Git($"merge --no-ff --no-edit {GitRepository.Branch}");
        Git($"tag {GitVersion.MajorMinorPatch}");
        
        Git($"checkout {DevelopBranch}");
        Git($"merge --no-ff --no-edit {GitRepository.Branch}");
        
        Git($"branch -D {GitRepository.Branch}");

        Git($"push origin {MasterBranch} {DevelopBranch} {GitVersion.MajorMinorPatch}");
    }
}
