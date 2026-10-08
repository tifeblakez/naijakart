# ADR-0001: Engine-agnostic gameplay core compiled by both Unity and .NET

**Status:** accepted

## Context
The PRD requires server authority for all competitive state (§65, §91), client prediction (§66) and
tests for every gameplay system (§90). A dedicated server cannot run Unity cheaply, and Unity cannot
run the server's tests in CI without a licence.

## Decision
All gameplay rules live in `Assets/NaijaKart/Runtime/Core` as plain C# (netstandard2.1, C# 9, no
UnityEngine). Unity compiles it through an asmdef with `noEngineReferences`; `Build/NaijaKart.Core`
compiles the same files with the .NET SDK for the server and CI. Unity-specific code lives in a
separate assembly that references Core.

## Consequences
* One implementation of driving, checkpoints, items and LASTMA for server, prediction and tests.
* Core cannot use Unity maths/physics; it has its own `Vec3` and a kinematic arcade model (which the PRD prefers anyway: "fun over simulation").
* Config POCOs use public fields so JsonUtility, Newtonsoft and System.Text.Json all work.
