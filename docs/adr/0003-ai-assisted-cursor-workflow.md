# ADR 0003: AI-assisted `cursor/*` branch workflow

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** EntitleX portfolio maintainers

## Context

EntitleX uses GitHub pull requests for all product changes. Cloud Agents and other AI assistants routinely open branches named `cursor/<topic>-<suffix>` to land incremental work (phases, remediations, quality gates). Reviewers need a clear contract: AI accelerates drafting and mechanical changes; humans own design decisions and merge authority.

## Decision

- **`cursor/*` branches are intentional.** They mark AI-assisted delivery streams. Naming is stable for stacking, CI, and tracker links — not a temporary experiment.
- **Design ownership stays human.** Architecture, AuthZ posture, data-model, and production ops choices are accepted via ADR/docs and PR review. Agents implement against those decisions; they do not redefine them silently.
- **Stacked PRs are preferred for large backlogs** so each PR remains mergeable alone once its base lands, with focused review surfaces.
- **CI remains the acceptance gate.** Format, tests, vulnerability scans, and image builds apply equally to human and AI branches.

## Consequences

### Positive

- Clear provenance of AI-assisted work without blocking velocity
- Reviewers can calibrate scrutiny (prefer design/docs + AuthZ/tests over mechanical renames)
- Compatible with existing Dependabot / CodeQL / Trivy gates

### Negative / trade-offs

- Branch naming convention must stay documented (this ADR + README)
- Stacked PR bases need retargeting after earlier merges on GitHub
