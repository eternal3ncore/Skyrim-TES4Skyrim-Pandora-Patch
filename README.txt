TES4ConverterPandoraPatch_v0_3 - source/build package

Expected extracted project folder:
  C:\Games\Skyrim\ChatGPT Projects\TES4ConverterPandoraPatch_v0_3

Build:
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkyrimRoot "C:\Games\Skyrim"

Pandora v4.4.0-beta's architecture-specific *_win-x64_net.zip release is a
self-contained single-file build. Therefore an installed Pandora can legitimately have
no loose "Pandora API.dll" for a native plugin project to reference.

v0.3 first searches Skyrim, its parent Games directory, and Downloads. If no loose API
assembly is found, build.ps1 automatically downloads Pandora's pinned v4.4.0-beta
ordinary (non-single-file) release into a local .pandora-reference cache and uses its
Pandora API.dll only as a compile-time reference. Pandora DLLs are NOT bundled into the
finished patch.

Manual override, if desired:
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -PandoraReferenceDir "C:\path\to\loose\Pandora\dlls"

Successful build output:
  dist\TES4ConverterPandoraPatch_v0_3.zip

Install that ZIP as a Skyrim mod. Refresh/reopen Pandora, tick:
  TES4Converter Compatibility
then Launch Pandora normally.

Project files are intentionally at this archive's root.
