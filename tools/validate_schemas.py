"""Validate every schema offline and exercise the real CLI against synthetic evidence."""
import argparse
import copy
import hashlib
import json
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path

from jsonschema import Draft202012Validator
from referencing import Registry, Resource
from elf_fixture import create as create_elf

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--cli", type=Path, default=ROOT / "src/Thanos.Cli/bin/Release/net10.0/kytyfixer.dll")
    args = parser.parse_args()
    schemas = {}
    registry = Registry()
    for path in sorted((ROOT / "schemas").glob("*.json")):
        schema = json.loads(path.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        schemas[path.name] = schema
        registry = registry.with_resource(schema["$id"], Resource.from_contents(schema))

    def validate(name, data):
        Draft202012Validator(schemas[f"{name}-schema-v1.json"], registry=registry).validate(data)

    def run(*arguments, expected=0):
        result = subprocess.run([args.dotnet, str(args.cli), *map(str, arguments)], capture_output=True, text=True, encoding="utf-8", timeout=30, cwd=ROOT)
        if result.returncode != expected:
            raise AssertionError(f"CLI {arguments}: expected exit {expected}, got {result.returncode}: {result.stderr}")
        return result

    validate("error-signature", json.loads((ROOT / "examples/error-signature.json").read_text()))
    validate("family-relationship", json.loads((ROOT / "examples/family-relationship.json").read_text()))
    validate("knowledge-bundle", json.loads((ROOT / "examples/knowledge.json").read_text()))
    with tempfile.TemporaryDirectory(prefix="thanos-cli-") as temporary:
        directory = Path(temporary)
        game = directory / "game with spaces.elf"
        data = bytearray(64)
        data[:7] = b"\x7fELF\x02\x01\x01"
        struct.pack_into("<HHI", data, 16, 2, 62, 1)
        struct.pack_into("<Q", data, 24, 0x401000)
        struct.pack_into("<H", data, 52, 64)
        game.write_bytes(data)
        expected_hash = hashlib.sha256(data).hexdigest()
        log = ROOT / "examples/sample-kyty.txt"
        fingerprint = json.loads(run("fingerprint", game).stdout)
        validate("game", fingerprint)
        assert fingerprint["sha256"] == expected_hash
        assert fingerprint["titleId"] is None
        assert fingerprint["confidence"] == 0
        kyty = json.loads(run("detect-kyty", game).stdout)
        validate("kyty-fingerprint", kyty)
        assert kyty["capabilities"] == [] and kyty["version"] is None
        parsed = json.loads(run("parse-log", log).stdout)
        validate("parsed-log", parsed)
        assert parsed["sha256"] == hashlib.sha256(log.read_bytes()).hexdigest()
        assert len(parsed["events"]) == 7
        report = json.loads(run("analyze", game, "--kyty", game, "--log", log).stdout)
        validate("analysis", report)
        minimal = json.loads(run("analyze", game).stdout)
        validate("analysis", minimal)
        assert minimal["kyty"] is None and minimal["log"] is None
        database = directory / "knowledge.db"
        saved = run("analyze", game, "--log", log, "--database", database)
        identifier = saved.stderr.strip().removeprefix("Saved analysis: ")
        exported = json.loads(run("database", "export", database, identifier).stdout)
        validate("analysis", exported)
        assert exported == json.loads(saved.stdout)
        assert game.read_bytes() == data
        run("fingerprint", directory / "missing", expected=3)
        run("apply-patch", "proposal.json", expected=2)
        run("analyze", game, "--log", log, "--log", log, expected=2)
        run("analyze", game, "--unknown", "value", expected=2)
        invalid = copy.deepcopy(report)
        invalid["schemaVersion"] = 99
        assert not Draft202012Validator(schemas["analysis-schema-v1.json"], registry=registry).is_valid(invalid)
        invalid = copy.deepcopy(fingerprint)
        invalid["sha256"] = "fabricated"
        assert not Draft202012Validator(schemas["game-schema-v1.json"], registry=registry).is_valid(invalid)

        executable = directory / "research.elf"
        executable.write_bytes(create_elf())
        inspected = json.loads(run("binary-info", executable).stdout)
        validate("binary-inspection", inspected)
        assert inspected["status"] == "inspected"
        assert len(inspected["loadMappings"]) == 1
        assert any(s["name"] == ".bss" for s in inspected["fingerprint"]["sections"])
        mapped = json.loads(run("map-address", executable, "virtualAddress", "0x400108", "fileOffset").stdout)
        validate("address-mapping", mapped)
        assert mapped["output"]["value"] == "0x108"
        zero_fill = json.loads(run("map-address", executable, "virtualAddress", "0x400140", "fileOffset").stdout)
        validate("address-mapping", zero_fill)
        assert zero_fill["output"] is None
        no_bias = json.loads(run("map-address", executable, "runtimeAddress", "0x500108", "fileOffset").stdout)
        validate("address-mapping", no_bias)
        assert no_bias["output"] is None
        with_bias = json.loads(run("map-address", executable, "runtimeAddress", "0x500108", "fileOffset", "0x100000").stdout)
        validate("address-mapping", with_bias)
        assert with_bias["output"]["value"] == "0x108"
        relation = json.loads(run("compare-builds", executable, executable).stdout)
        validate("family-relationship", relation)
        assert relation["strength"] == "strong"
        candidate = directory / "candidate.elf"
        changed = create_elf()
        changed[300] ^= 1
        candidate.write_bytes(changed)
        difference = json.loads(run("diff-binaries", executable, candidate).stdout)
        validate("binary-difference", difference)
        assert difference["changedBytes"] == 1 and difference["ranges"][0]["offset"]["value"] == "0x12c"
        research = json.loads(run("diagnose", executable, "--log", log, "--related", candidate).stdout)
        validate("research", research)
        assert research["diagnosis"]["primaryFailure"]["category"] == "shader"
        assert len(research["diagnosis"]["secondaryFailures"]) == 2
        assert research["diagnosis"]["candidates"][0]["layer"] == "KYTY EMULATOR / SHADER"
        assert research["relatedBuilds"][0]["strength"] == "unknown"
        zipped_game = directory / "Game.zip"
        zipped_log = directory / "Logs.zip"
        with zipfile.ZipFile(zipped_game, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("Game/eboot.bin", executable.read_bytes())
        with zipfile.ZipFile(zipped_log, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("logs/run.log", log.read_bytes())
        inventory = json.loads(run("list-input", zipped_game).stdout)
        assert inventory["members"] == ["Game/eboot.bin"]
        archive_report = json.loads(run("diagnose", zipped_game, "--log", zipped_log).stdout)
        validate("research", archive_report)
        assert archive_report["evidence"]["game"]["sha256"] == inspected["fingerprint"]["sha256"]
        assert archive_report["evidence"]["log"]["rawText"] == parsed["rawText"]
        assert archive_report["evidence"]["game"]["executablePath"].endswith("!/Game/eboot.bin")
        explicit = json.loads(run("binary-info", str(zipped_game) + "!/Game/eboot.bin").stdout)
        validate("binary-inspection", explicit)
        run("parse-log", str(zipped_log) + "!/missing.log", expected=3)
        package = directory / "Game.pkg"
        package.write_bytes(b"\x7fCNT" + bytes(124))
        package_report = json.loads(run("diagnose", package, "--log", zipped_log).stdout)
        validate("research", package_report)
        assert package_report["binary"] is None and package_report["evidence"]["game"]["sha256"] is None
        knowledge = json.loads((ROOT / "examples/knowledge.json").read_text())
        provenance = knowledge["families"][0]["provenance"]
        provenance["date"] = "2026-10-06T00:00:00+00:00"  # Canonical DateTimeOffset representation of the same instant.
        knowledge["bindings"] = [{"executableSha256": inspected["fingerprint"]["sha256"], "gameFamilyId": "synthetic-family", "provenance": provenance}]
        knowledge["entries"] = [{"schemaVersion": 1, "id": "example-claim", "gameFamilyId": "synthetic-family", "kind": "issue", "summary": "Synthetic unverified issue", "executableHashes": [], "signatureIds": ["shader-unsupported"], "provenance": provenance}]
        knowledge_path = directory / "knowledge.json"
        knowledge_path.write_text(json.dumps(knowledge), encoding="utf-8")
        validate("knowledge-bundle", knowledge)
        run("knowledge", "import", database, knowledge_path)
        exported_knowledge = json.loads(run("knowledge", "export", database).stdout)
        validate("knowledge-bundle", exported_knowledge)
        assert exported_knowledge == knowledge
        saved_research = run("diagnose", executable, "--log", log, "--database", database)
        identifier = saved_research.stderr.strip().removeprefix("Saved research: ")
        reloaded = json.loads(run("database", "export-research", database, identifier).stdout)
        validate("research", reloaded)
        assert reloaded == json.loads(saved_research.stdout)
        assert reloaded["evidence"]["game"]["gameFamily"] is None
        assert reloaded["knownIssues"][0]["provenance"]["status"] == "unverified"

        research_file = directory / "research.json"
        research_file.write_text(json.dumps(reloaded), encoding="utf-8")
        modes = schemas["ai-prompt-schema-v1.json"]["properties"]["mode"]["enum"]
        for mode in modes:
            request = json.loads(run("generate-prompt", "--report", research_file, "--mode", mode).stdout)
            validate("ai-prompt", request)
            assert request["mode"] == mode and 0 <= request["readiness"] <= 1
            assert "UNKNOWN" in request["text"] and "INCONCLUSIVE" in request["text"]
            start = request["text"].index("{\n")
            end = request["text"].rindex("\nEND OF EVIDENCE")
            assert json.loads(request["text"][start:end]) == reloaded
        assert json.loads(run("generate-prompt", research_file).stdout) == json.loads(run("generate-prompt", "--report", research_file).stdout)
        analysis_file = directory / "analysis.json"
        analysis_file.write_text(json.dumps(report), encoding="utf-8")
        basic_request = json.loads(run("generate-prompt", analysis_file).stdout)
        validate("ai-prompt", basic_request)
        assert "Imported basic analysis snapshot" in basic_request["text"]
        assert any(item.startswith("Binary evidence") for item in basic_request["missingEvidence"])
        markdown = directory / "prompt.md"
        bundle = directory / "evidence.zip"
        request = json.loads(run("generate-prompt", "--report", research_file, "--output", markdown, "--bundle", bundle).stdout)
        assert markdown.read_text(encoding="utf-8") == request["text"]
        with zipfile.ZipFile(bundle) as archive:
            assert sorted(archive.namelist()) == ["manifest.json", "prompt.json", "prompt.md", "research.json"]
            manifest = json.loads(archive.read("manifest.json"))
            validate("evidence-package", manifest)
            for entry in manifest["entries"]:
                content = archive.read(entry["file"])
                assert len(content) == entry["bytes"] and hashlib.sha256(content).hexdigest() == entry["sha256"]
            assert json.loads(archive.read("research.json")) == reloaded
            assert json.loads(archive.read("prompt.json")) == request
        fresh_request = json.loads(run("generate-prompt", executable, "--log", log, "--knowledge", knowledge_path).stdout)
        assert fresh_request == request
        run("generate-prompt", "--report", research_file, "--output", markdown, expected=3)
        run("generate-prompt", executable, "--mode", "99", expected=2)
        run("generate-prompt", executable, "--mode", "0", expected=2)
        run("generate-prompt", "--report", research_file, "--log", log, expected=2)
        run("generate-prompt", "--report", expected=2)
        invalid_research = copy.deepcopy(reloaded)
        invalid_research["schemaVersion"] = 99
        research_file.write_text(json.dumps(invalid_research), encoding="utf-8")
        run("generate-prompt", "--report", research_file, expected=3)
        assert executable.read_bytes() == create_elf()
        run("knowledge", "import", database, knowledge_path, expected=3)
        run("diagnose", executable, "--knowledge", knowledge_path, "--database", database, expected=2)
        run("map-address", executable, "invalid", "0x1", "fileOffset", expected=2)
        run("map-address", executable, "virtualAddress", "-1", "fileOffset", expected=3)
    print(f"Validated {len(schemas)} schemas, synthetic examples, and CLI/storage integration.")


if __name__ == "__main__":
    main()
