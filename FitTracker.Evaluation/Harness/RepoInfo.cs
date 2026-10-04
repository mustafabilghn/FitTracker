using System.Diagnostics;

namespace FitTracker.Evaluation.Harness;

/// <summary>Çalıştırmanın hangi koda karşı yapıldığını kaydeder (commit + dirty durumu).</summary>
public sealed record RepoInfo(string Root, string CommitSha, bool WorkingTreeDirty, bool ProductionSourceDirty)
{
    public static RepoInfo Detect(string? start = null)
    {
        var root = FindRoot(start ?? Directory.GetCurrentDirectory())
                   ?? FindRoot(AppContext.BaseDirectory)
                   ?? throw new InvalidOperationException("Repository root (FitTrackr.sln) not found. Run from inside the repository.");

        var sha = Git(root, "rev-parse HEAD").Trim();
        var dirty = Git(root, "status --porcelain").Trim().Length > 0;
        // Production kaynakları: API projesi. Evaluation dosyalarının dirty olması baseline'ın ölçtüğü kodu değiştirmez.
        var productionDirty = Git(root, "status --porcelain -- FitTracker.API").Trim().Length > 0;
        return new RepoInfo(root, sha, dirty, productionDirty);
    }

    private static string? FindRoot(string directory)
    {
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FitTrackr.sln")))
                return dir.FullName;
        return null;
    }

    private static string Git(string root, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {arguments} failed.");
    }
}
