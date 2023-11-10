using Hardmob.Helpers;
using IniParser.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Hardmob
{
    /// <summary>
    /// Sending messages using Telegram Bot
    /// </summary>
    sealed class TelegramBot : IDisposable
    {
        #region Constants
        /// <summary>
        /// Chat ID configuration key
        /// </summary>
        private const string CHAT_KEY = """chat""";

        /// <summary>
        /// Queue default path name
        /// </summary>
        private const string DEFAULT_QUEUE_PATH = """telegram-queue""";

        /// <summary>
        /// Queue path key
        /// </summary>
        private const string QUEUE_PATH_KEY = """queue""";

        /// <summary>
        /// Retry sending interval, in milliseconds
        /// </summary>
        private const int RETRY_INTERVAL = 15 * 1000;

        /// <summary>
        /// Telegram API address
        /// </summary>
        private const string TELEGRAM_API_URL = """https://api.telegram.org/""";

        /// <summary>
        /// Telegram API method to send messages
        /// </summary>
        private const string TELEGRAM_SEND_MESSAGE_COMMAND = """sendMessage""";

        /// <summary>
        /// Token configuration key
        /// </summary>
        private const string TOKEN_KEY = """token""";
        #endregion

        #region Variables
        /// <summary>
        /// Waiting for <see cref="_Active"/> changes
        /// </summary>
        private readonly ManualResetEvent _ActiveWait = new(false);

        /// <summary>
        /// Chat ID
        /// </summary>
        private readonly long _Chat;

        /// <summary>
        /// Messages to be sent (files)
        /// </summary>
        private readonly ConcurrentQueue<string> _Queued = new();

        /// <summary>
        /// Waiting for messages in queue
        /// </summary>
        private readonly AutoResetEvent _QueuedWait = new(false);

        /// <summary>
        /// Path to write queued messages
        /// </summary>
        private readonly string _QueuePath;

        /// <summary>
        /// Sync for creating queue sender
        /// </summary>
        private readonly object _QueueSenderLock = new();

        /// <summary>
        /// Bot's secret token
        /// </summary>
        private readonly string _Token;

        /// <summary>
        /// Bot is active
        /// </summary>
        private bool _Active = true;

        /// <summary>
        /// Sending queued messages
        /// </summary>
        private Thread _QueueSender;
        #endregion

        #region Constructors
        /// <summary>
        /// New Telegram bot
        /// </summary>
        /// <param name="configurations">Bot's configurations</param>
        public TelegramBot(KeyDataCollection configurations)
        {
            // Check configuration
            if (configurations == null)
                throw new ArgumentNullException(nameof(configurations));

            // Get token and chat ID
            this._Token = configurations.ContainsKey(TOKEN_KEY) ? configurations[TOKEN_KEY] : throw new ArgumentNullException(TOKEN_KEY);
            this._Chat = configurations.ContainsKey(CHAT_KEY) && long.TryParse(configurations[CHAT_KEY], out long chat) ? chat : throw new ArgumentOutOfRangeException(CHAT_KEY);

            // Queued message path
            this._QueuePath = configurations.ContainsKey(QUEUE_PATH_KEY) ? Path.Combine(Core.AppDir, configurations[QUEUE_PATH_KEY]) : Path.Combine(Core.AppDir, DEFAULT_QUEUE_PATH);

            // Check if queue path contains files
            if (Directory.Exists(this._QueuePath) && Directory.EnumerateFiles(this._QueuePath).Any())
            {
                // Add all files to queue
                foreach (string file in Directory.EnumerateFiles(this._QueuePath))
                    this._Queued.Enqueue(file);

                // Start sending all queued messages
                this.StartQueueSender();
            }
        }

        /// <inheritdoc/>
        void IDisposable.Dispose()
        {
            // Set as not active
            this._Active = false;
            this._ActiveWait.Set();
        }
        #endregion

        #region Public
        /// <summary>
        /// Send message by bot
        /// </summary>
        /// <param name="text">Texto of message</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null</exception>
        /// <remarks>The message will be enqueued for later send if not succeeded</remarks>
        public void SendMessage(string text)
        {
            // Validate input
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            try
            {
                // Send the message
                this.SendMessageInternal(text);
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Other exceptions
            catch (Exception ex)
            {
                // Add to queue to be send later
                this.EnqueueMessage(text);

                // Log exception
                ex.Log();
            }
        }
        #endregion

        #region Private
        /// <summary>
        /// Enqueue message to be send later, will save it as file
        /// </summary>
        private void EnqueueMessage(string text)
        {
            // Queue path must exists
            Directory.CreateDirectory(this._QueuePath);

            // Randomize queue file name
            string file = Path.Combine(this._QueuePath, $"{Guid.NewGuid():N}.txt");

            // Save message to file
            File.WriteAllText(file, text);

            // Add to queue to be send later
            this._Queued.Enqueue(file);
            this._QueuedWait.Set();

            // Start que queue sender
            this.StartQueueSender();
        }

        /// <summary>
        /// Prepare message to be sent
        /// </summary>
        private byte[] PrepareMessage(string text)
        {
            // Prepare data
            JObject json = new();
            json.Add("""chat_id""", this._Chat);
            json.Add("""text""", text);

            // Prepare full message
            string rawmessage = json.ToString(Newtonsoft.Json.Formatting.None);

            // Encode to UTF-8
            return Encoding.UTF8.GetBytes(rawmessage);
        }

        /// <summary>
        /// Sending queued messages
        /// </summary>
        private void QueueSender()
        {
            try
            {
                // Signals to wait
                WaitHandle[] waits = new WaitHandle[] { this._QueuedWait, this._ActiveWait };

                // While bot is active
                while (this._Active)
                {
                    // Get next queued message, does not remove from queue yet
                    if (this._Queued.TryPeek(out string file))
                    {
                        try
                        {
                            // File does exists?
                            if (File.Exists(file))
                            {
                                // Read message from file
                                string message = File.ReadAllText(file);

                                // Send message
                                this.SendMessageInternal(message);

                                // Remove file
                                File.Delete(file);
                            }

                            // Remove from queue
                            this._Queued.TryDequeue(out _);
                        }

                        // Throws abort exceptions
                        catch (ThreadAbortException) { throw; }

                        // Ignore other exceptions
                        catch
                        {
                            // But wait for some time before trying again
                            this._ActiveWait.WaitOne(RETRY_INTERVAL);
                        }
                    }
                    else
                    {
                        // Wait for the next message
                        WaitHandle.WaitAny(waits);
                    }
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Report other exceptions
            catch (Exception ex) { ex.Log(); }
        }

        /// <summary>
        /// Try sending message
        /// </summary>
        private void SendMessageInternal(string text)
        {
            // Prepare message to be sent
            byte[] data = this.PrepareMessage(text);

            // Initializing HTTPS connection
            HttpWebRequest connection = (HttpWebRequest)HttpWebRequest.Create($"{TELEGRAM_API_URL}bot{this._Token}/{TELEGRAM_SEND_MESSAGE_COMMAND}");
            connection.Method = """POST""";
            connection.KeepAlive = false;
            connection.ContentType = """application/json;charset=utf-8""";
            connection.ContentLength = data.Length;

            // Connects and them post data
            using (Stream connectionstream = connection.GetRequestStream())
                connectionstream.Write(data, 0, data.Length);

            // Gets the response
            using WebResponse webresponse = connection.GetResponse();
            using Stream responsestream = webresponse.GetResponseStream();
            using StreamReader responsereader = new(responsestream, Encoding.UTF8);
            string responsetext = responsereader.ReadToEnd();

            // Check if it's all ok
            JObject json = JObject.Parse(responsetext);
            if (!json.ContainsKey("ok") || json["ok"].Type != JTokenType.Boolean || !(bool)json["ok"])
                throw new InvalidDataException($"Invalid Telegram response: {responsetext}");
        }

        /// <summary>
        /// Start sending queued messages
        /// </summary>
        private void StartQueueSender()
        {
            // Does not have a sender yet?
            if (this._QueueSender == null)
            {
                // Sync for creation, avoiding duplicates
                lock (this._QueueSenderLock)
                {
                    // Still doesn't have a sender?
                    if (this._QueueSender == null)
                    {
                        // Create and start sender
                        Thread sender = new(this.QueueSender);
                        sender.Priority = ThreadPriority.BelowNormal;
                        sender.IsBackground = true;
                        sender.Start();
                        this._QueueSender = sender;
                    }
                }
            }
        }
        #endregion
    }
}
