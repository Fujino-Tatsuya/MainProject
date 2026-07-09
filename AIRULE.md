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

## Blind-Spot Briefing

Before the grill interview on non-trivial work, briefly report the user's likely blind spots first:
- Risks, dependencies, and edge cases in this codebase that the request does not mention.
- Things the user may not know they don't know (existing systems that overlap, authority implications, asset/VCS side effects).
- Keep it short (3-7 bullets). The goal is to let the user give better instructions, not to stall.

If the request direction itself is still open, propose 2-3 genuinely different approaches (prototypes)
with one-line tradeoffs and a recommendation, and let the user pick before detailed grilling.

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
- `Docs/`: detailed design, tech, and workflow documents.
- `docs/adr/` or `Docs/adr/`: architecture decision records when a decision is hard to reverse.

## Reference-Based Implementation

When the user provides reference code (a file, snippet, or external example) that already behaves the way they want:
- Analyze the reference deeply first: its logic, invariants, and edge-case handling.
- Reimplement preserving the same logic and semantics, adapted to this project's language, architecture, and conventions.
- Do not copy verbatim when it conflicts with project patterns; note what was adapted and why.

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
- When reality forces a deviation from the approved plan (dependency conflict, wrong assumption, missing API):
  choose the safer, more conservative option, record the deviation and the reason in `IMPLEMENTATION_NOTES.md`
  (repo root, alongside `PLAN.md`), and keep going to completion instead of stopping.
  Review the notes with the user at the end.

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

## Ownership Quiz

After substantial work, the user must own the changes, not just accept them:
- Provide a summary report of all changes.
- Offer a short quiz (3 questions, multiple-choice or short-answer) that tests whether the user
  understands the changed code well enough to control it. Put answers at the end, separated.
- `/diff-check` generates this kind of checklist/quiz from the day's diff — prefer it for daily use.