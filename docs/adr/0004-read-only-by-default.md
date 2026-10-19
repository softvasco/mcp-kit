# 4. Tools are opt-in and read-only by default

Date: 2026-10-19

## Status

Accepted

## Context

ADR 0002 made tools opt-in per endpoint: nothing is exposed without `.AsMcpTool()`. That still left one marker for every kind of endpoint, so a deposit and a balance read looked the same to McpKit and to the agent. Adding `.AsMcpTool()` to a whole route group would have published every write in it.

Reads and writes carry very different risk when the caller is a model. A wrong read wastes a call. A wrong write moves money, and the model can make it because of something it read in a tool result or a document, not because the user asked.

MCP has tool annotations for this (`readOnlyHint`, `destructiveHint`, `idempotentHint`). The spec says they are hints and a client should not trust them from a server it doesn't trust, so they help a client decide when to ask the user, but they don't protect the API.

## Decision

- `AsMcpTool()` is for reads. On an endpoint whose method is not GET or HEAD it fails at startup and the message points to `AsMcpWriteTool()`. The verb is the only signal McpKit has; a GET that changes state has to be marked as a write by whoever wrote it.
- `AsMcpWriteTool()` marks an endpoint as a tool that changes state.
- The host has to allow write tools as well, with `McpKitOptions.AllowWriteTools`. It is false by default, and write tools are then left out of the tool list. A new deployment exposes reads only until someone turns writes on in its configuration.
- Every tool gets annotations from the same information: `readOnlyHint: true` for reads; for writes `readOnlyHint: false`, and `destructiveHint` and `idempotentHint` true only for PUT and DELETE.

## Consequences

- Turning on writes for a host is one line in its setup, which is easy to review and easy to keep off in a read-only deployment of the same API.
- A POST that only reads (a search with a body, say) can't be a read-only tool. It has to be a write tool for now. I'd rather have that false positive than a write that slips through as a read.
- A POST with an Idempotency-Key is still annotated as not idempotent. The hint describes the endpoint, and the key only helps if the agent sends it.
- Authorization per tool and a confirmation step for writes come next and build on this split.
