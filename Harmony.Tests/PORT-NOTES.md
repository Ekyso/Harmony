# Harmony.Tests port notes

The test suite and companion `TestLibrary` are ported from Pathoschild's Harmony fork. They target
`net10.0` in Release configuration.

## Project setup

All original test source files compile without exclusions. The test project keeps the assembly
name `HarmonyTests` so the existing `InternalsVisibleTo` entry remains valid.

The `MonoMod.Core` project reference retains the `mmc` alias used by Harmony internals. The test
sources do not declare that alias directly.

## Verification

The last recorded command was:

```bash
dotnet test Harmony.Tests -c Release
```

It passed 275 of 277 discovered tests with no failures. Two upstream NUnit cases remain excluded
by their existing attributes:

- Two exception-filter cases are ignored because DynamicMethods do not support filter exceptions.
- The nullable-result case is explicit because upstream marks it unsafe in some configurations.
