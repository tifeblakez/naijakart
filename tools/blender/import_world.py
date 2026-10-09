"""Imports the generated Naija Kart world (tools/world-preview/public/world.json) and a race replay
into Blender, with materials, signs, props, karts, lighting and a chase camera matching
tools/world-preview/index.html. The generator stays the source of truth: nothing here invents track
geometry; it only translates what WorldBuilder exported.

Three ways to run it (see tools/blender/README.md):
  blender -b --python tools/blender/import_world.py -- --world tools/world-preview/public/world.json \
      --replay tools/world-preview/public/replay.json --t 14 --save out/blender/third_mainland_rush.blend \
      --render out/blender/chase.png
  python -c "import bpy" environment (pip `bpy` wheel): same command with `python tools/blender/import_world.py ...`
  Blender's Text Editor or the MCP connection: exec the file, then call
      build(world="…/world.json", replay="…/replay.json", t=14)

Coordinates: the generator and previewer are Y-up (three.js); Blender is Z-up. Every point goes
through (x, y, z) -> (x, -z, y), a proper rotation, so yaw stays yaw and triangle winding is kept.
"""
import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

# ---------------------------------------------------------------- material table (mirrors material() in index.html)
NO_SHADOW_CAST = {"water", "ground", "road", "sand", "grass"}
DISPLAY_FONT_CANDIDATES = ["LilitaOne-Regular.ttf", "Lilita One.ttf", "lilita-one-latin-400-normal.ttf"]
TEXT_FONT_CANDIDATES = ["Nunito-Black.ttf", "Nunito-ExtraBold.ttf", "Nunito-Bold.ttf", "nunito-latin-900-normal.ttf"]


def to_blender(x, y, z):
    return (x, -z, y)


def log(msg):
    print("[nk-blender] " + msg)


# ---------------------------------------------------------------- node helpers
def _set_input(node, names, value):
    for n in names if isinstance(names, (list, tuple)) else [names]:
        if n in node.inputs:
            node.inputs[n].default_value = value
            return True
    return False


def _link(tree, a, b):
    tree.links.new(a, b)


def _math(tree, op, a, b=None, loc=(0, 0)):
    n = tree.nodes.new("ShaderNodeMath")
    n.operation = op
    n.location = loc
    if hasattr(a, "is_output"):
        _link(tree, a, n.inputs[0])
    else:
        n.inputs[0].default_value = a
    if b is not None:
        if hasattr(b, "is_output"):
            _link(tree, b, n.inputs[1])
        else:
            n.inputs[1].default_value = b
    return n.outputs[0]


def _noise(tree, coords, scale, detail=2.0, loc=(0, 0)):
    n = tree.nodes.new("ShaderNodeTexNoise")
    n.location = loc
    n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = detail
    _link(tree, coords, n.inputs["Vector"])
    return n.outputs["Fac"]


def _mix_color(tree, fac, a, b, loc=(0, 0)):
    """fac: socket or float; a/b: sockets or RGBA tuples. Returns the colour socket."""
    n = tree.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    n.location = loc
    if hasattr(fac, "is_output"):
        _link(tree, fac, n.inputs["Factor"])
    else:
        n.inputs["Factor"].default_value = fac
    for sock, val in (("A", a), ("B", b)):
        if hasattr(val, "is_output"):
            _link(tree, val, n.inputs[6 if sock == "A" else 7])
        else:
            n.inputs[6 if sock == "A" else 7].default_value = val
    return n.outputs[2]


def _scale_color(tree, color_socket, factor_socket, loc=(0, 0)):
    n = tree.nodes.new("ShaderNodeMix")
    n.data_type = "RGBA"
    n.blend_type = "MULTIPLY"
    n.location = loc
    n.inputs["Factor"].default_value = 1.0
    _link(tree, color_socket, n.inputs[6])
    f = tree.nodes.new("ShaderNodeCombineColor")
    f.location = (loc[0] - 180, loc[1] - 160)
    for i in range(3):
        _link(tree, factor_socket, f.inputs[i])
    _link(tree, f.outputs[0], n.inputs[7])
    return n.outputs[2]


