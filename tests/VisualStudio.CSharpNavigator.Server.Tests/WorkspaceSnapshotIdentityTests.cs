using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using VisualStudio.CSharpNavigator.Roslyn;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class WorkspaceSnapshotIdentityTests
{
    [Fact]
    public void VersionTracksDocumentBodyChangesEvenWhenProjectVersionIsUnchanged()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace);
        var before = document.Project.Solution;
        var after = before.WithDocumentText(document.Id, SourceText.From("class A { int Value() => 2; }"));

        Assert.Equal(before.GetProject(document.Project.Id)!.Version, after.GetProject(document.Project.Id)!.Version);
        Assert.Equal(WorkspaceSnapshotIdentity.GetVersion(before), WorkspaceSnapshotIdentity.GetVersion(before));
        Assert.NotEqual(WorkspaceSnapshotIdentity.GetVersion(before), WorkspaceSnapshotIdentity.GetVersion(after));
    }

    [Fact]
    public void VersionTracksConfigurationAndAdditionalDocumentChanges()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace);
        var before = document.Project.Solution;
        var configured = before.WithProjectParseOptions(document.Project.Id,
            new CSharpParseOptions(preprocessorSymbols: new[] { "FEATURE" }));
        var id = DocumentId.CreateNewId(document.Project.Id);
        var additional = before.AddAdditionalDocument(id, "input.json", SourceText.From("{}"));
        var changedInput = additional.WithAdditionalDocumentText(id, SourceText.From("{\"value\":1}"));

        Assert.NotEqual(WorkspaceSnapshotIdentity.GetVersion(before), WorkspaceSnapshotIdentity.GetVersion(configured));
        Assert.NotEqual(WorkspaceSnapshotIdentity.GetVersion(additional), WorkspaceSnapshotIdentity.GetVersion(changedInput));
    }

    [Fact]
    public async Task FingerprintTracksFullTextButIsStableAcrossEquivalentReplays()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace);
        var before = document.Project.Solution;
        var prefix = "class A { string Value => \"" + new string('x', 1000);
        var first = before.WithDocumentText(document.Id, SourceText.From(prefix + "one\"; }"));
        var replay = before.WithDocumentText(document.Id, SourceText.From(prefix + "one\"; }"));
        var changed = before.WithDocumentText(document.Id, SourceText.From(prefix + "two\"; }"));

        var fingerprint = await WorkspaceSnapshotIdentity.GetChangeFingerprintAsync(before, first, CancellationToken.None);
        Assert.Equal(fingerprint, await WorkspaceSnapshotIdentity.GetChangeFingerprintAsync(before, replay, CancellationToken.None));
        Assert.NotEqual(fingerprint, await WorkspaceSnapshotIdentity.GetChangeFingerprintAsync(before, changed, CancellationToken.None));
    }

    [Fact]
    public void DifferentWorkspacesNeverShareSnapshotIdentity()
    {
        using var first = new AdhocWorkspace();
        using var second = new AdhocWorkspace();
        Assert.NotEqual(WorkspaceSnapshotIdentity.GetVersion(first.CurrentSolution),
            WorkspaceSnapshotIdentity.GetVersion(second.CurrentSolution));
    }

    private static Document CreateDocument(AdhocWorkspace workspace)
    {
        var project = workspace.AddProject("Sample", LanguageNames.CSharp);
        return workspace.AddDocument(project.Id, "A.cs", SourceText.From("class A { int Value() => 1; }"));
    }
}
