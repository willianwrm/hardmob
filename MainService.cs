using Hardmob.Helpers;
using IniParser.Model;
using IniParser.Parser;
using RestSharp;
using System;
using System.IO;
using System.Net;
using System.ServiceProcess;

namespace Hardmob
{
    public partial class MainService : ServiceBase
    {
        #region Constants
        /// <summary>
        /// Application configuration section name
        /// </summary>
        private const string APP_SECTION = """App""";

        /// <summary>
        /// Configuration file name
        /// </summary>
        private const string CONFIGURATION_FILE = """config.ini""";

        /// <summary>
        /// Sample configuration file name
        /// </summary>
        private const string CONFIGURATION_SAMPLE_FILE = """config-sample.ini""";

        /// <summary>
        /// Crawler configuration section name
        /// </summary>
        private const string CRAWLER_SECTION = """Crawler""";

        /// <summary>
        /// REST proxy configuration
        /// </summary>
        private const string REST_SECTION = """REST""";

        /// <summary>
        /// Time before thread abortion when stopping the service
        /// </summary>
        private const int STOP_ABORT_TIMEOUT = 4 * 1000;

        /// <summary>
        /// Telegram configuration section name
        /// </summary>
        private const string TELEGRAM_SECTION = """Telegram""";

        /// <summary>
        /// User agent configuration key
        /// </summary>
        private const string USER_AGENT_KEY = """useragent""";
        #endregion

        #region Variables
        /// <summary>
        /// Forum crawler
        /// </summary>
        private Crawler _Crawler;

        /// <summary>
        /// Telegram's bot
        /// </summary>
        private TelegramBot _Telegram;
        #endregion

        #region Constructors
        /// <summary>
        /// Create a new instance of the main service
        /// </summary>
        public MainService()
        {
            // Create components
            this.InitializeComponent();
        }
        #endregion

        #region Internal
        /// <summary>
        /// Simulates the service start
        /// </summary>
        internal void StartDebug() => this.OnStart(null);

        /// <summary>
        /// Simulates the service stop
        /// </summary>
        internal void StopDebug() => this.OnStop();
        #endregion

        #region Protected
        /// <inheritdoc/>
        protected override void OnStart(string[] args)
        {
            // Configuring web security
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            ServicePointManager.ReusePort = true;

            // INI file name
            string inifile = Path.Combine(Core.AppDir, CONFIGURATION_FILE);

            // But file does not exists?
            if (!File.Exists(inifile))
            {
                // Sample INI and project INI files
                string samplefile = Path.Combine(Core.AppDir, $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}{CONFIGURATION_SAMPLE_FILE}");
                string projectfile = Path.GetFullPath(Path.Combine(Core.AppDir, $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}{CONFIGURATION_FILE}"));

                // Copy sample to project INI if available
                if (File.Exists(samplefile) && !File.Exists(projectfile))
                    File.Copy(samplefile, projectfile, true);

                // Create link from project to local INI if available
                if (File.Exists(projectfile))
                    FileHelper.CopyLink(projectfile, inifile);
            }

            // Using INI file parser
            IniDataParser parser = new();

            // Parse configuration file
            IniData ini = parser.Parse(File.ReadAllText(inifile));

            // Fetch configurations
            KeyDataCollection app = ini[APP_SECTION];
            Core.WebUserAgent = app.ContainsKey(USER_AGENT_KEY) && !string.IsNullOrWhiteSpace(app[USER_AGENT_KEY]) ? app[USER_AGENT_KEY] : null;

            // Create telegram bot
            this._Telegram = new(ini[TELEGRAM_SECTION]);

            // REST redirect, used only by crawler when fails
            RestClientOptions redirect = null;

            // Does have REST configuration?
            if (ini.Sections.ContainsSection(REST_SECTION))
            {
                // Get configuration
                KeyDataCollection restconfig = ini[REST_SECTION];
                string address = restconfig["""address"""];
                string user = restconfig["""user"""];
                string password = restconfig["""password"""];

                // Has address?
                if (!string.IsNullOrWhiteSpace(address))
                {
                    // Create proxy
                    WebProxy proxy = new();
                    proxy.Address = new(address);

                    // Has user? Create credentials
                    if (!string.IsNullOrWhiteSpace(user))
                        proxy.Credentials = new NetworkCredential(user, password);

                    // Create redirect
                    redirect = new();
                    redirect.Proxy = proxy;

                    // Accepting any certificate
                    redirect.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
            }

            // Create crawler
            this._Crawler = new(ini[CRAWLER_SECTION], this._Telegram, redirect);
        }

        /// <inheritdoc/>
        protected override void OnStop()
        {
            // Signals to stop
            this._Telegram?.Dispose();
            this._Crawler?.Dispose();

            // Abort threads
            ThreadHelper.SafeAbort(STOP_ABORT_TIMEOUT, this._Telegram?.QueueSenderThread, this._Crawler?.CrawlerThread);
        }
        #endregion
    }
}
