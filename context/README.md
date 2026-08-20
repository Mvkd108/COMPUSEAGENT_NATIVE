# Context index

This directory is the durable handoff for people and coding agents. Read it in
this order before inspecting the implementation:

1. `PRODUCT.md` — why this exists and what is deliberately out of scope.
2. `CURRENT.md` — verified implementation state and limitations.
3. `DECISIONS.md` — architectural decisions that should not be rediscovered.
4. `NEXT.md` — ordered work with acceptance criteria.
5. The newest entry under `sessions/` — the exact continuation point.
6. `prompts/` — decision-complete implementation prompts for the current slice.

## Handoff rule

After each coherent slice:

- Update `CURRENT.md` with behavior that was actually verified.
- Record durable choices in `DECISIONS.md`.
- Reorder `NEXT.md` rather than leaving stale tasks in place.
- Add or amend the dated session note with files changed, checks run, and the
  first recommended action for the next session.

Do not treat Codex JSONL, ChatGPT pastes, or agent reports as a substitute for
this directory.
