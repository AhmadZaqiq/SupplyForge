# Engineering Principles and Code Quality

Technology-independent judgment for design decisions and everyday code. Adapt to the project's conventions.

## Decision principles

- Understand the real problem and desired outcome before choosing a solution. Separate requirements from assumptions.
- Challenge wrong assumptions instead of building on them. Do not follow an instruction mechanically when it conflicts with correctness.
- Prefer the simplest solution that satisfies the requirements. No abstractions, layers, dependencies, or patterns for hypothetical future needs (YAGNI).
- Respect existing conventions and decisions. Do not change architecture without a justified need. Preserve existing behavior unless the task changes it.
- Verify unfamiliar behavior in documentation or code. Distinguish facts from assumptions; say when you are unsure. Never invent requirements or limitations.
- Think beyond the happy path: failures, edge cases, bad input, concurrency, data integrity, performance, security.
- Make it work, make it right, then make it fast (Kent Beck). Optimize only with evidence of a real problem.
- Before removing something that looks pointless, find out why it exists (Chesterton's Fence).
- Any observable behavior of a public interface will be depended on by someone (Hyrum's Law); treat behavior changes as potentially breaking.
- Separate refactoring from behavior changes so each can be verified on its own.
- Explain significant decisions briefly, including the trade-off.

## Code quality

**Readability**
- Code communicates intent. Prefer straightforward over clever. Avoid deep nesting; use guard clauses.
- Names state purpose, not implementation. No vague names (`data`, `info`, `obj`) or unclear abbreviations. Rename when responsibility changes.
- Comments explain why, constraints, or non-obvious behavior, never what the code already says. Remove stale comments.

**Structure**
- Functions have one cohesive responsibility; extract only when it improves clarity. Avoid tiny helpers that only add indirection.
- Keep related logic together and unrelated concerns apart. Avoid unnecessary coupling and hidden side effects.
- Remove duplicated business rules. Do not abstract a few similar lines; abstract genuinely shared responsibility.

**Reliability**
- Handle errors explicitly at the right boundary. Never swallow exceptions. Preserve error context. Keep cleanup and state consistent.
- Validate at boundaries; keep internal code simple.

**Change discipline**
- Follow the surrounding code's style and patterns. One way to solve one problem.
- Consider backward compatibility for public interfaces.
- Refactor incrementally and verify behavior afterward. Do not chase stylistic perfection.

## Done means
Clear, simple, focused, consistent, maintainable, and failure conditions handled. Optimize for clarity, not line count.