def make_material(name, hint, rgb, emissive, opacity, world_info):
    """One Principled material per batch, with the previewer's roughness/metalness table and
    procedural detail driven by world-space noise (no textures on disk)."""
    night = bool(world_info.get("isNight"))
    lamp_glow = world_info.get("lampGlow") or 0.6
    window_lit = world_info.get("windowLitChance") or 0.14
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    _link(tree, bsdf.outputs[0], out.inputs[0])
    color = (rgb[0], rgb[1], rgb[2], 1.0)
    base = tree.nodes.new("ShaderNodeRGB")
    base.location = (-900, 300)
    base.outputs[0].default_value = color
    coords = tree.nodes.new("ShaderNodeTexCoord")
    coords.location = (-1200, -200)
    wp = coords.outputs["Object"]  # objects sit at the origin, so Object == world coordinates
    rough, metal, color_socket = 0.85, 0.0, base.outputs[0]
    rough_socket = None
    emission_color, emission_strength = None, 0.0

    if hint == "paint":
        rough, metal = 0.38, 0.15
        _set_input(bsdf, ["Coat Weight", "Clearcoat"], 1.0)
        _set_input(bsdf, ["Coat Roughness", "Clearcoat Roughness"], 0.08)
    elif hint == "chrome":
        rough, metal = 0.12, 1.0
    elif hint == "metal":
        rough, metal = 0.42, 0.85
    elif hint == "glass":
        rough, metal = 0.08, 0.1
        color = (rgb[0] * 0.6, rgb[1] * 0.6, rgb[2] * 0.6, 1.0)
        base.outputs[0].default_value = color
        _set_input(bsdf, ["Transmission Weight", "Transmission"], 0.75)
        mat.blend_method = "BLEND" if hasattr(mat, "blend_method") else None
    elif hint == "water":
        rough, metal = 0.08, 0.0
        deep = _noise(tree, wp, 0.012, 3.0, (-900, -100))
        mr = tree.nodes.new("ShaderNodeMapRange")
        mr.location = (-700, -100)
        mr.interpolation_type = "SMOOTHSTEP"
        mr.inputs["From Min"].default_value = 0.3
        mr.inputs["From Max"].default_value = 0.7
        _link(tree, deep, mr.inputs["Value"])
        fac = mr.outputs["Result"]
        color_socket = _mix_color(tree, fac, (0.02, 0.24, 0.46, 1), (0.08, 0.5, 0.64, 1), (-500, -100))
        bump = tree.nodes.new("ShaderNodeBump")
        bump.location = (0, -300)
        bump.inputs["Strength"].default_value = 0.12
        bump.inputs["Distance"].default_value = 0.4
        _link(tree, _noise(tree, wp, 0.35, 6.0, (-500, -400)), bump.inputs["Height"])
        _link(tree, bump.outputs[0], bsdf.inputs["Normal"])
    elif hint == "emissive":
        rough = 0.5
        emission_color = color
        emission_strength = (emissive or 1.5) * ((lamp_glow / 0.6) if (emissive or 0) <= 0.9 else 1.0)
    elif hint == "hologram":
        rough, metal = 0.1, 0.3
        emission_color = color
        emission_strength = emissive or 1.0
        _set_input(bsdf, "Alpha", opacity or 0.55)
        if hasattr(mat, "blend_method"):
            mat.blend_method = "BLEND"
    elif hint == "rubber":
        rough = 0.95
    elif hint == "skin":
        rough = 0.55
    elif hint == "sign":
        rough = 0.6
    elif hint == "road":
        g = _math(tree, "ADD", _math(tree, "MULTIPLY", _noise(tree, wp, 6.0, 1.0, (-900, -100)), 0.4, (-700, -100)),
                  _math(tree, "MULTIPLY", _noise(tree, wp, 47.0, 1.0, (-900, -300)), 0.6, (-700, -300)), (-500, -200))
        patch = _noise(tree, wp, 0.05, 4.0, (-900, -500))
        f1 = _math(tree, "MULTIPLY_ADD", g, 0.4, (-300, -100))
        f1.node.inputs[2].default_value = 0.8
        f2 = _math(tree, "MULTIPLY_ADD", patch, 0.2, (-300, -300))
        f2.node.inputs[2].default_value = 0.9
        color_socket = _scale_color(tree, _scale_color(tree, base.outputs[0], f1, (-100, 100)), f2, (100, 100))
        rough_socket = _math(tree, "MULTIPLY_ADD", patch, -0.3, (-100, -400))
        rough_socket.node.inputs[2].default_value = 0.72
    elif hint in ("concrete", "barrier"):
        n = _noise(tree, wp, 0.45 if hint == "concrete" else 0.6, 4.0, (-900, -100))
        f = _math(tree, "MULTIPLY_ADD", n, 0.26, (-500, -100))
        f.node.inputs[2].default_value = 0.84
        color_socket = _scale_color(tree, base.outputs[0], f, (-100, 100))
        rough = 0.9 if hint == "concrete" else 0.85
    elif hint == "tower":
        rough = 0.7
        color_socket, rough_socket, emission_color, emission_strength = _tower_nodes(tree, bsdf, base.outputs[0], rgb, coords, window_lit, night)
    elif hint in ("foliage", "trunk", "cloth", "grass", "ground", "sand"):
        scale, lo, hi = {"foliage": (2.2, 0.72, 0.55), "trunk": (5.0, 0.78, 0.4), "cloth": (30.0, 0.9, 0.2),
                         "grass": (6.0, 0.78, 0.45), "ground": (1.7, 0.85, 0.3), "sand": (3.0, 0.9, 0.2)}[hint]
        n = _noise(tree, wp, scale, 2.0, (-900, -100))
        f = _math(tree, "MULTIPLY_ADD", n, hi, (-500, -100))
        f.node.inputs[2].default_value = lo
        color_socket = _scale_color(tree, base.outputs[0], f, (-100, 100))
        rough = {"foliage": 0.9, "trunk": 0.95}.get(hint, 1.0)

    _link(tree, color_socket, bsdf.inputs["Base Color"])
    if rough_socket is not None:
        _link(tree, rough_socket, bsdf.inputs["Roughness"])
    else:
        bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if hint not in ("emissive", "hologram", "tower"):
        if emissive and emissive > 0:
            emission_color, emission_strength = color, emissive
        if opacity is not None and opacity < 1 and hint not in ("glass", "water"):
            _set_input(bsdf, "Alpha", opacity)
            if hasattr(mat, "blend_method"):
                mat.blend_method = "BLEND"
    if emission_color is not None:
        if hasattr(emission_color, "is_output"):
            _link(tree, emission_color, bsdf.inputs["Emission Color"])
        else:
            _set_input(bsdf, ["Emission Color", "Emission"], emission_color)
        if hasattr(emission_strength, "is_output"):
            _link(tree, emission_strength, bsdf.inputs["Emission Strength"])
        else:
            _set_input(bsdf, "Emission Strength", emission_strength)
    return mat


