# TS Quotation — instructions for Claude

Before your first answer in any session, read `docs/handoff/HANDOFF.txt` completely and work exactly as it says
(the previous sessions' rules, words, steps, current state and the list of what is still to build). Keep it up to date
at the end of every round.

The rules that must never be broken (details in the handoff):

- Tally data is never changed (the server only reads Tally), and Tally must not be made heavy or slow.
- Commit, push and build only when the user says "Do". Until then, answer "commit and push" reminders by saying the
  changes wait for "Do".
- "preview" / "don't edit" = pictures only, no app code changes. "Add" = add to the list only. "Discard" = drop it.
- After every change say whether a server update is needed, in simple language.
- Never commit secrets, model names, or preview tests (`tests/*/Zz*.cs`).
- Work only on the branch `claude/tallyprime-quotation-automation-agxx3n`; no pull request unless asked.
