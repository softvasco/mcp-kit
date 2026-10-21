# 8. Audit every tool call, with a hash of the arguments instead of their values

Date: 2026-10-21

## Status

Accepted

## Context

When an agent does something odd through the API, the first questions are which tool it called, as whom, with what, and what came back. The endpoint's own logs answer part of it, but they don't know the call came from a tool, and a confirmation request (ADR 0006) never reaches the endpoint at all.

Logging the arguments would answer "with what", but arguments are account numbers, names and amounts. Copying them into a log that keeps them for months, readable by everyone with log access, is worse than the problem it solves.

## Decision

- Every endpoint tool call produces one `ToolCallRecord`: tool, caller id (name identifier, `sub` or name), a SHA-256 of the arguments, the outcome, the endpoint's status code if it ran, the size of the result in bytes, the duration from `TimeProvider` and when it started.
- Outcomes are `Succeeded`, `Failed`, `ConfirmationRequested` and `ConfirmationRefused`. An exception from the endpoint is recorded as `Failed` with no status, then thrown again.
- The arguments are hashed the same way the confirmation token hashes them: keys sorted, values as compact JSON, `confirmation_token` left out. The same call gives the same hash, so two records can be compared without either holding the values.
- The record goes to `IToolCallAudit`. The default writes one structured log entry per call (event `ToolCalled`). A host that needs an audit table or an event stream registers its own before `AddMcpKit()`.

## Consequences

- "Did the agent send the same transfer twice?" can be answered from the log: same tool, caller and hash.
- The hash is not encryption. Arguments with few possible values, like an account id and a small amount, can be guessed by hashing candidates. It keeps the values out of casual reading and out of log search, not away from someone set on finding them. A keyed hash (HMAC with a secret from the host) would close that, at the cost of a key to manage.
- The audit runs inside the tool call, so a slow `IToolCallAudit` slows every call. Anything that writes to a database should queue the record and return.
- Hand-written SDK tools are not audited; they don't go through the endpoint tool.
