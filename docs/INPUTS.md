# Logs, archives and packages

CLI and Avalonia use the same input services. A game can be a directory, an explicit executable, ZIP, RAR, 7z, Kyty ZAR, tar (including `.tgz`, `.tar.gz`, `.tar.bz2`, `.tar.xz`, `.tar.zst`), or PKG package evidence. Log input can be a UTF-8 file or one of the supported archives.

## Full logs

There are no configured byte-size, line-count, line-length or signature-match count limits. The old 16 MiB / 100,000-line / 65,536-character rejections were removed. UTF-8 decoding and SHA-256 run over fixed-size read buffers; all raw text and line events remain in the report. Decoder state preserves multibyte characters across buffers, and linear regex matching avoids backtracking on long lines.

Complete reports still retain text and events in memory. Practical capacity depends on available RAM, temporary disk space for archive members, and runtime string/array capacity. Cancellation remains available through CLI Ctrl+C. Knowledge JSON limits and ELF structural-inspection budgets are separate from log ingestion and remain documented in PHASE2.md.

## Archive selection

THANOS automatically selects a unique `eboot.bin`, falling back to a unique `.elf`. A log archive automatically selects a unique `.log` or `.txt`. Multiple candidates never select silently. Use `list-input` and specify a member explicitly; the same path notation works in the desktop text fields:

```sh
kytyfixer list-input ./Game.zip
kytyfixer diagnose "./Game.zip!/Game/eboot.bin" --log "./Logs.zip!/logs/run.log"
kytyfixer binary-info ./Game.zar
kytyfixer compare-builds ./Game.zip ./Related.rar
kytyfixer parse-log ./Logs.zip
kytyfixer fingerprint ./Game.pkg
```

Reported executable/log paths retain `archive!/member` and hashes refer to the complete decompressed member. Modules are inventoried by archive member name. The selected member is spooled into an anonymous file with deletion on disposal, providing seekable ELF reads. No archive path is written into a game directory, and original containers are unchanged. Missing members, unsafe paths, duplicates, links and unsupported/encrypted data produce explicit input errors. Nested archive recursion, password prompting and multi-volume discovery are not implemented. Names containing wildcard characters are rejected because external archive tools interpret them as patterns.

## Helpers

| Format | Runtime support |
| --- | --- |
| ZIP | Built-in .NET; no external helper |
| RAR, 7z, tar and compressed tar | libarchive `bsdtar`; Windows' bundled `tar` and macOS' `tar` can provide it. Linux generally needs `libarchive-tools`. GNU tar is not accepted as the RAR/7z backend. Set `THANOS_BSDTAR` to an explicit executable path if required. |
| ZAR | Bundled independent format reader plus Python. Python 3.14 includes `compression.zstd`; earlier supported Python versions need the `zstandard` module for compressed blocks. Set `THANOS_PYTHON` to the interpreter executable path if needed. |
| PKG | Read-only package size/hash and header-magic evidence. Executable remains UNKNOWN; package filesystem extraction/decryption is not implemented. Use an accessible extracted executable for ELF research. |

Helpers receive argument arrays without shell interpolation and stream selected bytes to the collector. They are invoked for input decoding only. Missing helpers fail with an actionable message. Compressed ZAR blocks must decode to 64 KiB, and malformed ranges, section overlaps and cyclic/multiply referenced file trees are rejected. Footer integrity authentication and ZAR metadata interpretation remain unimplemented.

Archive support in THANOS is independent of Kyty's launch support. The reviewed Kyty checkout directly mounts ZAR; it does not directly mount ZIP/RAR/PKG merely because THANOS accepts them as research input. See [Kyty source review](KYTY_SOURCE_MAP.md).

## Validation of this update

The local Release build completed with zero warnings/errors. All 118 fact/theory cases were exercised by an in-process verification harness, including synthetic ZIP/RAR5/7z/tar/ZAR games, malformed containers, logs exceeding former limits, Unicode boundaries and all schema-model cases. Six Python tests cover compressed/raw ZAR blocks, Unicode extended names, cross-block/offset-record reads and corruption. The 21-schema CLI/SQLite checks also pass against published outputs, including ZIP research, explicit members and PKG partial evidence; the published CLI inspected a compressed ZAR fixture successfully. Both CLI/Desktop publications include the bundled reader and the application package validates.

On 2026-10-10 the unrestricted session also completed the standard VSTest run: 118 passed, zero skipped or failed. Full solution formatting verification passed. The configured GitHub matrix retains standard tests/full formatting and adds Python archive tests. No native GUI interaction or real-game/Kyty execution is claimed.
