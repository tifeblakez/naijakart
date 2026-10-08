using System.Collections.Generic;
using NaijaKart.Core.Simulation;
using UnityEngine;

namespace NaijaKart.Unity.Race
{
    /// <summary>Ring buffer of timestamped snapshots for one remote kart, sampled with interpolation.</summary>
    public sealed class RemoteKartBuffer
    {
        private struct Entry { public float Time; public ParticipantSnapshot Snap; }
        private readonly List<Entry> _entries = new List<Entry>(16);

        public void Push(float time, ParticipantSnapshot snap)
        {
            _entries.Add(new Entry { Time = time, Snap = snap });
            if (_entries.Count > 12) _entries.RemoveAt(0);
        }

        public void Sample(float renderTime, out Vector3 position, out float heading, out ParticipantSnapshot latest)
        {
            latest = _entries.Count > 0 ? _entries[_entries.Count - 1].Snap : default;
            if (_entries.Count == 0) { position = Vector3.zero; heading = 0f; return; }
            if (_entries.Count == 1 || renderTime >= _entries[_entries.Count - 1].Time)
            {
                // Extrapolate briefly along the last known velocity.
                var last = _entries[_entries.Count - 1];
                float ahead = Mathf.Clamp(renderTime - last.Time, 0f, 0.15f);
                position = last.Snap.Position.ToUnity() + last.Snap.FullState.Velocity.ToUnity() * ahead;
                heading = last.Snap.Heading;
                return;
            }
            for (int i = 0; i < _entries.Count - 1; i++)
            {
                var a = _entries[i];
                var b = _entries[i + 1];
                if (renderTime < a.Time || renderTime > b.Time) continue;
                float t = Mathf.InverseLerp(a.Time, b.Time, renderTime);
                position = Vector3.Lerp(a.Snap.Position.ToUnity(), b.Snap.Position.ToUnity(), t);
                heading = Mathf.LerpAngle(a.Snap.Heading * Mathf.Rad2Deg, b.Snap.Heading * Mathf.Rad2Deg, t) * Mathf.Deg2Rad;
                latest = b.Snap;
                return;
            }
            var first = _entries[0];
            position = first.Snap.Position.ToUnity();
            heading = first.Snap.Heading;
        }
    }
}
