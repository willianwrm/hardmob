// Ignore Spelling: Hardmob

using System.Runtime.InteropServices;

namespace Hardmob.Helpers
{
    /// <summary>
    /// Helpers for <see cref="File"/>
    /// </summary>
    static class FileHelper
    {
        #region Enumerators
        /// <summary>
        /// Tipo de link do target
        /// </summary>
        [Flags]
        private enum SymbolicLink
        {
            /// <summary>
            /// Arquivo
            /// </summary>
            File = 0,

            /// <summary>
            /// Pasta
            /// </summary>
            Directory = 1,

            /// <summary>
            /// Specify this flag to allow creation of symbolic links when the process is not elevated
            /// </summary>
            AllowUnprivilegedCreate = 2,
        }
        #endregion

        #region Constants
        /// <summary>
        /// Kernel file name
        /// </summary>
        private const string KERNEL = "kernel32.dll";
        #endregion

        #region Public
        /// <summary>
        /// Tries to create file link, use file copy if fails
        /// </summary>
        public static void CopyLink(string source, string destination)
        {
            // Tries by hard-link
            if (!CreateHardLink(destination, source, IntPtr.Zero))
            {
                // Tries by symbolic-link
                if (!CreateSymbolicLink(destination, source, SymbolicLink.File))
                {
                    // Just copy the file
                    File.Copy(source, destination, true);
                }
            }
        }
        #endregion

        #region Externals
        /// <summary>
        /// Creates a hard link
        /// </summary>
        [DllImport(KERNEL)]
        private static extern bool CreateHardLink(string file, string target, IntPtr security);

        /// <summary>
        /// Creates a symbolic link
        /// </summary>
        [DllImport(KERNEL)]
        private static extern bool CreateSymbolicLink(string file, string target, SymbolicLink flags);
        #endregion
    }
}
