using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace VisualStudio.CSharpNavigator.Roslyn;

public static class WorkspaceSnapshotIdentity
{
    private static readonly ConditionalWeakTable<Solution, SnapshotToken> Tokens = new();

    public static string GetVersion(Solution solution)
    {
        if (solution is null)
        {
            throw new ArgumentNullException(nameof(solution));
        }

        // Solution 不可变；弱引用身份覆盖未保存文本和配置变化，又不扫描全工程。
        return Tokens.GetValue(solution, _ => new SnapshotToken()).Value;
    }

    public static async Task<string> GetChangeFingerprintAsync(
        Solution original,
        Solution changed,
        CancellationToken cancellationToken)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(GetVersion(original));
        foreach (var projectChange in changed.GetChanges(original).GetProjectChanges()
                     .OrderBy(change => change.ProjectId.Id))
        {
            writer.Write(projectChange.ProjectId.Id.ToString("N"));
            foreach (var id in projectChange.GetChangedDocuments().OrderBy(id => id.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = original.GetDocument(id)!;
                var after = changed.GetDocument(id)!;
                writer.Write(id.Id.ToString("N"));
                writer.Write(before.Name);
                writer.Write(after.Name);
                writer.Write(before.FilePath ?? string.Empty);
                writer.Write(after.FilePath ?? string.Empty);
                var oldText = await before.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var newText = await after.GetTextAsync(cancellationToken).ConfigureAwait(false);
                writer.Write((int)oldText.ChecksumAlgorithm);
                writer.Write(oldText.GetChecksum().Length);
                writer.Write(oldText.GetChecksum().ToArray());
                writer.Write((int)newText.ChecksumAlgorithm);
                writer.Write(newText.GetChecksum().Length);
                writer.Write(newText.GetChecksum().ToArray());
            }
        }

        writer.Flush();
        stream.FlushFinalBlock();
        return "sha256:" + BitConverter.ToString(hash.Hash!).Replace("-", string.Empty);
    }

    private sealed class SnapshotToken
    {
        public string Value { get; } = "snapshot:" + Guid.NewGuid().ToString("N");
    }
}
