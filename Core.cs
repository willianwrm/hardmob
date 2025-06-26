// Ignore Spelling: App Hardmob

using Hardmob.Helpers;
using System.Net;
using System.Net.Http.Headers;
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
        private static string? _AppDir;

        /// <summary>
        /// Current assembly
        /// </summary>
        private static Assembly? _Assembly;

        /// <summary>
        /// Current APP's product name
        /// </summary>
        private static string? _ProductName;
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
                Core._Assembly ??= Assembly.GetEntryAssembly()!;

                // Return assembly
                return Core._Assembly;
            }
        }

        /// <summary>
        /// APP's product name
        /// </summary>
        public static string? ProductName
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

        #region Public
        /// <summary>
        /// Create default web client
        /// </summary>
        public static HttpClient CreateWebClient(CookieContainer? cookies = null)
        {
            // Creates handler
            HttpClientHandler clientHandler = new();
            clientHandler.AllowAutoRedirect = true;
            clientHandler.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip | DecompressionMethods.Brotli;
            clientHandler.CookieContainer = cookies ?? new();
            clientHandler.SslProtocols = System.Security.Authentication.SslProtocols.Tls13;

            // Creates cliente
            return new(clientHandler, true);
        }

        /// <summary>
        /// Create default web request
        /// </summary>
        public static HttpRequestMessage CreateWebRequest(string url, HttpMethod? method = null, string? host = null)
        {
            // Defaults to get
            method ??= HttpMethod.Get;

            // Create request
            HttpRequestMessage request = new(method, url);

            // Default configuration
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("""text/html"""));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("""application/xhtml+xml"""));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("""application/xml""", 0.9));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("""*/*""", 0.8));
            request.Headers.AcceptCharset.Add(new StringWithQualityHeaderValue("""UTF-8"""));
            request.Headers.AcceptCharset.Add(new StringWithQualityHeaderValue("""ISO-8859-1""", 0.9));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("""pt-BR"""));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("""en-US""", 0.8));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("""en""", 0.5));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("""pt""", 0.3));
            request.Headers.Connection.Add("""keep-alive""");
            request.Headers.Date = DateTimeOffset.UtcNow;
            request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse("""Mozilla/5.0"""));
            request.Version = HttpVersion.Version30;
            if (host != null)
                request.Headers.Host = host;

            // Returns default request
            return request;
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
                    return Path.GetDirectoryName(location)!;
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
