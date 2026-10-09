using BenchmarkDotNet.Running;

// dotnet run -c Release --project tests/benchmarks/Modulus.Benchmarks -- --filter "*"
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
