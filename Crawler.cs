using Hardmob.Helpers;
using IniParser.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;

namespace Hardmob
{
    sealed class Crawler : IDisposable
    {
        #region Constants
        /// <summary>
        /// Main URL prefix
        /// </summary>
        private const string BASE_URL = $"""https://{SERVER}/""";

        /// <summary>
        /// Default pool full interval
        /// </summary>
        private const int DEFAULT_POOL_FULL_INTERVAL = 15 * 60 * 1000;

        /// <summary>
        /// Default pool interval
        /// </summary>
        private const int DEFAULT_POOL_INTERVAL = 15 * 1000;

        /// <summary>
        /// Filename to the default state file
        /// </summary>
        private const string DEFAULT_STATE_FILE = """state.json""";

        /// <summary>
        /// Default tries before log
        /// </summary>
        private const int DEFAULT_TRIES_BEFORE_LOG = 60;

        /// <summary>
        /// Forums URL suffix
        /// </summary>
        private const string FORUMS_URL = """forums""";

        /// <summary>
        /// Maximum value for pool interval
        /// </summary>
        private const int MAXIMUM_POOL_INTERVAL = 604800000;

        /// <summary>
        /// Minimum value for pool interval
        /// </summary>
        private const int MINIMUM_POOL_INTERVAL = 1;

        /// <summary>
        /// Next thread state key
        /// </summary>
        private const string NEXT_THREAD_KEY = """nextthread""";

        /// <summary>
        /// Pool full interval configuration key
        /// </summary>
        private const string POOL_FULL_INTERVAL_KEY = """poolfullinterval""";

        /// <summary>
        /// Pool interval configuration key
        /// </summary>
        private const string POOL_INTERVAL_KEY = """poolinterval""";

        /// <summary>
        /// Forum ID for promo
        /// </summary>
        private const int PROMO_FORUM_ID = 407;

        /// <summary>
        /// Limit size for message in sendMessage command
        /// </summary>
        private const int SEND_MESSAGE_TEXT_LENGTH = 4096;

        /// <summary>
        /// Limit size for caption in sendPhoto command
        /// </summary>
        private const int SEND_PHOTO_CAPTION_LENGTH = 1024;

        /// <summary>
        /// Server host address
        /// </summary>
        private const string SERVER = """www.hardmob.com.br""";

        /// <summary>
        /// State file configuration key
        /// </summary>
        private const string STATE_FILE_KEY = """statefile""";

        /// <summary>
        /// Threads URL suffix
        /// </summary>
        private const string THREADS_URL = """threads""";

        /// <summary>
        /// Tries before log configuration key
        /// </summary>
        private const string TRIES_BEFORE_LOG_KEY = """triesbeforelog""";

        /// <summary>
        /// Suffix for thread ID
        /// </summary>
        private static readonly char[] THREAD_ID_SUFIX = new char[] { '_', '-', ' ' };
        #endregion

        #region Variables
        /// <summary>
        /// Waiting for <see cref="_Active"/> changes
        /// </summary>
        private readonly ManualResetEvent _ActiveWait = new(false);

        /// <summary>
        /// Telegram bot
        /// </summary>
        private readonly TelegramBot _Bot;

        /// <summary>
        /// Cache policy
        /// </summary>
        private readonly RequestCachePolicy _Cache = new(RequestCacheLevel.NoCacheNoStore);

        /// <summary>
        /// Web cookies
        /// </summary>
        private readonly CookieContainer _Cookies = new();

        /// <summary>
        /// Main thread
        /// </summary>
        private readonly Thread _MainThread;

        /// <summary>
        /// Maximum time before a full check in milliseconds
        /// </summary>
        private readonly int _PoolFullInterval;

        /// <summary>
        /// Interval between checks in milliseconds
        /// </summary>
        private readonly int _PoolInterval;

        /// <summary>
        /// File to read and save state data
        /// </summary>
        private readonly string _StateFile;

        /// <summary>
        /// Maximum tries before registering exception to the bot and log
        /// </summary>
        private readonly int _TriesBeforeLog;

        /// <summary>
        /// Crawler is active
        /// </summary>
        private bool _Active = true;

        /// <summary>
        /// Next thread to fetch
        /// </summary>
        private long _NextThread;
        #endregion

