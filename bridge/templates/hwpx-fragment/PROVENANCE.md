# HWPX fragment template provenance

The first version used a synthetic pwh fixture. Following an actual Hancom 2024 InsertFile no-op, the cached version, package metadata, header and section/column settings in base.json were replaced with the local HWPX skill's `templates/base` scaffolding (`the local hwpx skill templates/base`). No personal document body, preview image, or font binary is included. The previous synthetic parts are retained in `investigation/synthetic-fragment-base.json` for comparison. The `version.xml` now uses the `tagetApplication` spelling read by Hancom's published OWPML model: https://github.com/hancom-io/hwpx-owpml-model/blob/main/OWPML/Document.cpp

The JavaScript builder authors a new one-section fragment body and adds its own paragraph, editable table, and embedded picture markup. Font family names are references to system-installed fonts only; no font is embedded or redistributed. Additional title/heading/caption character shapes and border fills are derived locally from the baseline. Actual glyph availability is determined by Hancom Office on the host.

Format references: the local hwpx skill templates/base and references/hwpx-format.md, plus the original python-hwpx object authoring source (Apache-2.0) https://github.com/airmang/python-hwpx/blob/main/src/hwpx/oxml/objects.py, consulted for the required OWPML picture child structure. Its implementation is not bundled or executed. ZIP encoding uses an authored uncompressed ZIP writer with Node built-ins only.

This is an insertion fragment, not a replacement document. KeepSection=0 is applied by the native insertion caller; rendered layout and native import/readback must be verified separately from structural package tests.
