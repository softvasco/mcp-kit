# 12. Generated tools for APIs as they are, hand-written ones for what an agent needs

Date: 2026-11-07

## Status

Accepted

## Context

McpKit now has three ways to get a tool in front of an agent:

| Source | Defined by | Called |
|---|---|---|
| Endpoint tool (ADR 0002) | `.AsMcpTool()` on a minimal API endpoint, name from `WithName`, description from its summary | in-process, through the endpoint's `RequestDelegate` |
| OpenAPI tool | one per operation of a document, name from `operationId`, description from the summary | over HTTP, to the API's base address |
| Hand-written tool | an `[McpServerTool]` method, like `utc_now` in the sample | as a plain method call |

The first two are generated: the tool follows the API, so they can't drift apart. The question is when a person should write a tool by hand instead, and what McpKit does about the gap between the two.

An API is shaped for programs that already know it. An agent picks tools from their names and descriptions, gets one call at a time, and pays for every tool in its context. A generated tool inherits whatever the API has: an `operationId` like `get_accounts_id`, a summary written for a reference page, ten operations where an agent needs two, and errors that only make sense next to the docs.

## Decision

- Generated tools stay the default. Most of what an agent needs from a well-written API is a read of a resource, and a generated tool gets that right with no code to maintain.
- The fixes for a generated tool are configuration, not code: `Include` and `Exclude` cut the list down, `Rename` gives a tool the name a model picks well, and a bad name in config fails at startup. A better summary belongs in the API itself, where every client benefits.
- A tool is written by hand when the agent's task doesn't map to one operation: a lookup that takes three calls, an answer that needs data from two APIs, or a result that has to be shaped before a model can use it. The hand-written tool calls the API (or the generated tools' `HttpClient`) itself and is tested like any other method.
- Writes follow ADR 0004 for generated tools: `AllowWriteTools` decides whether they are served. A hand-written tool is registered by the host on purpose, so it carries the SDK's own annotations (`ReadOnly`, `Destructive`) and the host decides whether to add it.
- McpKit doesn't wrap hand-written tools in anything of its own. The SDK's `[McpServerTool]` already does that job, and `WithTools<T>()` on the server builder serves them next to the generated ones.

## Consequences

- One server can mix all three sources, and the agent can't tell them apart. Tool names have to be unique across them; the SDK's tool collection refuses a second tool with a name it already holds.
- `AllowWriteTools` and the confirmation flow of ADR 0006 don't cover hand-written tools. A host that writes one which changes state has to give it the same care itself.
- A hand-written tool can drift from the API it calls. Its tests are what catches that, so it should be tested against the same contract the API publishes.
- An OpenAPI document that is wrong produces wrong tools. Testing the generator against a document nobody wrote for it (the Petstore) found one such gap already, array query parameters, which now go out in OpenAPI's default form style.
