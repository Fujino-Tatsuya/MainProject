# AIRULE.md - AI Development Rules

## Core Principle

AI-assisted development is fastest when the user and the AI share the same understanding before implementation.

Do not rush into coding. For non-trivial work, align first, then plan, then implement in small verified steps.

## When To Use This Workflow

Use this workflow for:
- New gameplay systems or major behavior changes.
- Networking, authority, synchronization, scene flow, or save/load changes.
- Boss, player ability, state abnormality, build, or data-table changes.
- Refactors that affect shared interfaces or module boundaries.
- Anything that could affect multiple teammates or PR review scope.

For tiny fixes, typo changes, or obvious one-line edits, proceed directly but still explain what changed.

## Grill Workflow

Before non-trivial implementation:
- Interview the user one question at a time.
- Explain why each question matters.
- Provide your recommended answer.
- If the codebase or docs can answer the question, inspect them instead of asking.
- Walk the decision tree carefully: resolve dependencies between decisions before moving on.
- Stop asking when the implementation plan is clear enough to verify.

Questions should clarify:
- Goal and player-facing behavior.
- Multiplayer authority and ownership.
- Host/client differences.
- Edge cases and failure states.
- Data ownership: ScriptableObject, prefab, scene, runtime state, or network state.
- What is in scope and out of scope.
- Acceptance criteria and verification steps.

## Shared Understanding Artifacts

Use these files consistently:
- `AGENT.md`: project onboarding and team-wide agent guide.
- `AIRULE.md`: AI development process and collaboration rules.
- `CONTEXT.md`: project vocabulary, domain terms, and shared language.
- `PLAN.md`: locked plan for the current substantial task.
- `Docs/`: detailed design, tech, workflow, and roadmap documents.
- `docs/adr/` or `Docs/adr/`: architecture decision records when a decision is hard to reverse.

## Before Coding

For substantial work, create or update `PLAN.md` with:
- Goal
- Current understanding
- Approach
- Key decisions and tradeoffs
- Multiplayer/networking authority assumptions
- Risks and open questions
- Out of scope
- Acceptance criteria
- Verification plan

Ask the user to approve the plan before implementation.

## Implementation Rules

After approval:
- Work in small vertical slices.
- Prefer changes that are easy to test in Multiplayer Play Mode.
- Keep modules deep: simple interface, meaningful implementation.
- Avoid shallow pass-through abstractions.
- Respect existing architecture and docs before inventing new patterns.
- Keep `.meta` files with their assets.
- Do not move large asset folders or change VCS ownership without explicit approval.

## Unity / Multiplayer Rules

Default assumptions unless project docs say otherwise:
- Unity version is fixed by `AGENT.md`.
- NGO is the networking base.
- Player movement and input are owner-authoritative unless explicitly changed.
- Boss, enemy, damage, state abnormality, drops, and game progression are server-authoritative.
- Test multiplayer changes with MPPM when feasible.
- Prefer deterministic, data-driven gameplay definitions using ScriptableObjects where the project already does so.

## Verification

Before finishing, report what was verified:
- Tests run, if any.
- Unity/editor/manual checks, if any.
- MPPM host/client scenario checks, if any.
- Risks not fully verified.

If verification was not possible, say why clearly.