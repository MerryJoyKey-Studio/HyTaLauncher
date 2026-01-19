using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    #region Models

    public class NewsArticle
    {
        [JsonProperty("title")] public string Title { get; set; } = "";
        [JsonProperty("dest_url")] public string DestUrl { get; set; } = "";
        [JsonProperty("description")] public string Description { get; set; } = "";
        [JsonProperty("image_url")] public string ImageUrl { get; set; } = "";

        public string FullImageUrl =>
            ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? ImageUrl
                : $"https://launcher.hytale.com/launcher-feed/release/{ImageUrl}";
    }

    public class NewsFeed
    {
        [JsonProperty("articles")] public List<NewsArticle> Articles { get; set; } = new();
    }

    internal class TranslationResponse
    {
        [JsonProperty("destination-text")] public string DestinationText { get; set; } = "";
        [JsonProperty("responseData")] public ResponseData ResponseData { get; set; } = new();
        [JsonProperty("translatedText")] public string TranslatedText { get; set; } = "";

        public string GetTranslatedText()
        {
            // Приоритет: если есть DestinationText, используем его, если нет - TranslatedText
            return !string.IsNullOrEmpty(DestinationText) ? DestinationText :
                   !string.IsNullOrEmpty(TranslatedText) ? TranslatedText :
                   ResponseData?.TranslatedText ?? string.Empty;
        }
    }

    internal class ResponseData
    {
        [JsonProperty("translatedText")] public string TranslatedText { get; set; } = "";
    }

    #endregion

    public class NewsFeedService
    {
        private readonly HttpClient _httpClient;
        private readonly Random _random = new();

        private const string FeedUrl = "https://launcher.hytale.com/launcher-feed/release/feed.json"; // Основная точка новостей

        // АПИ переводчиков
        private const string FreeTranslateUrl = "https://ftapi.pythonanywhere.com/translate";
        private const string MyMemoryUrl = "https://api.mymemory.translated.net/get";
        private readonly string[] LibreTranslateUrls = { "https://libretranslate.com/translate", "https://libretranslate.de/translate" };
        private const string SimpleTranslateUrl = "https://translate.googleapis.com/translate_a/single";

        private readonly Dictionary<string, List<NewsArticle>> _cachedArticles = new();

        public NewsFeedService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            LogService.LogGameVerbose("NewsFeedService инициализирован с fallback переводчиками");
        }

        public async Task<List<NewsArticle>> GetNewsAsync(string targetLanguage)
        {
            LogService.LogGame($"Загрузка новостей (язык: {targetLanguage})");

            try
            {
                LogService.LogGameVerbose($"GET {FeedUrl}");
                var json = await _httpClient.GetStringAsync(FeedUrl);
                var feed = JsonConvert.DeserializeObject<NewsFeed>(json);

                if (feed?.Articles == null || feed.Articles.Count == 0)
                {
                    LogService.LogError("Лента новостей пуста или не удалось распарсить JSON");
                    return new List<NewsArticle>();
                }

                LogService.LogGame($"Получено новостей: {feed.Articles.Count}");

                // Игнорируем перевод для языка "en"
                if (targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    LogService.LogGameVerbose("Язык en — перевод не требуется");
                    return feed.Articles;
                }

                // Проверка, если новости уже есть в кэше
                if (_cachedArticles.ContainsKey(targetLanguage) && _cachedArticles[targetLanguage].Count >= feed.Articles.Count)
                {
                    LogService.LogGameVerbose($"Перевод для языка {targetLanguage} уже актуален, возвращаем из кэша");
                    return _cachedArticles[targetLanguage];
                }

                LogService.LogGame($"Начат перевод новостей на язык: {targetLanguage}");

                // Параллельный перевод всех статей с fallback
                var translationTasks = feed.Articles.Select(async article =>
                {
                    article.Title = await TranslateWithFallbackAsync(article.Title, "en", targetLanguage);
                    article.Description = await TranslateWithFallbackAsync(article.Description, "en", targetLanguage);
                });

                await Task.WhenAll(translationTasks);

                // Сохраняем переведенные новости в кэш
                if (!_cachedArticles.ContainsKey(targetLanguage))
                {
                    _cachedArticles[targetLanguage] = new List<NewsArticle>();
                }

                _cachedArticles[targetLanguage] = feed.Articles;

                LogService.LogGame("Перевод новостей завершён");
                return feed.Articles;
            }
            catch (Exception ex)
            {
                LogService.LogError("Ошибка при загрузке новостей", ex);
                return new List<NewsArticle>();
            }
        }

        private async Task<string> TranslateWithFallbackAsync(string text, string sourceLang, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            if (text.Length > 500) // лимит
            {
                LogService.LogGameVerbose($"Текст слишком длинный ({text.Length} символов), перевод пропущен");
                return text;
            }

            var translators = new Func<string, string, string, Task<string>>[]
            {
                TryFreeTranslateAsync, TryMyMemoryTranslateAsync, TrySimpleGoogleTranslateAsync, TryLibreTranslateAsync
            };

            // Параллельный запуск всех переводчиков
            var translationTasks = translators.Select(translator => translator(text, sourceLang, targetLang));
            var firstCompletedTask = await Task.WhenAny(translationTasks);

            try { return await firstCompletedTask; }
            catch (Exception ex)
            {
                LogService.LogError($"Ошибка при переводе: {ex.Message}");
                return text;
            }
        }

        private async Task<string> TryFreeTranslateAsync(string text, string sourceLang, string targetLang)
        {
            var url = $"{FreeTranslateUrl}?dl={Uri.EscapeDataString(targetLang)}&text={Uri.EscapeDataString(text)}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await _httpClient.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Status: {response.StatusCode}");

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<TranslationResponse>(responseJson);

            return string.IsNullOrWhiteSpace(result?.DestinationText) ? throw new Exception("Пустой ответ") : result.DestinationText;
        }

        private async Task<string> TryMyMemoryTranslateAsync(string text, string sourceLang, string targetLang)
        {
            var url = $"{MyMemoryUrl}?q={Uri.EscapeDataString(text)}&langpair={sourceLang}|{targetLang}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await _httpClient.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Status: {response.StatusCode}");

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<TranslationResponse>(responseJson);

            return string.IsNullOrWhiteSpace(result?.ResponseData?.TranslatedText) ? throw new Exception("Пустой ответ") : result.ResponseData.TranslatedText;
        }

        private async Task<string> TryLibreTranslateAsync(string text, string sourceLang, string targetLang)
        {
            var baseUrl = LibreTranslateUrls[_random.Next(LibreTranslateUrls.Length)];
            var url = baseUrl;

            var payload = new { q = text, source = sourceLang, target = targetLang, format = "text" };
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await _httpClient.PostAsync(url, content, cts.Token);

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Status: {response.StatusCode}");

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<TranslationResponse>(responseJson);

            return string.IsNullOrWhiteSpace(result?.TranslatedText) ? throw new Exception("Пустой ответ") : result.TranslatedText;
        }

        private async Task<string> TrySimpleGoogleTranslateAsync(string text, string sourceLang, string targetLang)
        {
            var url = $"{SimpleTranslateUrl}?client=gtx&sl={sourceLang}&tl={targetLang}&dt=t&q={Uri.EscapeDataString(text)}";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var response = await _httpClient.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Status: {response.StatusCode}");

            var responseJson = await response.Content.ReadAsStringAsync();
            var array = JsonConvert.DeserializeObject<dynamic>(responseJson);

            if (array == null || array[0] == null) throw new Exception("Неверный формат ответа");

            var translatedText = "";
            foreach (var item in array[0])
            {
                if (item[0] != null) translatedText += item[0].ToString();
            }

            return string.IsNullOrWhiteSpace(translatedText) ? throw new Exception("Пустой перевод") : translatedText;
        }
    }
}