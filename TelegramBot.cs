using Hardmob.Helpers;
using ImageMagick;
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
        /// JSON content-type header value
        /// </summary>
        private const string JSON_CONTENT_TYPE = """application/json;charset=utf-8""";

        /// <summary>
        /// Maximum size for file photo size, in bytes
        /// </summary>
        private const int MAXIMUM_PHOTO_FILE_SIZE = 10 * 1000 * 1000;

        /// <summary>
        /// Maximum size for photo size, in pixels
        /// </summary>
        private const int MAXIMUM_PHOTO_SIZE = 2048;

        /// <summary>
        /// Maximum 
        /// </summary>
        private const int MAXIMUM_PHOTO_TRIES = 3;

        /// <summary>
        /// Queue path key
        /// </summary>
        private const string QUEUE_PATH_KEY = """queue""";

        /// <summary>
        /// Retry sending interval, in milliseconds
        /// </summary>
        private const int RETRY_INTERVAL = 5 * 1000;

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
        public void SendMessage(string text, TelegramParseModes mode)
        {
            // Validate input
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            // Prepare the message to be sent
            JObject message = new();
            message.Add("""text""", text);
            message.Add("""disable_web_page_preview""", true);
            SetParseMode(message, mode);

            // Send or enqueue
            this.SendOrEnqueue(message);
        }

        /// <summary>
        /// Send photo by bot
        /// </summary>
        /// <param name="photoUrl">Photo full URL</param>
        /// <param name="caption">Text with the photo</param>
        /// <param name="mode"><paramref name="caption"/> parse mode</param>
        /// <remarks>The photo will be enqueued for later send if not succeeded, will send as message if error persist</remarks>
        public void SendPhoto(string photoUrl, string caption, TelegramParseModes mode)
        {
            // Validate input
            if (photoUrl == null)
                throw new ArgumentNullException(nameof(photoUrl));

            // Prepare the message to be sent
            JObject message = new();
            message.Add("""photo""", photoUrl);
            if (!string.IsNullOrEmpty(caption))
                message.Add("""caption""", caption);
            SetParseMode(message, mode);

            // Send or enqueue
            this.SendOrEnqueue(message);
        }
        #endregion

        #region Private
        /// <summary>
        /// Check the Telegram server result, throws exception if fail
        /// </summary>
        private static void CheckTelegramResponse(HttpWebRequest connection)
        {
            try
            {
                // Gets the response
                using WebResponse webresponse = connection.GetResponse();
                string responsetext = webresponse.GetResponseText();

                // Check if it's all ok
                JObject json = JObject.Parse(responsetext);
                if (!json.ContainsKey("ok") || json["ok"].Type != JTokenType.Boolean || !(bool)json["ok"])
                    throw new WebException($"{TelegramBot.ResourceManager.GetString("InvalidResponse")}: {responsetext}{Environment.NewLine}{json.ToString(Formatting.None)}", WebExceptionStatus.UnknownError);
            }

            // Web exceptions
            catch (WebException wex)
            {
                // Is there a response?
                string errortext = wex.Response?.GetResponseText();
                if (errortext != null)
                    throw new WebException($"{TelegramBot.ResourceManager.GetString("InvalidResponse")}: {wex.Message}{Environment.NewLine}{errortext}", wex, wex.Status, wex.Response);

                // Nothing to do
                throw;
            }
        }

        /// <summary>
        /// Update the message for parse mode
        /// </summary>
        private static void SetParseMode(JObject message, TelegramParseModes mode)
        {
            // Check mode
            switch (mode)
            {
                // Parse enabled
                case TelegramParseModes.HTML:
                case TelegramParseModes.MarkdownV2:
                    message.Add("""parse_mode""", mode.ToString());
                    break;

                // No parse
                case TelegramParseModes.Text:
                    message.Remove("""parse_mode""");
                    break;
            }
        }

        /// <summary>
        /// Try to load message from file
        /// </summary>
        private static bool TryLoadMessage(string file, out JObject message)
        {
            try
            {
                // Load message
                message = JObject.Parse(File.ReadAllText(file));
                return true;
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Unable to load file
            catch
            {
                try
                {
                    // Delete file
                    File.Delete(file);
                }

                // Throws abort exceptions
                catch (ThreadAbortException) { throw; }

                // Ignore further exceptions
                catch {; }
            }

            // Unable to load
            message = null;
            return false;
        }

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
                    // Get next queued message
                    if (this._Queued.TryDequeue(out string file))
                    {
                        // Fetch message data
                        if (TryLoadMessage(file, out JObject message))
                        {
                            // Update the message
                            this.UpdateMessage(message);

                            // Exception count
                            int exceptions = 0;

                            // While not sent
                            while (this._Active)
                            {
                                try
                                {
                                    // Maximum photo exception reached?
                                    if (exceptions > 0 && exceptions % MAXIMUM_PHOTO_TRIES == 0 && message.ContainsKey("""photo"""))
                                    {
                                        // Clone message
                                        JObject textmessage = new(message);

                                        // Remove the photo
                                        textmessage.Remove("""photo""");

                                        // Does have caption and not text?
                                        if (textmessage.ContainsKey("""caption""") && !textmessage.ContainsKey("""text"""))
                                        {
                                            // Translate caption to text
                                            textmessage.Add("""text""", (string)textmessage["""caption"""]);
                                            textmessage.Remove("""caption""");
                                        }

                                        // Remove any link preview
                                        if (!textmessage.ContainsKey("""disable_web_page_preview"""))
                                            textmessage.Add("""disable_web_page_preview""", true);

                                        // Send message
                                        this.SendInternal(textmessage);
                                    }
                                    else
                                    {
                                        // Send message
                                        this.SendInternal(message);
                                    }

                                    // Remove file
                                    File.Delete(file);

                                    // Message sent
                                    break;
                                }

                                // Throws abort exceptions
                                catch (ThreadAbortException) { throw; }

                                // Other exceptions
                                catch { exceptions++; }

                                // Wait for some time before trying again
                                this._ActiveWait.WaitOne(RETRY_INTERVAL);
                            }
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
            // Sending photo?
            if (message.ContainsKey("""photo"""))
            {
                // Try send direct as URL, send as multi-part data if fail
                if (!this.TrySendPhoto(message))
                    this.SendPhotoMultipart(message);
            }
            else
            {
                // Send message
                this.SendJson(message, TELEGRAM_SEND_MESSAGE_COMMAND);
            }
        }

        /// <summary>
        /// Send JSON as command
        /// </summary>
        private void SendJson(JObject message, string command)
        {
            // Prepare full message
            string rawmessage = message.ToString(Formatting.None);

            // Encode to UTF-8
            byte[] data = Encoding.UTF8.GetBytes(rawmessage);

            // Initializing connection
            HttpWebRequest connection = Core.CreateWebRequest($"{TELEGRAM_API_URL}bot{this._Token}/{command}", """POST""");
            connection.ContentLength = data.Length;
            connection.ContentType = JSON_CONTENT_TYPE;
            connection.KeepAlive = false;

            // Connects and them post data
            using (Stream connectionstream = connection.GetRequestStream())
                connectionstream.Write(data, 0, data.Length);

            // Check result
            CheckTelegramResponse(connection);
        }

        /// <summary>
        /// Try send the message, will enqueue for later if fail
        /// </summary>
        private void SendOrEnqueue(JObject message)
        {
            // Update with current configuration
            this.UpdateMessage(message);

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
        /// Send photo as multi-part data
        /// </summary>
        private void SendPhotoMultipart(JObject message)
        {
            // Download image data
            HttpWebRequest connection = Core.CreateWebRequest((string)message["""photo"""]);
            using MemoryStream imagestream = new();
            {
                // Read response to stream
                using WebResponse webresponse = connection.GetResponse();
                using Stream webstream = webresponse.GetResponseStream();
                webstream.CopyTo(imagestream);
                imagestream.Position = 0;
            }

            // Image data to be send
            byte[] photodata;
            string phototype;

            // Read the image
            using MagickImage image = new(imagestream);

            // Check image size
            if (image.Height > MAXIMUM_PHOTO_SIZE || image.Width > MAXIMUM_PHOTO_SIZE)
            {
                // Larger width?
                if (image.Width > MAXIMUM_PHOTO_SIZE)
                {
                    // Resize with maximum width
                    image.Resize(MAXIMUM_PHOTO_SIZE, (int)Math.Ceiling(((double)MAXIMUM_PHOTO_SIZE * image.Height) / image.Width));
                }
                else
                {
                    // Resize with maximum height
                    image.Resize((int)Math.Ceiling(((double)MAXIMUM_PHOTO_SIZE * image.Width) / image.Height), MAXIMUM_PHOTO_SIZE);
                }

                // Force JPG format
                image.Format = MagickFormat.Jpg;
                image.Quality = 90;

                // Convert to JPG
                using MemoryStream jpegstream = new();
                image.Write(jpegstream);

                // Get data
                photodata = jpegstream.ToArray();
                phototype = """image/jpg""";
            }
            else
            {
                // Check the real format
                switch (image.Format)
                {
                    // JPEG
                    case MagickFormat.Jpg:
                    case MagickFormat.Jpeg:
                        photodata = imagestream.ToArray();
                        phototype = """image/jpg""";
                        break;

                    // PNG
                    case MagickFormat.Png:
                        photodata = imagestream.ToArray();
                        phototype = """image/png""";
                        break;

                    // Other formats
                    default:
                        {
                            // Force JPG format
                            image.Format = MagickFormat.Jpg;
                            image.Quality = 90;

                            // Convert to JPG
                            using MemoryStream jpegstream = new();
                            image.Write(jpegstream);

                            // Get data
                            photodata = jpegstream.ToArray();
                            phototype = """image/jpg""";
                        }
                        break;
                }
            }

            // But image data exceeds maximum file size?
            if (photodata.Length > MAXIMUM_PHOTO_FILE_SIZE)
            {
                // Image quality
                int quality = 100;

                // Force JPG format
                image.Format = MagickFormat.Jpg;
                phototype = """image/png""";

                // While not small enough
                while (photodata.Length > MAXIMUM_PHOTO_FILE_SIZE)
                {
                    // Decrements quality
                    quality -= 10;
                    image.Quality = quality;

                    // Convert to JPG
                    using MemoryStream jpegstream = new();
                    image.Write(jpegstream);

                    // Get data
                    photodata = jpegstream.ToArray();

                    // Force exit if quality is below limit
                    if (quality <= 1)
                        break;
                }
            }

            // Boundary separador
            string boundary = $"boundary{Guid.NewGuid():N}";
            byte[] boundarydata = Encoding.ASCII.GetBytes($"\r\n--{boundary}\r\n");
            byte[] boundaryenddata = Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n");

            // Multi part stream
            using MemoryStream multipart = new();

            // Write forma-data
            void WriteFormData(string key, string value)
            {
                // Writes the boundary
                multipart.Write(boundarydata, 0, boundarydata.Length);

                // Writes header
                string header = $"Content-Disposition: form-data; name=\"{key}\"\r\n\r\n";
                byte[] headerdata = Encoding.UTF8.GetBytes(header);
                multipart.Write(headerdata, 0, headerdata.Length);

                // Writes data value
                byte[] valuedata = Encoding.UTF8.GetBytes(value);
                multipart.Write(valuedata, 0, valuedata.Length);
            }

            // For each value in message
            foreach (var pair in message)
            {
                // Check key
                switch (pair.Key)
                {
                    // Photo data?
                    case """photo""":
                        {
                            // Writes the boundary
                            multipart.Write(boundarydata, 0, boundarydata.Length);

                            // Writes header
                            string header = $"Content-Disposition: form-data; name=\"photo\"; filename=\"photo\"\r\nContent-Type: {phototype}\r\n\r\n";
                            byte[] headerdata = Encoding.UTF8.GetBytes(header);
                            multipart.Write(headerdata, 0, headerdata.Length);

                            // Writes the photo file
                            multipart.Write(photodata, 0, photodata.Length);
                        }
                        break;

                    // Text? Will be translated as caption
                    case """text""":
                        WriteFormData("""caption""", (string)pair.Value);
                        break;

                    // Others
                    default:
                        WriteFormData(pair.Key, (string)pair.Value);
                        break;
                }
            }

            // Finalize with end boundary
            multipart.Write(boundaryenddata, 0, boundaryenddata.Length);
            multipart.Position = 0;

            // Initializing connection
            connection = Core.CreateWebRequest($"{TELEGRAM_API_URL}bot{this._Token}/{TELEGRAM_SEND_PHOTO_COMMAND}", """POST""");
            connection.ContentLength = multipart.Length;
            connection.ContentType = $"multipart/form-data; boundary=\"{boundary}\"";
            connection.KeepAlive = false;

            // Connects and post data
            using (Stream connectionstream = connection.GetRequestStream())
                multipart.CopyTo(connectionstream);

            // Check result
            CheckTelegramResponse(connection);
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

        /// <summary>
        /// Try send photo as URL
        /// </summary>
        private bool TrySendPhoto(JObject message)
        {
            try
            {
                // Send photo
                this.SendJson(message, TELEGRAM_SEND_PHOTO_COMMAND);

                // Photo sent
                return true;
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Ignore any other exception
            catch {; }

            // Not sent
            return false;
        }

        /// <summary>
        /// Update the message for current configuration
        /// </summary>
        private void UpdateMessage(JObject message)
        {
            // Already contains chat-id?
            if (message.ContainsKey("""chat_id"""))
            {
                // Update it
                message["""chat_id"""] = this._Chat;
            }
            else
            {
                // Add current chat-id
                message.Add("""chat_id""", this._Chat);
            }
        }
        #endregion
    }
}
