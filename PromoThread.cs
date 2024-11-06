using Hardmob.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Ignoring extra info
        /// </summary>
        private static readonly string[] IGNORE_EXTRA_INFO = ["""info""", """http""", """https""", """link""", """site"""];
        #endregion

        #region Variables
        /// <summary>
        /// Extra relative informations
        /// </summary>
        public string Extra;

        /// <summary>
        /// Thread ID
        /// </summary>
        public long ID;

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
        public static bool TryParse(string input, long id, string url, out PromoThread promo)
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
                        promo.ID = id;

                        // Get div content
                        string content = GetContentDiv(input);
                        if (content != null)
                        {
                            // Finding LINK
                            {
                                // Current position
                                int pos = 0;

                                // While not ended
                                while (true)
                                {
                                    // Try find where any link starts
                                    int linkstart = content.IndexOf("href=\"", pos, StringComparison.OrdinalIgnoreCase);
                                    if (linkstart < 0)
                                        break;

                                    // Find where it ends
                                    int linkend = content.IndexOf('"', linkstart + 6);
                                    if (linkend <= linkstart)
                                        break;

                                    // Validate link
                                    string link = content.Substring(linkstart + 6, linkend - linkstart - 6);
                                    if (IsFullURL(link) && !IsImageURL(link) && !IsServerURL(link))
                                    {
                                        // Link found
                                        promo.Link = link;
                                        break;
                                    }

                                    // Next search
                                    pos = linkend + 1;
                                }
                            }

                            // Finding image
                            {
                                // Current position
                                int pos = 0;

                                // While not ended
                                while (true)
                                {
                                    // Try find image tag
                                    int imgtagstart = content.IndexOf("<img", pos, StringComparison.OrdinalIgnoreCase);
                                    if (imgtagstart < 0)
                                        break;

                                    // Find where it ends
                                    int imgtagend = content.IndexOf("/>", imgtagstart + 4, StringComparison.Ordinal);
                                    if (imgtagend <= imgtagstart)
                                        break;

                                    // Try find image source start
                                    int imgsourcestart = content.IndexOf("src=\"", imgtagstart, imgtagend - imgtagstart, StringComparison.OrdinalIgnoreCase);
                                    if (imgsourcestart > 0)
                                    {
                                        // Find where source ends
                                        int imgsourcesend = content.IndexOf('\"', imgsourcestart + 5);
                                        if (imgsourcesend > imgsourcestart)
                                        {
                                            // Validate URL
                                            string imageurl = content.Substring(imgsourcestart + 5, imgsourcesend - imgsourcestart - 5);
                                            if (IsFullURL(imageurl) && IsImageURL(imageurl))
                                            {
                                                // Image found
                                                promo.Image = imageurl;
                                                break;
                                            }
                                        }
                                    }

                                    // Next search
                                    pos = imgtagend + 2;
                                }
                            }
                        }

                        // Get description meta
                        string description = GetDescriptionMeta(input);
                        if (description != null)
                        {
                            // Extra important informations
                            List<string> extra = new();

                            // For each line of description
                            foreach (string line in description.Split('\n'))
                            {
                                // Get some extra info
                                int n = line.IndexOf(':');
                                if (n < 0)
                                    n = line.IndexOf('=');

                                // Any extra info?
                                if (n > 0 && n < line.Length - 1)
                                {
                                    // Get left part, doesn't add if it's just a link
                                    string left = line.Substring(0, n).Trim();
                                    if (!IGNORE_EXTRA_INFO.Contains(left, StringComparer.OrdinalIgnoreCase))
                                    {
                                        // Get the right part, must not be empty
                                        string right = line.Substring(n + 1);
                                        if (!string.IsNullOrWhiteSpace(right))
                                            extra.Add(line.Trim());
                                    }
                                }
                            }

                            // Does have any extra info?
                            if (extra.Count > 0)
                                promo.Extra = string.Join(Environment.NewLine, extra);
                        }

                        // Missing link or image?
                        if (promo.Link == null || promo.Image == null)
                        {
                            // Have description?
                            if (description != null)
                            {
                                // Splits all words
                                foreach (string item in description.Split(EMPTY_SPACE_CHARS, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    // Item is full URL?
                                    if (IsFullURL(item))
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
        private static void GetValues(string input, Dictionary<string, string> output)
        {
            // Clears the output
            output.Clear();

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
        /// Check if it's a full HTTP URL
        /// </summary>
        private static bool IsFullURL(string url)
        {
            // Check minimum size
            if (url != null && url.Length > 7)
            {
                // Check for HTTP
                if ((url[0] == 'h' || url[0] == 'H') &&
                    (url[1] == 't' || url[1] == 'T') &&
                    (url[2] == 't' || url[2] == 'T') &&
                    (url[3] == 'p' || url[3] == 'P'))
                {
                    // Check for next char, must be S or :
                    switch (url[4])
                    {
                        // HTTPS://
                        case 's':
                        case 'S':
                            return url.Length > 8 && url[5] == ':' && url[6] == '/' && url[7] == '/';

                        // HTTP://
                        case ':':
                            return url[5] == '/' && url[6] == '/';
                    }
                }
            }

            // Invalid
            return false;
        }

        /// <summary>
        /// Check if URL (partial or full) seems to be in the forum server
        /// </summary>
        private static bool IsServerURL(string url)
        {
            // Start of url
            int start = url.IndexOf("://");
            start = start <= 0 ? 0 : start + 3;

            // Check for link
            return url.Substring(start).StartsWith(Crawler.SERVER, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if URL (partial or full) seems to be image
        /// </summary>
        private static bool IsImageURL(string url)
        {
            // Remove any extra url
            int extra = url.IndexOf('?');
            if (extra > 0)
                url = url.Substring(0, extra);

            // Check for extension
            int n = url.LastIndexOf('.');
            if (n > 0)
            {
                // Get extension
                string extension = url.Substring(n + 1);

                // Check extension
                switch (extension.ToUpper())
                {
                    // Image extensions
                    case "PNG":
                    case "JPG":
                    case "JPEG":
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
        public static bool TryFetchImage(string url, out string image)
        {
            try
            {
                // Initializing connection
                HttpWebRequest connection = Core.CreateWebRequest(url);

                // Gets the response
                using WebResponse webresponse = connection.GetResponse();
                string responsetext = webresponse.GetResponseText();

                // Buffered values from fetch
                Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

                // Trying meta data
                {
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
                        int end = responsetext.IndexOf(""">""", start, StringComparison.OrdinalIgnoreCase);
                        if (end <= start)
                            break;

                        // Fetch meta values
                        GetValues(responsetext.Substring(start + 5, end - start - 5), values);

                        // Check for name or property
                        if (values.TryGetValue("""property""", out string property) || values.TryGetValue("""name""", out property))
                        {
                            // Does have content?
                            if (values.TryGetValue("""content""", out string content))
                            {
                                // Check property name
                                switch (property.ToLower())
                                {
                                    // Image info
                                    case """og:image""":
                                    case """twitter:image""":
                                        {
                                            // Try parse to full URL
                                            if (TryParseFullURL(url, content, out image))
                                                return true;
                                        }
                                        break;
                                }
                            }
                        }

                        // Update next search
                        pos = end + 1;
                    }
                }

                // Trying link data
                {
                    // Current position in the response
                    int pos = 0;

                    // While not ended
                    while (true)
                    {
                        // Search for next link, start
                        int start = responsetext.IndexOf("""<link""", pos, StringComparison.OrdinalIgnoreCase);
                        if (start < 0)
                            break;

                        // Next end
                        int end = responsetext.IndexOf(""">""", start, StringComparison.OrdinalIgnoreCase);
                        if (end <= start)
                            break;

                        // Fetch meta values
                        GetValues(responsetext.Substring(start + 5, end - start - 5), values);

                        // Check for 'as' value
                        if (values.TryGetValue("""as""", out string property) && StringComparer.OrdinalIgnoreCase.Equals(property, """image"""))
                        {
                            // Does have content?
                            if (values.TryGetValue("""href""", out string content))
                            {
                                // Try parse to full URL
                                if (TryParseFullURL(url, content, out image))
                                    return true;
                            }
                        }

                        // Update next search
                        pos = end + 1;
                    }
                }

                // Trying script
                {
                    // Current position in the response
                    int pos = 0;

                    // While not ended
                    while (true)
                    {
                        // Search for next script, start
                        int start = responsetext.IndexOf("""<script""", pos, StringComparison.OrdinalIgnoreCase);
                        if (start < 0)
                            break;

                        // Search for script declaration end
                        int starte = responsetext.IndexOf('>', start + 7);
                        if (starte <= start)
                            break;

                        // Next end
                        int end = responsetext.IndexOf("""</script>""", starte, StringComparison.OrdinalIgnoreCase);
                        if (end <= start)
                            break;

                        // Get the script
                        string script = responsetext.Substring(starte + 1, end - starte - 1);

                        // Current position in the script
                        int scriptpos = 0;

                        // While not ended
                        while (true)
                        {
                            // Search for next URL, start
                            int imgstart = script.IndexOf("\"http", scriptpos, StringComparison.OrdinalIgnoreCase);
                            if (imgstart < 0)
                                break;

                            // Search for end
                            int imgend = script.IndexOf('"', imgstart + 5);
                            if (imgend <= imgstart)
                                break;

                            // It's an image URL?
                            string imgurl = script.Substring(imgstart + 1, imgend - imgstart - 1);
                            if (IsImageURL(imgurl))
                            {
                                // Check for full URL
                                if (IsFullURL(imgurl))
                                {
                                    // Image found
                                    image = imgurl;
                                    return true;
                                }
                            }

                            // Update next script search
                            scriptpos = imgend + 1;
                        }

                        // Update next search
                        pos = end + 9;
                    }
                }
            }

            // Throws abort exceptions
            catch (ThreadAbortException) { throw; }

            // Other exceptions
            catch
            {
                // Link does includes parameters?
                int n = url.IndexOf('&');
                if (n > 0)
                {
                    // Tries without parameters
                    return TryFetchImage(url.Substring(0, n), out image);
                }
            }

            // Image not found
            image = null;
            return false;
        }

        /// <summary>
        /// Try parse to complete URL
        /// </summary>
        private static bool TryParseFullURL(string @base, string input, out string url)
        {
            // Is already full?
            if (IsFullURL(input))
            {
                // Nothing to do
                url = input;
                return true;
            }

            // Try create prefix url
            if (Uri.TryCreate(@base, UriKind.Absolute, out Uri prefix))
            {
                // Try join both url
                if (Uri.TryCreate(prefix, input, out Uri output))
                {
                    // Completed
                    url = output.ToString();
                    return true;
                }
            }

            // Not complete
            url = null;
            return false;
        }
        #endregion
    }
}
