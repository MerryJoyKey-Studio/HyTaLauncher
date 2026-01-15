using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json;

namespace HyTaLauncher.Services
{
    public class UpdateInfo
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = "";

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonProperty("body")]
        public string Body { get; set; } = "";

        public string Version => TagName.TrimStart('v', 'V');
    }

    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private const string RepoOwner = "MerryJoyKey-Studio";
        private const string RepoName = "HyTaLauncher";
        private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        public static string CurrentVersion => "1.0.3";

        public UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(ApiUrl);
                var release = JsonConvert.DeserializeObject<UpdateInfo>(json);

                if (release != null && IsNewerVersion(release.Version, CurrentVersion))
                {
                    return release;
                }
            }
            catch
            {
                // Игнорируем ошибки проверки обновлений
            }

            return null;
        }

        private bool IsNewerVersion(string remote, string current)
        {
            try
            {
                var remoteParts = remote.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(remoteParts.Length, currentParts.Length); i++)
                {
                    var r = i < remoteParts.Length ? remoteParts[i] : 0;
                    var c = i < currentParts.Length ? currentParts[i] : 0;

                    if (r > c) return true;
                    if (r < c) return false;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
