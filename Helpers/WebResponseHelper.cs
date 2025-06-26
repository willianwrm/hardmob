// Ignore Spelling: Hardmob

using System.Net;
using System.Text;

namespace Hardmob.Helpers
{
    static class WebResponseHelper
    {
        /// <summary>
        /// Get response as text
        /// </summary>
        public static string GetResponseText(this WebResponse response)
        {
            // Response seems to be 404?
            if (Is404(response))
                throw new HttpRequestException("""Not found""", null, HttpStatusCode.NotFound);

            // Default text encoding
            Encoding encoding = Encoding.ASCII;

            // Check for content type
            if (response.ContentType != null)
            {
                // Checking for each content type
                foreach (string type in response.ContentType.Split(';'))
                {
                    // Has key and value?
                    int n = type.IndexOf('=');
                    if (n > 0)
                    {
                        // Get key
                        string key = type[..n].ToLower().Trim();
                        switch (key)
                        {
                            // Encoding type
                            case """charset""":
                                {
                                    try
                                    {
                                        // Recover encoding
                                        encoding = Encoding.GetEncoding(type[(n + 1)..].Trim());
                                    }

                                    // Ignore other exceptions
                                    catch {; }
                                }
                                break;
                        }
                    }
                }
            }

            // Read raw data and decode to string
            using Stream responsestream = response.GetResponseStream();
            using StreamReader responsereader = new(responsestream, encoding);
            return responsereader.ReadToEnd();
        }

        /// <summary>
        /// Check if response does look like 404 page
        /// </summary>
        private static bool Is404(WebResponse response)
        {
            // Check status code
            if (response is HttpWebResponse http && http.StatusCode == HttpStatusCode.NotFound)
                return true;

            // Does have response URI?
            Uri uri = response.ResponseUri;
            if (uri != null)
            {
                // Does have segments?
                string[] segments = uri.Segments;
                if (segments != null && segments.Length > 0)
                {
                    // Remove extension
                    string last = segments[^1];
                    int n = last.LastIndexOf('.');
                    if (n > 0)
                        last = last[..n];

                    // Does look like 404 error?
                    if (last == "404" ||
                        StringComparer.OrdinalIgnoreCase.Equals(last, "error404") ||
                        StringComparer.OrdinalIgnoreCase.Equals(last, "page404"))
                        return true;
                }
            }

            // Not 404
            return false;
        }
    }
}
