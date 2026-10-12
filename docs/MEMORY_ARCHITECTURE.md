# Memory architecture

`MemoryRecord` models five lifecycle classes: ShortTerm, Working,
LongTermProject, Episodic, and Semantic. Records include project ID, content,
importance, confidence, optional source agent, tags, creation/update times,
and optional expiration. User-curated records have no source agent and are
audit-attributed to the authenticated user.

## Current behavior

- Memory is stored in PostgreSQL through `NeuraDbContext.Memories`.
- The Memory Center supports owner/project-scoped search, user-curated memory
  entry and revision, retention periods, and safe deletion. Revisions preserve
  record identity, project scope, creation time, and source-agent provenance;
  successful changes update `UpdatedAt` and are audit-logged. Project choices
  are derived from visible missions; a mission may reuse an existing authorized
  project scope to receive its memories.
- `EfMemoryService` retrieves only unexpired records for the mission's
  `ProjectId`. Retrieval is bounded to at most 500 candidate records and
  20 results, ranking lexical token overlap with importance/confidence. The
  orchestrator requests at most five memories per task. This is lexical
  matching, not vector/embedding or semantic search.
- Prompt inclusion and continuity handoffs explicitly label persisted memory
  as **untrusted reference data**. Stored text is never promoted to system
  instructions. Results are capped per record, and unrelated project
  memories are not queried.
- Live neural events report retrieval metadata only (record IDs and types,
  never memory contents); the graph links these memory nodes to the task with
  an explicitly untrusted-context edge.
- `MemoryExpirationWorker` deletes expired records in bounded batches. The
  retrieval query independently filters expired records, so cleanup delay
  cannot make an expired item available to a task.
- Provider outputs are not automatically written to long-term memory. This
  avoids silently converting model-generated or externally influenced text
  into durable project facts. Users must curate and save memory explicitly.

## Remaining limits

There is no semantic/vector retrieval, confidence decay, automatic fact
extraction with human review, or memory version/merge workflow. Expiration
cleanup runs hourly; it is not a real-time scheduler. Configure reviewed
indexes and PostgreSQL full-text/pgvector only after the migration baseline
and retrieval-quality tests are in place.
