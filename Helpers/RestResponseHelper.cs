// Ignore Spelling: Hardmob

using RestSharp;
using System.Text;

namespace Hardmob.Helpers
{
    static class RestResponseHelper
    {
        /// <summary>
        /// Get response as text
        /// </summary>
        public static string? GetResponseText(this RestResponse response)
        {
            // Check input
            if (response.RawBytes == null)
                return null;

            // Default text encoding
            Encoding encoding = Encoding.UTF8;

            // Has headers?
            if (response.ContentHeaders != null)
            {
                // Check for content types
                foreach (var contentheader in response.ContentHeaders)
                {
                    // Has value?
                    if (contentheader.Value != null)
                    {
                        // Checking for each content type
                        foreach (string type in contentheader.Value.Split(';'))
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
                }
            }

            // Get the raw data
            byte[] raw = response.RawBytes;

            // Trying UTF-8 and then original encoding
            Encoding[] encodings = [encoding, Encoding.UTF8, Encoding.ASCII];
            foreach (Encoding textencoding in encodings)
            {
                try
                {
                    // Read raw data and decode to string
                    using Stream responsestream = new MemoryStream(raw, writable: false);
                    using StreamReader responsereader = new(responsestream, textencoding);
                    return responsereader.ReadToEnd();
                }

                // Ignore other exceptions
                catch {; }
            }

            // No result
            return null;
        }
    }
}
