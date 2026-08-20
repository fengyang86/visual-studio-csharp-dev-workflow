namespace CodeNavigator.Sample;

public sealed class Calculator
{
    public int Add(int left, int right)
    {
        return left + right;
    }

    public int SumTwice(int left, int right)
    {
        var first = Add(left, right);
        var second = Add(right, left);
        return first + second;
    }

    public int AddThenSumTwice(int left, int right)
    {
        return Add(left, right) + SumTwice(left, right);
    }

    public string GetGeneratedMessage()
    {
        return Generated.GeneratedGreeter.Message;
    }
}
