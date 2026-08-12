**This is a fork of [Pathoschild/Harmony](https://github.com/Pathoschild/Harmony), itself a fork of
[Harmony 2.4.2](https://github.com/pardeike/Harmony).** See
[pardeike/Harmony](https://github.com/pardeike/Harmony) for documentation and usage.

## What's different from Pathoschild's fork

### Mono IL compatibility

A Mono-only post-transpiler pass resolves raw local variable indices to compatible local slots.
This handles transpilers whose expected local moves after a game update. CoreCLR may tolerate the
resulting type mismatch, while Mono rejects it with `InvalidProgramException`.

### Other Mono compatibility fixes

- **By-ref parameter handling**
  - On Mono, `ldnull` is incompatible with by-ref parameters (`type &`), causing "Invalid IL code" errors. Static method patches now use a default-initialized temp local and pass its address instead.

- **HasDefault parameter attribute stripping**
  - DMD wrappers don't need default values, and copying them causes Cecil to resolve the parameter type at write time, which fails for game assembly types. This strips `HasDefault` to prevent crashes on Mono.

- **Dynamic method owner preservation**
  - Preserves the original dynamic method owner so `Type.GetType` resolves game assembly types from generated patch methods.

- **Fault block emission**
  - The vendored MonoMod emitter writes `endfault` for fault exception regions instead of treating them as finally regions.

- **CodeMatcher insert validation fix**
  - Fixes insert position validation to use `Pos < 0 || Pos > Length` instead of `IsInvalid`, allowing inserts at the end of the instruction list.

- **DMD diagnostic dump**
  - When `InvalidProgramException` occurs during patching, dumps assembly references, mismatched assemblies, and token resolution info for debugging.

### Build changes

- Targets `net10.0` only
- Uses .NET SDK 10.0.100 with `latestMajor` rollforward
- Vendors the patched MonoMod and Iced sources required by the build
- Adds .NET 10 CoreCLR detour support to the vendored MonoMod runtime

## What's different from upstream Harmony

Same as Pathoschild's fork: generated method names include the patching Harmony IDs to help
troubleshoot error logs:

```c#
// with original Harmony
   at StardewValley.Farm.resetLocalState_Patch1(Object )

// with fork
   at StardewValley.Farm.resetLocalState_PatchedBy<Pathoschild.SmallBeachFarm>(Object )
```

## How to build

Requires [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

```bash
git clone https://github.com/Ekyso/Harmony.git
cd Harmony
dotnet build Lib.Harmony.Thin -c Release
```

Output in `Lib.Harmony.Thin/bin/Release/net10.0/`:

- `0Harmony.dll`: Harmony library
- `Mono.Cecil*.dll`: IL and metadata manipulation
- `MonoMod.*.dll`: runtime detour and utilities

## Build variants

The SMAPI community uses the `Lib.Harmony.Thin` version, since SMAPI manages the other
dependencies.
