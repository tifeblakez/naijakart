#!/usr/bin/env python3
"""Generates Assets/StreamingAssets/NaijaKart/Config/tracks/third-mainland-rush.json.

Greybox layout for the vertical-slice track (PRD §46.1, §83). Units are metres, Y is up.
The loop is ~1.5 km: a long bridge straight (overtaking), a construction chicane (risk),
the Oworonshoki descent with a hairpin (skill section), an under-bridge shortcut (risk route),
and the Adekunle return with a kink before the start/finish line.
Re-run after editing; do not hand-edit the JSON.
"""
import json, math, os

pts = []
def add(x, z, y=0.0):
    pts.append((x, y, z))

def arc(cx, cz, r, a0, a1, n, y=0.0):
    for i in range(1, n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        add(cx + r * math.cos(a), cz + r * math.sin(a), y)

# Start/finish on the bridge deck heading +Z. Bridge rises gently then falls.
for i in range(0, 13):
    add(0, i * 30, min(6.0, i * 1.0) if i < 6 else max(0.0, 6.0 - (i - 6) * 1.2))
# Construction chicane (narrow, right-left) around z=390..470
add(6, 400); add(14, 420); add(14, 445); add(4, 465); add(-4, 485)
# Sweep right onto the Oworonshoki ramp
arc(60, 500, 64, 180, 90, 6)              # from (-4,500) around to (60,564)
add(120, 572); add(180, 572); add(230, 566)
# Descent + hairpin
arc(240, 520, 46, 90, -90, 8)             # hairpin: top at (240,566) down to (240,474)
add(200, 468); add(150, 462); add(110, 452)
# Lagoon-side S-bend
add(80, 430); add(70, 400); add(84, 372); add(92, 345); add(80, 318)
# Adekunle return: come back south on a SEPARATE road east of the bridge (x = 34, never closer
# than ~30 m to the outbound road, so nearest-centreline projection is unambiguous), then a
# radius-17 hairpin south of the start line feeds the start straight heading +Z.
add(70, 290); add(52, 255); add(38, 220); add(34, 190); add(34, 140); add(34, 90); add(34, 30); add(34, -60)
arc(17, -60, 17, 0, -180, 8)              # hairpin: (34,-60) → (17,-77) → (0,-60)
# start straight (0,-60) → (0,0); the grid sits on it.
# (loop closes back to (0,0) heading +Z)

# Remove near-duplicate consecutive points
clean = []
for p in pts:
    if not clean or math.dist((clean[-1][0], clean[-1][2]), (p[0], p[2])) > 2.0:
        clean.append(p)
pts = clean

# geometry helpers
def seglen(a, b): return math.dist((a[0], a[2]), (b[0], b[2]))
cum = [0.0]
for i in range(len(pts)):
    cum.append(cum[-1] + seglen(pts[i], pts[(i + 1) % len(pts)]))
lap = cum[-1]

def sample(d):
    d %= lap
    for i in range(len(pts)):
        if d <= cum[i + 1]:
            a, b = pts[i], pts[(i + 1) % len(pts)]
            t = (d - cum[i]) / max(1e-6, cum[i + 1] - cum[i])
            p = tuple(a[k] + (b[k] - a[k]) * t for k in range(3))
            dx, dz = b[0] - a[0], b[2] - a[2]
            n = math.hypot(dx, dz) or 1.0
            return p, (dx / n, dz / n)
    return pts[0], (0, 1)

def v3(p): return {"X": round(p[0], 2), "Y": round(p[1], 2), "Z": round(p[2], 2)}

HALF_WIDTH = 7.5
checkpoints = []
n_cp = 12
for c in range(n_cp):
    p, _ = sample(lap * c / n_cp)
    checkpoints.append({"id": f"cp{c:02d}", "gates": [{"position": v3(p), "radius": HALF_WIDTH + 3.0, "isShortcut": False, "label": "start" if c == 0 else ""}]})

# Risk route: the under-bridge shortcut. A narrow secondary road cuts straight across the inside
# of the hairpin (the main road goes around at x≈286). Checkpoints that sit on the hairpin get an
# alternative gate on the shortcut, so taking it is legal and skipping gates stays impossible.
shortcut_points = [(234.0, 0.0, 574.0), (240.0, -2.0, 556.0), (240.0, -3.0, 486.0), (232.0, -1.0, 468.0), (214.0, 0.0, 466.0)]
shortcut_roads = [{"id": "under_bridge", "points": [v3(p) for p in shortcut_points], "halfWidth": 4.0, "gripMultiplier": 0.85, "speedMultiplier": 1.0}]
def on_hairpin(cp):
    g = cp["gates"][0]["position"]
    return g["X"] > 230 and 470 <= g["Z"] <= 580
hairpin_cps = [i for i in range(n_cp) if on_hairpin(checkpoints[i])]
for i in hairpin_cps:
    z = checkpoints[i]["gates"][0]["position"]["Z"]
    gz = 540.0 if z > 520 else 480.0
    checkpoints[i]["gates"].append({"position": {"X": 240.0, "Y": -3.0, "Z": gz}, "radius": 5.0, "isShortcut": True, "label": "under_bridge"})

item_boxes = []
for r, frac in enumerate([0.12, 0.42, 0.70, 0.88]):
    p, d = sample(lap * frac)
    rx, rz = d[1], -d[0]
    for k in (-1, 0, 1):
        off = k * HALF_WIDTH * 0.55
        item_boxes.append({"id": f"box{r}_{k+1}", "position": v3((p[0] + rx * off, p[1], p[2] + rz * off))})

anchors = [
    {"id": "bridge_traffic", "distanceAlongTrack": round(lap * 0.08, 1), "allowedKinds": ["Traffic", "Pothole", "DanfoCross"]},
    {"id": "construction", "distanceAlongTrack": round(lap * 0.27, 1), "allowedKinds": ["Pothole", "GoSlow"]},
    {"id": "ramp", "distanceAlongTrack": round(lap * 0.40, 1), "allowedKinds": ["OkadaCross", "Traffic"]},
    {"id": "lagoon", "distanceAlongTrack": round(lap * 0.65, 1), "allowedKinds": ["Flood", "OkadaCross"]},
    {"id": "adekunle", "distanceAlongTrack": round(lap * 0.85, 1), "allowedKinds": ["DanfoCross", "CheckpointStop", "Traffic"]},
]

track = {
    "id": "third_mainland_rush",
    "displayName": "Third Mainland Rush",
    "city": "Lagos",
    "description": "Bridge traffic, Danfos, potholes, construction and an under-bridge shortcut. Learn it or lose it.",
    "centreline": [v3(p) for p in pts],
    "roadHalfWidth": HALF_WIDTH,
    "shortcutRoads": shortcut_roads,
    "checkpoints": checkpoints,
    "itemBoxes": item_boxes,
    "roadEventAnchors": anchors,
    "referenceLapSeconds": 68.0,
    "baseGripMultiplier": 1.0,
    "baseSpeedMultiplier": 1.0,
    "sceneKey": "Track_ThirdMainlandRush"
}
out = os.path.join(os.path.dirname(__file__), "..", "Assets", "StreamingAssets", "NaijaKart", "Config", "tracks", "third-mainland-rush.json")
with open(out, "w") as f:
    json.dump(track, f, indent=2)
print(f"wrote {os.path.normpath(out)}: {len(pts)} points, lap {lap:.0f} m, {len(checkpoints)} checkpoints, {len(item_boxes)} boxes, shortcut on {hairpin_cps}")
