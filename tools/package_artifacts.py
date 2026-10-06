"""Package Phase 1 framework-dependent outputs with sources of license notices."""
import hashlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
destination = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts")
for project, assembly in [("cli", "kytyfixer"), ("desktop", "Thanos.Desktop")]:
    assert (destination / project / f"{assembly}.dll").is_file(), f"Missing {project} publication"
    assert (destination / project / f"{assembly}.runtimeconfig.json").is_file()
    for document in ["LICENSE", "THIRD-PARTY-NOTICES", "README.md"]:
        shutil.copy2(ROOT / document, destination / project / document)
    shutil.copytree(ROOT / "licenses", destination / project / "licenses", dirs_exist_ok=True)
    revision = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, capture_output=True, text=True, check=False).stdout.strip()
    source = "https://github.com/Xx-LiDAF-xX/THANOS"
    (destination / project / "SOURCE.txt").write_text(f"THANOS source (GPL-3.0-or-later): {source}\nRevision: {revision or 'uncommitted development snapshot'}\n")
archive = shutil.make_archive(str(destination / "THANOS-phase1"), "zip", destination, "cli")
# Include desktop in the same archive without archiving the ZIP itself.
with zipfile.ZipFile(archive, "a", zipfile.ZIP_DEFLATED) as package:
    for file in sorted((destination / "desktop").rglob("*")):
        if file.is_file():
            package.write(file, file.relative_to(destination))
checksum = hashlib.sha256(Path(archive).read_bytes()).hexdigest()
Path(archive + ".sha256").write_text(checksum + "  THANOS-phase1.zip\n")
print(f"Validated Phase 1 package: {archive}")
