using System.Net.Http;
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

    public class NewsFeedService
    {
        private readonly HttpClient _httpClient;
        private const string FeedUrl = "https://launcher.hytale.com/launcher-feed/release/feed.json";

        public NewsFeedService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<List<NewsArticle>> GetNewsAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(FeedUrl);
                var feed = JsonConvert.DeserializeObject<NewsFeed>(json);
                return feed?.Articles ?? new List<NewsArticle>();
            }
            catch
            {
                return new List<NewsArticle>();
            }
        }
    }
}
