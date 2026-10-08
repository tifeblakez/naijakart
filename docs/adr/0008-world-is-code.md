# ADR-0008: The world is code (no artists, no imported assets)

## Status
Accepted (2026-10-08)

## Context
The product owner decided that every environment, vehicle and effect is built in this repository
without an art team: no modelling, texturing or sourcing of assets. At the same time the graphics
must match the Race HUD concept image: a sunlit Third Mainland Bridge over the lagoon with a dense
skyline, crowds, signage and stylised karts with visible drivers. The concept is an illustration;
we need a real-time scene on mid-range Android phones (PRD §5, §21).

## Decision
1. `Core/World/WorldBuilder` generates the whole world from the track definition: geometry,
   placement, colours and sign text, deterministic per seed. It is engine-agnostic and tested like
   any other Core system.
2. Surface detail is procedural in shaders, keyed by material hints; text is rasterised at runtime.
   No texture, model or font file is committed for the world (the previewer's HUD font is a
   dependency, not an asset).
3. `tools/world-preview` is the reference renderer and the review loop: look-development rounds
   render stills and a video against the concept image, and the approved look is then matched in
   Unity (URP shader graphs implementing the same hints).
4. Fidelity work is iterative: each round adds scene density, material response or effects,
   measured against the concept, and the mobile budget (PRD §21) decides what ships: the generator
   exposes density parameters so the phone build can lower crowd, boat and building counts without
   changing the layout.

## Consequences
* Everything visual is reviewable as code and reproducible from a seed.
* An illustration's painterly detail is not reachable with geometry alone; the target is the same
  composition, lighting, colour and density, in a consistent stylised 3D look.
* Unity work later is shader and lighting setup, not asset import.
