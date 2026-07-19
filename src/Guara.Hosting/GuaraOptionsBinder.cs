using Guara.Configuration;

namespace Guara.Hosting;

/// <summary>
/// Leitura explícita das opções globais na raiz da seção <c>Guara</c>
/// (AOT-safe, sem reflection).
/// </summary>
internal static class GuaraOptionsBinder
{
    public static void Bind(GuaraConfiguration? configuration, GuaraOptions options)
    {
        if (configuration is null)
        {
            return; // sem UseConfiguration: valem os defaults + delegate de código
        }

        var root = configuration.Root;
        options.ApplicationName =
            GuaraConfigurationValues.ReadString(root, nameof(options.ApplicationName)) ?? options.ApplicationName;
        options.DefaultQueues =
            GuaraConfigurationValues.ReadStringArray(root, nameof(options.DefaultQueues)) ?? options.DefaultQueues;
    }
}