def _tower_nodes(tree, bsdf, base_color, rgb, coords, window_lit, night):
    """Window grid in world space, picked per face from the normal, lit cells by hash: the tower
    shader of the previewer, as nodes."""
    bid = (rgb[0] * 13.7 + rgb[1] * 7.1 + rgb[2] * 3.3) % 1.0
    cw = 2.2 + 1.6 * bid
    ch = 3.2 + 0.8 * ((bid * 5.3) % 1.0)
    geo = tree.nodes.new("ShaderNodeNewGeometry")
    geo.location = (-1200, -600)
    nsep = tree.nodes.new("ShaderNodeSeparateXYZ")
    nsep.location = (-1000, -600)
    _link(tree, geo.outputs["Normal"], nsep.inputs[0])
    psep = tree.nodes.new("ShaderNodeSeparateXYZ")
    psep.location = (-1000, -800)
    _link(tree, coords.outputs["Object"], psep.inputs[0])
    # Blender: walls facing +-X use (y, z) as the window plane; walls facing +-Y use (x, z).
    ax = _math(tree, "ABSOLUTE", nsep.outputs[0], None, (-800, -560))
    ay = _math(tree, "ABSOLUTE", nsep.outputs[1], None, (-800, -660))
    use_yz = _math(tree, "GREATER_THAN", ax, ay, (-600, -600))
    u = tree.nodes.new("ShaderNodeMix")
    u.data_type = "FLOAT"
    u.location = (-400, -700)
    _link(tree, use_yz, u.inputs["Factor"])
    _link(tree, psep.outputs[0], u.inputs[2])
    _link(tree, psep.outputs[1], u.inputs[3])
    comb = tree.nodes.new("ShaderNodeCombineXYZ")
    comb.location = (-200, -700)
    _link(tree, u.outputs[0], comb.inputs[0])
    _link(tree, psep.outputs[2], comb.inputs[1])
    brick = tree.nodes.new("ShaderNodeTexBrick")
    brick.location = (0, -700)
    brick.offset = 0.0
    brick.inputs["Scale"].default_value = 1.0
    brick.inputs["Mortar Size"].default_value = 0.22 + 0.16 * ((bid * 3.1) % 1.0)
    brick.inputs["Brick Width"].default_value = cw
    brick.inputs["Row Height"].default_value = ch
    brick.inputs["Color1"].default_value = (0, 0, 0, 1)
    brick.inputs["Color2"].default_value = (0, 0, 0, 1)
    brick.inputs["Mortar"].default_value = (1, 1, 1, 1)
    _link(tree, comb.outputs[0], brick.inputs["Vector"])
    window = _math(tree, "SUBTRACT", 1.0, brick.outputs["Fac"], (200, -700))
    # Roofs and floors (normal mostly vertical) have no windows.
    az = _math(tree, "ABSOLUTE", nsep.outputs[2], None, (-800, -760))
    wall = _math(tree, "LESS_THAN", az, 0.5, (-600, -760))
    window = _math(tree, "MULTIPLY", window, wall, (400, -700))
    tint = 0.2 + 0.6 * ((bid * 1.7) % 1.0)
    glass = (0.1 + (0.55 - 0.1) * tint, 0.25 + (0.8 - 0.25) * tint, 0.45 + (1.0 - 0.45) * tint, 1.0)
    color_socket = _mix_color(tree, window, base_color, glass, (600, -500))
    rough_socket = _math(tree, "MULTIPLY_ADD", window, -0.6, (600, -650))
    rough_socket.node.inputs[2].default_value = 0.7
    metal_socket = _math(tree, "MULTIPLY", window, 0.7, (600, -750))
    _link(tree, metal_socket, bsdf.inputs["Metallic"])
    # Lit cells: white noise on the cell index, thresholded by windowLitChance.
    cell = tree.nodes.new("ShaderNodeVectorMath")
    cell.operation = "FLOOR"
    cell.location = (200, -900)
    div = tree.nodes.new("ShaderNodeVectorMath")
    div.operation = "DIVIDE"
    div.location = (0, -900)
    div.inputs[1].default_value = (cw, ch, 1.0)
    _link(tree, comb.outputs[0], div.inputs[0])
    _link(tree, div.outputs[0], cell.inputs[0])
    wn = tree.nodes.new("ShaderNodeTexWhiteNoise")
    wn.noise_dimensions = "3D"
    wn.location = (400, -900)
    _link(tree, cell.outputs[0], wn.inputs["Vector"])
    lit = _math(tree, "GREATER_THAN", wn.outputs["Value"], 1.0 - window_lit, (600, -900))
    strength = _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", window, lit, (800, -900)), 0.3 + (0.9 if night else 0.0), (1000, -900))
    return color_socket, rough_socket, (1.0, 0.82, 0.5, 1.0), strength


# ---------------------------------------------------------------- meshes
def make_mesh_object(name, batch, material, collection):
    verts = batch["vertices"]
    tris = batch["triangles"]
    nv = len(verts) // 3
    nt = len(tris) // 3
    if nv == 0 or nt == 0:
        return None
    mesh = bpy.data.meshes.new(name)
    mesh.vertices.add(nv)
    co = [0.0] * (nv * 3)
    for i in range(nv):
        x, y, z = verts[3 * i], verts[3 * i + 1], verts[3 * i + 2]
        co[3 * i], co[3 * i + 1], co[3 * i + 2] = x, -z, y
    mesh.vertices.foreach_set("co", co)
    mesh.loops.add(nt * 3)
    mesh.loops.foreach_set("vertex_index", tris)
    mesh.polygons.add(nt)
    mesh.polygons.foreach_set("loop_start", list(range(0, nt * 3, 3)))
    if not mesh.polygons[0].bl_rna.properties["loop_total"].is_readonly:  # Blender < 3.6
        mesh.polygons.foreach_set("loop_total", [3] * nt)
    mesh.update(calc_edges=True)
    mesh.validate(verbose=False)
    normals = batch.get("normals")
    if normals and len(normals) == len(verts):
        nn = [0.0] * (nv * 3)
        for i in range(nv):
            x, y, z = normals[3 * i], normals[3 * i + 1], normals[3 * i + 2]
            nn[3 * i], nn[3 * i + 1], nn[3 * i + 2] = x, -z, y
        try:
            mesh.normals_split_custom_set_from_vertices([nn[3 * i:3 * i + 3] for i in range(nv)])
        except Exception as e:  # older/newer API differences never block the import
            log(f"custom normals skipped for {name}: {e}")
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    hint = batch["material"]
    if hint in NO_SHADOW_CAST:
        try:
            obj.visible_shadow = False
        except Exception:
            pass
    return obj


# ---------------------------------------------------------------- signs (text objects on a plane, like the canvas textures)
_font_cache = {}


def load_font(kind, font_dir):
    if kind in _font_cache:
        return _font_cache[kind]
    font = None
    candidates = DISPLAY_FONT_CANDIDATES if kind == "display" else TEXT_FONT_CANDIDATES
    repo_fonts = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "out", "fonts")
    for d in [font_dir, os.environ.get("NK_FONT_DIR"), repo_fonts]:
        if not d:
            continue
        for c in candidates:
            p = os.path.join(d, c)
            if os.path.exists(p):
                try:
                    font = bpy.data.fonts.load(p, check_existing=True)
                    break
                except Exception as e:
                    log(f"font {p} failed: {e}")
        if font:
            break
    _font_cache[kind] = font
    return font


def flat_material(name, rgb, emission_strength, alpha=1.0, cache=None):
    key = (name, tuple(round(c, 3) for c in rgb), round(emission_strength, 3), alpha)
    if cache is not None and key in cache:
        return cache[key]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    bsdf.inputs["Roughness"].default_value = 0.55
    _set_input(bsdf, ["Emission Color", "Emission"], (rgb[0], rgb[1], rgb[2], 1.0))
    _set_input(bsdf, "Emission Strength", emission_strength)
    if alpha < 1.0:
        _set_input(bsdf, "Alpha", alpha)
        if hasattr(mat, "blend_method"):
            mat.blend_method = "BLEND"
    if cache is not None:
        cache[key] = mat
    return mat