        #region Constructors
        /// <summary>
        /// Create and start new crawler
        /// </summary>
        public Crawler(KeyDataCollection configurations, TelegramBot bot)
        {
            // Check configuration
            if (configurations == null)
                throw new ArgumentNullException(nameof(configurations));
            this._Bot = bot ?? throw new ArgumentNullException(nameof(bot));

            // Fetch the configurations
            this._PoolInterval = configurations.ContainsKey(POOL_INTERVAL_KEY) && int.TryParse(configurations[POOL_INTERVAL_KEY], out int poolinterval) ? poolinterval : DEFAULT_POOL_INTERVAL;
            this._PoolFullInterval = configurations.ContainsKey(POOL_FULL_INTERVAL_KEY) && int.TryParse(configurations[POOL_FULL_INTERVAL_KEY], out int poolfullinterval) ? poolfullinterval : DEFAULT_POOL_FULL_INTERVAL;
            this._TriesBeforeLog = configurations.ContainsKey(TRIES_BEFORE_LOG_KEY) && int.TryParse(configurations[TRIES_BEFORE_LOG_KEY], out int triesbeforelog) ? triesbeforelog : DEFAULT_TRIES_BEFORE_LOG;
            this._StateFile = Path.GetFullPath(Path.Combine(Core.AppDir, configurations.ContainsKey(STATE_FILE_KEY) && !string.IsNullOrWhiteSpace(configurations[STATE_FILE_KEY]) ? configurations[STATE_FILE_KEY] : DEFAULT_STATE_FILE));

            // Check limits
            if (this._PoolInterval < MINIMUM_POOL_INTERVAL)
                this._PoolInterval = MINIMUM_POOL_INTERVAL;
            if (this._PoolInterval > MAXIMUM_POOL_INTERVAL)
                this._PoolInterval = MAXIMUM_POOL_INTERVAL;
            if (this._PoolFullInterval < MINIMUM_POOL_INTERVAL)
                this._PoolFullInterval = MINIMUM_POOL_INTERVAL;
            if (this._PoolFullInterval > MAXIMUM_POOL_INTERVAL)
                this._PoolFullInterval = MAXIMUM_POOL_INTERVAL;

            // Check for state file
            if (File.Exists(this._StateFile))
            {
                try
                {
                    // Load state file
                    JObject state = JObject.Parse(File.ReadAllText(this._StateFile));

                    // Get next thread
                    if (state.ContainsKey(NEXT_THREAD_KEY))
                        this._NextThread = (long)state[NEXT_THREAD_KEY];
                }

                // Throws abort exceptions
                catch (ThreadAbortException) { throw; }

                // Only register any other exception
                catch (Exception ex) { ex.Log(); }
            }

            // Create main thread
            this._MainThread = new(this.Main);
            this._MainThread.Start();
        }
        #endregion

        #region Properties
        /// <summary>
        /// Main thread
        /// </summary>
        public Thread CrawlerThread => this._MainThread;
        #endregion

        #region Public
        /// <inheritdoc/>
        public void Dispose()
        {
            // Set as inactive
            this._Active = false;
            this._ActiveWait.Set();
        }
        #endregion

        #region Private
        /// <summary>
        /// Create new connection to host
        /// </summary>
        private HttpWebRequest CreateConnection(string url)
        {
            // Initializing connection
            HttpWebRequest connection = Core.CreateWebRequest(url);
            connection.CachePolicy = this._Cache;
            connection.CookieContainer = this._Cookies;
            connection.KeepAlive = true;

            // Return connection
            return connection;
        }

