using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace Hardmob.Helpers
{
    /// <summary>
    /// Helpers for <see cref="Exception"/>
    /// </summary>
    static class ExceptionHelper
    {
        #region Statics
        /// <summary>
        /// Source name to write logs to
        /// </summary>
        private static readonly string EVENT_LOG_SOURCE = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product;
        #endregion

        #region Public
        /// <summary>
        /// Starting main helper
        /// </summary>
        static ExceptionHelper()
        {
            // Handling every exception
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        /// <summary>
        /// Register exceptions to log
        /// </summary>
        public static void Log(this Exception exception)
        {
            try
            {
                // Formats the exception
                string message = $"Exception: {exception.GetType().FullName}\r\n" +
                                 $"Message: {exception.Message}\r\n" +
                                 $"Stack:\r\n" +
                                 $"{exception.StackTrace}";

                // Writes to debug
                Debug.Write(message);

                // Creates log entry if none exists
                if (!EventLog.SourceExists(EVENT_LOG_SOURCE))
                    EventLog.CreateEventSource(EVENT_LOG_SOURCE, EVENT_LOG_SOURCE);

                // Writes exception to log
                EventLog.WriteEntry(EVENT_LOG_SOURCE, message, EventLogEntryType.Warning);
            }

            // Throws thread abort
            catch (ThreadAbortException) { throw; }

            // Ignores other further exceptions
            catch {; }
        }
        #endregion

        #region Private
        /// <summary>
        /// Register any not yet handled exception
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                // Check parameters
                if (e != null)
                {
                    // Release events if terminating
                    if (e.IsTerminating)
                        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;

                    // Register exception
                    else if (e.ExceptionObject is Exception ex)
                        ex.Log();
                }
            }

            // Ignores any exception
            catch {; }
        }
        #endregion
    }
}
