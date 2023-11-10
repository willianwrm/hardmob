using System;
using System.Threading;

namespace Hardmob.Helpers
{
    /// <summary>
    /// Helper methods for <see cref="Thread"/>
    /// </summary>
    static class ThreadHelper
    {
        /// <summary>
        /// Tries to abort thread <see cref="Thread.Abort()"/> without other exceptions
        /// </summary>
        /// <param name="thread">Thread to be terminated</param>
        public static void SafeAbort(this Thread thread)
        {
            // Is source valid?
            if (thread != null)
            {
                try
                {
                    // Normal abort
                    thread.Abort();
                }

                // Throws this thread abortion
                catch (ThreadAbortException) { throw; }

                // Ignore any other exception
                catch {; }
            }
        }

        /// <summary>
        /// Tries to abort thread <see cref="Thread.Abort()"/> without other exceptions
        /// </summary>
        /// <param name="thread">Thread to be terminated</param>
        /// <param name="timeout">Time to wait before abort</param>
        public static void SafeAbort(this Thread thread, int timeout)
        {
            // Is valid thread?
            if (thread != null)
            {
                try
                {
                    // Is timeout valid?
                    if (timeout > 0)
                    {
                        // Don't wait for myself
                        if (Thread.CurrentThread != thread)
                        {
                            // Initial time
                            int start = Environment.TickCount;

                            // While thread is alive
                            while (thread.IsAlive)
                            {
                                // Check for timeout
                                if (Environment.TickCount - start >= timeout)
                                    break;

                                // Wait some more time
                                Thread.Sleep(1);
                            }
                        }
                    }

                    // Aborts the thread
                    thread.SafeAbort();
                }

                // Throws this thread abortion
                catch (ThreadAbortException) { throw; }

                // Ignore any other exception, just abort the thread
                catch { thread.SafeAbort(); }
            }
        }
    }
}
