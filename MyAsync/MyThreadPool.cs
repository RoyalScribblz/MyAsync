using System.Collections.Concurrent;

namespace MyAsync;

public static class MyThreadPool
{
    private readonly static BlockingCollection<(Action, ExecutionContext?)> Actions = [];

    public static void QueueWorkItem(Action action) => Actions.Add((action, ExecutionContext.Capture()));

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() =>
            {
                while (true)
                {
                    var (action, context) = Actions.Take();
                    if (context is null)
                    {
                        action();
                    }
                    else
                    {
                        ExecutionContext.Run(context, state => ((Action)state!).Invoke(), action);
                    }
                }
                // ReSharper disable once FunctionNeverReturns
            })
            {
                IsBackground = true,
            }.Start();
        }
    }
}