// Ignore Spelling: Hardmob

namespace Hardmob.Helpers
{
    /// <summary>
    /// Helper methods for <see cref="Thread"/>
    /// </summary>
    static class ThreadHelper
    {
        /// <summary>
        /// Tries to interrupt thread <see cref="Thread.Interrupt()"/> without other exceptions
        /// </summary>
        /// <param name="thread">Thread to be interrupted</param>
        public static void SafeInterrupt(this Thread? thread)
        {
            // Is source valid?
            if (thread != null)
            {
                try
                {
                    // Normal abort
                    thread.Interrupt();
                }

                // Throws this thread abortion
                catch (ThreadAbortException) { throw; }
                catch (ThreadInterruptedException) { throw; }

                // Ignore any other exception
                catch {; }
            }
        }

        /// <summary>
        /// Tries to interrupt threads <see cref="Thread.Interrupt()"/> without other exceptions
        /// </summary>
        /// <param name="source">Threads to be interrupted</param>
        public static void SafeInterrupt(params Thread?[]? source)
        {
            // Check the input
            if ((source != null) && (source.Length > 0))
            {
                // Interrupt it all
                for (int i = 0, c = source.Length; i < c; i++)
                    source[i]?.SafeInterrupt();
            }
        }
    }
}
