# 1. Record architecture decisions

Date: 2026-10-06

## Status

Accepted

## Context

A library that sits between AI agents and an API makes choices that are easy to get wrong quietly: what is exposed by default, how tool schemas are derived, where authorization runs. In a few months I won't remember why each one went the way it did, and a reader of the code has less to go on than me.

## Decision

Lightweight architecture decision records live in `docs/adr/`, one Markdown file per decision, numbered in order: `NNNN-short-title.md`.

Each record has a status (proposed, accepted, superseded), the context, the decision and its consequences. Accepted records are not rewritten. A change gets a new record that supersedes the old one, and both link to each other.

A decision gets a record when it is hard to reverse, when it picks one reasonable option over another, or when someone reading the code would ask "why not X?".

## Consequences

- The reasoning sits next to the code and is reviewed in the same pull request.
- Obvious choices stay out, or nobody reads the records.
- Superseded records stay, so the path the design took is visible.
