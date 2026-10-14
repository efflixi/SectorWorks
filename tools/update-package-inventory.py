#!/usr/bin/env python3
"""Record legal metadata from the solution's restored NuGet packages."""

import json
from pathlib import Path
import xml.etree.ElementTree as ET


root = Path(__file__).resolve().parent.parent
packages = {}
for scope in ("src", "tests", "tools"):
    for assets_path in sorted((root / scope).glob("*/obj/project.assets.json")):
        assets = json.loads(assets_path.read_text(encoding="utf-8"))
        for name, library in assets["libraries"].items():
            if library["type"] != "package":
                continue
            nuspec_name = name.split("/")[0].lower() + ".nuspec"
            candidates = [
                Path(folder) / library["path"] / nuspec_name
                for folder in assets["packageFolders"]
            ]
            nuspec = next((path for path in candidates if path.is_file()), None)
            if nuspec is None:
                raise FileNotFoundError(f"Missing restored NuGet metadata: {name}")
            document = ET.parse(nuspec).getroot()
            namespace = document.tag.removesuffix("package")
            metadata = document.find(namespace + "metadata")
            if metadata is None:
                raise ValueError(f"Missing package metadata: {nuspec}")
            legal = {}
            for field in ("authors", "license", "licenseUrl", "copyright", "repository"):
                node = metadata.find(namespace + field)
                if node is not None:
                    legal[field] = {
                        "value": node.text.strip() if node.text and node.text.strip() else None,
                        "attributes": dict(node.attrib),
                    }
            if name in packages and packages[name] != legal:
                raise ValueError(f"Inconsistent legal metadata for {name}")
            packages[name] = legal

if not packages:
    raise RuntimeError("No restored packages found; restore the solution first.")
destination = root / "docs/licenses/package-inventory.json"
destination.write_text(
    json.dumps(packages, indent=2, sort_keys=True) + "\n", encoding="utf-8"
)
print(f"Recorded legal metadata for {len(packages)} resolved packages.")
