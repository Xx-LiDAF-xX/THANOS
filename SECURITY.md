# Security

Phase 1 is an early foundation. All game files, containers, logs, JSON and future AI output are untrusted. Inputs are read-only; no AI scripts, binaries selected as Kyty, or patches are executed. No archive extraction or decryption exists.

For a sensitive vulnerability, use the repository's GitHub **Report a vulnerability** feature if enabled. Otherwise contact the repository owner through an available private channel or request a reporting channel without disclosing exploit details. No security email address has been configured in this repository.

Include affected version/platform, a minimal synthetic reproduction, expected versus actual behavior and impact. Do not upload proprietary games, keys, personal paths or real secrets. Avoid public exploit details until coordinated with maintainers.

Keep package auditing and malformed-input checks enabled. Current safeguards include bounded logs and regex timeouts, guarded ELF header reads, explicit symbolic-link handling, parameterized SQL and schema version checks. These are not comprehensive binary parser protections; full binary inspection and patch validation are later phases. Access errors are explicit errors, while inaccessible code inside a hashable container permits partial evidence.

Reports exported by the user may contain filesystem paths, raw logs, hardware details and title IDs. Review/redact them before publishing. Application logs omit those details. AI output must never be treated as executable instructions by the application.
