using System.Collections.Concurrent;
using System.Windows;

namespace KidaGoSync.Tests;

/// <summary>
/// One long-lived STA thread for every WPF test: an Application cannot be created twice in a process and is only visible
/// on the thread that made it, so all tests that need it (theme, controls) run their body here.
/// </summary>
public static class WpfHost
{
    private static readonly BlockingCollection<(Action Body, TaskCompletionSource Done)> Queue = [];

    static WpfHost()
    {
        var thread = new Thread(() =>
        {
            _ = Application.Current ?? new Application();
            foreach (var (body, done) in Queue.GetConsumingEnumerable())
            {
                try { body(); done.SetResult(); } catch (Exception e) { done.SetException(e); }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void Run(Action body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add((body, done));
        done.Task.GetAwaiter().GetResult();
    }
}