def make_text(name, body, size, z, fg_mat, font, parent, collection, max_w, depth_offset=-0.012):
    curve = bpy.data.curves.new(name, type="FONT")
    curve.body = body
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    if font:
        curve.font = font
    curve.materials.append(fg_mat)
    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)
    obj.parent = parent
    obj.rotation_euler = (math.pi / 2, 0, 0)
    obj.location = (0, depth_offset, z)
    bpy.context.view_layer.update()
    w = obj.dimensions.x
    if w > max_w and w > 0:
        obj.scale.x = max_w / w
    return obj


def make_sign(sign, parent_collection, world_info, font_dir, mat_cache, index):
    style = sign.get("style") or "banner"
    w, h = sign["width"], sign["height"]
    bg, fg = sign.get("background") or [0.1, 0.1, 0.1], sign.get("foreground") or [1, 1, 1]
    night = bool(world_info.get("isNight"))
    glow = 1.6 if night and style in ("billboard", "shop", "gantry") else 0.12
    text, sub = sign.get("text") or "", sign.get("subText")
    display = load_font("display", font_dir)
    textfont = load_font("text", font_dir)
    name = f"sign_{index}_{style}"
    if style == "hologram":
        root = bpy.data.objects.new(name, None)
        parent_collection.objects.link(root)
        make_text(name + "_t", text, h * 0.85, -h * 0.05, flat_material("sign_holo", fg, 3.0, 0.9, mat_cache), display, root, parent_collection, w, 0.0)
    else:
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata([(-w / 2, 0, -h / 2), (w / 2, 0, -h / 2), (w / 2, 0, h / 2), (-w / 2, 0, h / 2)], [], [(0, 1, 2, 3)])
        mesh.update()
        mesh.materials.append(flat_material("sign_bg", bg, glow, 1.0, mat_cache))
        root = bpy.data.objects.new(name, mesh)
        parent_collection.objects.link(root)
        fg_mat = flat_material("sign_fg", fg, glow + 0.1, 1.0, mat_cache)
        if style == "gantry":
            make_text(name + "_t", text, h * (0.3 if sub else 0.42), h * (0.16 if sub else 0.0), fg_mat, textfont, root, parent_collection, w * 0.86)
            if sub:
                make_text(name + "_s", sub, h * 0.19, -h * 0.22, fg_mat, textfont, root, parent_collection, w * 0.86)
        elif style == "billboard":
            make_text(name + "_t", text, h * 0.4, h * 0.1, fg_mat, display, root, parent_collection, w * 0.9)
            if sub:
                make_text(name + "_s", sub, h * 0.13, -h * 0.18, fg_mat, textfont, root, parent_collection, w * 0.8)
            bar = bpy.data.meshes.new(name + "_bar")
            z0, z1 = h / 2 - h * 0.83, h / 2 - h * 0.78
            bar.from_pydata([(-w / 2, -0.01, z0), (w / 2, -0.01, z0), (w / 2, -0.01, z1), (-w / 2, -0.01, z1)], [], [(0, 1, 2, 3)])
            bar.update()
            bar.materials.append(fg_mat)
            bo = bpy.data.objects.new(name + "_bar", bar)
            parent_collection.objects.link(bo)
            bo.parent = root
        elif style == "plate":
            make_text(name + "_t", text, h * 0.6, -h * 0.02, fg_mat, display, root, parent_collection, w * 0.8)
        else:  # banner, shop
            make_text(name + "_t", text, h * (0.4 if sub else 0.56), h * (0.14 if sub else 0.0), fg_mat, display, root, parent_collection, w * 0.9)
            if sub:
                make_text(name + "_s", sub, h * 0.22, -h * 0.24, fg_mat, textfont, root, parent_collection, w * 0.9)
    p = sign["position"]
    root.location = to_blender(p["X"], p["Y"], p["Z"])
    root.rotation_euler = (0, 0, sign.get("yaw") or 0.0)
    return root


# ---------------------------------------------------------------- templates, props, replay
def build_template(t, world_info, font_dir, mat_cache, library):
    col = bpy.data.collections.new("tpl_" + t["name"])
    library.children.link(col)
    for i, b in enumerate(t["batches"]):
        mat = make_material(f"{t['name']}_{b['name']}", b["material"], (b["r"], b["g"], b["b"]), b.get("emissive"), b.get("opacity"), world_info)
        make_mesh_object(f"{t['name']}_{b['name']}", b, mat, col)
    for i, s in enumerate(t.get("signs") or []):
        make_sign(s, col, world_info, font_dir, mat_cache, f"{t['name']}_{i}")
    return col


def instance(name, template_col, collection, pos, yaw, scale=1.0):
    o = bpy.data.objects.new(name, None)
    o.instance_type = "COLLECTION"
    o.instance_collection = template_col
    o.empty_display_size = 0.5
    collection.objects.link(o)
    o.location = to_blender(*pos)
    o.rotation_euler = (0, 0, yaw)
    o.scale = (scale, scale, scale)
    return o


def frame_at(frames, t):
    lo, hi = 0, len(frames) - 2
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if frames[mid]["t"] <= t:
            lo = mid
        else:
            hi = mid - 1
    a, b = frames[lo], frames[lo + 1]
    u = min(1.0, max(0.0, (t - a["t"]) / max(1e-6, b["t"] - a["t"])))
    return a, b, u


def lerp_angle(a, b, u):
    d = b - a
    while d > math.pi:
        d -= 2 * math.pi
    while d < -math.pi:
        d += 2 * math.pi
    return a + d * u


def kart_states(replay, t):
    a, b, u = frame_at(replay["frames"], t)
    out = {}
    for ka in a["karts"]:
        kb = next((k for k in b["karts"] if k["id"] == ka["id"]), ka)
        x = ka["x"] + (kb["x"] - ka["x"]) * u
        y = ka["y"] + (kb["y"] - ka["y"]) * u
        z = ka["z"] + (kb["z"] - ka["z"]) * u
        yaw = lerp_angle(ka["h"], kb["h"], u)
        out[ka["id"]] = dict(ka, x=x, y=y, z=z, yaw=yaw)
    return out, a


