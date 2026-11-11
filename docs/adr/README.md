# Architecture decision records

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-tools-from-endpoint-metadata.md) | Tools from endpoint metadata, called in-process | Accepted |
| [0003](0003-header-arguments.md) | Tool arguments sent as request headers | Accepted |
| [0004](0004-read-only-by-default.md) | Tools are opt-in and read-only by default | Accepted |
| [0005](0005-authorization-per-tool.md) | Authorization per tool comes from the endpoint's policy | Accepted |
| [0006](0006-confirmation-before-a-write.md) | Confirmation before a write, with a token bound to the call | Accepted |
| [0007](0007-tool-output-as-data.md) | Tool output goes back as JSON data, never as loose text | Accepted |
| [0008](0008-audit-every-tool-call.md) | Audit every tool call, with a hash of the arguments instead of their values | Accepted |
| [0009](0009-rate-limits-per-client-and-tool.md) | Rate limits per client and tool, counted inside the tool call | Accepted |
| [0010](0010-redact-fields-in-tool-output.md) | Redact fields in tool output by name or path | Accepted |
| [0011](0011-upstream-credentials-not-token-passthrough.md) | Upstream credentials are picked per caller, never passed through | Accepted |
| [0012](0012-generated-vs-hand-written-tools.md) | Generated tools for APIs as they are, hand-written ones for what an agent needs | Accepted |
| [0013](0013-resources-from-get-endpoints.md) | Resources from GET endpoints, read through the same pipeline as tools | Accepted |
| [0014](0014-prompt-templates-from-code.md) | Prompt templates registered from code, with an optional attached resource | Accepted |
