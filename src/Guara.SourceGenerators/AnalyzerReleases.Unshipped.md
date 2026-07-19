; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
GUARA0102 | Guara.SourceGenerators | Error | Parâmetro de job com tipo fora do conjunto serializável suportado
GUARA0103 | Guara.SourceGenerators | Error | Placeholder de chave de concorrência sem argumento correspondente
GUARA0105 | Guara.SourceGenerators | Error | Job genérico (método ou tipo) não suportado
GUARA0106 | Guara.SourceGenerators | Error | Retorno de job deve ser Task, ValueTask ou void
GUARA0107 | Guara.SourceGenerators | Error | CancellationToken deve ser o último parâmetro
GUARA0108 | Guara.SourceGenerators | Error | Discriminador tipo.método duplicado entre jobs
GUARA0109 | Guara.SourceGenerators | Error | Parâmetro ref/out/in não suportado em job
