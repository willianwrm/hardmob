using Hardmob.Helpers;
using System;
using System.IO;
using System.Reflection;

namespace Hardmob
{
    /// <summary>
    /// Core properties
    /// </summary>
    static class Core
    {
        #region Statics
        /// <summary>
        /// Current APP's directory
        /// </summary>
        private static string _AppDir;

        /// <summary>
        /// Current assembly
        /// </summary>
        private static Assembly _Assembly;

        /// <summary>
        /// Current APP's product name
        /// </summary>
        private static string _ProductName;
        #endregion

        #region Properties
        /// <summary>
        /// APP's directory
        /// </summary>
        public static string AppDir
        {
            get
            {
                // Read the current dir
                Core._AppDir ??= Core.GetAppDir();

                // Return the dir
                return Core._AppDir;
            }
        }

        /// <summary>
        /// Current assembly
        /// </summary>
        public static Assembly Assembly
        {
            get
            {
                // Get current assembly
                Core._Assembly ??= Assembly.GetEntryAssembly();

                // Return assembly
                return Core._Assembly;
            }
        }

        /// <summary>
        /// APP's product name
        /// </summary>
        public static string ProductName
        {
            get
            {
                // Reads the product name
                Core._ProductName ??= Core.Assembly.GetProductName();

                // Return the name
                return Core._ProductName;
            }
        }
        #endregion

        #region Private
        /// <summary>
        /// Get the APP's directory
        /// </summary>
        private static string GetAppDir()
        {
            // Tries by assembly
            Assembly assembly = Core.Assembly;
            if (assembly != null)
            {
                // Get file location
                string location = assembly.Location;
                if (!string.IsNullOrEmpty(location))
                    return Path.GetDirectoryName(location);
            }

            // Tries by domain
            AppDomain domain = AppDomain.CurrentDomain;
            if (domain != null)
            {
                // Get file location
                string location = domain.BaseDirectory;
                if (!string.IsNullOrEmpty(location))
                    return location;
            }

            // None found, returns temporary
            return Path.GetTempPath();
        }
        #endregion
    }
}
