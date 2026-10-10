using System.Runtime.InteropServices;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class LocalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(ContainerInputs.Split(path).Container);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public Stream OpenRead(string path) => OpenReadAsync(path).GetAwaiter().GetResult();
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => ContainerInputs.OpenReadAsync(path, cancellationToken);
    public IEnumerable<string> EnumerateFiles(string directory) => Directory.EnumerateFiles(directory, "*", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        MaxRecursionDepth = 32
    });
    public bool IsSymbolicLink(string path) => (File.GetAttributes(ContainerInputs.Split(path).Container) & FileAttributes.ReparsePoint) != 0;
}

public sealed class LocalPathService : IPathService
{
    public string GetFullPath(string path)
    {
        var (container, member) = ContainerInputs.Split(path);
        return Path.GetFullPath(container) + (member is null ? "" : "!/" + member);
    }
    public string Combine(string directory, string name) => Path.Combine(directory, name);
    public string GetFileName(string path) => Path.GetFileName(path);
}

public sealed class LocalEnvironmentService : IEnvironmentService
{
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);
}

public sealed class LocalPlatformService : IPlatformService
{
    public string ApplicationDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "THANOS");
}

public sealed class ExecutableDiscovery(IEnvironmentService environment, IFileSystem files, IPathService paths) : IExecutableDiscovery
{
    public string? Find(string name)
    {
        if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return files.FileExists(name) ? paths.GetFullPath(name) : null;
        }
        var extensions = OperatingSystem.IsWindows()
            ? (environment.GetVariable("PATHEXT") ?? ".EXE").Split(';') : [""];
        foreach (var directory in (environment.GetVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions.Prepend(""))
            {
                var candidate = paths.Combine(directory, name + extension);
                if (files.FileExists(candidate))
                {
                    return paths.GetFullPath(candidate);
                }
            }
        }
        return null;
    }
}
