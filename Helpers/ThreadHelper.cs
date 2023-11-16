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

        /// <summary>
        /// Tries to abort thread <see cref="Thread.Abort()"/> without other exceptions
        /// </summary>
        /// <param name="source">Threads to be terminated</param>
        /// <param name="timeout">Time to wait before abort</param>
        public static void SafeAbort(int timeout, params Thread[] source)
        {
            // Check the input
            if ((source != null) && (source.Length > 0))
            {
                // Does have a timeout interval?
                // First check for self-abort (invalid)
                if (timeout > 0)
                {
                    // Current thread
                    Thread current = Thread.CurrentThread;

                    // For each thread to abort
                    for (int i = 0, c = source.Length; i < c; i++)
                    {
                        // It's the current thread?
                        if (source[i] == current)
                        {
                            // We can not wait, just abort itself
                            timeout = 0;
                            break;
                        }
                    }
                }

                // Must wait all threads before abort?
                if (timeout > 0)
                {
                    // First we will try to wait for all threads to end
                    try
                    {
                        // Start the watch
                        int start = Environment.TickCount;

                        // Loop
                        while (true)
                        {
                            // Timed-out?
                            if (Environment.TickCount - start > timeout)
                                break;

                            // Active thread count
                            int active = 0;

                            // Check for all threads
                            for (int i = 0, c = source.Length; i < c; i++)
                            {
                                // Still active?
                                Thread thread = source[i];
                                if ((thread != null) && (thread.IsAlive))
                                    active++;
                            }

                            // Exit if none thread is active
                            if (active == 0)
                                return;

                            // Wait before next check
                            Thread.Sleep(1);
                        }
                    }

                    // Throws abort exceptions
                    catch (ThreadAbortException) { throw; }

                    // Ignore any other exception
                    catch {; }
                }

                // Now we can abort it all
                for (int i = 0, c = source.Length; i < c; i++)
                    source[i]?.SafeAbort();
            }
        }
    }
}
