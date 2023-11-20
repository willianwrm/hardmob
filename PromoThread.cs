using Hardmob.Helpers;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Hardmob
{
    /// <summary>
    /// Promo thread informations
    /// </summary>
    sealed class PromoThread
    {
        #region Constants
        /// <summary>
        /// Empty chars
        /// </summary>
        private static readonly char[] EMPTY_SPACE_CHARS = [' ', '\n', '\r', '\t'];
        #endregion

        #region Variables
        /// <summary>
        /// Image URL, may be null
        /// </summary>
        public string Image;

        /// <summary>
        /// Direct link to product, may be null
        /// </summary>
        public string Link;

        /// <summary>
        /// Topic title
        /// </summary>
        public string Title;

        /// <summary>
        /// Topic URL
        /// </summary>
        public string URL;
        #endregion

        #region Public
        /// <summary>
        /// Try extract promo info from raw HTML
        /// </summary>
        public static bool TryParse(string input, string url, out PromoThread promo)
        {
            // Check input
            if (input != null)
            {
                // Check for title tag, start
                int titlestart = input.IndexOf("""<title>""", StringComparison.OrdinalIgnoreCase);
                if (titlestart >= 0)
                {
                    // Check for title tag, end
                    int titleend = input.IndexOf("""</title>""", titlestart, StringComparison.OrdinalIgnoreCase);
                    if (titleend > titlestart)
                    {
                        // Start the output
                        promo = new();
                        promo.Title = input.Substring(titlestart + 7, titleend - titlestart - 7).Trim();
                        promo.URL = url;

                        // Get's div content
                        string content = GetContentDiv(input);
                        if (content != null)
                        {
                            // Try find where any link starts
                            int linkstart = content.IndexOf("href=\"", StringComparison.OrdinalIgnoreCase);
                            if (linkstart > 0)
                            {
                                // Find where it ends
                                int linkend = content.IndexOf('"', linkstart + 6);
                                if (linkend > linkstart)
                                    promo.Link = content.Substring(linkstart + 6, linkend - linkstart - 6);
                            }

                            // Try find image tag
                            int imgtagstart = content.IndexOf("<img", StringComparison.OrdinalIgnoreCase);
                            if (imgtagstart > 0)
                            {
                                // Find where it ends
                                int imgtagend = content.IndexOf("/>", imgtagstart + 4, StringComparison.Ordinal);
                                if (imgtagend > imgtagstart)
                                {
                                    // Try find image source start
                                    int imgsourcestart = content.IndexOf("src=\"", imgtagstart, imgtagend - imgtagstart, StringComparison.OrdinalIgnoreCase);
                                    if (imgsourcestart > 0)
                                    {
                                        // Find where source ends
                                        int imgsourcesend = content.IndexOf('\"', imgsourcestart + 5);
                                        if (imgsourcesend > imgsourcestart)
                                            promo.Image = content.Substring(imgsourcestart + 5, imgsourcesend - imgsourcestart - 5);
                                    }
                                }
                            }
                        }

                        // Missing link or image?
                        if (promo.Link == null || promo.Image == null)
                        {
                            // Gets description meta
                            string description = GetDescriptionMeta(input);
                            if (description != null)
                            {
                                // Splits all words
                                foreach (string item in description.Split(EMPTY_SPACE_CHARS, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    // Check for URL
                                    if (item.StartsWith("""https://""") || item.StartsWith("""http://"""))
                                    {
                                        // Look like image?
                                        if (IsImageURL(item))
                                        {
                                            // Update image
                                            promo.Image ??= item;
                                        }
                                        else
                                        {
                                            // Update link
                                            promo.Link ??= item;
                                        }
                                    }
                                }
                            }

                            // Missing image but not link?
                            if (promo.Image == null && promo.Link != null)
                            {
                                // Try fetch the image from the link
                                if (TryFetchImage(promo.Link, out string image))
                                    promo.Image = image;
                            }
                        }

                        // Basic information extracted
                        return true;
                    }
                }
            }

            // No output
            promo = null;
            return false;
        }
        #endregion

        #region Private
        /// <summary>
        /// Find where current div finishes
        /// </summary>
        private static int FindDivEnd(string input, int start)
        {
            // Search for next div end
            int end = input.IndexOf("""</div>""", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                return input.Length;

            // Check for any other div start between current position and end
            int other = input.IndexOf("""<div""", start + 4, StringComparison.OrdinalIgnoreCase);
            if (other < 0 || other > end)
                return end + 6;

            // Tries next end by recursive search
            end = input.IndexOf("""</div>""", FindDivEnd(input, other), StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                return input.Length;

            // Ended
            return end + 6;
        }

        /// <summary>
        /// Get the content of 'content' div
        /// </summary>
        private static string GetContentDiv(string input)
        {
            // Current position
            int pos = 0;

            // While not ended
            while (true)
            {
                // Search for next div, start
                int start = input.IndexOf("""<div""", pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    break;

                // Search for div close
                int close = input.IndexOf('>', start);
                if (close < start)
                    break;

                // It's content class?
                if (IsContentDiv(input, start, close))
                {
                    // Find where the div ends
                    int end = FindDivEnd(input, start);
                    if (end > start)
                    {
                        // Get's div content
                        return input.Substring(start, end - start);
                    }
                }

                // Trying next div
                pos = close + 1;
            }

            // Not found
            return null;
        }

        /// <summary>
        /// Get contents from description meta
        /// </summary>
        private static string GetDescriptionMeta(string input)
        {
            // Current position
            int pos = 0;

            // While not ended
            while (true)
            {
                // Search for next meta, start
                int start = input.IndexOf("""<meta """, pos, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    break;

                // Next end
                int end = input.IndexOf("""/>""", start, StringComparison.OrdinalIgnoreCase);
                if (end <= start)
                    break;

                // Contains description?
                if (input.IndexOf("name=\"description\"", start, end - start, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    // Gets contents start
                    int contentstart = input.IndexOf("content=\"", start, end - start, StringComparison.OrdinalIgnoreCase);
                    if (contentstart >= 0)
                    {
                        // Gets contents end
                        int contentend = input.LastIndexOf('"', end);
                        if (contentend > contentstart + 9)
                        {
                            // Returns content
                            return input.Substring(contentstart + 9, contentend - contentstart - 9);
                        }
                    }
                }

                // Update next search
                pos = end;
            }

            // Not found
            return null;
        }

        /// <summary>
        /// Get properties from HTML tag
        /// </summary>
        private static Dictionary<string, string> GetValues(string input)
        {
            // Initialize output
            Dictionary<string, string> output = new(StringComparer.OrdinalIgnoreCase);

            // Current position
            int pos = 0;

            // While not ended
            while (true)
            {
                // Find next value
                int valuesep = input.IndexOf('=', pos);
                if (valuesep < 0)
                    break;

                // Left part
                string left = input.Substring(pos, valuesep - pos).Trim();
                int whitespace = left.LastIndexOf(' ');
                if (whitespace >= 0)
                    left = left.Substring(whitespace + 1);

                // Find where the value starts
                int start = input.IndexOf('"', valuesep + 1);
                if (start < 0)
                    break;

                // Find where the value ends
                int end = input.IndexOf('"', start + 1);
                if (end < start)
                    break;

                // Right part
                string right = input.Substring(start + 1, end - start - 1);

                // Return item
                if (!output.ContainsKey(left))
                    output.Add(left, right);

                // Update next search
                pos = end + 1;
            }

            // Returns itens
            return output;
        }

        /// <summary>
        /// Check if current div has 'content' class
        /// </summary>
        private static bool IsContentDiv(string input, int start, int close)
        {
            // Does have class definition?
            int classstart = input.IndexOf("class=\"", start, close - start, StringComparison.OrdinalIgnoreCase);
            if (classstart >= 0)
            {
                // Gets class close
                int classend = input.IndexOf('"', classstart + 7);
                if (classend > classstart)
                {
                    // Check for content type
                    return input.IndexOf("""content""", classstart + 7, classend - classstart - 7) > 0;
                }
            }

            // It's not content
            return false;
        }

        /// <summary>
        /// Check if URL seems to be image
        /// </summary>
        private static bool IsImageURL(string url)
        {
            // Check for extension
            int n = url.LastIndexOf('.');
            if (n > 0)
            {
                // Get extension
                string extension = url.Substring(n + 1);

                // Remove any extra url
                int extra = extension.IndexOf('?');
                if (extra > 0)
                    extension = extension.Substring(0, extra);

                // Check extension
                switch (extension.ToUpper())
                {
                    // Image extensions
                    case "PNG":
                    case "BMP":
                    case "JPG":
                    case "JPEG":
                    case "GIF":
                    case "WEBP":
                        return true;
                }
            }

            // Probably not image
            return false;
        }

        /// <summary>
        /// Try to fetch an image from URL HTML
        /// </summary>
        private static bool TryFetchImage(string url, out string image)
        {          
            try
            {
                // Initializing connection
                HttpWebRequest connection = Core.CreateWebRequest(url);

                // Gets the response
                using WebResponse webresponse = connection.GetResponse();
                string responsetext = webresponse.GetResponseText();

                // Current position in the response
                int pos = 0;

                // While not ended
                while (true)
                {
                    // Search for next meta, start
                    int start = responsetext.IndexOf("""<meta""", pos, StringComparison.OrdinalIgnoreCase);
                    if (start < 0)
                        break;

                    // Next end
                    int end = responsetext.IndexOf("""/>""", start, StringComparison.OrdinalIgnoreCase);
                    if (end <= start)
                        break;

                    // Fetch meta values
                    Dictionary<string, string> values = GetValues(responsetext.Substring(start + 5, end - start - 5));

                    // Check if content is an image link
                    bool ContentImage(out string imageurl)
                    {
                        // Content is image link?
                        if (values.TryGetValue("""content""", out string content) && IsImageURL(content))
                        {
                            // Image found
                            imageurl = content;
                            return true;
                        }

                        // Not found
                        imageurl = null;
                        return false;
                    }

                    // Does have property and it is open-graph image?
                    if (values.TryGetValue("""property""", out string property) && StringComparer.OrdinalIgnoreCase.Equals(property, """og:image"""))
                    {
                        // Content is image link?
                        if (ContentImage(out image))
                            return true;
                    }

                    // Does have name and it is twitter image?
                    if (values.TryGetValue("""name""", out string name) && StringComparer.OrdinalIgnoreCase.Equals(name, """twitter:image"""))
                    {
                        // Content is image link?
                        if (ContentImage(out image))
                            return true;
                    }

                    // Update next search
                    pos = end + 2;
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Ignore any other exception
            catch {; }

            // Image not found
            image = null;
            return false;
        }
        #endregion
    }
}
