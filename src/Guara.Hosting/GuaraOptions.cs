namespace Guara.Hosting;

/// <summary>Opções globais do Guará, validadas no registro (falha cedo, nunca em runtime).</summary>
public sealed class GuaraOptions
{
    /// <summary>Nome lógico da aplicação (identifica o nó no dashboard e nos logs).</summary>
    public string ApplicationName { get; set; } = "guara";

    /// <summary>Filas default consumidas pelo servidor, em ordem de prioridade.</summary>
    public string[] DefaultQueues { get; set; } = ["default"];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            throw new InvalidOperationException(
                "GuaraOptions.ApplicationName não pode ser vazio.");
        }

        if (DefaultQueues is not { Length: > 0 } || DefaultQueues.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "GuaraOptions.DefaultQueues precisa de pelo menos uma fila com nome não vazio.");
        }
    }
}
