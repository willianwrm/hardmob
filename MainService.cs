using Hardmob.Helpers;
using IniParser.Model;
using IniParser.Parser;
using System.IO;
using System.ServiceProcess;

namespace Hardmob
{
    public partial class MainService : ServiceBase
    {
        #region Constants
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
        private const string CRAWLER_SECTION = """crawler""";

        /// <summary>
        /// Time before thread abortion when stopping the service
        /// </summary>
        private const int STOP_ABORT_TIMEOUT = 4 * 1000;

        /// <summary>
        /// Telegram configuration section name
        /// </summary>
        private const string TELEGRAM_SECTION = """Telegram""";
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

            // Create telegram bot
            this._Telegram = new(ini[TELEGRAM_SECTION]);

            // Create crawler
            this._Crawler = new(ini[CRAWLER_SECTION], this._Telegram);
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
