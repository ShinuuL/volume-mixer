# Task 14 Report: Publicacao single-file

**Status:** done

## Changes

- Modified `VolumeMixer/VolumeMixer.csproj`: added `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, and `RuntimeIdentifier` properties to the PropertyGroup.

## Publish Command

```bash
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet publish VolumeMixer -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o VolumeMixer/bin/publish
```

## Publish Output

```
VolumeMixer -> VolumeMixer/bin/Release/net8.0-windows/win-x64/VolumeMixer.dll
VolumeMixer -> VolumeMixer/bin/publish/
```

## Artifact Verification

| File | Size |
|------|------|
| VolumeMixer.exe | 154 MB (161,676,807 bytes) |
| VolumeMixer.pdb | 29 KB (30,392 bytes) |

## Tests

65/65 passed, 0 failed, 0 skipped (144 ms)

## Concerns

- None. Single-file self-contained publish working correctly on win-x64.
