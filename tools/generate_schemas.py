"""Generate draft 2020-12 wire contracts. Run with --check in CI to prevent drift."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = "https://raw.githubusercontent.com/Xx-LiDAF-xX/THANOS/main/schemas/"
STRING = {"type": "string"}
INTEGER = {"type": "integer"}
NUMBER = {"type": "number"}
HASH = {"type": "string", "pattern": "^[a-f0-9]{64}$"}
CONFIDENCE = {"type": "number", "minimum": 0, "maximum": 1}


def enum(*values):
    return {"type": "string", "enum": list(values)}


def nullable(schema):
    return {"anyOf": [schema, {"type": "null"}]}


def array(schema=STRING):
    return {"type": "array", "items": schema}


def obj(properties):
    return {"type": "object", "properties": properties, "required": list(properties), "additionalProperties": False}


def common(name):
    return {"$ref": f"common-schema-v1.json#/$defs/{name}"}


def ref(name):
    return {"$ref": f"{name}-schema-v1.json"}


CATEGORIES = ["boot", "elf", "loader", "module", "syscall", "memory", "thread", "timing", "filesystem", "graphics", "vulkan", "shader", "gpu", "driver", "audio", "video", "network", "input", "presentation", "gamePatch", "versionMismatch", "performance", "environment", "unknown"]
PROVENANCE = obj(dict(source=STRING, author=nullable(STRING), date={"type": "string", "format": "date-time"}, kytyVersion=nullable(STRING), gameVersion=nullable(STRING), hardware=nullable(STRING), status=enum("unverified", "communityReported", "repeated", "confirmed", "toolVerified")))
DEFS = {
    "Evidence": obj(dict(source=STRING, detail=STRING, lineNumber=nullable({"type": "integer", "minimum": 1}))),
    "Provenance": PROVENANCE,
    "BinaryAddress": obj(dict(addressSpace=enum("fileOffset", "virtualAddress", "runtimeAddress", "relativeAddress"), value={"type": "string", "pattern": "^0x[0-9a-fA-F]+$"})),
    "BinarySection": obj(dict(name=STRING, offset=common("BinaryAddress"), address=common("BinaryAddress"), size={"type": "integer", "minimum": 0})),
    "BinarySegment": obj(dict(type=STRING, offset=common("BinaryAddress"), address=common("BinaryAddress"), fileSize={"type": "integer", "minimum": 0}, memorySize={"type": "integer", "minimum": 0})),
    "KytyCapability": obj(dict(name=STRING, arguments=array(), evidence=common("Evidence"))),
}
EVIDENCE = array(common("Evidence"))
SCHEMAS = {
    "game": dict(gameFamily=nullable(STRING), titleId=nullable(STRING), titleIds=array(), titleName=nullable(STRING), region=nullable(STRING), gameVersion=nullable(STRING), buildVersion=nullable(STRING), engine=nullable(STRING), engineVersion=nullable(STRING), executablePath=nullable(STRING), sha256=nullable(HASH), fileSize={"type": "integer", "minimum": 0}, architecture=nullable(STRING), entryPoint=nullable(common("BinaryAddress")), elfType=nullable(STRING), sections=array(common("BinarySection")), segments=array(common("BinarySegment")), modules=array(), metadataSources=EVIDENCE, confidence=CONFIDENCE, status=enum("partial", "complete"), limitations=array()),
    "game-family": dict(id=STRING, name=STRING, titleIds=array(), engine=nullable(STRING), provenance=common("Provenance")),
    "kyty-fingerprint": dict(executablePath=STRING, sha256=HASH, fileSize={"type": "integer", "minimum": 0}, version=nullable(STRING), architecture=nullable(STRING), capabilities=array(common("KytyCapability")), capabilityStatus=enum("Unknown", "Detected"), evidence=EVIDENCE, limitations=array()),
    "error-event": dict(eventType=STRING, severity=enum("information", "warning", "error", "fatal"), category=enum(*CATEGORIES), subsystem=STRING, message=STRING, opcode=nullable(STRING), programCounter=nullable(common("BinaryAddress")), sourceFile=nullable(STRING), sourceLine=nullable({"type": "integer", "minimum": 1}), lineNumber={"type": "integer", "minimum": 1}, rawLine=STRING),
    "parsed-log": dict(sourcePath=STRING, sha256=HASH, rawText=STRING, kytyVersion=nullable(STRING), titleId=nullable(STRING), hardware={"type": "object", "additionalProperties": STRING}, events=array(ref("error-event")), limitations=array()),
    "diagnosis": dict(layer=STRING, facts=array(), evidence=EVIDENCE, hypotheses=array(), confidence=CONFIDENCE, recommendation=STRING),
    "patch-family": dict(id=STRING, gameFamilyId=STRING, logicalFix=STRING, implementationIds=array(), provenance=common("Provenance")),
    "patch": dict(patchId=STRING, patchFamily=STRING, gameFamily=STRING, titleIds=array(), gameVersions=array(), kytyVersions=array(), targetFile={"type": "string", "minLength": 1}, sha256=HASH, signature={"type": "string", "pattern": "^(?:[0-9a-fA-F]{2}|\\?\\?)(?: (?:[0-9a-fA-F]{2}|\\?\\?))*$"}, originalBytes={"type": "string", "pattern": "^[0-9a-fA-F]{2}(?: [0-9a-fA-F]{2})*$"}, replacementBytes={"type": "string", "pattern": "^[0-9a-fA-F]{2}(?: [0-9a-fA-F]{2})*$"}, location=common("BinaryAddress"), expectedMatches={"type": "integer", "minimum": 1}, reason=STRING, provenance=common("Provenance"), verificationRules=array(), risk=STRING),
    "ai-prompt": dict(mode=enum("diagnose", "findExistingFix", "writePatch", "portPatch", "compareBuilds", "investigateEmulatorIssue", "optimizePerformance", "deepReverseEngineering"), text=STRING, readiness=CONFIDENCE, missingEvidence=array(), evidence=EVIDENCE),
    "verification": dict(id=STRING, gameFamilyId=STRING, binarySha256=HASH, kytySha256=HASH, patchId=nullable(STRING), scenario=enum("BOOT", "MENU", "GAMEPLAY", "CRASH REPRODUCTION", "STABILITY", "PERFORMANCE"), level={"type": "integer", "minimum": 0, "maximum": 7}, outcome=enum("pass", "fail", "improved", "regressed", "inconclusive"), baselineLog=nullable(STRING), candidateLog=nullable(STRING), metrics={"type": "object", "additionalProperties": NUMBER}, provenance=common("Provenance")),
    "error-signature": dict(id=STRING, containsAll=array(), category=enum(*CATEGORIES), hypothesis=STRING, confidence=CONFIDENCE, provenance=common("Provenance")),
    "family-relationship": dict(leftBuild=STRING, rightBuild=STRING, strength=enum("exactFamily", "strong", "probable", "weak", "unknown"), evidence=EVIDENCE, confidence=CONFIDENCE, provenance=common("Provenance")),
    "analysis": dict(game=ref("game"), kyty=nullable(ref("kyty-fingerprint")), log=nullable(ref("parsed-log")), limitations=array()),
}

# Phase 2 adds independent contracts; the Phase 1 schemas remain unchanged.
DEFS.update({
    "SymbolInfo": obj(dict(name=STRING, address=common("BinaryAddress"), size={"type": "integer", "minimum": 0}, binding=STRING, type=STRING, sectionIndex={"type": "integer", "minimum": 0}, isImport={"type": "boolean"}, isExport={"type": "boolean"})),
    "RelocationInfo": obj(dict(location=common("BinaryAddress"), type=STRING, symbol=nullable(STRING), addend=nullable(INTEGER))),
    "BinaryString": obj(dict(location=common("BinaryAddress"), value=STRING)),
    "CodeSignature": obj(dict(name=STRING, location=common("BinaryAddress"), size={"type": "integer", "minimum": 1}, sha256=HASH, kind=enum("section", "segment", "symbolFunction"))),
    "LoadMapping": obj(dict(offset=common("BinaryAddress"), address=common("BinaryAddress"), fileSize={"type": "integer", "minimum": 0}, memorySize={"type": "integer", "minimum": 0}, flags={"type": "integer", "minimum": 0, "maximum": 4294967295})),
    "SignatureMatch": obj(dict(signatureId=STRING, lineNumber={"type": "integer", "minimum": 1}, hypothesis=STRING, confidence=CONFIDENCE, provenance=common("Provenance"))),
    "CorrelationLink": obj(dict(fromLine={"type": "integer", "minimum": 1}, toLine={"type": "integer", "minimum": 1}, relationship=STRING, confidence=CONFIDENCE, evidence=EVIDENCE)),
    "FamilyBinding": obj(dict(executableSha256=HASH, gameFamilyId=STRING, provenance=common("Provenance"))),
    "ChangedRange": obj(dict(offset=common("BinaryAddress"), originalSize={"type": "integer", "minimum": 0}, candidateSize={"type": "integer", "minimum": 0}, originalBytes=STRING, candidateBytes=STRING, beforeContext=STRING, afterContext=STRING)),
})
SCHEMAS.update({
    "binary-inspection": dict(fingerprint=ref("game"), status=enum("inspected", "unsupported", "malformed", "limited"), format=nullable(enum("ELF32", "ELF64")), byteOrder=nullable(enum("little", "big")), symbols=array(common("SymbolInfo")), imports=array(), exports=array(), relocations=array(common("RelocationInfo")), strings=array(common("BinaryString")), codeSignatures=array(common("CodeSignature")), codeInventoryComplete={"type": "boolean"}, loadMappings=array(common("LoadMapping")), limitations=array()),
    "address-mapping": dict(input=common("BinaryAddress"), output=nullable(common("BinaryAddress")), status=enum("Mapped", "Unknown"), evidence=EVIDENCE),
    "diagnosis-report": dict(primaryFailure=nullable(ref("error-event")), secondaryFailures=array(ref("error-event")), independentFailures=array(ref("error-event")), matches=array(common("SignatureMatch")), correlations=array(common("CorrelationLink")), candidates=array(ref("diagnosis")), limitations=array()),
    "knowledge-entry": dict(id=STRING, gameFamilyId=nullable(STRING), kind=enum("issue", "fix", "compatibility"), summary=STRING, executableHashes=array(HASH), signatureIds=array(), provenance=common("Provenance")),
    "knowledge-bundle": dict(families=array(ref("game-family")), bindings=array(common("FamilyBinding")), patchFamilies=array(ref("patch-family")), entries=array(ref("knowledge-entry")), errorSignatures=array(ref("error-signature")), relationships=array(ref("family-relationship"))),
    "research": dict(evidence=ref("analysis"), binary=nullable(ref("binary-inspection")), diagnosis=ref("diagnosis-report"), relatedFamilies=array(ref("game-family")), relatedBuilds=array(ref("family-relationship")), knownIssues=array(ref("knowledge-entry")), patchFamilies=array(ref("patch-family")), limitations=array()),
    "binary-difference": dict(originalSha256=HASH, candidateSha256=HASH, changedBytes={"type": "integer", "minimum": 0}, ranges=array(common("ChangedRange")), truncated={"type": "boolean"}, limitations=array()),
})


DEFS["PackageEntry"] = obj(dict(file=STRING, sha256=HASH, bytes={"type": "integer", "minimum": 0}))
SCHEMAS["evidence-package"] = dict(templateVersion={"const": "1"}, mode=SCHEMAS["ai-prompt"]["mode"], entries=array(common("PackageEntry")), limitations=array())

# Phase 4 preserves PatchProposal v1 and adds workflow result/transaction contracts.
DEFS["PatchCheck"] = obj(dict(name=STRING, passed={"type": "boolean"}, critical={"type": "boolean"}, detail=STRING, evidence=EVIDENCE))
SCHEMAS.update({
    "patch-import": dict(status=enum("normalized", "inspectionRequired", "rejected"), detectedFormat=STRING, patch=nullable(ref("patch")), findings=array(), limitations=array()),
    "patch-validation": dict(patchId=STRING, status=enum("valid", "rejected"), targetPath=nullable(STRING), targetSha256=nullable(HASH), fileOffset=nullable(common("BinaryAddress")), checks=array(common("PatchCheck")), limitations=array()),
    "patch-transaction": dict(transactionId=STRING, patchId=STRING, status=enum("applied", "rolledBack"), createdAt={"type": "string", "format": "date-time"}, updatedAt={"type": "string", "format": "date-time"}, originalRoot=STRING, targetRelativePath=STRING, originalSha256=HASH, backupSha256=HASH, candidateSha256=HASH, workingRoot={"const": "working"}, backupRoot={"const": "backup"}, fileOffset=common("BinaryAddress"), length={"type": "integer", "minimum": 1}, limitations=array()),
})


def generate(check=False):
    documents = {name: obj(dict(schemaVersion={"const": 1}, **properties)) for name, properties in SCHEMAS.items()}
    documents["common"] = {"$defs": DEFS}
    for name, document in documents.items():
        filename = f"{name}-schema-v1.json"
        content = json.dumps({"$schema": "https://json-schema.org/draft/2020-12/schema", "$id": BASE + filename, "title": name, **document}, indent=2) + "\n"
        path = ROOT / "schemas" / filename
        if check:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                raise SystemExit(f"Schema out of date: {path}")
        else:
            path.parent.mkdir(exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    generate(parser.parse_args().check)
