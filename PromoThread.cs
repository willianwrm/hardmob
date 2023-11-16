using System;

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
        private static readonly char[] EMPTY_SPACE_CHARS = new char[] { ' ', '\n', '\r', '\t' };
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
        public static bool TryParse(string input, out PromoThread promo)
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
        #endregion
    }
}
