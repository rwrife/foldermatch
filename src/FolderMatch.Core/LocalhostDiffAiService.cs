using System.Net;
using System.Text;
using System.Text.Json;

namespace FolderMatch.Core;

public sealed class LocalhostDiffAiService : IDiffAiService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _endpointUri;
    private readonly Uri _modelsProbeUri;
    private readonly LocalDiffAiOptions _options;

    public LocalhostDiffAiService(LocalDiffAiOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new ArgumentException("AI endpoint is required.", nameof(options));
        }

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpointUri))
        {
            throw new ArgumentException("AI endpoint must be an absolute URI.", nameof(options));
        }

        if (!IsLocalhost(endpointUri.Host))
        {
            throw new ArgumentException("AI endpoint must target localhost/loopback.", nameof(options));
        }

        if (options.MaxMetadataItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxMetadataItems, "Max metadata items must be greater than zero.");
        }

        _options = options;
        _endpointUri = endpointUri;
        _modelsProbeUri = new UriBuilder(endpointUri)
        {
            Path = "/v1/models",
            Query = string.Empty
        }.Uri;

        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = options.Timeout
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            _httpClient.Timeout = options.Timeout;
        }
    }

    public bool Enabled => _options.Enabled;

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _modelsProbeUri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> SummarizeAsync(DiffAiSummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enabled)
        {
            return null;
        }

        var metadataJson = JsonSerializer.Serialize(request, JsonOptions);
        var promptPayload = new
        {
            model = _options.Model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You summarize folder diff metadata. Never infer file contents, only describe metadata-level change patterns. Keep it concise and practical."
                },
                new
                {
                    role = "user",
                    content = "Summarize this folder diff metadata for a desktop end-user. Mention hotspots and conflicts.\n\n" + metadataJson
                }
            }
        };

        using var body = new StringContent(JsonSerializer.Serialize(promptPayload, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpointUri)
        {
            Content = body
        };

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractSummaryText(responseBody);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string? ExtractSummaryText(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsLocalhost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
    }
}
