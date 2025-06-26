// Ignore Spelling: Hardmob

using System.Net.Http.Headers;
using System.Text;

namespace Hardmob.Helpers
{
    static class HttpResponseMessageHelper
    {
        /// <summary>
        /// Get response as text
        /// </summary>
        public static string GetResponseText(this HttpResponseMessage response, CancellationToken? cancellation = null, bool checkStatus = true)
        {
            // Must check status?
            if (checkStatus)
            {
                try
                {
                    // Check if it's ok
                    response.EnsureSuccessStatusCode();
                }

                // HTTP exceptions
                catch (HttpRequestException hre)
                {
                    try
                    {
                        // Get the response anyway
                        string exceptionResponse = GetResponseText(response, cancellation, checkStatus: false);

                        // Add to exception data
                        hre.Data.Add("""response""", exceptionResponse);
                    }

                    // Throw thread exceptions
                    catch (ThreadAbortException) { throw; }
                    catch (ThreadInterruptedException) { throw; }
                    catch (OperationCanceledException) { throw; }

                    // Ignore other exceptions
                    catch {; }

                    // Continue with exception
                    throw;
                }
            }

            // Gets content
            using HttpContent content = response.Content;

            // Default text encoding
            Encoding encoding = Encoding.ASCII;

            // Contains content type?
            MediaTypeHeaderValue? contentType = content.Headers.ContentType;
            if (contentType != null)
            {
                // Contains char-set?
                string? charset = contentType.CharSet;
                if (charset != null)
                {
                    try
                    {
                        // Recover encoding
                        encoding = Encoding.GetEncoding(charset);
                    }

                    // Ignore exceptions
                    catch {; }
                }
            }

            // Reads as stream
            Stream responseStream = cancellation == null ? content.ReadAsStream() : content.ReadAsStream(cancellation.Value);

            // Reads as string
            using StreamReader responseReader = new(responseStream, encoding);
            return responseReader.ReadToEnd();
        }
    }
}
