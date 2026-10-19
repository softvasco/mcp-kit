# 3. Tool arguments sent as request headers

Date: 2026-10-19

## Status

Accepted

## Context

Some endpoint behaviour depends on a header that no handler parameter declares. The case that forced this is ledger-core's Idempotency-Key: an endpoint filter reads it from the request, runs the command once and replays the stored response to retries. The schema built in ADR 0002 only knows route, query and body, so a tool call had no way to send the key, and a retried `deposit` call ran twice.

Agents retry more than most HTTP clients. A model that gets a timeout calls the tool again with the same arguments, and that is the retry the key is there to catch.

I considered three ways to get the key there:

| Option | Problem |
|---|---|
| McpKit makes up a key per tool call | each retry is a new call, so it gets a new key and the filter sees two different requests |
| Map every `[FromHeader]` handler parameter | the idempotency filter reads the header itself, there is no parameter to find |
| The endpoint declares which argument becomes which header | one extra line per endpoint or group |

## Decision

An endpoint (or a route group, so every endpoint in it) declares the header with `.WithMcpToolHeader("Idempotency-Key", "idempotency_key", description)`. The argument shows up in the tool schema as an optional string with that description, and the invoker sends its value as the header instead of putting it in the body or query string.

The description is the only place to tell the model how to use the value, so it should say it: pick a new one per operation, send the same one when retrying.

A header argument with the same name as a route, query or body argument is a startup error.

## Consequences

- The key only works if the model reuses it on a retry. That depends on the client and the description, and McpKit can't enforce it. An agent that leaves it out gets the behaviour an HTTP client without the header gets.
- Anything else a filter reads from headers (a tenant, a correlation id) can use the same mechanism.
- `[FromHeader]` parameters are still left out of the schema. Mapping them automatically is a separate change if an endpoint needs it.
