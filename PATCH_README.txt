TES4Converter Compatibility for Pandora Behaviour Engine+
Version 0.1

PURPOSE
-------
Preserves the TES4Converter's custom Oblivion/Morrowind projects in Pandora's final
animationdatasinglefile.txt and animationsetdatasinglefile.txt without replacing
Pandora's changes to normal Skyrim projects.

INSTALLATION
------------
Install the built TES4ConverterPandoraPatch_v0_3.zip as a normal Skyrim mod.
It places the patch at:

  Data\Pandora_Engine\mod\TES4ConverterCompatibility\

Refresh/restart Pandora, tick "TES4Converter Compatibility", then Launch Pandora normally.

HOW IT WORKS
------------
Pandora 4.x preloads its template AnimData/AnimSetData before selectable patches run.
A normal text-only patch therefore cannot create the converter's custom projects.
This patch uses Pandora's supported native patch mechanism. At PreLaunch it parses the
bundled converter files with Pandora's own current parsers and appends only missing
projects whose names start with:

  tes4oblivion_
  tes4morrowind_

Existing Pandora projects and all changes already applied by other selected patches are
left intact. If a TES4 project already exists in Pandora's active data, this patch leaves
that active version untouched rather than overwriting it.

The supplied converter snapshot contains 110 TES4 projects in each file.

DIAGNOSTIC LOG
--------------
The native plugin writes:

  Data\Pandora_Engine\mod\TES4ConverterCompatibility\native\TES4ConverterCompatibility\TES4ConverterPandoraPatch.log

Expected success on current Pandora templates is approximately:

  AnimData ... found 110 TES4 projects; injected 110 missing projects.
  AnimSetData ... found 110 TES4 projects; injected 110 missing projects.
  SUCCESS ...

If a future Pandora version natively includes some/all TES4 projects, injected counts may
be lower; existing entries are deliberately preserved.

COMPATIBILITY TARGET
--------------------
Designed from Pandora Behaviour Engine+ source at commit:
  90e412bff0a6f752e79a9560ff0aeb22d5f7c828
and Pandora API source at commit:
  9ec15512aec306e68359c1893b5236f3ce8a24fb

The plugin intentionally uses reflection only for Pandora implementation details that are
not exposed by the public API. If Pandora changes those internals, the patch fails loudly
and records the exception instead of silently generating incomplete converter data.
