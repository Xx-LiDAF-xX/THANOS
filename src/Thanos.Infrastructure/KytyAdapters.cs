using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class DefaultKytyAdapter : IKytyAdapter
{
    public string Name => "DefaultKytyAdapter";

    public bool Supports(KytyFingerprint kyty) => true;

    public ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy)
        => CreateRequest(kyty, workingCopy, ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot));

    public ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy, ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(kyty);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopy);
        ArgumentNullException.ThrowIfNull(scenario);

        var arguments = new List<string> { "--game", workingCopy };
        if (scenario.AdditionalArguments.Length > 0)
        {
            arguments.AddRange(scenario.AdditionalArguments);
        }

        var env = new Dictionary<string, string>(scenario.Environment);
        var workingDir = string.IsNullOrWhiteSpace(kyty.ExecutablePath) ? null : Path.GetDirectoryName(Path.GetFullPath(kyty.ExecutablePath));

        return new ProcessRequest(
            kyty.ExecutablePath,
            arguments.AsReadOnly(),
            workingDir,
            scenario.Timeout,
            env);
    }
}

public sealed class KytyPs5Adapter : IKytyAdapter
{
    public string Name => "KytyPs5Adapter";

    public bool Supports(KytyFingerprint kyty)
    {
        if (kyty == null) return false;
        if (kyty.Capabilities.Any(c => c.Name.Equals("kyty-ps5", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (!string.IsNullOrWhiteSpace(kyty.Version) && kyty.Version.Contains("ps5", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy)
        => CreateRequest(kyty, workingCopy, ScenarioDefinition.CreateBuiltIn(ScenarioType.Boot));

    public ProcessRequest CreateRequest(KytyFingerprint kyty, string workingCopy, ScenarioDefinition scenario)
    {
        ArgumentNullException.ThrowIfNull(kyty);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopy);
        ArgumentNullException.ThrowIfNull(scenario);

        if (workingCopy.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            workingCopy.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
            workingCopy.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Kyty does not accept {Path.GetExtension(workingCopy)} archives directly; only .zar is supported natively.");
        }

        var arguments = new List<string> { "--game", workingCopy };
        if (scenario.AdditionalArguments.Length > 0)
        {
            arguments.AddRange(scenario.AdditionalArguments);
        }

        var env = new Dictionary<string, string>(scenario.Environment);
        var workingDir = string.IsNullOrWhiteSpace(kyty.ExecutablePath) ? null : Path.GetDirectoryName(Path.GetFullPath(kyty.ExecutablePath));

        return new ProcessRequest(
            kyty.ExecutablePath,
            arguments.AsReadOnly(),
            workingDir,
            scenario.Timeout,
            env);
    }
}

public sealed class KytyAdapterRegistry : IKytyAdapterRegistry
{
    private readonly List<IKytyAdapter> adapters = [];

    public KytyAdapterRegistry()
    {
        adapters.Add(new KytyPs5Adapter());
        adapters.Add(new DefaultKytyAdapter());
    }

    public IReadOnlyList<IKytyAdapter> Adapters => adapters.AsReadOnly();

    public void Register(IKytyAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        adapters.Insert(0, adapter);
    }

    public IKytyAdapter Select(KytyFingerprint kyty)
    {
        ArgumentNullException.ThrowIfNull(kyty);
        foreach (var adapter in adapters)
        {
            if (adapter.Supports(kyty))
            {
                return adapter;
            }
        }
        return new DefaultKytyAdapter();
    }
}
