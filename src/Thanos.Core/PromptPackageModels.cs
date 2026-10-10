namespace Thanos.Core;

public sealed record PackageEntry(string File, string Sha256, long Bytes);
public sealed record EvidencePackageManifest(int SchemaVersion, string TemplateVersion, PromptMode Mode, PackageEntry[] Entries, string[] Limitations);
