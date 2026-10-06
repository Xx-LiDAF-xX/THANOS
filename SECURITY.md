# Security

Phases 1–2 provide evidence collection and research. All game files, containers, logs, JSON and future AI output are untrusted. Inputs are read-only; no AI scripts, binaries selected as Kyty, or patches are executed. No archive extraction or decryption exists.

For a sensitive vulnerability, use the repository's GitHub **Report a vulnerability** feature if enabled. Otherwise contact the repository owner through an available private channel or request a reporting channel without disclosing exploit details. No security email address has been configured in this repository.

Include affected version/platform, a minimal synthetic reproduction, expected versus actual behavior and impact. Do not upload proprietary games, keys, personal paths or real secrets. Avoid public exploit details until coordinated with maintainers.

Keep package auditing and malformed-input checks enabled. Current safeguards include bounded logs and regex timeouts, ELF range/overflow/table checks, explicit symbolic-link handling, bounded symbol/string/code inventories, literal signature tokens, explicit address spaces, parameterized SQL, schema versions and transactional knowledge imports. Knowledge JSON rejects duplicate properties and missing wire fields; provenance status is preserved rather than silently promoted. Imported confirmed/tool-verified statuses are assertions by the supplied source and are not cryptographically authenticated. Patch validation is a later phase. Access errors are explicit errors, while inaccessible code inside a hashable container permits partial/log analysis.

Reports exported by the user may contain filesystem paths, raw logs, hardware details and title IDs. Review/redact them before publishing. Application logs omit those details. AI output must never be treated as executable instructions by the application.
