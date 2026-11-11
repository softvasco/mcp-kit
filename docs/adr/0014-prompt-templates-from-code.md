# 14. Prompt templates registered from code, with an optional attached resource

Date: 2026-11-11

## Status

Accepted

## Context

MCP prompts are the third kind of thing a server offers, after tools and resources. The client shows them to the user, usually as a slash command, and the user picks one. A prompt that only returns text saves some typing. The useful ones bring data with them: "review this statement" is much better when the statement is already in the message.

The SDK has `[McpServerPrompt]` methods for this. A method that wants the statement would have to call the API itself, which means a second way to load the data that skips the endpoint's authorization and McpKit's redaction. That is the same problem tools and resources had (ADR 0002, ADR 0013).

## Decision

- Prompts are registered on the options with `options.Prompts.Add(name, description, text, attach)`. Names follow the tool naming rule (snake_case), and a duplicate name fails when it is added.
- Every `{snake_case}` placeholder in the text or in the attached URI is a required string argument. Only that shape counts, so JSON or code in the text stays as written. There are no optional arguments: a prompt with a missing value is a worse prompt, not a shorter one.
- `attach` is a resource URI with placeholders, such as `api://accounts/{account_id}/statement`. Values are percent-encoded before they go in it, so a value with a slash stays one route value. If no resource matches the URI, the host fails at startup.
- A prompt with an attachment returns two user messages: the text, then the resource as an embedded resource. The read goes through `EndpointMcpServerResource.ReadAsync` as the user who asked for the prompt, with the same authorization, JSON formatting and redaction as a direct read. An endpoint error fails the whole prompt with the same protocol error a read would give.
- A missing, blank or unknown argument is `InvalidParams` with the names in the message.
- Hosts with no prompts don't advertise the capability.

## Consequences

- The text of a prompt is the host's, but the values in it are the user's. A value is inserted as it is, so a prompt shouldn't put an argument where it could be read as an instruction to the model; the attached data is safer, because it arrives as a resource and as JSON (ADR 0007).
- One attachment per prompt. Two would be easy to add, but I haven't needed it, and a long prompt with three resources is usually a sign it should be a tool.
- Argument completion (`completion/complete`) is not supported yet, so the client can't suggest account ids.
- Hand-written SDK prompts still work next to these; McpKit only adds its own to the collection.
