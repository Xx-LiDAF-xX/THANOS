# Security

Phases 1-4 provide evidence collection, research, prompt generation, and validated patch transactions. All game files, containers, logs, JSON, and external AI outputs are untrusted. Inputs are read-only; no AI scripts, diffs, binaries selected as Kyty, or untrusted proposals are executed. Archive members are decoded into anonymous temporary spools, never extracted using archive paths. PKG extraction and decryption are not implemented. RAR/7z/tar and ZAR use explicit input-decoding helpers documented in docs/INPUTS.md.

For a sensitive vulnerability, use the repository's GitHub **Report a vulnerability** feature if enabled. Otherwise contact the repository owner through an available private channel or request a reporting channel without disclosing exploit details. No security email address has been configured in this repository.

Include affected version/platform, a minimal synthetic reproduction, expected versus actual behavior and impact. Do not upload proprietary games, keys, personal paths or real secrets. Avoid public exploit details until coordinated with maintainers.

Keep package auditing and malformed-input checks enabled. Current safeguards include:
- Linear non-backtracking log regexes.
- ELF range, integer overflow, and program/section table checks.
- Explicit symbolic-link and reparse-point rejection during target and transaction resolution.
- Bounded symbol, string, and code inventories.
- Literal signature tokens and single-space hex formatting.
- Explicit address space disambiguation (no implicit conversion between file offsets, virtual addresses, and runtime addresses).
- Parameterized SQL with versioned migrations.
- Strict JSON shape validation (no trailing commas, comments, or unexpected/untyped properties).
- Archive paths rejecting traversal, wildcards, links, and duplicates, with external helpers receiving argument arrays without shell interpolation.
- Patch import classifying scripts, diffs, and binary patches as inspect-only, never compiling or running them.
- Strict patch normalization requiring unverified external-AI provenance.
- Patch validation enforcing exact target SHA-256, accessible ELF structures, file-backed PT_LOAD bounds, exact original bytes, and exact signature matches without nearby substitution.
- Patch application modifying only separate working copies while keeping original game files read-only and preserving pristine target backups.
- Atomic staging via temporary sibling directories with rollback on any failure.
- Explicit approval invariants: patch application requires exact patchId confirmation; rollback requires exact transactionId confirmation; original restoration requires exact original root confirmation and saves displaced bytes in recovery.

Logs have no configured size/line limits and full evidence uses memory; archive member spools consume temporary disk until disposed. Do not infer unlimited physical capacity. Knowledge JSON rejects duplicate properties and missing wire fields; provenance status is preserved rather than silently promoted. Imported confirmed/tool-verified statuses are assertions by the supplied source and are not cryptographically authenticated.

Prompt generation contacts no external AI. Evidence ZIP hashes detect internal mismatches but do not authenticate a publisher or imported claims. Prompt rules label embedded logs/JSON as untrusted data; they cannot guarantee that an external model will resist malicious instructions. Exports preserve complete raw evidence and may need substantial memory/disk; no configured log limits are introduced. New prompt/ZIP exports refuse overwrites and remove their own incomplete temporary files on failure.

Reports, prompt packages, and patch transactions exported by the user may contain filesystem paths, raw logs, hardware details, and title IDs. Review/redact them before publishing. Application logs omit those details. AI output must never be treated as executable instructions by the application.
