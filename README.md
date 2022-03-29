**This is a custom fork of Harmony 2.4.2 used by the Stardew Valley community.** It's bundled with
[SMAPI](https://github.com/Pathoschild/SMAPI) automatically. See
[pardeike/Harmony](https://github.com/pardeike/Harmony) for documentation and usage.

This fork is identical to the official version, except that  generated method names include the
patching Harmony IDs to help troubleshoot error logs:

  ```c#
  // with original Harmony
     at StardewValley.Farm.resetLocalState_Patch1(Object )

  // with fork
     at StardewValley.Farm.resetLocalState_PatchedBy<Pathoschild.SmallBeachFarm>(Object )
  ```

The SMAPI community uses the `LibHarmony.Thin` version, since SMAPI manages the other dependencies.

Here's a [list of changes](https://github.com/Pathoschild/Harmony/compare/v2.4.2.0...fork) in the
forked version.
