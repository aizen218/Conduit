namespace Conduit;

public readonly record struct RetryPolicy
{
    public int MaxAttempts { get; init; }
    public TimeSpan BaseDelay { get; init; }
    public double BackoffMultiplier { get; init; }
    /// <summary>Return false to stop retrying for a specific exception type.</summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }
    /// <summary>Called after each failed attempt, before the next delay. Receives (exception, attemptNumber).</summary>
    public Action<Exception, int>? OnRetry { get; init; }

    /// <summary>Retry with delay that doubles each attempt: baseDelay, baseDelay*2, baseDelay*4...</summary>
    public static RetryPolicy Exponential(
        int maxAttempts,
        TimeSpan baseDelay,
        double multiplier = 2.0,
        Func<Exception, bool>? shouldRetry = null,
        Action<Exception, int>? onRetry = null) => new()
    {
        MaxAttempts = maxAttempts,
        BaseDelay = baseDelay,
        BackoffMultiplier = multiplier,
        ShouldRetry = shouldRetry,
        OnRetry = onRetry
    };

    /// <summary>Retry with a constant delay between each attempt.</summary>
    public static RetryPolicy Linear(
        int maxAttempts,
        TimeSpan delay,
        Func<Exception, bool>? shouldRetry = null,
        Action<Exception, int>? onRetry = null) => new()
    {
        MaxAttempts = maxAttempts,
        BaseDelay = delay,
        BackoffMultiplier = 1.0,
        ShouldRetry = shouldRetry,
        OnRetry = onRetry
    };

    /// <summary>Retry immediately with no delay between attempts.</summary>
    public static RetryPolicy Immediate(
        int maxAttempts,
        Func<Exception, bool>? shouldRetry = null,
        Action<Exception, int>? onRetry = null) => new()
    {
        MaxAttempts = maxAttempts,
        BaseDelay = TimeSpan.Zero,
        BackoffMultiplier = 1.0,
        ShouldRetry = shouldRetry,
        OnRetry = onRetry
    };
}
