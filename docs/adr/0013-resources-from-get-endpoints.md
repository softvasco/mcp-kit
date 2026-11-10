# 13. Resources from GET endpoints, read through the same pipeline as tools

Date: 2026-11-10

## Status

Accepted

## Context

Tools are things the model decides to call. MCP resources are different: the client, or the user in it, picks something to read and puts it in the context, like attaching a file. An account, a statement page or a document fits that better than a `get_statement` tool the model has to think of calling.

The SDK lets a host write resources by hand with `[McpServerResource]` methods. That repeats what the API already has: the route, the parameters, the authorization policy and the JSON shape. It also skips everything McpKit puts around an endpoint, like redaction.

## Decision

- `.AsMcpResource()` on a GET endpoint makes it a resource template. Any other verb fails at startup, because a read must not change anything.
- The URI is the route under a scheme, `api` by default (`options.ResourceScheme`): `/accounts/{id}/statement` becomes `api://accounts/{id}/statement`. Constraints like `:guid` are left out of the URI. The read skips routing, so a value of the wrong type is caught by the endpoint's parameter binding and comes back as `InvalidParams`.
- Query parameters go in an RFC 6570 query expansion, `{?page,pageSize}`, with the same names the tool schema uses. That is how a client pages through a read model: the endpoint's own paging parameters, filled in the URI.
- Routes a URI template can't say, such as optional parameters, catch-alls or two parameters in one segment, fail at startup instead of matching wrongly later.
- A read runs the endpoint in-process with `EndpointToolInvoker`, the same as a tool call. The endpoint's authorization policy is checked, and the output goes through the same JSON formatting and redaction rules.
- A failed read is a protocol error, since `resources/read` has no error result like a tool call: 404 is `ResourceNotFound`, 400 and 422 are `InvalidParams`, other 4xx `InvalidRequest`, and 5xx `InternalError`. The message is the endpoint's problem details, redacted. A query parameter the endpoint doesn't take is refused before it runs.
- A host with no resources doesn't advertise the capability.

## Consequences

- One endpoint can be a tool and a resource at once. The model can call it and the user can attach it, with one implementation behind both.
- Reads are not audited or rate limited yet; those checks live in the tool call (ADR 0008, ADR 0009). A client reads resources when the user asks, not in a loop, so this matters less, but the same checks should move to a shared step when that changes.
- Only templates are listed. There is no `resources/list` of concrete URIs, because listing every account is a query the API should page, not something the server should enumerate.
- Change notifications (`resources/subscribe`) are not supported yet.