def chase_camera_target(me, cam_state, dt):
    """The previewer's chase camera: behind and above the kart, looking 6.5 m ahead, smoothed."""
    fwd = Vector((math.sin(me["yaw"]), 0.0, math.cos(me["yaw"])))
    right = Vector((fwd.z, 0.0, -fwd.x))
    pos = Vector((me["x"], me["y"], me["z"]))
    dist = 6.2 + me["v"] * 0.03
    desired = pos - fwd * dist + Vector((0, 2.6, 0))
    if me.get("d"):
        desired -= right * (me.get("dd") or 0) * 1.0
    look = pos + Vector((0, 0.45, 0)) + fwd * 6.5
    if cam_state.get("pos") is None:
        cam_state["pos"], cam_state["look"] = desired, look
    else:
        cam_state["pos"] = cam_state["pos"].lerp(desired, 1 - math.exp(-6.5 * dt))
        cam_state["look"] = cam_state["look"].lerp(look, 1 - math.exp(-9 * dt))
    fov = 56 + min(me["v"], 45) * 0.18 + (8 if me.get("b") else 0)
    return cam_state["pos"], cam_state["look"], fov


def hero_camera_target(me):
    """Key-art framing like the concept images: low, close and wide, kart in the lower centre."""
    fwd = Vector((math.sin(me["yaw"]), 0.0, math.cos(me["yaw"])))
    right = Vector((fwd.z, 0.0, -fwd.x))
    pos = Vector((me["x"], me["y"], me["z"]))
    cam = pos - fwd * 5.2 + Vector((0, 1.9, 0)) + right * 0.35
    look = pos + Vector((0, 0.9, 0)) + fwd * 14.0
    return cam, look, 66.0


def place_camera(cam_obj, pos_three, look_three, fov_deg):
    p = Vector(to_blender(*pos_three))
    l = Vector(to_blender(*look_three))
    cam_obj.location = p
    cam_obj.rotation_euler = (l - p).to_track_quat("-Z", "Y").to_euler()
    cam_obj.data.sensor_fit = "VERTICAL"
    cam_obj.data.angle_y = math.radians(fov_deg)


