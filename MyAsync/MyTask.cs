using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace MyAsync;

[AsyncMethodBuilder(typeof(MyTask))]
public class MyTask
{
    private bool _completed;
    private Exception? _exception;
    private Action? _continuation;
    private ExecutionContext? _context;
    private readonly object _lock = new();

    public readonly struct Awaiter(MyTask task) : ICriticalNotifyCompletion
    {
        public bool IsCompleted => task.IsCompleted;
        public void OnCompleted(Action continuation) => task.ContinueWith(continuation);

        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

        public void GetResult() => task.Wait();
    }

    public Awaiter GetAwaiter() => new(this);

    public bool IsCompleted
    {
        get
        {
            lock (_lock)
            {
                return _completed;
            }
        }
    }

    public void SetResult() => Complete(null);

    public void SetException(Exception exception) => Complete(exception);

    public void Complete(Exception? exception)
    {
        lock (_lock)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Task is already completed");
            }

            _completed = true;
            _exception = exception;

            if (_continuation is null)
            {
                return;
            }

            MyThreadPool.QueueWorkItem(delegate
            {
                if (_context is null)
                {
                    _continuation();
                }
                else
                {
                    ExecutionContext.Run(_context, state => ((Action)state!).Invoke(), _continuation);
                }
            });
        }
    }

    public void Wait()
    {
        ManualResetEventSlim? resetEvent = null;

        lock (_lock)
        {
            if (!_completed)
            {
                resetEvent = new ManualResetEventSlim();
                ContinueWith(resetEvent.Set);
            }
        }

        resetEvent?.Wait();

        if (_exception is not null)
        {
            ExceptionDispatchInfo.Throw(_exception);
        }
    }

    public MyTask ContinueWith(Action action)
    {
        MyTask task = new();

        Action callback = () =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                task.SetException(exception);
                return;
            }

            task.SetResult();
        };
        
        lock (_lock)
        {
            if (_completed)
            {
                MyThreadPool.QueueWorkItem(callback);
            }
            else
            {
                _continuation = callback;
                _context = ExecutionContext.Capture();
            }
        }

        return task;
    }

    public static MyTask Run(Action action)
    {
        MyTask task = new();
        
        MyThreadPool.QueueWorkItem(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                task.SetException(exception);
                return;
            }
            
            task.SetResult();
        });

        return task;
    }

    public static MyTask WhenAll(List<MyTask> tasks)
    {
        MyTask task = new();

        if (tasks.Count == 0)
        {
            task.SetResult();
        }
        else
        {
            int remaining = tasks.Count;

            Action continuation = () =>
            {
                if (Interlocked.Decrement(ref remaining) == 0)
                {
                    // TODO exceptions
                    task.SetResult();
                }
            };
            
            foreach (var t in tasks)
            {
                t.ContinueWith(continuation);
            }
        }

        return task;
    }

    public static MyTask Delay(int timeoutMilliseconds)
    {
        MyTask task = new();
        
        new Timer(_ => task.SetResult()).Change(timeoutMilliseconds, -1);
        
        return task;
    }

    public static MyTask Iterate(IEnumerable<MyTask> tasks)
    {
        MyTask task = new();

        var enumerator = tasks.GetEnumerator();
        
        void MoveNext()
        {
            try
            {
                if (enumerator.MoveNext())
                {
                    var next = enumerator.Current;
                    next.ContinueWith(MoveNext);
                    return;
                }
            }
            catch (Exception exception)
            {
                task.SetException(exception);
                return;
            }
            
            enumerator.Dispose();
            
            task.SetResult();
        }
        
        MoveNext();

        return task;
    }
}