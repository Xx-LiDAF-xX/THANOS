# Infrastructure

BCL file/path/platform adapters, streaming SHA-256, guarded ELF header reads, bounded lexical log parsing, conservative Kyty file detection, JSON, structured application logs and SQLite. ServiceFactory composes shared services. Storage/schema-v1.sql is an embedded migration. Create a SqliteKnowledgeStore, InitializeAsync, then SaveAnalysisAsync/GetAnalysisAsync. EvidenceTests and StorageTests use synthetic files and temporary databases. See ARCHITECTURE.md for limits and future capability adapters.