# ---------------------------------------------------------------- scene: lights, sky, fog, colour
def setup_world(scene, world_info):
    night = bool(world_info.get("isNight"))
    sun_dir = world_info.get("sunDirection") or {"X": -0.4, "Y": -0.8, "Z": -0.3}
    d = Vector(to_blender(sun_dir["X"], sun_dir["Y"], sun_dir["Z"])).normalized()  # direction light travels
    sun_data = bpy.data.lights.new("Sun", type="SUN")
    sun_data.energy = float(world_info.get("sunIntensity") or 4.0) * (0.9 if not night else 0.6)
    sc = world_info.get("sunColor") or [1, 0.9, 0.77]
    sun_data.color = (sc[0], sc[1], sc[2])
    sun_data.angle = math.radians(1.2)
    sun = bpy.data.objects.new("Sun", sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sun.location = (0, 0, 120)

    world = bpy.data.worlds.new("Lagos sky")
    scene.world = world
    world.use_nodes = True
    tree = world.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputWorld")
    out.location = (600, 0)
    bg = tree.nodes.new("ShaderNodeBackground")
    bg.location = (400, 0)
    bg.inputs["Strength"].default_value = 0.7 if not night else 0.35
    _link(tree, bg.outputs[0], out.inputs[0])
    tc = tree.nodes.new("ShaderNodeTexCoord")
    tc.location = (-800, 0)
    sep = tree.nodes.new("ShaderNodeSeparateXYZ")
    sep.location = (-600, 0)
    _link(tree, tc.outputs["Generated"], sep.inputs[0])
    # In a world shader "Generated" is the view direction, so Z is the height above the horizon.
    up = _math(tree, "MAXIMUM", sep.outputs[2], 0.0, (-250, 0))
    up = _math(tree, "POWER", up, 0.5, (-100, 0))
    top, hor = world_info.get("skyTop") or [0.1, 0.36, 0.88], world_info.get("skyHorizon") or [0.76, 0.86, 0.97]
    col = _mix_color(tree, up, (hor[0], hor[1], hor[2], 1), (top[0], top[1], top[2], 1), (100, 0))
    # Warm band just above the horizon and the sun's glow, as in the previewer's sky shader.
    band = _math(tree, "MULTIPLY_ADD", up, -3.5, (-100, 200))
    band.node.inputs[2].default_value = 1.0
    band = _math(tree, "MAXIMUM", band, 0.0, (50, 200))
    band = _math(tree, "MULTIPLY", band, 0.35, (150, 200))
    col = _mix_color(tree, band, col, (1.0, 0.95, 0.88, 1), (300, 100))
    to_sun = -d
    dot = tree.nodes.new("ShaderNodeVectorMath")
    dot.operation = "DOT_PRODUCT"
    dot.location = (-600, 400)
    dot.inputs[1].default_value = (to_sun.x, to_sun.y, to_sun.z)
    _link(tree, tc.outputs["Generated"], dot.inputs[0])
    sdot = _math(tree, "MAXIMUM", dot.outputs["Value"], 0.0, (-400, 400))
    glow = _math(tree, "ADD", _math(tree, "MULTIPLY", _math(tree, "POWER", sdot, 48.0, (-250, 400)), 0.5, (-100, 400)),
                 _math(tree, "MULTIPLY", _math(tree, "POWER", sdot, 5.0, (-250, 550)), 0.12, (-100, 550)), (50, 450))
    glow = _math(tree, "ADD", glow, _math(tree, "MULTIPLY", _math(tree, "POWER", sdot, 1200.0, (-250, 700)), 6.0, (-100, 700)), (200, 500))
    # Clouds: noise on the direction projected onto a plane above the viewer, as in the previewer.
    proj = tree.nodes.new("ShaderNodeVectorMath")
    proj.operation = "DIVIDE"
    proj.location = (-600, -300)
    _link(tree, tc.outputs["Generated"], proj.inputs[0])
    zdiv = _math(tree, "ADD", sep.outputs[2], 0.12, (-800, -300))
    cz = tree.nodes.new("ShaderNodeCombineXYZ")
    cz.location = (-700, -400)
    for i in range(3):
        _link(tree, zdiv, cz.inputs[i])
    _link(tree, cz.outputs[0], proj.inputs[1])
    cloud_n = tree.nodes.new("ShaderNodeTexNoise")
    cloud_n.location = (-400, -300)
    cloud_n.inputs["Scale"].default_value = 2.1
    cloud_n.inputs["Detail"].default_value = 6.0
    cloud_n.inputs["Roughness"].default_value = 0.62
    _link(tree, proj.outputs[0], cloud_n.inputs["Vector"])
    cov = tree.nodes.new("ShaderNodeMapRange")
    cov.location = (-200, -300)
    cov.interpolation_type = "SMOOTHSTEP"
    cov.inputs["From Min"].default_value = 0.52
    cov.inputs["From Max"].default_value = 0.72
    _link(tree, cloud_n.outputs["Fac"], cov.inputs["Value"])
    horizon_fade = tree.nodes.new("ShaderNodeMapRange")
    horizon_fade.location = (-200, -500)
    horizon_fade.interpolation_type = "SMOOTHSTEP"
    horizon_fade.inputs["From Min"].default_value = 0.01
    horizon_fade.inputs["From Max"].default_value = 0.12
    _link(tree, sep.outputs[2], horizon_fade.inputs["Value"])
    cloud_fac = _math(tree, "MULTIPLY", _math(tree, "MULTIPLY", cov.outputs["Result"], horizon_fade.outputs["Result"], (0, -300)), 0.92, (150, -300))
    shade = tree.nodes.new("ShaderNodeTexNoise")
    shade.location = (-400, -650)
    shade.inputs["Scale"].default_value = 2.1
    shade.inputs["Detail"].default_value = 3.0
    _link(tree, proj.outputs[0], shade.inputs["Vector"])
    cloud_col = _mix_color(tree, shade.outputs["Fac"], (0.72, 0.78, 0.9, 1) if not night else (0.12, 0.1, 0.2, 1), (1.0, 1.0, 1.0, 1) if not night else (0.2, 0.18, 0.28, 1), (0, -650))
    col = _mix_color(tree, cloud_fac, col, cloud_col, (300, -200))
    sun_rgb = (1.0, 0.93, 0.8, 1.0) if not night else (0.7, 0.8, 1.0, 1.0)
    glow_col = _scale_color(tree, _mix_color(tree, 1.0, sun_rgb, sun_rgb, (250, 650)), glow, (400, 600))
    add = tree.nodes.new("ShaderNodeMix")
    add.data_type = "RGBA"
    add.blend_type = "ADD"
    add.location = (500, 300)
    add.inputs["Factor"].default_value = 1.0
    _link(tree, col, add.inputs[6])
    _link(tree, glow_col, add.inputs[7])
    col = add.outputs[2]
    _link(tree, col, bg.inputs["Color"])
    fog = world_info.get("fogColor") or [0.8, 0.87, 0.96]
    world.mist_settings.start = float(world_info.get("fogStart") or 200)
    world.mist_settings.depth = float((world_info.get("fogEnd") or 1600) - (world_info.get("fogStart") or 200))
    world.mist_settings.falloff = "LINEAR"
    return fog


def setup_fog_compositor(scene, fog_rgb):
    """Linear distance fog like scene.fog in the previewer, mixed in the compositor from the mist pass."""
    try:
        scene.view_layers[0].use_pass_mist = True
        scene.view_layers[0].use_pass_z = True
        if hasattr(scene, "compositing_node_group"):
            tree = bpy.data.node_groups.new("NK fog", "CompositorNodeTree")
            scene.compositing_node_group = tree
            iface = tree.interface
            iface.new_socket("Image", in_out="INPUT", socket_type="NodeSocketColor")
            iface.new_socket("Image", in_out="OUTPUT", socket_type="NodeSocketColor")
            gin = tree.nodes.new("NodeGroupInput")
            gout = tree.nodes.new("NodeGroupOutput")
        else:
            scene.use_nodes = True
            tree = scene.node_tree
            for n in list(tree.nodes):
                tree.nodes.remove(n)
            gin = tree.nodes.new("CompositorNodeRLayers")
            gout = tree.nodes.new("CompositorNodeComposite")
        rl = gin if gin.bl_idname == "CompositorNodeRLayers" else tree.nodes.new("CompositorNodeRLayers")
        mix = None
        for idname in ("CompositorNodeMixRGB", "ShaderNodeMix"):
            try:
                mix = tree.nodes.new(idname)
                break
            except RuntimeError:
                continue
        if mix is None:
            raise RuntimeError("no mix node type available in this compositor")
        if mix.bl_idname == "ShaderNodeMix":
            mix.data_type = "RGBA"
            fac, a, b, outp = mix.inputs["Factor"], mix.inputs[6], mix.inputs[7], mix.outputs[2]
        else:
            fac, a, b, outp = mix.inputs[0], mix.inputs[1], mix.inputs[2], mix.outputs[0]
        b.default_value = (fog_rgb[0], fog_rgb[1], fog_rgb[2], 1.0)
        # The background has no depth: keep the sky dome out of the fog, like scene.fog in the previewer.
        depth_name = "Depth" if "Depth" in rl.outputs else "Z"
        near = tree.nodes.new("ShaderNodeMath" if mix.bl_idname == "ShaderNodeMix" else "CompositorNodeMath")
        near.operation = "LESS_THAN"
        near.inputs[1].default_value = 1.0e6
        _link(tree, rl.outputs[depth_name], near.inputs[0])
        amount = tree.nodes.new(near.bl_idname)
        amount.operation = "MULTIPLY"
        _link(tree, rl.outputs["Mist"], amount.inputs[0])
        _link(tree, near.outputs[0], amount.inputs[1])
        scaled = tree.nodes.new(near.bl_idname)
        scaled.operation = "MULTIPLY"
        scaled.inputs[1].default_value = 0.65
        _link(tree, amount.outputs[0], scaled.inputs[0])
        _link(tree, rl.outputs["Image"], a)
        _link(tree, scaled.outputs[0], fac)
        # Colour grade from the previewer's ShaderPass: a little more saturation and contrast.
        try:
            hsv = tree.nodes.new("CompositorNodeHueSat")
            _set_input(hsv, "Saturation", 1.18)
            _link(tree, outp, hsv.inputs["Image"] if "Image" in hsv.inputs else hsv.inputs[0])
            _link(tree, hsv.outputs[0], gout.inputs[0])
        except Exception:
            _link(tree, outp, gout.inputs[0])
        return True
    except Exception as e:
        log(f"fog compositor not set up ({e}); render without distance fog")
        try:
            if hasattr(scene, "compositing_node_group"):
                scene.compositing_node_group = None
            else:
                scene.use_nodes = False
        except Exception:
            pass
        return False


def setup_render(scene, engine, samples, width, height):
    for e in ([engine] if engine else []) + ["CYCLES", "BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"]:
        try:
            scene.render.engine = e
            break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
        try:
            scene.cycles.device = "GPU"
        except Exception:
            pass
    else:
        try:
            scene.eevee.taa_render_samples = max(16, samples)
        except Exception:
            pass
    for vt in ("AgX", "Filmic", "Standard"):
        try:
            scene.view_settings.view_transform = vt
            break
        except TypeError:
            continue
    for look in ("AgX - Punchy", "AgX - Medium High Contrast", "Punchy", "Medium High Contrast", "None"):
        try:
            scene.view_settings.look = look
            break
        except TypeError:
            continue
    scene.view_settings.exposure = 0.15
    scene.view_settings.gamma = 1.0
    log(f"render: {scene.render.engine}, view {scene.view_settings.view_transform}, look {scene.view_settings.look}")


# ---------------------------------------------------------------- main build
def build(world, replay=None, t=14.0, mode="chase", font_dir=None, anim=None, fps=24, keep_scene=False,
          engine=None, samples=48, width=1280, height=592, max_karts=None, follow=None):
    """Imports the world and places the race at time t. Returns the camera object.
    anim=(start, duration) also bakes kart and camera keyframes for that window."""
    t_start = time.time()
    scene = bpy.context.scene
    if not keep_scene:
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        for c in list(bpy.data.collections):
            bpy.data.collections.remove(c)
    with open(world) as f:
        w = json.load(f)
    rep = None
    if replay:
        with open(replay) as f:
            rep = json.load(f)
    log(f"loaded world {w.get('displayName')} ({len(w['batches'])} batches, {len(w.get('signs') or [])} signs, {len(w.get('templates') or [])} templates)"
        + (f", replay {rep['karts'].__len__()} karts {len(rep['frames'])} frames" if rep else ""))
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    root = bpy.data.collections.new("Third Mainland Rush" if "mainland" in (w.get("trackId") or "") else (w.get("displayName") or "World"))
    scene.collection.children.link(root)
    col_world = bpy.data.collections.new("World meshes")
    col_signs = bpy.data.collections.new("Signs")
    col_props = bpy.data.collections.new("Props")
    col_race = bpy.data.collections.new("Race")
    library = bpy.data.collections.new("Templates (library)")
    for c in (col_world, col_signs, col_props, col_race, library):
        root.children.link(c)
    mat_cache = {}

    for b in w["batches"]:
        mat = make_material("w_" + b["name"], b["material"], (b["r"], b["g"], b["b"]), b.get("emissive"), b.get("opacity"), w)
        make_mesh_object("w_" + b["name"], b, mat, col_world)
    log(f"world meshes done in {time.time() - t_start:.1f}s")
    for i, s in enumerate(w.get("signs") or []):
        make_sign(s, col_signs, w, font_dir, mat_cache, i)
    log(f"signs done in {time.time() - t_start:.1f}s")

    templates = {}
    for tpl in w.get("templates") or []:
        templates[tpl["name"]] = build_template(tpl, w, font_dir, mat_cache, library)
    # The library holds the master copies; instances point at it. Hide it from view and render.
    lib_layer = next((lc for lc in bpy.context.view_layer.layer_collection.children[root.name].children if lc.collection == library), None)
    if lib_layer:
        lib_layer.exclude = True
    fallback = templates.get("kart_compact_sedan") or next(iter(templates.values()), None)

    for i, p in enumerate(w.get("props") or []):
        tpl = templates.get(p["template"], fallback)
        if not tpl:
            continue
        pos = (p["position"]["X"], p["position"]["Y"], p["position"]["Z"])
        o = instance(f"prop_{i}_{p['template']}", tpl, col_props, pos, p.get("yaw") or 0.0, p.get("scale") or 1.0)
        if p["template"] == "item_box":
            o.rotation_euler = (math.sin(t * 0.9) * 0.2, 0, t * 1.6)
            o.location.z = p["position"]["Y"] + math.sin(t * 2 + p["position"]["X"]) * 0.2

    cam_data = bpy.data.cameras.new("Chase camera")
    cam_data.clip_start = 0.3
    cam_data.clip_end = 3500
    cam = bpy.data.objects.new("Chase camera", cam_data)
    col_race.objects.link(cam)
    scene.camera = cam

    follow = (follow or rep.get("followKartId")) if rep else None
    kart_objs = {}
    hazard_objs = {}
    if rep:
        hazard_template = {"TrafficCar": "danfo_bus", "DanfoCrossing": "danfo_bus", "OkadaCrossing": "okada_bike", "OilPatch": "oil_patch", "Pothole": "pothole",
                           "SpikeStrip": "spike_strip", "GoSlowZone": "cone_row", "FloodZone": "flood", "PoliceCheckpoint": "checkpoint"}
        for k in rep["karts"][: (max_karts or len(rep["karts"]))]:
            tpl = templates.get(k.get("template"), fallback)
            kart_objs[k["id"]] = instance(("you_" if k["id"] == follow else "kart_") + k["id"], tpl, col_race, (0, 0, 0), 0.0)
            kart_objs[k["id"]]["racer"] = k.get("name") or k["id"]

        def apply(time_s, cam_state, dt, key=None):
            states, frame = kart_states(rep, time_s)
            me = states.get(follow) if follow else None
            for kid, ks in states.items():
                o = kart_objs.get(kid)
                if not o:
                    continue
                o.location = to_blender(ks["x"], ks["y"], ks["z"])
                lean = (-(ks.get("dd") or 0) * 0.12 if ks.get("d") else 0.0) - (ks.get("lat") or 0) * 0.015
                o.rotation_euler = (0.0, lean, ks["yaw"] + ((ks.get("dd") or 0) * 0.25 if ks.get("d") else 0.0))
                o.hide_render = o.hide_viewport = ks.get("st") == "Disconnected"
                if key is not None:
                    o.keyframe_insert("location", frame=key)
                    o.keyframe_insert("rotation_euler", frame=key)
            seen = set()
            for h in frame.get("hazards") or []:
                seen.add(h["id"])
                o = hazard_objs.get(h["id"])
                if o is None:
                    tpl = templates.get(hazard_template.get(h.get("k"), "cone_row"), fallback)
                    o = instance("hazard_" + h["id"], tpl, col_race, (0, 0, 0), 0.0)
                    hazard_objs[h["id"]] = o
                y = h["y"] + (math.sin(time_s * 12) * 0.15 if not h.get("armed") else 0.0)
                o.location = to_blender(h["x"], y, h["z"])
                if h.get("dx") or h.get("dz"):
                    o.rotation_euler = (0, 0, math.atan2(h.get("dx") or 0.0, h.get("dz") or 0.0))
                o.hide_render = o.hide_viewport = False
                if key is not None:
                    o.keyframe_insert("location", frame=key)
                    o.keyframe_insert("rotation_euler", frame=key)
                    o.keyframe_insert("hide_render", frame=key)
            for hid, o in hazard_objs.items():
                if hid not in seen:
                    o.hide_render = o.hide_viewport = True
                    if key is not None:
                        o.keyframe_insert("hide_render", frame=key)
            if me:
                if mode == "hero":
                    pos, look, fov = hero_camera_target(me)
                    place_camera(cam, pos, look, fov)
                elif mode == "overview":
                    u = max(0.0, time_s + 3) / 5
                    ang = math.pi * 1.25 - u * 0.5
                    r = 260 - u * 60
                    cx, cz = 17, 170
                    place_camera(cam, (cx + math.cos(ang) * r, 95 - u * 45, cz + math.sin(ang) * r), (cx + 20, 6, cz + 80), 44)
                else:
                    pos, look, fov = chase_camera_target(me, cam_state, dt)
                    place_camera(cam, pos, look, fov)
                if key is not None:
                    cam.keyframe_insert("location", frame=key)
                    cam.keyframe_insert("rotation_euler", frame=key)
                    cam.data.keyframe_insert("lens", frame=key)
            return me

        if anim:
            start, duration = anim
            scene.render.fps = fps
            scene.frame_start, scene.frame_end = 1, int(duration * fps)
            cam_state = {}
            for i in range(scene.frame_end):
                apply(start + i / fps, cam_state, 1.0 / fps, key=i + 1)
            scene.frame_set(max(1, min(scene.frame_end, int((t - start) * fps) + 1)))
            log(f"baked {scene.frame_end} frames from t={start}s")
        else:
            # Settle the smoothed camera the way the previewer does over the previous 2 s.
            cam_state = {}
            for i in range(48):
                apply(t - 2.0 + i / 24.0, cam_state, 1.0 / 24.0)
            apply(t, cam_state, 1.0 / 24.0)
    else:
        mm = w.get("minimap") or []
        if mm:
            p = mm[0]
            place_camera(cam, (p["X"], p["Y"] + 2.6, p["Z"] - 6.5), (p["X"], p["Y"] + 0.45, p["Z"] + 6.5), 60)

    fog = setup_world(scene, w)
    setup_render(scene, engine, samples, width, height)
    setup_fog_compositor(scene, fog)
    log(f"scene ready in {time.time() - t_start:.1f}s: {len(bpy.data.objects)} objects, {len(bpy.data.materials)} materials")
    return cam


def _parse(argv):
    import argparse
    p = argparse.ArgumentParser(description="Import the generated Naija Kart world into Blender.")
    p.add_argument("--world", default="tools/world-preview/public/world.json")
    p.add_argument("--replay", default="tools/world-preview/public/replay.json")
    p.add_argument("--no-replay", action="store_true")
    p.add_argument("--t", type=float, default=14.0, help="race time in seconds to show")
    p.add_argument("--mode", choices=["chase", "hero", "overview"], default="chase")
    p.add_argument("--anim", nargs=2, type=float, metavar=("START", "DURATION"), help="also bake keyframes for this window")
    p.add_argument("--fps", type=int, default=24)
    p.add_argument("--fonts", default=None, help="folder with LilitaOne-Regular.ttf / Nunito-Black.ttf (TTF); Blender's font otherwise")
    p.add_argument("--engine", default=None)
    p.add_argument("--samples", type=int, default=48)
    p.add_argument("--size", default="1280x592")
    p.add_argument("--save", default=None, help=".blend to write")
    p.add_argument("--render", default=None, help=".png to render")
    p.add_argument("--max-karts", type=int, default=None)
    p.add_argument("--follow", default=None, help="kart id to follow instead of the replay's followKartId")
    p.add_argument("--motion-blur", action="store_true", help="bake a short window around --t and render with motion blur")
    p.add_argument("--dof", type=float, default=0.0, help="f-stop for depth of field focused on the followed kart (0 = off)")
    return p.parse_args(argv)


def main(argv=None):
    if argv is None:
        argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    a = _parse(argv)
    wd, ht = (int(v) for v in a.size.lower().split("x"))
    anim = tuple(a.anim) if a.anim else ((a.t - 2.0 / a.fps, 4.0 / a.fps) if a.motion_blur else None)
    cam = build(a.world, None if a.no_replay else a.replay, t=a.t, mode=a.mode, font_dir=a.fonts, anim=anim,
                fps=a.fps, engine=a.engine, samples=a.samples, width=wd, height=ht, max_karts=a.max_karts, follow=a.follow)
    scene = bpy.context.scene
    if a.motion_blur:
        scene.render.use_motion_blur = True
        try:
            scene.render.motion_blur_shutter = 0.35
        except Exception:
            pass
    if a.dof > 0:
        you = next((o for o in bpy.data.objects if o.name.startswith("you_")), None)
        cam.data.dof.use_dof = True
        cam.data.dof.aperture_fstop = a.dof
        if you:
            cam.data.dof.focus_object = you
    if a.save:
        os.makedirs(os.path.dirname(os.path.abspath(a.save)), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.save))
        log("saved " + a.save)
    if a.render:
        os.makedirs(os.path.dirname(os.path.abspath(a.render)), exist_ok=True)
        bpy.context.scene.render.filepath = os.path.abspath(a.render)
        t0 = time.time()
        bpy.ops.render.render(write_still=True)
        log(f"rendered {a.render} in {time.time() - t0:.0f}s with {bpy.context.scene.render.engine}")


if __name__ == "__main__":
    main()
