# Security policy

Please report vulnerabilities privately through GitHub Security Advisories ("Report a vulnerability" on the Security tab). Don't open a public issue for security problems.

I'll reply within 7 days.

## Notes for hosts

McpKit lets a model call your API. Treat every tool call as a request from a user who can be talked into things by whatever the model read last. These are the defaults and the gaps I know of.

What McpKit does for you:

- Nothing is a tool until you mark the endpoint, and writes need both `AsMcpWriteTool()` and `AllowWriteTools` on the host ([ADR 0004](docs/adr/0004-read-only-by-default.md)).
- A tool needs the same authorization policy as its endpoint, and a client only sees the tools its policies allow ([ADR 0005](docs/adr/0005-authorization-per-tool.md)).
- `RequireMcpConfirmation()` makes a write take two calls, with a token bound to the tool, the caller and the arguments ([ADR 0006](docs/adr/0006-confirmation-before-a-write.md)).
- Tool output is always one escaped JSON value, so a stored string can't pose as markup around the result ([ADR 0007](docs/adr/0007-tool-output-as-data.md)).
- Every call is audited with a hash of its arguments, not the values ([ADR 0008](docs/adr/0008-audit-every-tool-call.md)).
- Rate limits per caller and tool, and field redaction in results, are there if you configure them ([ADR 0009](docs/adr/0009-rate-limits-per-client-and-tool.md), [ADR 0010](docs/adr/0010-redact-fields-in-tool-output.md)).

What you still have to do:

- Protect `/mcp` itself with `MapMcpKit().RequireAuthorization()`. Without it every caller is anonymous and they all share one rate limit count.
- Share the data protection key ring between instances, or confirmation tokens from one instance fail on another.
- Set rate limits. There is no default, because a good number depends on the tool.
- Check ownership inside the handler (is this the caller's account?). McpKit checks policies, not resources.
- Don't expose tools that return free text written by other people unless the agent needs it. Escaping stops a note from breaking out of its string, not the model from reading it.

Known limits:

- A confirmation token shows the step was taken, not that a person approved it. A model can confirm its own call.
- The argument hash in the audit can be guessed when the values are few (an account id and a small amount).
- Rate limit counts live in memory per instance, so three instances allow up to three times the limit.
- Redaction matches field names, not values. An email inside a free-text field is not masked.
- Middleware tied to the endpoint (idempotency, `RequireRateLimiting`) doesn't run for a tool call, because the tool calls the request delegate in-process.
- Hand-written SDK tools registered next to McpKit get none of the above.
