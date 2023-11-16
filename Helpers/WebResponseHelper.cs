using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Hardmob.Helpers
{
    static class WebResponseHelper
    {
        /// <summary>
        /// Get response as text
        /// </summary>
        public static string GetResponseText(this WebResponse response)
        {
            // Check input
            if (response == null)
                return null;

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
                        string key = type.Substring(0, n).ToLower().Trim();
                        switch (key)
                        {
                            // Encoding type
                            case """charset""":
                                {
                                    try
                                    {
                                        // Recover encoding
                                        encoding = Encoding.GetEncoding(type.Substring(n + 1).Trim());
                                    }

                                    // Throws abort exceptions
                                    catch (ThreadAbortException) { throw; }

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
    }
}