        /// <summary>
        /// Read text from a thread ID
        /// </summary>
        private ThreadStatus GetThread(long id, out string text)
        {
            try
            {
                // Initializing connection
                HttpWebRequest connection = this.CreateConnection($"{BASE_URL}{THREADS_URL}/{id}");

                // Gets the response
                using WebResponse webresponse = connection.GetResponse();
                text = webresponse.GetResponseText();

                // It's look like valid
                return ThreadStatus.Public;
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // HTTP exceptions
            catch (WebException wex)
            {
                // Does have a response?
                if (wex.Response is HttpWebResponse httpresponse)
                {
                    // Check for the result
                    switch (httpresponse.StatusCode)
                    {
                        // Probably valid thread but not public
                        case HttpStatusCode.Unauthorized:
                        case HttpStatusCode.PaymentRequired:
                        case HttpStatusCode.Forbidden:
                            text = null;
                            return ThreadStatus.Private;

                        // Not found
                        case HttpStatusCode.NotFound:
                            text = null;
                            return ThreadStatus.NotFound;
                    }
                }

                // Continue with exception
                throw;
            }
        }

        /// <summary>
        /// Main thread
        /// </summary>
        private void Main()
        {
            try
            {
                // Sequential exceptions
                int exceptions = 0;

                // Last time a update worked
                int lastupdate = Environment.TickCount;

                // Last save state
                long lastsavestate = this._NextThread;

                // Debug
                Debug.WriteLine("Crawler started");

                // While active
                while (this._Active)
                {
                    try
                    {
                        // Debug
                        Debug.WriteLine($"Next thread: {this._NextThread}");

                        // Need to fetch older thread id?
                        if (this._NextThread <= 0 || (Environment.TickCount - lastupdate) > this._PoolFullInterval)
                        {
                            // Debug
                            Debug.Write($"Full fetch... ");

                            // List all available IDs
                            HashSet<long> ids = new(this.ThreadsID());

                            // Debug
                            Debug.WriteLine($"ok, {ids.Count} threads");

                            // Did get any thread?
                            if (ids.Count > 0)
                            {
                                // Get the maximum ID
                                long maximum = ids.Max();

                                // Missed any thread?
                                if (this._NextThread > 0 && maximum >= this._NextThread)
                                {
                                    // Fetching every missed thread
                                    for (long i = this._NextThread; i <= maximum; i++)
                                    {
                                        // Process it if in forum
                                        if (ids.Contains(i))
                                            this.ProcessThread(i);
                                    }
                                }

                                // Update last full update
                                lastupdate = Environment.TickCount;

                                // Update next fetch
                                this._NextThread = maximum + 1;
                            }
                        }
                        else
                        {
                            // While processing threads
                            while (this._Active)
                            {
                                // Fetch next tread
                                ThreadStatus status = this.GetThread(this._NextThread, out string threadtext);

                                // Not found, does not exist yet
                                if (status == ThreadStatus.NotFound)
                                    break;

                                // Does exists but it's private
                                else if (status == ThreadStatus.Private)
                                    this._NextThread++;

                                // Does exists and it's public
                                else if (status == ThreadStatus.Public)
                                {
                                    // Parse the thread
                                    if (PromoThread.TryParse(threadtext, this._NextThread, $"{BASE_URL}{THREADS_URL}/{this._NextThread}", out PromoThread thread))
                                    {
                                        // Check if it's a promo thread
                                        if (this.ThreadsID().Contains(this._NextThread))
                                        {
                                            // Process the thread
                                            this.ProcessThread(thread);

                                            // Update last fast update
                                            lastupdate = Environment.TickCount;
                                        }
                                    }

                                    // Next thread
                                    this._NextThread++;
                                }

                                // Unknown
                                else
                                    throw new NotImplementedException();

                                // Reset exception count
                                exceptions = 0;
                            }
                        }

                        // Next thread ID changed since last save?
                        if (lastsavestate != this._NextThread)
                        {
                            // Save current state
                            this.SaveState();
                            lastsavestate = this._NextThread;
                        }
                    }

                    // Throw abortion
                    catch (ThreadAbortException) { throw; }

                    // Other exceptions
                    catch (Exception ex)
                    {
                        // Too many exceptions?
                        if (this._TriesBeforeLog >= 0 && exceptions++ == this._TriesBeforeLog)
                        {
                            // Register exception in log
                            ex.Log();

                            // Notify bot about it
                            this._Bot.SendMessage($"<b>Server exception</b>\r\n<b>Message:</b> {WebUtility.HtmlEncode(ex.Message)}\r\n<b>Type:</b> {WebUtility.HtmlEncode(ex.GetType().FullName)}", TelegramParseModes.HTML);
                        }

                        // Next thread ID changed since last save?
                        if (lastsavestate != this._NextThread)
                        {
                            // Save current state
                            this.SaveState();
                            lastsavestate = this._NextThread;
                        }
                    }

                    // Wait next pool
                    this._ActiveWait.WaitOne(this._PoolInterval);
                }

                // Save current state
                if (lastsavestate != this._NextThread)
                    this.SaveState();
            }

            // Ignore abort exceptions
            catch (ThreadAbortException) {; }

            // Report any other exception
            catch (Exception ex) { ex.Log(); }
        }

        /// <summary>
        /// Fetch and process thread ID
        /// </summary>
        private void ProcessThread(long id)
        {
            // Debug
            Debug.Write($"Processing thread {id}... ");

            // Fetch HTML data
            if (this.GetThread(id, out string threadtext) == ThreadStatus.Public)
            {
                // Parse data and process
                if (PromoThread.TryParse(threadtext, id, $"{BASE_URL}{THREADS_URL}/{id}", out PromoThread thread))
                    this.ProcessThread(thread);

                // Debug
                Debug.WriteLine("ok");
            }
            else
            {
                // Debug
                Debug.WriteLine("failed");
            }
        }

        /// <summary>
        /// Process thread
        /// </summary>
        private void ProcessThread(PromoThread thread)
        {
            // Message to be sent
            StringBuilder output = new();

            // Message without values, used to compute parsed size
            StringBuilder parsed = new();

            // Write main post
            output.AppendLine($"<a href=\"{thread.URL}\">{WebUtility.HtmlEncode(thread.Title)}</a>");
            parsed.AppendLine(thread.Title);

            // Has custom link?
            if (thread.Link != null)
            {
                // Write custom link
                output.AppendLine();
                output.AppendLine($"<a href=\"{thread.Link}\">LINK</a>");
                parsed.AppendLine();
                parsed.AppendLine("LINK");
            }

            // Has any extra info?
            if (thread.Extra != null)
            {
                // Write description
                output.AppendLine();
                output.AppendLine(thread.Extra);
                parsed.AppendLine();
                parsed.AppendLine(thread.Extra);
            }

            // Has image?
            if (thread.Image != null)
            {
                // Prepare caption message
                string caption = output.ToString();

                // Limiting the length
                if (parsed.Length > SEND_PHOTO_CAPTION_LENGTH)
                    caption = caption.Substring(0, caption.Length - (parsed.Length - SEND_PHOTO_CAPTION_LENGTH));

                // Send as photo
                this._Bot.SendPhoto(thread.Image, caption, TelegramParseModes.HTML);
            }
            else
            {
                // Prepare text message
                string text = output.ToString();

                // Limiting the length
                if (parsed.Length > SEND_MESSAGE_TEXT_LENGTH)
                    text = text.Substring(0, text.Length - (parsed.Length - SEND_MESSAGE_TEXT_LENGTH));

                // Send as message
                this._Bot.SendMessage(text, TelegramParseModes.HTML);
            }
        }

        /// <summary>
        /// Save current state
        /// </summary>
        private void SaveState()
        {
            // Debug
            Debug.Write("Saving state... ");

            // Create state data
            JObject state = new();
            state[NEXT_THREAD_KEY] = this._NextThread;

            // Save to file
            File.WriteAllText(this._StateFile, state.ToString(Newtonsoft.Json.Formatting.None));

            // Debug
            Debug.WriteLine("ok");
        }

        /// <summary>
        /// List all threads ID from the promo forum
        /// </summary>
        private IEnumerable<long> ThreadsID()
        {
            // Initializing connection
            HttpWebRequest connection = this.CreateConnection($"{BASE_URL}{FORUMS_URL}/{PROMO_FORUM_ID}");

            // Gets the response
            using WebResponse webresponse = connection.GetResponse();
            string response = webresponse.GetResponseText();

            // Current search position
            int pos = 0;

            // While not read all input
            // We are searching for: <li class="threadbit {class}" id="thread_{id}">
            // TODO: search for all '<li', extract the informations and the fetch data; the host can change their order any time
            while (true)
            {
                // Try find next thread id
                int next = response.IndexOf("""<li class="threadbit""", pos, StringComparison.OrdinalIgnoreCase);
                if (next < 0)
                    break;

                // Get the ID parameter, start
                int idstart = response.IndexOf("id=\"", next, StringComparison.OrdinalIgnoreCase);
                if (idstart < 0)
                    break;

                // Get the ID parameter, end
                int idend = response.IndexOf("\"", idstart + 4);
                if (idend < 0)
                    break;

                // Get the ID, RAW
                string idraw = response.Substring(idstart + 4, idend - idstart - 4);

                // Remove any suffix
                int suffix = idraw.LastIndexOfAny(THREAD_ID_SUFIX);
                if (suffix > 0)
                    idraw = idraw.Substring(suffix + 1);

                // Try parse to ID
                if (long.TryParse(idraw, out long id))
                    yield return id;

                // Update next search position
                pos = idend;
            }
        }
        #endregion
    }
}
