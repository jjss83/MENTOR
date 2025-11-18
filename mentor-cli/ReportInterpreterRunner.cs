using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MentorTrainingRunner;

internal sealed class ReportInterpreterRunner
{
    private readonly ReportInterpreterOptions options;
    private const int MaxRetries = 3;

    public ReportInterpreterRunner(ReportInterpreterOptions options)
    {
        this.options = options;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (options.CheckOpenAi)
        {
            return await RunCheckAsync(cancellationToken);
        }

        var reportOptions = new ReportOptions(options.RunId, options.ResultsDirectory);
        var generator = new TrainingReportGenerator(reportOptions);

        JsonObject report;
        try
        {
            report = await generator.GenerateReportAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to generate report: {ex.Message}");
            return 1;
        }

        var payload = new JsonObject
        {
            ["agent"] = "ReportInterpreter",
            ["prompt"] = options.Prompt,
            ["report"] = report,
        };

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            EmitPayload(payload, note: "OPENAI_API_KEY not set; skipping LLM call.");
            return 0;
        }

        try
        {
            var completion = await CallOpenAiWithRetryAsync(apiKey, payload, options.Prompt, cancellationToken);
            EmitPayload(payload, completion);
            return 0;
        }
        catch (OpenAiResponseException ex)
        {
            EmitPayload(payload, note: $"LLM call failed after retries: {(int)ex.StatusCode} {ex.StatusCode} body: {ex.ResponseBody}");
            return 1;
        }
        catch (Exception ex)
        {
            EmitPayload(payload, note: $"LLM call failed after retries: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunCheckAsync(CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        var payload = new JsonObject
        {
            ["agent"] = "ReportInterpreter",
            ["prompt"] = "Check OpenAI connectivity",
            ["report"] = new JsonObject { ["status"] = "noop" },
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            EmitPayload(payload, note: "OPENAI_API_KEY not set; cannot run check.");
            return 1;
        }

        try
        {
            var completion = await CallOpenAiWithRetryAsync(apiKey, payload, "Echo: ok", cancellationToken);
            EmitPayload(payload, completion);
            return 0;
        }
        catch (OpenAiResponseException ex)
        {
            EmitPayload(payload, note: $"LLM call failed after retries: {(int)ex.StatusCode} {ex.StatusCode} body: {ex.ResponseBody}");
            return 1;
        }
        catch (Exception ex)
        {
            EmitPayload(payload, note: $"LLM call failed after retries: {ex.Message}");
            return 1;
        }
    }

    private string? ResolveApiKey()
    {
        return options.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    private void EmitPayload(JsonObject payload, string? completion = null, string? note = null)
    {
        var root = new JsonObject
        {
            ["request"] = payload,
            ["note"] = note,
            ["response"] = completion,
        };

        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(root.ToJsonString(serializerOptions));

        if (!string.IsNullOrWhiteSpace(completion))
        {
            Console.WriteLine();
            Console.WriteLine("--- OpenAI Response (plain text) ---");
            Console.WriteLine(completion);
        }
    }

    private async Task<string> CallOpenAiWithRetryAsync(string apiKey, JsonObject payload, string userPrompt, CancellationToken cancellationToken)
    {
        var delayMs = 1000;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await CallOpenAiAsync(apiKey, payload, userPrompt, cancellationToken);
            }
            catch (OpenAiResponseException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                lastError = ex;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new InvalidOperationException("OpenAI call failed without an exception.");
    }

    private async Task<string> CallOpenAiAsync(string apiKey, JsonObject payload, string userPrompt, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MentorTrainingRunner/1.0");

        var systemPrompt = "You are the Report Interpreter Agent for Mentor CLI runs. Given the JSON payload, explain the current results concisely: identify run-id, missing artifacts, summarize training_status checkpoints/metadata, timers highlights, and configuration notes. Keep it short and actionable.";

        var requestBody = new
        {
            model = options.OpenAiModel,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
                new { role = "user", content = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var truncated = responseBody.Length > 4000 ? responseBody[..4000] + "..." : responseBody;
            throw new OpenAiResponseException(response.StatusCode, truncated);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("OpenAI response did not contain content.");
        }

        return message;
    }
}

internal sealed class OpenAiResponseException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public OpenAiResponseException(HttpStatusCode statusCode, string responseBody)
        : base($"OpenAI call failed with status {(int)statusCode} {statusCode}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
