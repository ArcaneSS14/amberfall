import argparse
import json
import re
from pathlib import Path

from PIL import Image


def read_dmi(path: Path):
    image = Image.open(path).convert("RGBA")
    with Image.open(path) as source:
        description = source.info["Description"]
    width = int(re.search(r"^\s*width = (\d+)$", description, re.M).group(1))
    height = int(re.search(r"^\s*height = (\d+)$", description, re.M).group(1))
    states = []
    offset = 0
    for block in re.split(r"(?=^state = )", description, flags=re.M)[1:]:
        name = re.search(r'^state = "(.*)"$', block, re.M).group(1)
        directions = int(re.search(r"^\s*dirs = (\d+)$", block, re.M).group(1))
        frames = int(re.search(r"^\s*frames = (\d+)$", block, re.M).group(1))
        states.append((name, offset, directions, frames))
        offset += directions * frames
    columns = image.width // width
    if image.width % width or image.height % height or offset > columns * (image.height // height):
        raise ValueError("DMI metadata does not fit the spritesheet")
    return image, width, height, columns, states


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("states", nargs="+")
    parser.add_argument("--tiles", action="store_true", help="write PNGs directly instead of an RSI")
    parser.add_argument("--source-url", help="source URL recorded in RSI metadata")
    args = parser.parse_args()
    image, width, height, columns, states = read_dmi(args.source)
    requested = set(args.states)
    missing = requested - {state[0] for state in states}
    if missing:
        raise ValueError(f"states not found: {sorted(missing)}")
    selected = [state for state in states if state[0] in requested]
    if not args.tiles and not args.source_url:
        raise ValueError("--source-url is required for RSI provenance")
    if len(selected) != len(requested):
        raise ValueError("DMI contains duplicate state names")
    for name, _, directions, frames in selected:
        if not name or not re.fullmatch(r"[A-Za-z0-9_-]+", name):
            raise ValueError(f"unsafe output name: {name!r}")
        if frames != 1 or directions not in (1, 4, 8):
            raise ValueError(f"{name}: only static states with 1, 4 or 8 directions are supported")
        if args.tiles and directions not in (1, 4):
            raise ValueError(f"{name}: tiles require 1 or 4 variants")
    args.destination.mkdir(parents=True, exist_ok=True)
    stale = {path.stem for path in args.destination.glob("*.png")} - requested
    if stale:
        raise ValueError(f"destination contains stale PNGs: {sorted(stale)}")
    metadata = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": f"Taken from {args.source_url} and converted from BYOND format for Space Station 14",
        "size": {"x": width, "y": height},
        "states": [],
    }
    for name, offset, directions, _ in selected:
        # RSI uses a square sheet for directional states; tile variants use one row.
        out_columns = directions if args.tiles else (1 if directions == 1 else (2 if directions == 4 else 4))
        rows = directions // out_columns
        sprite = Image.new("RGBA", (width * out_columns, height * rows))
        for direction in range(directions):
            source_index = offset + direction
            source_box = (
                source_index % columns * width,
                source_index // columns * height,
                (source_index % columns + 1) * width,
                (source_index // columns + 1) * height,
            )
            sprite.paste(image.crop(source_box),
                         (direction % out_columns * width, direction // out_columns * height))
        sprite.save(args.destination / f"{name}.png")
        state = {"name": name}
        if directions != 1:
            state["directions"] = directions
        metadata["states"].append(state)
    if not args.tiles:
        (args.destination / "meta.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(f"Extracted {len(selected)} static states from {args.source}")


if __name__ == "__main__":
    main()
