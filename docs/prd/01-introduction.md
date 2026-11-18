<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §1 · nothing changed · navigation: docs/prd/INDEX.md -->

## 1. Introduction

### 1.1 Purpose

This specification defines the product outcomes, functional requirements, constraints, quality attributes, data rules, acceptance criteria, delivery gates, and unresolved decisions for SupplyForge. It is the requirements authority for implementation. Architecture Decision Records (ADRs) are authoritative for accepted mechanisms; companion documents are navigation aids and must not override this specification.

### 1.2 Scope of this document

This is a documentation-only baseline. It does not claim production certification, legal compliance, or completed implementation. Every requirement retains its identifier; newly introduced requirements use the next available identifier in their prefix.

### 1.3 Requirement conventions

- **ID** = prefix + number (example `INV-014`). Prefixes: `BRN`, `PLT`, `MD`, `INV`, `PRC`, `INB`, `SAL`, `FUL`, `SHP`, `FIN`, `RPT`, `AI`, `INT`, `NFR`, `MOD`, `DM`, `TXN`, `IDEM`, `OUT`, `SEC`, `WAC`, `TD` (temporary defaults).
- **Priority**: **P1A / P1B** = delivery stage inside P1 (see §11); **P2** = after P1 stable; **P3** = future / non-goal for first release. Priority tags **P1** mean “somewhere in P1A or P1B” and are mapped in §11. **Every requirement is tagged.**
- `D-n` → §12 · `Q-n` → §13.1 · `A-n` → §3.2 · `TD-n` → §3.3 · `ADR-nnn` → `ADR/` folder · PA/PC items → `docs/Decision Register.md`.

---

### 1.4 Authority and references

- Requirements and priorities: this PRD.
- Accepted mechanisms: `ADR/ADR-001` through `ADR-016`.
- Proposed mechanisms requiring owner approval: `ADR/ADR-017` through `ADR-019`; they are not accepted decisions.
- Conceptual diagram: `SF.png`, read-only; alignment is described in Appendix B.


