using VisualStudio.CSharpNavigator.Protocol;

namespace VisualStudio.CSharpNavigator.Server.Tests;

public sealed class DebugControlSemanticsTests
{
    [Fact]
    public void TryGetAlreadySatisfiedMessage_WhenStopAlreadyDesign_ReturnsSuccessMessage()
    {
        var status = new DebugSessionStatus { State = DebuggerState.Design };

        var isAlreadySatisfied = DebugControlSemantics.TryGetAlreadySatisfiedMessage(
            DebugControlAction.Stop,
            status,
            out var message);

        Assert.True(isAlreadySatisfied);
        Assert.Contains("already in design mode", message);
    }

    [Theory]
    [InlineData(DebugControlAction.Stop, DebuggerState.Run)]
    [InlineData(DebugControlAction.Stop, DebuggerState.Break)]
    [InlineData(DebugControlAction.StepOver, DebuggerState.Design)]
    [InlineData(DebugControlAction.Continue, DebuggerState.Design)]
    public void TryGetAlreadySatisfiedMessage_WhenStateDoesNotSatisfyAction_ReturnsFalse(
        DebugControlAction action,
        DebuggerState state)
    {
        var status = new DebugSessionStatus { State = state };

        var isAlreadySatisfied = DebugControlSemantics.TryGetAlreadySatisfiedMessage(
            action,
            status,
            out var message);

        Assert.False(isAlreadySatisfied);
        Assert.Equal(string.Empty, message);
    }
}
