using VisualStudio.CSharpNavigator.Protocol;
using VisualStudio.CSharpNavigator.Server.Agentic;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class MutationSessionSafetyTests
{
    [Theory]
    [InlineData("snapshot:old", "sha256:new", "MutationChangesChanged")]
    [InlineData("snapshot:new", "sha256:old", "WorkspaceVersionChanged")]
    [InlineData("snapshot:old", "", "MutationFingerprintRequired")]
    public void RejectsChangedOrMissingPreviewEvidence(string version, string fingerprint, string blocker)
    {
        var store = new MutationSessionStore();
        store.Record(Preview("snapshot:old", "sha256:old"));
        var result = store.Validate(new CSharpCodeFixRequest { PreviewSessionId = "preview" }, Preview(version, fingerprint));
        Assert.False(result.IsAllowed);
        Assert.Contains(blocker, result.Blocker);
    }

    [Fact]
    public void AllowsSameSnapshotCandidateAndFullDifference()
    {
        var store = new MutationSessionStore();
        store.Record(Preview("snapshot:old", "sha256:old"));
        Assert.True(store.Validate(new CSharpCodeFixRequest { PreviewSessionId = "preview" },
            Preview("snapshot:old", "sha256:old")).IsAllowed);
    }

    [Fact]
    public void CandidateCannotBeChangedByMutatingOriginalPreviewObject()
    {
        var store = new MutationSessionStore();
        var original = Preview("snapshot:old", "sha256:old");
        store.Record(original);
        original.CandidateIdentity.EquivalenceKey = "generatefield";
        var result = store.Validate(new CSharpCodeFixRequest { PreviewSessionId = "preview" }, original);
        Assert.False(result.IsAllowed);
        Assert.Contains("MutationCandidateChanged", result.Blocker);
    }

    private static WorkspaceMutationPreview Preview(string version, string fingerprint) => new()
    {
        SessionId = "preview",
        WorkspaceVersion = version,
        ChangeFingerprint = fingerprint,
        CandidateIdentity = new MutationCandidateIdentity { EquivalenceKey = "GenerateField", StableKey = "candidate" },
    };
}
