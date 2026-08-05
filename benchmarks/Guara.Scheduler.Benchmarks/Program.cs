using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Âncora para o <c>BenchmarkSwitcher</c> localizar o assembly.</summary>
public partial class Program;
