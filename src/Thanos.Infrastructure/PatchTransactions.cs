using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Thanos.Core;

namespace Thanos.Infrastructure;

public sealed class PatchTransactionService(IPatchValidationService validator) : IPatchTransactionService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public async Task<PatchTransaction> ApplyAsync(PatchProposal patch, string gameInput, string transactionDirectory, string approvalPatchId, ResearchReport? research = null, CancellationToken cancellationToken = default)
    {
        patch = PatchImportService.Normalize(patch);
        if (approvalPatchId != patch.PatchId) { throw new InvalidDataException("Explicit approval must exactly match patchId."); }
        var validation = await validator.ValidateAsync(patch, gameInput, research, cancellationToken);
        if (validation.Status != PatchValidationStatus.Valid || validation.FileOffset is null) { throw new InvalidDataException("PATCH REJECTED: all critical validation checks must pass before creating a working copy."); }
        var target = PatchValidationService.ResolveTarget(patch, gameInput);
        var destination = Path.GetFullPath(transactionDirectory);
        if (File.Exists(destination) || Directory.Exists(destination)) { throw new IOException("Transaction destination already exists; existing data is never replaced."); }
        if (target.RootIsDirectory && PatchValidationService.Within(target.Root, destination)) { throw new InvalidDataException("Transaction destination cannot be inside the original game directory."); }
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Transaction destination needs a parent directory.");
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".thanos-transaction-" + Guid.NewGuid().ToString("N"));
        var transactionId = Guid.NewGuid().ToString("N");
        var originalBytes = PatchImportService.Bytes(patch.OriginalBytes, "originalBytes");
        var replacementBytes = PatchImportService.Bytes(patch.ReplacementBytes, "replacementBytes");
        var offset = AddressMapper.Parse(validation.FileOffset);
        try
        {
            Directory.CreateDirectory(temporary);
            var backupRoot = Path.Combine(temporary, "backup");
            var workingRoot = Path.Combine(temporary, "working");
            var backupTarget = SafeCombine(backupRoot, target.RelativePath);
            var workingTarget = SafeCombine(workingRoot, target.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupTarget)!);
            Directory.CreateDirectory(Path.GetDirectoryName(workingTarget)!);

            await using var lockedOriginal = new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var lockedHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(lockedOriginal, cancellationToken));
            if (lockedHash != patch.Sha256) { throw new InvalidDataException("PATCH REJECTED: original changed after validation."); }
            lockedOriginal.Position = 0;
            await CopyStreamAsync(lockedOriginal, backupTarget, cancellationToken);
            if (target.RootIsDirectory) { await CopyTreeAsync(target.Root, workingRoot, target.Path, cancellationToken); }
            else { Directory.CreateDirectory(workingRoot); }
            await CopyFileAsync(backupTarget, workingTarget, cancellationToken);
            var backupHash = await PatchValidationService.HashAsync(backupTarget, cancellationToken);
            var workingHash = await PatchValidationService.HashAsync(workingTarget, cancellationToken);
            if (backupHash != patch.Sha256 || workingHash != patch.Sha256) { throw new InvalidDataException("Transaction copy did not preserve exact original target bytes."); }

            await using (var candidate = new FileStream(workingTarget, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                candidate.Position = checked((long)offset);
                var current = new byte[originalBytes.Length];
                await candidate.ReadExactlyAsync(current, cancellationToken);
                if (!current.SequenceEqual(originalBytes)) { throw new InvalidDataException("PATCH REJECTED: working-copy original bytes changed before write."); }
                candidate.Position = checked((long)offset);
                await candidate.WriteAsync(replacementBytes, cancellationToken);
                await candidate.FlushAsync(cancellationToken);
                candidate.Flush(flushToDisk: true);
            }
            var candidateHash = await PatchValidationService.HashAsync(workingTarget, cancellationToken);
            if (candidateHash == patch.Sha256) { throw new InvalidDataException("Patched candidate hash unexpectedly equals the original hash."); }

            await File.WriteAllTextAsync(Path.Combine(temporary, "patch.json"), JsonData.Serialize(patch), new UTF8Encoding(false), cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var transaction = new PatchTransaction
            {
                TransactionId = transactionId,
                PatchId = patch.PatchId,
                Status = PatchTransactionStatus.Applied,
                CreatedAt = now,
                UpdatedAt = now,
                OriginalRoot = target.Root,
                TargetRelativePath = target.RelativePath,
                OriginalSha256 = patch.Sha256,
                BackupSha256 = backupHash,
                CandidateSha256 = candidateHash,
                WorkingRoot = "working",
                BackupRoot = "backup",
                FileOffset = validation.FileOffset,
                Length = replacementBytes.LongLength,
                Limitations = ["The original was not modified. Only the separate working copy contains replacement bytes.", "A valid byte transaction does not prove compatibility or safety; Phase 5 runtime verification is not included."]
            };
            await File.WriteAllTextAsync(Path.Combine(temporary, "transaction.json"), JsonData.Serialize(transaction), new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporary, destination);
            return transaction;
        }
        finally
        {
            if (Directory.Exists(temporary)) { DeleteOwnedTree(temporary, parent); }
        }
    }

    public async Task<PatchTransaction> RollbackAsync(string transactionDirectory, string approvalTransactionId, string? confirmedOriginalRoot = null, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(transactionDirectory);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) { throw new DirectoryNotFoundException("Transaction directory was not found or is a reparse point."); }
        var metadataPath = Path.Combine(root, "transaction.json");
        var transaction = await ReadTransactionAsync(metadataPath, cancellationToken);
        if (transaction.TransactionId != approvalTransactionId) { throw new InvalidDataException("Explicit approval must exactly match transactionId."); }
        var workingRoot = ExactChild(root, transaction.WorkingRoot, "working");
        var backupRoot = ExactChild(root, transaction.BackupRoot, "backup");
        var backupTarget = SafeCombine(backupRoot, transaction.TargetRelativePath);
        if (!File.Exists(backupTarget) || await PatchValidationService.HashAsync(backupTarget, cancellationToken) != transaction.BackupSha256 || transaction.BackupSha256 != transaction.OriginalSha256) { throw new InvalidDataException("Rollback backup is missing or does not match transaction metadata."); }

        var restoreOriginal = confirmedOriginalRoot is not null;
        if (restoreOriginal)
        {
            if (!Path.GetFullPath(confirmedOriginalRoot!).Equals(Path.GetFullPath(transaction.OriginalRoot), PathComparison)) { throw new InvalidDataException("Confirmed original root does not exactly match transaction metadata."); }
            var originalTarget = Directory.Exists(transaction.OriginalRoot) ? SafeCombine(transaction.OriginalRoot, transaction.TargetRelativePath) : Path.GetFullPath(transaction.OriginalRoot);
            PatchValidationService.EnsureNoReparse(Directory.Exists(transaction.OriginalRoot) ? transaction.OriginalRoot : Path.GetDirectoryName(originalTarget)!, originalTarget);
            if (File.Exists(originalTarget) && (File.GetAttributes(originalTarget) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Original target is now a symbolic link/reparse point; restore rejected."); }
            Directory.CreateDirectory(Path.GetDirectoryName(originalTarget)!);
            if (File.Exists(originalTarget) && await PatchValidationService.HashAsync(originalTarget, cancellationToken) != transaction.OriginalSha256)
            {
                var recovery = Path.Combine(root, "recovery"); Directory.CreateDirectory(recovery);
                var preserved = Path.Combine(recovery, "pre-restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
                await CopyFileAsync(originalTarget, preserved, cancellationToken);
            }
            var temporary = Path.Combine(Path.GetDirectoryName(originalTarget)!, ".thanos-restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                await CopyFileAsync(backupTarget, temporary, cancellationToken);
                File.Move(temporary, originalTarget, overwrite: true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
            if (await PatchValidationService.HashAsync(originalTarget, cancellationToken) != transaction.OriginalSha256) { throw new IOException("Restored original hash does not match the recorded backup."); }
        }

        if (Directory.Exists(workingRoot)) { DeleteOwnedTree(workingRoot, root); }
        var updated = transaction with
        {
            Status = PatchTransactionStatus.RolledBack,
            UpdatedAt = DateTimeOffset.UtcNow,
            Limitations = [.. transaction.Limitations, restoreOriginal ? "Rollback restored the recorded backup to the original target; any displaced bytes were preserved under recovery/." : "Rollback removed the transaction working copy. The original was not changed by apply and was not overwritten."]
        };
        await AtomicMetadataAsync(metadataPath, updated, cancellationToken);
        return updated;
    }

    private static async Task<PatchTransaction> ReadTransactionAsync(string path, CancellationToken token)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new InvalidDataException("Transaction metadata must be an object."); }
        var expected = new HashSet<string>(StringComparer.Ordinal) { "schemaVersion", "transactionId", "patchId", "status", "createdAt", "updatedAt", "originalRoot", "targetRelativePath", "originalSha256", "backupSha256", "candidateSha256", "workingRoot", "backupRoot", "fileOffset", "length", "limitations" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject()) { if (!seen.Add(property.Name)) { throw new InvalidDataException("Duplicate transaction properties are not accepted."); } }
        if (!seen.SetEquals(expected)) { throw new InvalidDataException("Transaction metadata shape is incomplete or contains unknown properties."); }
        var result = document.RootElement.Deserialize<PatchTransaction>(JsonData.Options) ?? throw new InvalidDataException("Transaction metadata cannot be null.");
        if (result.SchemaVersion != 1 || string.IsNullOrWhiteSpace(result.TransactionId) || string.IsNullOrWhiteSpace(result.PatchId) || result.CreatedAt == default || result.UpdatedAt == default || result.Length < 1) { throw new InvalidDataException("Invalid transaction metadata."); }
        KnowledgeValidation.Hash(result.OriginalSha256); KnowledgeValidation.Hash(result.BackupSha256); KnowledgeValidation.Hash(result.CandidateSha256);
        _ = AddressMapper.Parse(result.FileOffset);
        return result;
    }

    private static async Task CopyTreeAsync(string source, string destination, string excludedTarget, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        var stack = new Stack<(string Source, string Destination)>(); stack.Push((source, destination));
        while (stack.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(item.Source).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Working-copy creation rejects symbolic links/reparse points."); }
                var output = Path.Combine(item.Destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) { Directory.CreateDirectory(output); stack.Push((entry, output)); }
                else if (!PathComparer.Equals(Path.GetFullPath(entry), Path.GetFullPath(excludedTarget))) { await CopyFileAsync(entry, output, token); }
            }
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyStreamAsync(input, destination, token);
    }

    private static async Task CopyStreamAsync(Stream input, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, token);
        await output.FlushAsync(token);
        output.Flush(flushToDisk: true);
    }

    private static string SafeCombine(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) { throw new InvalidDataException("Transaction relative path is unsafe."); }
        var result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!PatchValidationService.Within(root, result)) { throw new InvalidDataException("Transaction path escapes its root."); }
        return result;
    }

    private static string ExactChild(string root, string recorded, string expected)
    {
        if (!recorded.Equals(expected, StringComparison.Ordinal)) { throw new InvalidDataException("Transaction subdirectory metadata is invalid."); }
        return SafeCombine(root, expected);
    }

    private static async Task AtomicMetadataAsync(string path, PatchTransaction transaction, CancellationToken token)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonData.Serialize(transaction), new UTF8Encoding(false), token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static void DeleteOwnedTree(string target, string parent)
    {
        var full = Path.GetFullPath(target);
        if (!PatchValidationService.Within(parent, full)) { throw new InvalidDataException("Refusing to remove a directory outside the transaction parent."); }
        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            var attributes = File.GetAttributes(entry);
            if (Directory.Exists(entry))
            {
                if ((attributes & FileAttributes.ReparsePoint) == 0) { DeleteOwnedTree(entry, full); }
                else { Directory.Delete(entry); }
            }
            else { File.Delete(entry); }
        }
        Directory.Delete(full);
    }

    public Task<PatchTransaction> InspectAsync(string transactionDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(transactionDirectory);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) { throw new DirectoryNotFoundException("Transaction directory was not found or is a reparse point."); }
        return ReadTransactionAsync(Path.Combine(root, "transaction.json"), cancellationToken);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

