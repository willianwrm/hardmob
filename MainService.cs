using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Hardmob.Helpers;
using IniParser.Model;
using IniParser.Parser;

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
        /// Stops the service
        /// </summary>
        private readonly CancellationTokenSource _Cancellation = new();

        /// <summary>
        /// Main thread
        /// </summary>
        private readonly Thread _Main;

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

            // Create main thread
            this._Main = new(this.Main);
        }
        #endregion

        #region Internal
        /// <summary>
        /// Simulates the service start
        /// </summary>
        internal void Start() => this.OnStart(null);
        #endregion

        #region Protected
        /// <inheritdoc/>
        protected override void OnStart(string[] args)
        {
            // Starts the main thread
            this._Main.Start();
        }

        /// <inheritdoc/>
        protected override void OnStop()
        {
            // Signals to stop
            this._Cancellation.Cancel();

            // Aborts main service
            this._Main.SafeAbort(STOP_ABORT_TIMEOUT);
        }
        #endregion

        #region Private
        /// <summary>
        /// Loads configuration file
        /// </summary>
        private void LoadConfigurations()
        {
            // Using INI file parser
            IniDataParser parser = new();

            // Parse configuration file
            IniData ini = parser.Parse(File.ReadAllText(Path.Combine(Core.AppDir, CONFIGURATION_FILE)));

            // Check for telegram section and create bot
            if (ini.Sections.ContainsSection(TELEGRAM_SECTION))
                this._Telegram = new(ini[TELEGRAM_SECTION]);
        }

        /// <summary>
        /// Main service
        /// </summary>
        private void Main()
        {
            try
            {
                // Load configuration file
                this.LoadConfigurations();

                // Run while not canceled
                while (!this._Cancellation.IsCancellationRequested)
                {
                    

                    return;
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Ignore cancellations
            catch (OperationAbortedException) {; }

            // Report other exceptions
            catch (Exception ex) { ex.Log(); }
        }
        #endregion
    }
}
