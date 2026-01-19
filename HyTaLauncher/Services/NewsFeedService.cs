using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class NewsArticle
    {
        [JsonProperty("title")]
        public string Title { get; set; } = "";

        [JsonProperty("dest_url")]
        public string DestUrl { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";

        [JsonProperty("image_url")]
        public string ImageUrl { get; set; } = "";

        public string FullImageUrl => ImageUrl.StartsWith("http")
            ? ImageUrl
            : $"https://launcher.hytale.com/launcher-feed/release/{ImageUrl}";
    }

    public class NewsFeed
    {
        [JsonProperty("articles")]
        public List<NewsArticle> Articles { get; set; } = new();
    }

    public class NewsFeedService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed;
        private const string FeedUrl = "https://launcher.hytale.com/launcher-feed/release/feed.json";
        private const int MaxRetries = 3;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        public NewsFeedService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = RequestTimeout;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher/1.0");
        }

        public async Task<List<NewsArticle>> GetNewsAsync(CancellationToken cancellationToken = default)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    LogService.LogGameVerbose($"Fetching news feed (attempt {attempt}/{MaxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(RequestTimeout);

                    var json = await _httpClient.GetStringAsync(FeedUrl, cts.Token);
                    var feed = JsonConvert.DeserializeObject<NewsFeed>(json);

                    var articles = feed?.Articles ?? new List<NewsArticle>();
                    LogService.LogGameVerbose($"News feed loaded: {articles.Count} articles");

                    return articles;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // User cancelled, don't retry
                    LogService.LogGameVerbose("News feed fetch cancelled");
                    return new List<NewsArticle>();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogGameVerbose($"News feed fetch failed (attempt {attempt}): {ex.Message}");

                    if (attempt < MaxRetries)
                    {
                        // Exponential backoff: 1s, 2s, 4s...
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        try
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            return new List<NewsArticle>();
                        }
                    }
                }
            }

            LogService.LogError($"Failed to fetch news feed after {MaxRetries} attempts", lastException!);
            return new List<NewsArticle>();
        }

        /// <summary>
        /// Disposes resources used by NewsFeedService
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
