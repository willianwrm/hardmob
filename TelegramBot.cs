﻿// Ignore Spelling: Hardmob

using Hardmob.Helpers;
using ImageMagick;
using IniParser.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Resources;
using System.Text;

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
        private const string JSON_CONTENT_TYPE = """application/json""";

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
        private const string TELEGRAM_API_HOST = """api.telegram.org""";

        /// <summary>
        /// Telegram API address
        /// </summary>
        private const string TELEGRAM_API_URL = $"https://{TELEGRAM_API_HOST}/";

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
        private static WeakReference<ResourceManager>? _Resource;

        /// <summary>
        /// Waiting for <see cref="_Active"/> changes
        /// </summary>
        private readonly ManualResetEvent _ActiveWait = new(false);

        /// <summary>
        /// Waiting for <see cref="_Active"/> changes
        /// </summary>
        private readonly CancellationTokenSource _Cancellation = new();

        /// <summary>
        /// Chat ID
        /// </summary>
        private readonly long _Chat;

        /// <summary>
        /// Web cookies
        /// </summary>
        private readonly CookieContainer _Cookies = new();

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
        private readonly Lock _QueueSenderLock = new();

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
        private Thread? _QueueSender;
        #endregion

        #region Constructors
        /// <summary>
        /// New Telegram bot
        /// </summary>
        /// <param name="configurations">Bot's configurations</param>
        public TelegramBot(KeyDataCollection configurations)
        {
            // Get token and chat ID
            this._Token = configurations.ContainsKey(TOKEN_KEY) && !string.IsNullOrWhiteSpace(configurations[TOKEN_KEY]) ? configurations[TOKEN_KEY] : throw new ArgumentNullException(nameof(configurations), TelegramBot.ResourceManager.GetString("EmptyToken"));
            this._Chat = configurations.ContainsKey(CHAT_KEY) && long.TryParse(configurations[CHAT_KEY], out long chat) ? chat : throw new ArgumentOutOfRangeException(nameof(configurations), TelegramBot.ResourceManager.GetString("EmptyChatID"));

            // Queued message path
            this._QueuePath = configurations.ContainsKey(QUEUE_PATH_KEY) && !string.IsNullOrWhiteSpace(configurations[QUEUE_PATH_KEY]) ? Path.GetFullPath(Path.Combine(Core.AppDir, configurations[QUEUE_PATH_KEY])) : Path.Combine(Core.AppDir, DEFAULT_QUEUE_PATH);

            // Check if queue path contains files
            if (Directory.Exists(this._QueuePath) && Directory.EnumerateFiles(this._QueuePath).Any())
            {
                // Get all files in queue path
                List<string> queuefiles = [.. Directory.EnumerateFiles(this._QueuePath)];

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
                if (TelegramBot._Resource?.TryGetTarget(out ResourceManager? resource) ?? false)
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
        public Thread? QueueSenderThread => this._QueueSender;
        #endregion

        #region Public
        /// <inheritdoc/>
        public void Dispose()
        {
            // Set as inactive
            this._Active = false;
            this._ActiveWait.Set();
            this._Cancellation.Cancel();
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
            // Prepare the message to be sent
            JObject message = [];
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
            // Prepare the message to be sent
            JObject message = [];
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
        private static void CheckTelegramResponse(HttpResponseMessage response)
        {
            try
            {
                // Gets the response
                string responsetext = response.GetResponseText();

                // Check if it's all ok
                JObject json = JObject.Parse(responsetext);
                if (!json.ContainsKey("ok") || json["ok"]!.Type != JTokenType.Boolean || !(bool)json["ok"]!)
                    throw new WebException($"{TelegramBot.ResourceManager.GetString("InvalidResponse")}: {responsetext}{Environment.NewLine}{json.ToString(Formatting.None)}", WebExceptionStatus.UnknownError);
            }

            // Web exceptions
            catch (HttpRequestException hre)
            {
                // Is there a response?
                if (hre.Data["""respose"""] is string errortext)
                    throw new WebException($"{TelegramBot.ResourceManager.GetString("InvalidResponse")}: {hre.Message}{Environment.NewLine}{errortext}", hre);

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
        private static bool TryLoadMessage(string file, [NotNullWhen(true)] out JObject? message)
        {
            try
            {
                // Load message
                message = JObject.Parse(File.ReadAllText(file));
                return true;
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }
            catch (ThreadInterruptedException) { throw; }
            catch (OperationCanceledException) { throw; }

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
                catch (ThreadInterruptedException) { throw; }
                catch (OperationCanceledException) { throw; }

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
                    if (this._Queued.TryDequeue(out string? file))
                    {
                        // Fetch message data
                        if (TryLoadMessage(file, out JObject? message))
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
                                        JObject textmessage = [.. message];

                                        // Remove the photo
                                        textmessage.Remove("""photo""");

                                        // Does have caption and not text?
                                        if (textmessage.ContainsKey("""caption""") && !textmessage.ContainsKey("""text"""))
                                        {
                                            // Translate caption to text
                                            textmessage.Add("""text""", (string)textmessage["""caption"""]!);
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

            // Client HTTP
            using HttpClient client = Core.CreateWebClient(this._Cookies);

            // HTTP request
            using HttpRequestMessage httpMessage = Core.CreateWebRequest($"{TELEGRAM_API_URL}bot{this._Token}/{command}", HttpMethod.Post, TELEGRAM_API_HOST);
            httpMessage.Content = new StringContent(rawmessage, Encoding.UTF8, JSON_CONTENT_TYPE);

            // Gets result and check it
            using Task<HttpResponseMessage> responseAsync = client.SendAsync(httpMessage, this._Cancellation.Token);
            responseAsync.Wait(this._Cancellation.Token);
            CheckTelegramResponse(responseAsync.Result);
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
            catch (ThreadInterruptedException) { throw; }
            catch (OperationCanceledException) { throw; }

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
            // Client HTTP
            using HttpClient client = Core.CreateWebClient(this._Cookies);

            // HTTP request
            using HttpRequestMessage httpMessage = Core.CreateWebRequest((string)message["""photo"""]!);

            // Download image data
            using MemoryStream imagestream = new();
            {
                // Read response to stream
                using Task<HttpResponseMessage> responseAsync = client.SendAsync(httpMessage, this._Cancellation.Token);
                responseAsync.Wait(this._Cancellation.Token);
                using HttpContent responseContent = responseAsync.Result.Content;
                using Stream webstream = responseContent.ReadAsStream(this._Cancellation.Token);
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
                    image.Resize(MAXIMUM_PHOTO_SIZE, (uint)Math.Ceiling(((double)MAXIMUM_PHOTO_SIZE * image.Height) / image.Width));
                }
                else
                {
                    // Resize with maximum height
                    image.Resize((uint)Math.Ceiling(((double)MAXIMUM_PHOTO_SIZE * image.Width) / image.Height), MAXIMUM_PHOTO_SIZE);
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
                uint quality = 100;

                // Force JPG format
                image.Format = MagickFormat.Jpg;
                phototype = """image/jpg""";

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
                    if (quality <= 10)
                        break;
                }
            }

            // Boundary separador
            MultipartFormDataContent postContent = [];

            // Multi part data
            using MultipartFormDataContent multipart = [];

            // Write forma-data
            void WriteFormData(string key, string value)
            {
                // Create content
                StringContent content = new(value, Encoding.UTF8);

                // Add to multi part
                multipart.Add(content, key);
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
                            // Create content
                            ByteArrayContent photoContent = new(photodata);
                            photoContent.Headers.ContentType = new MediaTypeHeaderValue(phototype);

                            // Add to multi part
                            multipart.Add(photoContent, """photo""", """photo""");
                        }
                        break;

                    // Text? Will be translated as caption
                    case """text""":
                        WriteFormData("""caption""", pair.Value?.ToString() ?? string.Empty);
                        break;

                    // Others
                    default:
                        WriteFormData(pair.Key, pair.Value?.ToString() ?? string.Empty);
                        break;
                }
            }

            // HTTP request
            using HttpRequestMessage telegramPost = Core.CreateWebRequest($"{TELEGRAM_API_URL}bot{this._Token}/{TELEGRAM_SEND_PHOTO_COMMAND}", HttpMethod.Post, TELEGRAM_API_HOST);
            telegramPost.Content = multipart;

            // Gets result and check it
            using Task<HttpResponseMessage> telegramResponseAsync = client.SendAsync(telegramPost, this._Cancellation.Token);
            telegramResponseAsync.Wait(this._Cancellation.Token);
            CheckTelegramResponse(telegramResponseAsync.Result);
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
            catch (ThreadInterruptedException) { throw; }
            catch (OperationCanceledException) { throw; }

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
