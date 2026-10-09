#!/usr/bin/env python3
"""Converts the previewer's design fonts (woff2 from @fontsource, installed by `npm install` in
tools/world-preview) to TTF files Blender can load, into out/fonts/. import_world.py picks that
folder up automatically. Needs `pip install fonttools brotli` (any Python 3).

    python3 -I tools/blender/make_fonts.py
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "tools", "world-preview", "node_modules", "@fontsource")
OUT = os.path.join(ROOT, "out", "fonts")
FILES = {
    os.path.join("lilita-one", "files", "lilita-one-latin-400-normal.woff2"): "LilitaOne-Regular.ttf",
    os.path.join("nunito", "files", "nunito-latin-900-normal.woff2"): "Nunito-Black.ttf",
    os.path.join("nunito", "files", "nunito-latin-700-normal.woff2"): "Nunito-Bold.ttf",
}


def main():
    try:
        from fontTools.ttLib import TTFont
    except ImportError:
        print("fonttools is missing: pip install fonttools brotli", file=sys.stderr)
        return 1
    if not os.path.isdir(SRC):
        print("run `npm install` in tools/world-preview first (the fonts come from its packages)", file=sys.stderr)
        return 1
    os.makedirs(OUT, exist_ok=True)
    for rel, name in FILES.items():
        font = TTFont(os.path.join(SRC, rel))
        font.flavor = None
        font.save(os.path.join(OUT, name))
        print("wrote", os.path.relpath(os.path.join(OUT, name), ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
