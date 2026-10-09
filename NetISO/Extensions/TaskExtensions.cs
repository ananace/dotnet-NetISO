namespace NetISO.Extensions;

public static class TaskExtensions
{
  public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> Task, TimeSpan Timeout, CancellationToken ct = default)
  {
    var completedTask = await System.Threading.Tasks.Task.WhenAny(Task, System.Threading.Tasks.Task.Delay(Timeout, ct));
    ct.ThrowIfCancellationRequested();
    if (completedTask == Task)
      return await Task;

    throw new TimeoutException("Operation deadline exceeded");
  }
}
