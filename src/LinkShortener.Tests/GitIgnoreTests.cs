namespace LinkShortener.Tests;

public sealed class GitIgnoreTests
{
    [Fact]
    public void Gitignore_covers_generated_temporary_and_environment_specific_files()
    {
        var repositoryRoot = FindRepositoryRoot();
        var ignoredPatterns = File.ReadAllLines(Path.Combine(repositoryRoot, ".gitignore"));

        Assert.Contains("**/bin/", ignoredPatterns);
        Assert.Contains("**/obj/", ignoredPatterns);
        Assert.Contains("TestResults/", ignoredPatterns);
        Assert.Contains("coverage/", ignoredPatterns);
        Assert.Contains(".vs/", ignoredPatterns);
        Assert.Contains("*.user", ignoredPatterns);
        Assert.Contains(".env", ignoredPatterns);
        Assert.Contains(".env.*", ignoredPatterns);
        Assert.Contains("!.env.example", ignoredPatterns);
        Assert.Contains("*.db", ignoredPatterns);
        Assert.Contains("*.tmp", ignoredPatterns);
        Assert.Contains("*.log", ignoredPatterns);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LinkShortener.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
