// Ignore Spelling: Hardmob

namespace Hardmob
{
    /// <summary>
    /// Exception while crawling forum
    /// </summary>
    sealed class CrawlerThreadException(Exception inner, long id) : Exception($"Crawler exception at thread {id}", inner)
    {
        /// <summary>
        /// Forum thread ID
        /// </summary>
        public readonly long ID = id;
    }
}
