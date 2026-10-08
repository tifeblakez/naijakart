# ADR-0003: Transport — loopback + TCP/JSON first, UDP binary next

**Status:** accepted (TCP/JSON); **proposed** (UDP)

## Context
PRD §66 requires low-latency input, interpolation, prediction, reconnect and testing across network
conditions. The choice of networking library materially affects architecture (PRD §105), so it is
recorded here rather than decided silently.

## Decision now
* `ITransport` abstracts the wire. All server and client logic is written against it.
* `LoopbackTransportHub` provides deterministic latency/jitter/loss/disconnect for tests and offline practice.
* `TcpJsonServerTransport` / `TcpJsonClientTransport` ship as the first real transport: trivial to debug, no third-party dependency, works from Unity with `System.Net.Sockets`.

## Proposed next
Replace with a UDP transport (candidates: LiteNetLib, ENet-CSharp, or Unity Transport) using a binary
codec for `RaceSnapshot` with per-field quantisation and delta against the last acked snapshot.
Handlers do not change: envelopes are flat and codec-independent.

## Why not Netcode for GameObjects / Mirror / Photon?
They couple simulation to Unity objects and (for NGO/Photon) to a Unity host, which conflicts with
ADR-0001/0002 and with running the authority as a cheap headless .NET process. They remain usable for
cosmetic-only sync in the future if ever needed.
