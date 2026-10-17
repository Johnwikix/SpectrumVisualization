using WinExSpectrumTest.Rendering;
if (args.Length != 1) throw new ArgumentException("Expected the build-time shader output directory.");
string output = Path.GetFullPath(args[0]);
var keys = new HashSet<string>(StringComparer.Ordinal);
ShaderCatalog.ForEach((source, entry, profile) =>
{
    string key = ShaderCompiler.Key(source, entry, profile);
    if (!keys.Add(key)) return;
    ShaderCompiler.CompileToDirectory(source, entry, profile, output);
    if (!File.Exists(Path.Combine(output, key + ".bin"))) throw new IOException("Unable to write precompiled shader cache.");
});
// Only owned cache files in this build directory are pruned; stale hashes must not be packaged.
foreach (string file in Directory.EnumerateFiles(output, "*.bin", SearchOption.TopDirectoryOnly))
{
    string name = Path.GetFileNameWithoutExtension(file);
    if (name.Length == 64 && name.AsSpan().IndexOfAnyExcept("0123456789ABCDEF".AsSpan()) < 0 && !keys.Contains(name)) File.Delete(file);
}
Console.WriteLine($"Precompiled {keys.Count} HLSL entries; compiled={ShaderCompiler.CompilationCount}, diskHits={ShaderCompiler.DiskHitCount}");
