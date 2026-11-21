using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace FinSense.Infrastructure.Services;

public sealed class AiServiceClient : IAiServiceClient
{
    private static readonly JsonSerializerOptions SnakeCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiServiceClient> _logger;

    public AiServiceClient(HttpClient httpClient, ILogger<AiServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<InternalAiChatResponse> ChatAsync(
        InternalAiChatRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<InternalAiChatRequest, InternalAiChatResponse>(
            "api/v1/internal/ai/chat",
            request,
            "AI_CHAT_FAILED",
            cancellationToken);
    }

    public Task<InternalReceiptOcrResponse> ExtractReceiptAsync(
        InternalReceiptOcrRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<InternalReceiptOcrRequest, InternalReceiptOcrResponse>(
            "api/v1/internal/ocr/receipt",
            request,
            "AI_OCR_FAILED",
            cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest payload,
        string clientErrorCode,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                relativePath,
                payload,
                SnakeCaseJsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var resolvedUri = response.RequestMessage?.RequestUri?.ToString() ?? relativePath;
                _logger.LogWarning(
                    "AI service returned status {StatusCode} for {Uri}. Body: {Body}",
                    (int)response.StatusCode,
                    resolvedUri,
                    errorBody);

                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                {
                    var msg = !string.IsNullOrWhiteSpace(errorBody)
                        ? $"Yapay zeka servisi gönderilen veriyi işleyemedi ({response.StatusCode}): {errorBody}"
                        : "Yapay zeka servisi gönderilen veriyi işleyemedi. Lütfen girdiyi kontrol edip tekrar deneyin.";
                    throw new DomainException(clientErrorCode, msg);
                }

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var msg = !string.IsNullOrWhiteSpace(errorBody)
                        ? $"Yapay zeka servisi yetkilendirme hatası verdi ({response.StatusCode}): {errorBody}"
                        : $"Yapay zeka servisi yetkilendirme hatası verdi ({response.StatusCode}).";
                    throw new DomainException("AI_SERVICE_AUTH_FAILED", msg);
                }

                var detailedMsg = !string.IsNullOrWhiteSpace(errorBody)
                    ? $"Yapay zeka servisi şu anda yanıt veremiyor ({response.StatusCode} @ {resolvedUri}): {errorBody}"
                    : $"Yapay zeka servisi şu anda yanıt veremiyor ({response.StatusCode} @ {resolvedUri}). Lütfen biraz sonra tekrar deneyin.";
                throw new DomainException("AI_SERVICE_UNAVAILABLE", detailedMsg);
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(
                SnakeCaseJsonOptions,
                cancellationToken);

            if (result is null)
            {
                throw new DomainException(
                    "AI_SERVICE_INVALID_RESPONSE",
                    "Yapay zeka servisinden geçersiz veya boş bir yanıt alındı.");
            }

            return result;
        }
        catch (DomainException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "AI service request timed out on {Path}. Message: {Message}", relativePath, ex.Message);
            throw new DomainException(
                "AI_SERVICE_TIMEOUT",
                $"Yapay zeka servisi zaman aşımına uğradı ({relativePath}). Lütfen biraz sonra tekrar deneyin.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI service is unreachable on {Path}. Message: {Message}", relativePath, ex.Message);
            throw new DomainException(
                "AI_SERVICE_UNAVAILABLE",
                $"Yapay zeka servisine şu anda ulaşılamıyor ({ex.Message}). Lütfen servisin çalıştığından emin olun.",
                ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize AI service response on {Path}. Message: {Message}", relativePath, ex.Message);
            throw new DomainException(
                "AI_SERVICE_INVALID_RESPONSE",
                $"Yapay zeka servisinden gelen yanıt okunamadı: {ex.Message}",
                ex);
        }
    }
}
