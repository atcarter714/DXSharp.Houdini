# Architecture

The SDK is independent of any proprietary Arkaen project. Public interfaces must not expose internal project schemas, orbital scripts or assets.

## Design decisions

- HDK: native C++ compiled against the exact installed toolkit; narrow C ABI or process boundary for future managed GPU execution. Never assume Houdini viewport device ownership.
- HAPI: Windows x64, dynamic installation discovery, strict original baseline gate for qualified operations, stable version-query entry point first. Generate blittable bindings from locally installed headers, preserve notices where applicable, and verify sizes, offsets and signatures with native fixtures before use.
- Sessions: named-pipe transport first; serialize operations per session, explicit ownership/cleanup, cancellation and diagnostic capture. Do not initialize or load assets without explicit user intent and valid licensing.
- Geometry: preserve points, vertex/corner topology, primitive and detail domains, explicit coordinate conventions and typed attributes. Initial polygon topology is deliberately small; arbitrary polygon triangulation and attribute expansion are separate operations.
- DXSharp: preview.4 dependency only in integration layer; renderer-independent HAPI and geometry projects stay usable without Direct3D.
- HOM: Python tools, package installer and asset export are subsequent milestones.
- Private testing: realistic planetary assets and proprietary orbital algorithms remain outside the public source tree. Use local paths and independently authorized fixtures.

## Next milestones

1. Qualify the native build and version probe on Houdini 22.0.514; capture ABI JSON.
2. Generate and test native HAPI structures and function signatures from the exact local headers.
3. Named-pipe session lifecycle and licensed cook of a minimal original asset.
4. Point/vertex/primitive attributes and topology round-trip, then GPU upload.
5. HDK compute SOP with explicit D3D12 resource ownership, fences and CPU readback.
6. Optional asset baking, SessionSync, HOM packaging and Noesis editor integration.
