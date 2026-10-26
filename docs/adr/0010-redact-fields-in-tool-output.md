# 10. Redact fields in tool output by name or path

Date: 2026-10-25

## Status

Accepted

## Context

An endpoint written for a screen returns what the screen shows: the full IBAN, the holder's name and email. When the same endpoint becomes a tool, all of that goes into the model's context, and from there into the provider's logs, the chat history and whatever the agent writes next. Most agent tasks don't need it. "Which of my accounts has the lowest balance" needs the balance and something to tell the accounts apart.

Changing the endpoint is the wrong fix, because the screen still needs the full values. The tool layer is the one place that sees every result on its way to the model.

## Decision

- `McpKitOptions.Redaction` holds rules for every tool. `WithMcpRedaction` on an endpoint adds rules for that tool only, on top of the host's.
- A rule names a field (`email`), matched at any depth, or a dotted path from the root (`holder.name`). Array items keep the array's path, so `payments.payee.name` covers every payment. Names compare without case.
- `Mask` replaces the value with `***`. `KeepLast(field, n)` keeps the last n characters (`***0154`), enough for a person or an agent to tell two IBANs apart. A value no longer than n is masked fully.
- The mask has a fixed width, objects and numbers are masked as a whole, and null stays null.
- Redaction runs on the parsed JSON before it is written out, so the text block and `structuredContent` carry the same masked object and the ADR 0007 escaping still applies.

## Consequences

- The endpoint's HTTP response is not touched. Only tool results are.
- Rules match field names, not values. An email inside a free-text note is not found; a tool that returns notes written by people should mask the note or leave the tool out.
- Plain text bodies come back as one string (ADR 0007), so field rules can't reach inside them.
- A masked value is gone for the agent too. A tool that must pass an IBAN on to a write tool has to get it some other way, for example by account id.
- The audit's result size (ADR 0008) is the size after redaction, which is what left the server.
