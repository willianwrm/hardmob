// Ignore Spelling: Hardmob

using Hardmob.Helpers;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using System.Diagnostics;

namespace Hardmob
{
    static class Puppeteer
    {
        #region Variables
        /// <summary>
        /// Browser instance caching
        /// </summary>
        private static readonly WeakReference<BrowserInstance> _Browser = new(null!);

        /// <summary>
        /// General synchronization to generate browser or open a new tab
        /// </summary>
        private static readonly Lock _SyncRoot = new();

        /// <summary>
        /// Revision info
        /// </summary>
        private static RevisionInfo? _BrowserRevisionInfo;
        #endregion

        #region Public
        /// <summary>
        /// Using class for the first time
        /// </summary>
        static Puppeteer()
        {
            // Releasing instance when finishing application
            Application.ApplicationExit += Puppeteer.Application_ApplicationExit;
        }

        /// <summary>
        /// Navigate to URL and get it's content
        /// </summary>
        public static string Navigate(string url, CancellationToken cancellation)
        {
            try
            {
                // Recover browser
                BrowserInstance browser = Puppeteer.GetBrowser(cancellation);

                // Create new tab
                BrowserTabInstance tab = browser.GetTab(cancellation);
                IPage page = tab.Page;

                // Navigate to the page
                page.GoToAsync(url).Wait(cancellation);

                // Get page content
                Task<string> contentAsync = page.GetContentAsync();
                contentAsync.Wait(cancellation);

                // Return code
                return contentAsync.Result;
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { Puppeteer.FreeBrowser(); throw; }
            catch (ThreadInterruptedException) { Puppeteer.FreeBrowser(); throw; }
            catch (OperationCanceledException) { Puppeteer.FreeBrowser(); throw; }

            // Other exceptions
            catch (Exception ex)
            {
                // Log it
                ex.Log();

                // Release current browser
                Puppeteer.FreeBrowser();

                // Throw exception
                throw;
            }
        }
        #endregion

        #region Private
        /// <summary>
        /// Application closed
        /// </summary>
        private static void Application_ApplicationExit(object? sender, EventArgs e) => Puppeteer.FreeBrowser();

        /// <summary>
        /// Release current browser
        /// </summary>
        private static void FreeBrowser()
        {
            try
            {
                // Retrieves possible browser instance
                if (Puppeteer._Browser.TryGetTarget(out BrowserInstance? instance))
                {
                    // Release reference in cache
                    Puppeteer._Browser.SetTarget(null!);

                    // Release instance
                    instance.Dispose();
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }
            catch (ThreadInterruptedException) { throw; }

            // Ignores other exceptions
            catch {; }
        }

        /// <summary>
        /// Recovers access to the browser
        /// </summary>
        private static BrowserInstance GetBrowser(CancellationToken cancellation)
        {
            // Attempts to recover an existing instance
            if (Puppeteer._Browser.TryGetTarget(out BrowserInstance? instance))
                return instance;

            // Synchronize access
            lock (Puppeteer._SyncRoot)
            {
                // Try to retrieve from cache again
                // Another instance may have launched the browser in the meantime.
                if (Puppeteer._Browser.TryGetTarget(out instance))
                    return instance;

                // Attempts to start application
                int tries = 2;

                // Until it starts
                while (true)
                {
                    // Check if cancelled
                    cancellation.ThrowIfCancellationRequested();

                    try
                    {
                        // Download or update browser
                        BrowserFetcher fetcher = new();
                        Task<RevisionInfo> downloadAsync = fetcher.DownloadAsync();
                        downloadAsync.Wait(cancellation);
                        Puppeteer._BrowserRevisionInfo = downloadAsync.Result;

                        // Kill any other instance
                        Puppeteer.KillChrome();

                        // Initialization plug-in builder
                        PuppeteerExtra extra = new();

                        // Use stealth plug-in
                        extra.Use(new StealthPlugin());

                        // Prepare parameters to start the browser
                        LaunchOptions options = new()
                        {
                            Headless = true,
                            Args = ["""--disable-gpu"""],
                        };

                        // Browser creation task
                        using Task<IBrowser> task = extra.LaunchAsync(options);

                        // Wait for browser to start
                        task.Wait(cancellation);

                        // Recover browser
                        instance = new(task.Result ?? throw new NullReferenceException($"{nameof(IBrowser)} is null"));

                        // Add to instance cache
                        Puppeteer._Browser.SetTarget(instance);

                        // Started successfully
                        break;
                    }

                    // Aggregate failures
                    catch (AggregateException ae)
                    {
                        // Continues if it is a process failure
                        if (ae.InnerException is not ProcessException)
                            throw;

                        // Continues if there are no more attempts
                        if (--tries <= 0)
                            throw;

                        // Terminates any instance of chrome
                        Puppeteer.KillChrome();
                    }

                    // Wait before next try
                    cancellation.WaitHandle.WaitOne(100);
                }
            }

            // Retorna navegador
            return instance;
        }

        /// <summary>
        /// Force quit any instance of chrome
        /// </summary>
        private static void KillChrome()
        {
            try
            {
                // Application address
                string? path = Puppeteer._BrowserRevisionInfo?.ExecutablePath;

                // Check if process is the expected chrome
                bool CheckChrome(Process process)
                {
                    // Valid path?
                    if (path != null)
                    {
                        // Does it have a main module?
                        ProcessModule? main = process.MainModule;
                        if (main != null)
                        {
                            // Does the module have an application address?
                            string file = main.FileName;
                            if (!string.IsNullOrEmpty(file))
                                return StringComparer.OrdinalIgnoreCase.Equals(file, path);
                        }
                    }

                    // Couldn't define, so let's assume it must be chrome
                    return true;
                }

                // For each process
                foreach (Process process in Process.GetProcessesByName("""chrome"""))
                {
                    try
                    {
                        // End process
                        if (CheckChrome(process))
                            process.Kill();
                    }

                    // Throws abort exceptions
                    catch (ThreadAbortException) { throw; }
                    catch (ThreadInterruptedException) { throw; }

                    // Ignores other exceptions
                    catch {; }
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }
            catch (ThreadInterruptedException) { throw; }

            // Ignores other exceptions
            catch {; }
        }
        #endregion


        #region BrowserInstance
        /// <summary>
        /// Storing browser instances with <see cref="GC.Collect()"/> support
        /// </summary>
        private class BrowserInstance(IBrowser browser) : IDisposable
        {
            #region Variables
            /// <summary>
            /// Browser instance
            /// </summary>
            public readonly IBrowser Browser = browser;

            /// <summary>
            /// Page instance caching, per thread
            /// </summary>
            private readonly ThreadLocal<WeakReference<BrowserTabInstance>> _Page = new(true);
            #endregion

            #region Public
            /// <summary>
            /// Destructor via <see cref="GC"/>
            /// </summary>
            ~BrowserInstance() => this.DisposeInternal(false);

            /// <inheritdoc/>
            public void Dispose()
            {
                // Releases resources
                this.DisposeInternal(true);

                // No need to collect
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Create or retrieve page
            /// </summary>
            public BrowserTabInstance GetTab(CancellationToken cancellation)
            {
                // Recovers thread cache
                WeakReference<BrowserTabInstance>? cache = this._Page.Value;
                if (cache == null)
                {
                    // Creates new cache and stores it in the thread
                    cache = new(null!);
                    this._Page.Value = cache;
                }

                // Try to retrieve page from cache
                if (cache.TryGetTarget(out BrowserTabInstance? instance))
                    return instance;

                // Create new page
                instance = new(this.CreatePage(cancellation));

                // Store in cache
                cache.SetTarget(instance);

                // Returns page
                return instance;
            }
            #endregion

            #region Private
            /// <summary>
            /// Create new page
            /// </summary>
            private IPage CreatePage(CancellationToken cancellation)
            {
                // Building page
                using Task<IPage> task = this.Browser.NewPageAsync();

                // Waiting for task to finish
                task.Wait(cancellation);

                // Returns result
                return task.Result;
            }

            /// <summary>
            /// Releases allocated resources
            /// </summary>
            private void DisposeInternal(bool managed)
            {
                // Release manageable resources?
                if (managed)
                {
                    // Do we have pages?
                    if (this._Page != null)
                    {
                        try
                        {
                            // For each page
                            foreach (WeakReference<BrowserTabInstance> cache in this._Page.Values)
                            {
                                // Try to recover value
                                if (cache.TryGetTarget(out BrowserTabInstance? page))
                                    page?.Dispose();
                            }
                        }

                        // Throws abort exceptions
                        catch (ThreadAbortException) { throw; }
                        catch (ThreadInterruptedException) { throw; }

                        // Ignores other exceptions
                        catch {; }
                    }
                }

                // Release browser
                this.Browser?.Dispose();
            }
            #endregion
        }
        #endregion

        #region BrowserPageInstance
        /// <summary>
        /// Storing page in instance with support for <see cref="GC.Collect()"/>
        /// </summary>
        private class BrowserTabInstance(IPage page) : IDisposable
        {
            #region Variables
            /// <summary>
            /// Browser page
            /// </summary>
            public readonly IPage Page = page;
            #endregion

            #region Public
            /// <summary>
            /// Destructor via <see cref="GC"/>
            /// </summary>
            ~BrowserTabInstance() => this.DisposeInternal();

            /// <inheritdoc/>
            public void Dispose()
            {
                // Frees resources
                this.DisposeInternal();

                // No need to collect
                GC.SuppressFinalize(this);
            }
            #endregion

            #region Private
            /// <summary>
            /// Releases allocated resources
            /// </summary>
            private void DisposeInternal()
            {
                // Release page
                this.Page?.Dispose();
            }
            #endregion
        }
        #endregion
    }
}
