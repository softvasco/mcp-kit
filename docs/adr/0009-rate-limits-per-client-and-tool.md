# 9. Rate limits per client and tool, counted inside the tool call

Date: 2026-10-22

## Status

Accepted

## Context

An agent stuck in a loop calls the same tool again and again, often with the same arguments, until something stops it. A person clicking a button can't do that at the same speed.

ASP.NET Core already has rate limiting middleware, but it doesn't help here. Every MCP request goes to the one `/mcp` route, so a limiter on that route sees one endpoint and can't tell `get_balance` from `transfer`. The tool then runs the endpoint's request delegate in-process (ADR 0002), so a `RequireRateLimiting` policy on the endpoint itself never runs either, the same way the idempotency middleware doesn't.

## Decision

- The limit is checked in the endpoint tool, before confirmation and before the endpoint runs. The key is the caller (the same id the audit uses, ADR 0008) and the tool name.
- A limit is a number of calls per fixed window. The host sets one for every tool with `options.RateLimit`, and an endpoint can set its own with `.WithMcpRateLimit(permits, window)`, which replaces the host's for that tool.
- There is no default limit. Ten calls a minute is fine for a transfer and far too low for a lookup the agent makes per row, so the number has to come from whoever knows the API.
- A refused call returns an error result with code `tool.rate_limited` and `retry_after_seconds`, rounded up. The audit records it as `RateLimited` with no status code.
- Refused calls still count. An agent that keeps calling doesn't get through sooner.
- The counter is mine, about 40 lines on `TimeProvider`, not `System.Threading.RateLimiting`. The BCL limiters replenish on their own timer, so tests would have to sleep to see a window end; with `FakeTimeProvider` they move the clock instead.

## Consequences

- Counts live in memory on each instance. With three instances behind a load balancer, a client gets up to three times the limit. That is still a cap on a runaway loop, which is the point; an exact global limit needs a shared store (Redis or the database) behind the same check.
- All unauthenticated callers share one count per tool, because they have the same empty caller id. Protect `/mcp` if that matters.
- Fixed windows let a client make twice the limit across a window boundary. A sliding window or token bucket would smooth that out, and can replace the counter without changing the options.
- Windows nobody has used since they ended are removed once a minute, so memory follows the number of active callers, not every caller ever seen.
- There is no limit across all tools for one client yet. If that is needed it's a second key on the same counter.
