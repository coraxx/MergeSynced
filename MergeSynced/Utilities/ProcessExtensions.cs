using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MergeSynced.Utilities
{

    /// <summary>
    /// https://stackoverflow.com/questions/470256/process-waitforexit-asynchronously
    /// This adds async await capabilities to processes.
    /// </summary>
    public static class ProcessExtensions
    {
        public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void ProcessExited(object sender, EventArgs e)
            {
                tcs.TrySetResult(true);
            }

            process.EnableRaisingEvents = true;
            process.Exited += ProcessExited!;

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                process.Exited -= ProcessExited!;
            }
        }
    }
}