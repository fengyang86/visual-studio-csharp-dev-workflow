using System;
using System.Threading;
using System.Threading.Tasks;

namespace DebugControl.Sample.App;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("Debug control sample started.");
        Thread.Sleep(TimeSpan.FromSeconds(2));
        var calculator = new SampleCalculator();
        for (var i = 0; i < 5; i++)
        {
            var total = calculator.Add(i, 3);
            Console.WriteLine($"Total {i}: {total}");
            Thread.Sleep(TimeSpan.FromMilliseconds(250));
        }

        var worker = new SampleWorker();
        worker.RunConcurrentWork();
        worker.CaptureExpectedException();

        Thread.Sleep(TimeSpan.FromSeconds(30));
        Console.WriteLine("Debug control sample finished.");
    }
}

public sealed class SampleCalculator
{
    public int Add(int left, int right)
    {
        return left + right;
    }
}

public sealed class SampleWorker
{
    public int RunConcurrentWork()
    {
        var gate = new ManualResetEventSlim(false);
        var first = Task.Run(() => ComputeOnWorker(gate, 10));
        var second = Task.Run(() => ComputeOnWorker(gate, 20));

        gate.Set();
        return first.Result + second.Result;
    }

    public string CaptureExpectedException()
    {
        try
        {
            ThrowExpectedException();
            return "not-thrown";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static int ComputeOnWorker(ManualResetEventSlim gate, int seed)
    {
        gate.Wait();
        var currentThreadId = Environment.CurrentManagedThreadId;
        Thread.Sleep(TimeSpan.FromSeconds(5));
        return seed + currentThreadId;
    }

    private static void ThrowExpectedException()
    {
        throw new InvalidOperationException("Expected debug-control sample exception.");
    }
}
