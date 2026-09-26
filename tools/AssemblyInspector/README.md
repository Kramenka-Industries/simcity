# Assembly inspector

This small developer tool reads managed DLL metadata with the `Mono.Cecil.dll` already installed by BepInEx. It does not load or execute game code. The repo contains the inspector source, but no game or BepInEx binaries.

## Use

From the repository root:

```sh
just inspect Rearmer
just inspect Rearmer RefillOtherRearmer
just inspect @fields Rearmer
```

The first command lists every type whose full name contains `Rearmer`, including nested types, with its fields and method signatures. The second also prints the IL for matching methods. The third finds fields declared with a type whose name contains `Rearmer`.

The search ignores letter case and uses substring matching. A method search can match more than one method or type, so use a specific type fragment when the output is too long. Exit status is `1` when no type matches and `2` for invalid arguments. The tool prints IL as stored in the DLL; it does not reconstruct C# source or explain Unity prefab values.

By default, `just inspect` reads `NuclearOption_Data/Managed/Assembly-CSharp.dll` from the configured Nuclear Option installation. Set `NUCLEAR_OPTION_GAME` to another game directory if needed. For another managed DLL, call the executable directly:

```sh
dotnet run --project tools/AssemblyInspector/AssemblyInspector.csproj -- \
  '/path/to/OtherAssembly.dll' 'TypeName' 'MethodName'
```

The tool targets the installed .NET 10 SDK so it can run on this development machine. It is separate from the BepInEx plugin, which targets .NET Standard 2.0. Restore uses only the installed SDK and local BepInEx Cecil DLL; `NuGet.Config` disables network package sources. The plugin project excludes this tool's source files.
