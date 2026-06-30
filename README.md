# JMDParser

A tool for decrypting and decompressing Raycity JMD files into the `DataRaw` directory structure.

## Overview

Raycity packages its game resources inside encrypted `.jmd` archives stored under the `Data` directory. JMDParser reads these archives, decrypts their contents (XOR-based cipher with Adler32-derived keys), decompresses any zlib-compressed blocks, and writes the resulting files into a `DataRaw` folder that mirrors the original directory hierarchy.

## Loading Unpacked Resources

To make the game load resources from the unpacked `DataRaw` folder instead of the original `.jmd` packages, add the following entries to `config.xml` in the game root

```xml
<config>
  <iamshuruk pw="1122qq"/>
  <dataPackOff/>
</config>
```

- **`<iamshuruk>`** — Enables the developer/debug configuration mode.
- **`<dataPackOff/>`** — Disables packed (JMD) resource loading and switches to raw file loading from `DataRaw`.

---

## Field Loading Crash with `<dataPackOff/>`

On newer client versions (e.g. build 1627 KR), enabling `<dataPackOff/>` alone will cause the game to crash during field data initialization.

### Cause

The game engine maintains a global **Data Manager** structure at a fixed offset (`base + 0x00D8F9B0`). This structure contains:

- **Flags** (`[0x00]`–`[0x05]`) — Boolean load-completion flags for each data category
- **Pointers** (`[0x18]`, `[0x1C]`, `[0x20]`, ...) — Pointers to loaded data for field, fieldCommon, fieldInfo, car, sound, etc.

```
DataManager (base + 0x00D8F9B0)
 Offset   Field
────────────────────────────────────
 [0x00]   flags[0]      ai loaded
 [0x01]   flags[1]      car loaded
 [0x02]   flags[2]      effect loaded
 [0x03]   flags[3]      etc loaded
 [0x04]   flags[4]      field loaded         ← not set when dataPackOff
 [0x05]   flags[5]      fieldCommon loaded   ← not set when dataPackOff
   ...
 [0x18]   field*        → field data pointer
 [0x1C]   fieldCommon*  → field common data pointer
 [0x20]   fieldInfo*    → field info data pointer
 [0x24]   car*          → car data pointer
 [0x3C]   sound*        → sound data pointer
```

When `<dataPackOff/>` is active, the engine switches to an unpacked file loading path. The field data itself is loaded successfully—the `field`, `fieldCommon`, and `fieldInfo` pointers at `[0x18]`–`[0x20]` are populated correctly. However, the **load-completion flags** `flags[4]` and `flags[5]` are only set through the packed (JMD) initialization code path. Since that path is bypassed, these flags remain `0`.

With these flags unset, a later stage of the field loader falls through to a code path that expects original developer-format text files. For example, it attempts to open:

```
DataRaw\field\common\xref\j2m_xref.xml
```

A standard JMD extraction only produces the compiled binary form:

```
DataRaw\field\common\xref\j2m_xref.0m
```

The `.xml` file does not exist. The engine attempts to open it, receives a null pointer, and dereferences invalid memory — resulting in an access violation crash.

### Fix: Runtime Memory Patch (Recommended)

The crash can be prevented by forcing the two missing flags to `1` at runtime via a background thread or DLL injection:

- **Target address**: `base + 0x00D8F9B0`
- **Patch**: Set `flags[4]` and `flags[5]` to `1`
- **Precondition**: Wait until `flags[0]`–`[3]` are all set (confirming core data categories have loaded) and the field data pointers at `[0x18]`, `[0x1C]`, `[0x20]` are non-null (confirming field data was loaded from `DataRaw` successfully).

Once both flags are set, the engine recognizes the field data as fully loaded and skips the developer-mode text file fallback path, allowing the game to run normally with unpacked resources.

### Alternative Fix: Manual File Placement

If the memory patch is not used, the missing developer-format files must be placed manually in the expected paths under `DataRaw`. However, this approach is generally **impractical** because:

- The original developer `.xml` source files are not included in the retail client's JMD archives.
- The compiled `.0m` binary format is not a simple rename of the `.xml`; they have different internal structures.
