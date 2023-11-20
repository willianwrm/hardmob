using Hardmob.Helpers;
using IniParser.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Resources;
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
        /// Telegram API method to send photos
        /// </summary>
        private const string TELEGRAM_SEND_PHOTO_COMMAND = """sendPhoto""";

        /// <summary>
        /// Token configuration key
        /// </summary>
        private const string TOKEN_KEY = """token""";
        #endregion

        #region Variables
        /// <summary>
        /// Class resource cache
        /// </summary>
        private static WeakReference<ResourceManager> _Resource;

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
            this._Token = configurations.ContainsKey(TOKEN_KEY) && !string.IsNullOrWhiteSpace(configurations[TOKEN_KEY]) ? configurations[TOKEN_KEY] : throw new ArgumentNullException(TOKEN_KEY, TelegramBot.ResourceManager.GetString("EmptyToken"));
            this._Chat = configurations.ContainsKey(CHAT_KEY) && long.TryParse(configurations[CHAT_KEY], out long chat) ? chat : throw new ArgumentOutOfRangeException(CHAT_KEY, TelegramBot.ResourceManager.GetString("EmptyChatID"));

            // Queued message path
            this._QueuePath = configurations.ContainsKey(QUEUE_PATH_KEY) && !string.IsNullOrWhiteSpace(configurations[QUEUE_PATH_KEY]) ? Path.GetFullPath(Path.Combine(Core.AppDir, configurations[QUEUE_PATH_KEY])) : Path.Combine(Core.AppDir, DEFAULT_QUEUE_PATH);

            // Check if queue path contains files
            if (Directory.Exists(this._QueuePath) && Directory.EnumerateFiles(this._QueuePath).Any())
            {
                // Get all files in queue path
                List<string> queuefiles = new(Directory.EnumerateFiles(this._QueuePath));

                // Sort then to be added in the order they were written
                queuefiles.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));

                // Add all files to queue
                foreach (string file in queuefiles)
                    this._Queued.Enqueue(file);

                // Start sending all queued messages
                this.StartQueueSender();
            }
        }
        #endregion

        #region Properties
        /// <summary>
        /// Class resources
        /// </summary>
        public static ResourceManager ResourceManager
        {
            get
            {
                // Tries by cache
                if (TelegramBot._Resource?.TryGetTarget(out ResourceManager resource) ?? false)
                    return resource;

                // Load new resource
                resource = new(typeof(TelegramBot));

                // Stores at local cache
                if (TelegramBot._Resource == null)
                    TelegramBot._Resource = new(resource);
                else
                    TelegramBot._Resource.SetTarget(resource);

                // Return resource
                return resource;
            }
        }

        /// <summary>
        /// Thread sending any queued message
        /// </summary>
        public Thread QueueSenderThread => this._QueueSender;
        #endregion

        #region Public
        /// <inheritdoc/>
        public void Dispose()
        {
            // Set as inactive
            this._Active = false;
            this._ActiveWait.Set();
        }

        /// <summary>
        /// Send message by bot
        /// </summary>
        /// <param name="text">Texto of message</param>
        /// <param name="mode">Parse mode</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null</exception>
        /// <remarks>The message will be enqueued for later send if not succeeded</remarks>
        public void SendMessage(string text, TelegramParseModes mode, bool preview)
        {
            // Validate input
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            // Prepare the message to be sent
            JObject message = new();
            message.Add("""text""", text);
            message.Add("""disable_web_page_preview""", !preview);

            // Check mode
            switch (mode)
            {
                // Parse enabled
                case TelegramParseModes.HTML:
                case TelegramParseModes.MarkdownV2:
                    message.Add("""parse_mode""", mode.ToString());
                    break;
            }

            try
            {
                // Send the message
                this.SendInternal(message);
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Other exceptions
            catch (Exception ex)
            {
                // Add to queue to be send later
                this.EnqueueMessage(message);

                // Log exception
                ex.Log();
            }
        }

        /// <summary>
        /// Send as photo, will convert the <paramref name="caption"/> to message if fail
        /// </summary>
        public void SendPhoto(string photo, string caption, TelegramParseModes mode)
        {
            // Validate input
            if (photo == null)
                throw new ArgumentNullException(nameof(photo));
            if (caption == null)
                throw new ArgumentNullException(nameof(caption));

            // Prepare the message to be sent
            JObject message = new();
            message.Add("""photo""", photo);
            message.Add("""caption""", caption);
            message.Add("""disable_web_page_preview""", true);

            // Check mode
            switch (mode)
            {
                // Parse enabled
                case TelegramParseModes.HTML:
                case TelegramParseModes.MarkdownV2:
                    message.Add("""parse_mode""", mode.ToString());
                    break;
            }

            try
            {
                // Send the message
                this.SendInternal(message);
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Other exceptions
            catch
            {
                // Continue as message
                this.SendMessage(caption, mode, false);
            }
        }
        #endregion

        #region Private
        /// <summary>
        /// Enqueue message to be send later, will save it as file
        /// </summary>
        private void EnqueueMessage(JObject message)
        {
            // Queue path must exists
            Directory.CreateDirectory(this._QueuePath);

            // Randomize queue file name
            string file = Path.Combine(this._QueuePath, $"{Guid.NewGuid():N}.json");

            // Save message to file
            File.WriteAllText(file, message.ToString(Formatting.None));

            // Add to queue to be send later
            this._Queued.Enqueue(file);
            this._QueuedWait.Set();

            // Start que queue sender
            this.StartQueueSender();
        }

        /// <summary>
        /// Prepare message to be sent
        /// </summary>
        private byte[] PrepareMessage(JObject message)
        {
            // Update to current chat id
            if (message.ContainsKey("""chat_id"""))
                message["""chat_id"""] = this._Chat;
            else
                message.Add("""chat_id""", this._Chat);

            // Prepare full message
            string rawmessage = message.ToString(Formatting.None);

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
                WaitHandle[] waits = [this._QueuedWait, this._ActiveWait];

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
                                JObject message = JObject.Parse(File.ReadAllText(file));

                                // Send message
                                this.SendInternal(message);

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

            // Ignore abort exceptions
            catch (ThreadAbortException) {; }

            // Report other exceptions
            catch (Exception ex) { ex.Log(); }
        }

        /// <summary>
        /// Try sending message
        /// </summary>
        private void SendInternal(JObject message)
        {
            // Prepare message to be sent
            byte[] data = this.PrepareMessage(message);

            // Telegram command
            string command = message.ContainsKey("""photo""") ? TELEGRAM_SEND_PHOTO_COMMAND : TELEGRAM_SEND_MESSAGE_COMMAND;

            // Initializing HTTPS connection
            HttpWebRequest connection = Core.CreateWebRequest($"{TELEGRAM_API_URL}bot{this._Token}/{command}", """POST""");
            connection.ContentLength = data.Length;
            connection.ContentType = """application/json;charset=utf-8""";
            connection.KeepAlive = false;

            // Connects and them post data
            using (Stream connectionstream = connection.GetRequestStream())
                connectionstream.Write(data, 0, data.Length);

            // Gets the response
            using WebResponse webresponse = connection.GetResponse();
            string responsetext = webresponse.GetResponseText();

            // Check if it's all ok
            JObject json = JObject.Parse(responsetext);
            if (!json.ContainsKey("ok") || json["ok"].Type != JTokenType.Boolean || !(bool)json["ok"])
                throw new InvalidDataException($"{TelegramBot.ResourceManager.GetString("InvalidResponse")}: {responsetext}");
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
