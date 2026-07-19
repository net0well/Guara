using Guara.Configuration;
using Guara.Core;

namespace Guara.Executor;

/// <summary>
/// Leitura explícita da seção <c>Guara:Retry</c> (AOT-safe, sem reflection).
/// Apenas <see cref="RetryOptions.MaxAttempts"/> é configurável por arquivo — o
/// back-off é uma função e se define por código.
/// </summary>
internal static class RetryOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, RetryOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var section = configuration.Component("Retry");
        options.MaxAttempts =
            GuaraConfigurationValues.ReadInt32(section, nameof(options.MaxAttempts)) ?? options.MaxAttempts;
    }

    public static void Validate(RetryOptions options)
    {
        if (options.MaxAttempts < 0)
        {
            throw new InvalidOperationException(
                $"RetryOptions.MaxAttempts não pode ser negativo (recebido: {options.MaxAttempts}).");
        }

        if (options.Backoff is null)
        {
            throw new InvalidOperationException("RetryOptions.Backoff não pode ser nulo.");
        }
    }
}
