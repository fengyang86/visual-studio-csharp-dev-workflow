using VisualStudio.CSharpNavigator.Server.Agentic;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class EvidenceStoreTests
{
    [Fact]
    public void ReadResource_SupportsBoundedRanges()
    {
        var store = new EvidenceStore();
        store.AddTextResource(
            "csharp://task/one",
            "one",
            "One",
            "test",
            "text/plain",
            "0123456789",
            isPartial: false);

        var result = store.ReadResource("csharp://task/one?offset=3&maxChars=4");

        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(
            result.Contents.Single());
        Assert.Equal("3456", text.Text);
    }

    [Fact]
    public void AddTextResource_EnforcesPerResourceLimitAndMarksPartial()
    {
        var store = new EvidenceStore();
        store.AddTextResource(
            "csharp://task/large",
            "large",
            "Large",
            "test",
            "text/plain",
            new string('x', 500_001),
            isPartial: false);

        var result = store.ReadResource("csharp://task/large");
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(
            result.Contents.Single());
        Assert.Contains("EvidenceTruncated", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void IdenticalResourcesAreDeduplicatedButAliasesRemainReadable()
    {
        var store = new EvidenceStore();
        store.AddTextResource("csharp://task/one", "one", "One", "test", "text/plain", "same", false);
        store.AddTextResource("csharp://task/two", "two", "Two", "test", "text/plain", "same", false);

        Assert.Single(store.ListResources());
        var result = store.ReadResource("csharp://task/two");
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(result.Contents.Single());
        Assert.Equal("same", text.Text);
    }

    [Fact]
    public async Task ExpiredResourcesReturnMachineReadableStatus()
    {
        var store = new EvidenceStore();
        store.AddTextResource("csharp://task/short", "short", "Short", "test", "text/plain", "value", false, TimeSpan.FromMilliseconds(10));
        await Task.Delay(40);

        var result = store.ReadResource("csharp://task/short");
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(result.Contents.Single());
        Assert.Contains("EvidenceResourceExpired", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredUriMarkersAreBounded()
    {
        var store = new EvidenceStore();
        for (var index = 0; index < 2050; index++)
        {
            var uri = $"csharp://task/expired/{index:D4}";
            store.AddTextResource(
                uri,
                $"expired-{index}",
                "Expired",
                "test",
                "text/plain",
                index.ToString(),
                isPartial: false,
                timeToLive: TimeSpan.FromMilliseconds(-1));
        }

        var oldest = store.ReadResource("csharp://task/expired/0000");
        var oldestText = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(oldest.Contents.Single());
        Assert.Contains("EvidenceResourceNotFound", oldestText.Text, StringComparison.Ordinal);

        var newest = store.ReadResource("csharp://task/expired/2049");
        var newestText = Assert.IsType<ModelContextProtocol.Protocol.TextResourceContents>(newest.Contents.Single());
        Assert.Contains("EvidenceResourceExpired", newestText.Text, StringComparison.Ordinal);
    }
}
