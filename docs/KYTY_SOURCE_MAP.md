# KytyPS5 source review

Reviewed on 2026-10-07 against the local checkout at `C:\Users\TAPIOCA\Desktop\repos\KytyPS5`. This is an architecture and evidence-path review of the first-party source, not a claim that every library, shader instruction or game behavior has been verified. No Kyty code was copied into THANOS, and the emulator was not executed.

## Boot and execution

`src/main.cpp` parses settings-file arguments before CLI arguments. `--game` accepts a directory, a supported archive with root `eboot.bin`, or an explicit executable. The archive backend currently registered in `src/common/archive.cpp` is `.zar`, backed by Exzap ZArchive. ZIP/RAR acceptance in THANOS therefore does not imply that Kyty accepts those archives directly.

`src/emulator.cpp` builds the configuration and subsystem lifecycle. It loads `sce_sys/param.json` when accessible, initializes timer/threading/network/memory/filesystem/controller/audio/graphics services, mounts the input at `/app0` and `/hostapp`, registers host implementations through `Libs::InitAll`, loads the executable through the runtime linker, discovers an optional game patch, and starts guest execution while the graphics window runs on the host. Settings and patch discovery affect reproducibility even when executable hashes match.

`src/loader/elf.cpp` handles ELF64 and recognized SELF wrappers. Its SELF segment path requires accessible, uncompressed segment bytes; merely locating an ELF header inside a SELF is insufficient to establish usable file offsets. `src/loader/runtimeLinker.cpp` maps programs, processes symbols/relocations/TLS and invokes guest code. It can install unresolved-import stubs; a stubbed import in a log is not automatically a successful implementation. `src/loader/symbolDatabase.*` and `src/libs/libs.cpp` connect imports to emulator library implementations.

For binary investigation, distinguish the container, the SELF wrapper, a reconstructed raw ELF, its virtual addresses and runtime mappings. THANOS presently analyzes raw ELF members and preserves opaque/SELF input as partial evidence.

## Files, memory and libraries

`src/kernel/fileSystem.cpp` resolves guest mount points to host files or archive-backed paths. `src/common/file.*` and `src/common/archive.*` share archive routing, member cursors and reader lifetime. Kyty's member path notation is `archive.zar!/path`. Its archive worker performs decompression on a host stack, then copies data into guest memory.

`src/kernel/memory.cpp` and `src/common/virtualMemory.*` implement guest allocation/mapping and host protections. Runtime exception handling is also in the loader. CPU instruction emulation/patching lives in `src/loader/x64InstructionEmulator.*` and `guestInstructionPatcher.*`. A host exception can involve emulator code, translated/modified guest instructions or mappings; its PC alone does not identify a game-byte patch.

`src/libs` contains host implementations for kernel/library entry points, audio/video, controller, networking and other services. Missing or incomplete behavior must be traced to the actual import/library and source implementation instead of assuming a game binary defect.

## Graphics and shaders

`src/graphics/guest_gpu` models guest command formats/state. The host renderer under `src/graphics/host_gpu/renderer` manages Vulkan resources, caches, render targets and pipelines; `pipeline/pipelineCache.cpp` connects pipeline state to shader compilation.

`src/graphics/shader/recompiler/ShaderRecompiler.cpp` coordinates the shader path: decoder, control-flow graph, translation to IR, optimization/resource passes and SPIR-V emission. The corresponding directories are `frontend/decode`, `frontend/cfg`, `frontend/translate`, `ir/passes` and `backend/spirv`. Unsupported RDNA2 instructions and invalid control flow can fail at different stages, so preserve the instruction family, raw words, stage/hash and failure phase whenever present.

`ShaderDecoder.cpp` emits diagnostics including `family=`, `opcode=`, `raw=` and `reason=`. `ShaderCFG.cpp` reports `shader CFG build failed` and `pc 0x...`. These PCs are shader-relative positions, not guest CPU runtime addresses. THANOS now tags them as relative addresses and recognizes the whitespace PC spelling.

## Logging and evidence

`src/common/logging/log.cpp` uses spdlog with a raw formatter. `LOGF` writes formatted text, rather than a standard severity-tagged record. Console styling can add escape sequences; file output keeps payload text. Logs can contain multiline reports and large shader/resource dumps.

`src/common/assert.cpp` constructs a build banner, fatal banner, detail with `file:line`, and (on Windows) host stack addresses. The banner must remain context rather than becoming the diagnosed primary cause. `runtimeLinker.cpp` reports unhandled host exceptions with PC/access/address fields. Those addresses need domain/module evidence before game-address mapping.

THANOS preserves raw lines and bytes' SHA-256, recognizes `Not implemented`, retains source references, and treats categories/severity as heuristics. Neither a fatal banner nor a later Vulkan error proves that game code caused the failure.

## Archive format reference

Kyty's `3rdparty/CMakeLists.txt` pins Exzap ZArchive at `d2c717730092c7bf8cbb033b12fd4001b7c4d932`. Its local reader patch fixes extended name-length decoding and malformed-table handling. THANOS independently reads the documented big-endian footer, name/tree records, 48-bit member ranges, compression-offset records and 64 KiB raw/zstd blocks. It validates ranges and tree structure, and reads only the selected member payload.

References: [ZArchive format definitions](https://github.com/Exzap/ZArchive/blob/d2c717730092c7bf8cbb033b12fd4001b7c4d932/include/zarchive/zarchivecommon.h), [ZArchive reference reader](https://github.com/Exzap/ZArchive/blob/d2c717730092c7bf8cbb033b12fd4001b7c4d932/src/zarchivereader.cpp). The footer's archive integrity digest is not authenticated by THANOS; reported member SHA-256 is computed from bytes actually read.

## Implications for later phases

An eventual version-specific runner must capture the installed executable hash, settings, arguments, patches and relevant environment variables. Source support for an argument is evidence for this checkout only; it does not authenticate an installed Kyty build. Game mutations and patch execution remain later-phase work. This review changes input collection and address interpretation; it does not introduce an automatic Kyty runner.
