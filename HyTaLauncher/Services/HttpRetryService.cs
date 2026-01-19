using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HyTaLauncher.Services
{
    /// <summary>
    /// Configuration for HTTP retry behavior
    /// </summary>
    public class RetryConfig
    {
        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Initial delay between retries (will be multiplied by attempt number for exponential backoff)
        /// </summary>
        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between retries
        /// </summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Request timeout
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Enable resume support for downloads
        /// </summary>
        public bool EnableResume { get; set; } = true;

        /// <summary>
        /// Buffer size for streaming downloads (default 80KB)
        /// </summary>
        public int BufferSize { get; set; } = 81920;

        /// <summary>
        /// Default configuration for quick operations
        /// </summary>
        public static RetryConfig Quick => new()
        {
            MaxRetries = 2,
            Timeout = TimeSpan.FromSeconds(10),
            EnableResume = false
        };

        /// <summary>
        /// Default configuration for large file downloads
        /// </summary>
        public static RetryConfig LargeFile => new()
        {
            MaxRetries = 5,
            Timeout = TimeSpan.FromMinutes(60),
            EnableResume = true,
            InitialRetryDelay = TimeSpan.FromSeconds(2)
        };

        /// <summary>
        /// Whether to bypass SSL certificate validation errors
        /// Use with caution - only for debugging or when corporate proxy interferes
        /// </summary>
        public bool BypassSslValidation { get; set; } = false;
    }

    /// <summary>
    /// SSL/TLS error types for better error reporting
    /// </summary>
    public enum SslErrorType
    {
        None,
        CertificateValidation,
        ProtocolError,
        AuthenticationFailed,
        Unknown
    }

    /// <summary>
    /// Result of a download operation
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FilePath { get; set; }
        public long BytesDownloaded { get; set; }
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }
        public int AttemptsUsed { get; set; }
        public bool WasResumed { get; set; }
        public string? Sha256Hash { get; set; }
    }

    /// <summary>
    /// Centralized HTTP client with retry policy, resume support, and proper error handling
    /// </summary>
    public class HttpRetryService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly RetryConfig _defaultConfig;
        private bool _disposed;

        /// <summary>
        /// Event raised when download progress changes (0-100)
        /// </summary>
        public event Action<double>? ProgressChanged;

        /// <summary>
        /// Event raised when download status changes
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Gets or sets whether to bypass SSL certificate validation
        /// </summary>
        public bool BypassSslValidation { get; set; }

        /// <summary>
        /// Creates a new HttpRetryService with default configuration
        /// </summary>
        public HttpRetryService() : this(new RetryConfig()) { }

        /// <summary>
        /// Creates a new HttpRetryService with specified configuration
        /// </summary>
        public HttpRetryService(RetryConfig config)
        {
            _defaultConfig = config ?? new RetryConfig();
            BypassSslValidation = _defaultConfig.BypassSslValidation;

            // Enable TLS 1.2 and 1.3 for older systems
            // Suppress warnings about deprecated TLS versions - we need them for compatibility
#pragma warning disable SYSLIB0039
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
#pragma warning restore SYSLIB0039

            var handler = CreateHttpClientHandler();

            _httpClient = new HttpClient(handler)
            {
                Timeout = _defaultConfig.Timeout
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HyTaLauncher/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
        }

        /// <summary>
        /// Creates an HttpClientHandler with appropriate SSL settings
        /// </summary>
        private HttpClientHandler CreateHttpClientHandler()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                // Enable TLS 1.2 and 1.3 for security and compatibility
#pragma warning disable SYSLIB0039
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
#pragma warning restore SYSLIB0039
            };

            // If bypass is enabled, accept all certificates
            if (_defaultConfig.BypassSslValidation)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    if (errors != SslPolicyErrors.None)
                    {
                        LogService.LogNetworkVerbose($"SSL validation bypassed. Errors: {errors}");
                    }
                    return true; // Accept all certificates
                };
                LogService.LogNetworkVerbose("SSL certificate validation bypass enabled");
            }

            return handler;
        }

        /// <summary>
        /// Analyzes an exception to determine if it's an SSL error and what type
        /// </summary>
        public static (SslErrorType Type, string Message) AnalyzeSslError(Exception ex)
        {
            var innerEx = ex;
            while (innerEx != null)
            {
                // Check for SSL/TLS specific exceptions
                if (innerEx is AuthenticationException authEx)
                {
                    return (SslErrorType.AuthenticationFailed,
                        $"SSL authentication failed: {authEx.Message}");
                }

                if (innerEx is System.Security.Cryptography.CryptographicException cryptoEx)
                {
                    return (SslErrorType.CertificateValidation,
                        $"Certificate error: {cryptoEx.Message}");
                }

                // Check message content for SSL-related errors
                var message = innerEx.Message.ToLowerInvariant();
                if (message.Contains("ssl") || message.Contains("tls") ||
                    message.Contains("certificate") || message.Contains("secure channel"))
                {
                    if (message.Contains("certificate"))
                    {
                        return (SslErrorType.CertificateValidation,
                            "SSL certificate validation failed. This may be caused by:\n" +
                            "• Antivirus software intercepting connections\n" +
                            "• Corporate proxy/firewall\n" +
                            "• Outdated Windows root certificates\n" +
                            "• VPN software\n\n" +
                            "Try: Disable antivirus SSL scanning, update Windows, or use VPN.");
                    }

                    if (message.Contains("protocol") || message.Contains("handshake"))
                    {
                        return (SslErrorType.ProtocolError,
                            "SSL/TLS protocol error. Try:\n" +
                            "• Update Windows to get latest TLS support\n" +
                            "• Check if your network blocks certain connections\n" +
                            "• Try using a VPN");
                    }

                    return (SslErrorType.Unknown,
                        $"SSL/TLS error: {innerEx.Message}\n\n" +
                        "Try disabling antivirus SSL scanning or using a VPN.");
                }

                innerEx = innerEx.InnerException;
            }

            return (SslErrorType.None, string.Empty);
        }

        /// <summary>
        /// Checks if an exception is an SSL-related error
        /// </summary>
        public static bool IsSslError(Exception ex)
        {
            return AnalyzeSslError(ex).Type != SslErrorType.None;
        }

        /// <summary>
        /// Performs a GET request with retry support
        /// </summary>
        public async Task<string?> GetStringAsync(string url, RetryConfig? config = null, CancellationToken cancellationToken = default)
        {
            config ??= _defaultConfig;
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < config.MaxRetries)
            {
                attempt++;

                try
                {
                    LogService.LogNetworkVerbose($"GET {url} (attempt {attempt}/{config.MaxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(config.Timeout);

                    var response = await _httpClient.GetAsync(url, cts.Token);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(cts.Token);
                    LogService.LogNetworkVerbose($"GET {url} completed: {content.Length} chars");

                    return content;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // User cancelled, don't retry
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogNetworkVerbose($"GET {url} failed (attempt {attempt}): {ex.Message}");

                    // Check for SSL errors
                    var (sslErrorType, sslMessage) = AnalyzeSslError(ex);
                    if (sslErrorType != SslErrorType.None)
                    {
                        LogService.LogError($"SSL error on GET {url}: {sslErrorType}");
                        StatusChanged?.Invoke($"SSL Error: {sslErrorType}");

                        // For SSL errors, don't retry as much - it's unlikely to resolve
                        if (attempt >= 2)
                        {
                            LogService.LogError($"SSL error details: {sslMessage}");
                            break;
                        }
                    }

                    if (attempt < config.MaxRetries)
                    {
                        var delay = CalculateDelay(attempt, config);
                        StatusChanged?.Invoke($"Retry in {delay.TotalSeconds:F0}s...");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            // Provide better error message for SSL errors
            if (lastException != null)
            {
                var (sslErrorType, sslMessage) = AnalyzeSslError(lastException);
                if (sslErrorType != SslErrorType.None)
                {
                    LogService.LogError($"GET {url} failed due to SSL error: {sslMessage}");
                }
                else
                {
                    LogService.LogError($"GET {url} failed after {attempt} attempts", lastException);
                }
            }

            return null;
        }

        /// <summary>
        /// Performs a HEAD request to check if resource exists and get its size
        /// </summary>
        public async Task<(bool exists, long? size)> HeadAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var size = response.Content.Headers.ContentLength;
                    return (true, size);
                }

                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }

        /// <summary>
        /// Downloads a file with retry and resume support
        /// </summary>
        public async Task<DownloadResult> DownloadFileAsync(
            string url,
            string destPath,
            RetryConfig? config = null,
            long expectedSize = -1,
            string? expectedHash = null,
            CancellationToken cancellationToken = default)
        {
            config ??= _defaultConfig;
            var result = new DownloadResult { FilePath = destPath };
            var tempPath = destPath + ".tmp";
            var attempt = 0;

            while (attempt < config.MaxRetries)
            {
                attempt++;
                result.AttemptsUsed = attempt;

                try
                {
                    LogService.LogNetworkVerbose($"Download {url} (attempt {attempt}/{config.MaxRetries})");

                    long existingBytes = 0;

                    // Check for partially downloaded file
                    if (config.EnableResume && File.Exists(tempPath))
                    {
                        existingBytes = new FileInfo(tempPath).Length;

                        // If expected size is known and file is complete, verify it
                        if (expectedSize > 0 && existingBytes >= expectedSize)
                        {
                            if (await VerifyAndFinalizeAsync(tempPath, destPath, expectedHash))
                            {
                                result.Success = true;
                                result.BytesDownloaded = existingBytes;
                                result.WasResumed = true;
                                return result;
                            }
                            // Hash mismatch, delete and re-download
                            existingBytes = 0;
                            SafeDeleteFile(tempPath);
                        }
                        else if (existingBytes > 0)
                        {
                            LogService.LogNetworkVerbose($"Resuming from {existingBytes} bytes");
                            result.WasResumed = true;
                        }
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(config.Timeout);

                    // Create request with Range header if resuming
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (existingBytes > 0 && config.EnableResume)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                        StatusChanged?.Invoke($"Resuming from {FormatBytes(existingBytes)}...");
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    // Check if server supports resume
                    if (existingBytes > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        LogService.LogNetworkVerbose("Server doesn't support resume, starting fresh");
                        existingBytes = 0;
                        result.WasResumed = false;
                        SafeDeleteFile(tempPath);
                    }

                    response.EnsureSuccessStatusCode();

                    var contentLength = response.Content.Headers.ContentLength ?? -1;
                    var totalBytes = expectedSize > 0 ? expectedSize : (existingBytes + contentLength);

                    LogService.LogNetworkVerbose($"Content length: {contentLength}, total: {totalBytes}");

                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Download with progress reporting
                    await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
                    await using var fileStream = new FileStream(
                        tempPath,
                        existingBytes > 0 ? FileMode.Append : FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        config.BufferSize,
                        useAsync: true);

                    var buffer = new byte[config.BufferSize];
                    int bytesRead;
                    var downloadedBytes = existingBytes;
                    var lastProgressReport = DateTime.UtcNow;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                        downloadedBytes += bytesRead;

                        // Report progress (throttled to avoid UI spam)
                        if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds > 100)
                        {
                            if (totalBytes > 0)
                            {
                                var progress = (double)downloadedBytes / totalBytes * 100;
                                ProgressChanged?.Invoke(Math.Min(progress, 100));
                            }
                            lastProgressReport = DateTime.UtcNow;
                        }
                    }

                    await fileStream.FlushAsync(cts.Token);
                    fileStream.Close();

                    result.BytesDownloaded = downloadedBytes;

                    // Verify size if expected
                    if (expectedSize > 0 && downloadedBytes != expectedSize)
                    {
                        throw new Exception($"Download incomplete: {downloadedBytes}/{expectedSize} bytes");
                    }

                    // Verify hash and finalize
                    if (await VerifyAndFinalizeAsync(tempPath, destPath, expectedHash))
                    {
                        result.Success = true;
                        result.Sha256Hash = expectedHash ?? await ComputeFileHashAsync(destPath);
                        ProgressChanged?.Invoke(100);
                        LogService.LogNetworkVerbose($"Download completed: {downloadedBytes} bytes");
                        return result;
                    }

                    throw new Exception("File verification failed (hash mismatch)");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    result.ErrorMessage = "Download cancelled";
                    result.Exception = null;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Exception = ex;
                    result.ErrorMessage = ex.Message;
                    LogService.LogNetworkVerbose($"Download failed (attempt {attempt}): {ex.Message}");

                    // Check for SSL errors
                    var (sslErrorType, sslMessage) = AnalyzeSslError(ex);
                    if (sslErrorType != SslErrorType.None)
                    {
                        LogService.LogError($"SSL error during download: {sslErrorType}");
                        result.ErrorMessage = sslMessage;
                        StatusChanged?.Invoke($"SSL Error - check antivirus/VPN");

                        // For SSL errors, limit retries
                        if (attempt >= 2)
                        {
                            LogService.LogError($"SSL error details: {sslMessage}");
                            break;
                        }
                    }

                    if (attempt < config.MaxRetries)
                    {
                        var delay = CalculateDelay(attempt, config);
                        StatusChanged?.Invoke($"Download failed, retrying in {delay.TotalSeconds:F0}s...");
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            // Enhance error message for SSL errors
            if (result.Exception != null)
            {
                var (sslErrorType, sslMessage) = AnalyzeSslError(result.Exception);
                if (sslErrorType != SslErrorType.None)
                {
                    result.ErrorMessage = sslMessage;
                    LogService.LogError($"Download {url} failed due to SSL error: {sslMessage}");
                }
                else
                {
                    LogService.LogError($"Download {url} failed after {attempt} attempts", result.Exception);
                }
            }

            return result;
        }

        /// <summary>
        /// Downloads a file to a byte array with retry support
        /// </summary>
        public async Task<byte[]?> DownloadBytesAsync(string url, RetryConfig? config = null, CancellationToken cancellationToken = default)
        {
            config ??= _defaultConfig;
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < config.MaxRetries)
            {
                attempt++;

                try
                {
                    LogService.LogNetworkVerbose($"DownloadBytes {url} (attempt {attempt}/{config.MaxRetries})");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(config.Timeout);

                    var bytes = await _httpClient.GetByteArrayAsync(url, cts.Token);
                    LogService.LogNetworkVerbose($"DownloadBytes completed: {bytes.Length} bytes");

                    return bytes;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogService.LogNetworkVerbose($"DownloadBytes failed (attempt {attempt}): {ex.Message}");

                    if (attempt < config.MaxRetries)
                    {
                        var delay = CalculateDelay(attempt, config);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }

            LogService.LogError($"DownloadBytes {url} failed after {attempt} attempts", lastException!);
            return null;
        }

        /// <summary>
        /// Verifies file hash and moves to final destination
        /// </summary>
        private async Task<bool> VerifyAndFinalizeAsync(string tempPath, string destPath, string? expectedHash)
        {
            if (!File.Exists(tempPath))
                return false;

            // Verify hash if provided
            if (!string.IsNullOrEmpty(expectedHash))
            {
                var actualHash = await ComputeFileHashAsync(tempPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.LogNetworkVerbose($"Hash mismatch: expected {expectedHash}, got {actualHash}");
                    SafeDeleteFile(tempPath);
                    return false;
                }
            }

            // Move temp file to final destination
            try
            {
                SafeDeleteFile(destPath);
                File.Move(tempPath, destPath);
                return true;
            }
            catch (Exception ex)
            {
                LogService.LogError($"Failed to finalize download: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Computes SHA256 hash of a file
        /// </summary>
        public static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Calculates exponential backoff delay
        /// </summary>
        private static TimeSpan CalculateDelay(int attempt, RetryConfig config)
        {
            // Exponential backoff with jitter
            var baseDelay = config.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var jitter = Random.Shared.NextDouble() * 0.3 * baseDelay; // 0-30% jitter
            var delay = TimeSpan.FromMilliseconds(baseDelay + jitter);

            return delay > config.MaxRetryDelay ? config.MaxRetryDelay : delay;
        }

        /// <summary>
        /// Safely deletes a file without throwing exceptions
        /// </summary>
        private static void SafeDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>
        /// Formats bytes to human-readable string
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:F1} {suffixes[order]}";
        }

        /// <summary>
        /// Disposes the HTTP client
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

    /// <summary>
    /// Extension methods for HttpRetryService
    /// </summary>
    public static class HttpRetryServiceExtensions
    {
        /// <summary>
        /// Creates a shared instance for quick operations
        /// </summary>
        public static HttpRetryService CreateQuick() => new(RetryConfig.Quick);

        /// <summary>
        /// Creates a shared instance for large file downloads
        /// </summary>
        public static HttpRetryService CreateForDownloads() => new(RetryConfig.LargeFile);

        /// <summary>
        /// Creates an instance with SSL bypass enabled (use with caution)
        /// </summary>
        public static HttpRetryService CreateWithSslBypass()
        {
            var config = new RetryConfig
            {
                MaxRetries = 3,
                BypassSslValidation = true
            };
            return new HttpRetryService(config);
        }

        /// <summary>
        /// Gets a user-friendly error message for the exception
        /// </summary>
        public static string GetFriendlyErrorMessage(Exception ex)
        {
            var (sslErrorType, sslMessage) = HttpRetryService.AnalyzeSslError(ex);
            if (sslErrorType != SslErrorType.None)
            {
                return sslMessage;
            }

            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.Message.Contains("No such host"))
                {
                    return "Could not connect to server. Check your internet connection.";
                }
                if (httpEx.Message.Contains("timed out") || httpEx.Message.Contains("Timeout"))
                {
                    return "Connection timed out. The server may be slow or your connection unstable.";
                }
            }

            if (ex is TaskCanceledException)
            {
                return "Operation timed out. Try again or check your connection.";
            }

            return ex.Message;
        }
    }
}
