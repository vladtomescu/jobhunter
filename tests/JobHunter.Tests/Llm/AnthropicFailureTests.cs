using System.Net;
using Anthropic.Exceptions;
using JobHunter.Llm;

namespace JobHunter.Tests.Llm;

/// <summary>Response bodies the API answers with, and the exceptions the client raises for them, built through the client's own factory.</summary>
internal static class AnthropicErrors
{
    /// <summary>What the API answered on 2026-09-24 once the account reached its spend limit.</summary>
    public const string UsageLimitBody = """{"type":"error","error":{"type":"invalid_request_error","message":"You have reached your specified API usage limits. You will regain access on 2026-10-01 at 00:00 UTC."},"request_id":"req_011CZ9vXhq4bRSU6H1mLqJ3e"}""";

    /// <summary>A plain invalid request that has nothing to do with the account.</summary>
    public const string InvalidRequestBody = """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: Field required"},"request_id":"req_011CZ9vY2a7Kd3Qm5nPx8Wtu"}""";

    /// <summary>The exception the client raises for one status and body.</summary>
    public static AnthropicApiException Exception(HttpStatusCode status, string body)
    {
        return AnthropicExceptionFactory.CreateApiException(status, body);
    }
}

/// <summary>Proves how a client exception becomes a failure reason: readable, without the request identifier, and the account usage limit recognised as such.</summary>
public sealed class AnthropicFailureTests
{
    [Fact]
    public void Describe_OfTheUsageLimitAnswer_ReportsTheLimitWithTheMomentAccessReturns()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.BadRequest, AnthropicErrors.UsageLimitBody));

        Assert.True(failure.UsageLimitReached);
        Assert.True(failure.Retryable);
        Assert.Equal("Anthropic account usage limit reached — access returns 2026-10-01 00:00 UTC", failure.Reason);
    }

    [Fact]
    public void Describe_OfAUsageLimitAnswerWithoutADate_ReportsTheLimitAlone()
    {
        const string body = """{"type":"error","error":{"type":"invalid_request_error","message":"You have reached your specified API usage limits."},"request_id":"req_1"}""";

        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.BadRequest, body));

        Assert.True(failure.UsageLimitReached);
        Assert.Equal(AnthropicFailure.UsageLimitReason, failure.Reason);
    }

    [Fact]
    public void Describe_OfAnotherInvalidRequest_UsesTheBodyMessageWithoutTheRequestId()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.BadRequest, AnthropicErrors.InvalidRequestBody));

        Assert.False(failure.UsageLimitReached);
        Assert.False(failure.Retryable);
        Assert.Equal("The request was rejected: max_tokens: Field required", failure.Reason);
    }

    [Fact]
    public void Describe_OfTheUsageLimitWordingUnderAnotherStatus_IsNotTheUsageLimit()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.TooManyRequests, AnthropicErrors.UsageLimitBody.Replace("invalid_request_error", "rate_limit_error", StringComparison.Ordinal)));

        Assert.False(failure.UsageLimitReached);
        Assert.True(failure.Retryable);
        Assert.StartsWith("The model is rate limited: You have reached", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_OfAnApiErrorWithABodyThatIsNotJson_FallsBackToTheExceptionMessage()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.BadGateway, "<html>bad gateway</html>"));

        Assert.False(failure.UsageLimitReached);
        Assert.True(failure.Retryable);
        Assert.Contains("bad gateway", failure.Reason, StringComparison.Ordinal);
    }
}
