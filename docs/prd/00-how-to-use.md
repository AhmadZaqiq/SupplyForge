<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of Title + §0 · nothing changed · navigation: docs/prd/INDEX.md -->

# SupplyForge — Product Requirements Document

**Edition:** AI-optimised. Content identical to v0.8 except: the Appendix C acceptance table (a verbatim restatement of §5–§6 requirements) was removed, the grouped-alias appendix was compacted and renumbered as Appendix C, §13.2 numbering and a §10.2 sentence were fixed, and §0 was added. No requirement text was changed.  

**Version:** 0.8  
**Date:** 15 November 2026  
**Product:** SupplyForge  
**Audience:** Product owner, software architect, backend developers, testers, and delivery leads.

## 0. How to use this document (AI agents and reviewers)

1. **Authority.** This PRD is the requirements authority. Accepted mechanisms live in `ADR/ADR-001`…`ADR-016`. `ADR-017`…`ADR-019` are proposals, not decisions. Companion documents never override this PRD (§1.1, §1.4).
2. **Scope per task.** Work only on the slice or gate you are assigned (§11). `P1A` and `P1B` are delivery stages inside P1 (§1.3, §11.3). Do not implement `P2` or `P3` requirements, including AI (§5.12) and advanced integrations (§5.13, §7), unless the task says so.
3. **Stop and ask, do not decide.**
   - Text marked `NEEDS OWNER APPROVAL`, or a dependency on a proposed ADR: do not implement that behaviour; ask the product owner.
   - Questions marked `Open-PO` in §13.1: if a `Default` is stated there or in §3.3 (`TD-n`), implement the default; otherwise ask.
   - A requirement that relies on an ADR or Decision Register entry you were not given: ask for it instead of inventing the mechanism.
4. **Invariants always apply.** §6.1 global invariants, §6.3 negative stock, §4.2 architecture rules and module dependency kinds (§4.1), the credit-reservation contract (§4.3), idempotency, transactions and outbox (§4.5), and branch isolation with the 404/403 policy (BRN-011…014). Sales must not read Finance tables (SAL-074).
5. **Traceability.** Put the requirement ID in test names (NFR-051) and in commit or PR titles.
6. **Acceptance.** A requirement is accepted when tests named with its ID verify its statement. §10.1 lists the cross-cutting scenarios that must pass.
7. **Reference forms.** `D-n` → §12, `Q-n` → §13.1, `A-n` → §3.2, `TD-n` → §3.3, PA/PC items → `docs/Decision Register.md`. `OD-n` marks a decision awaiting owner approval (e.g. OD-03, OD-05): treat it as rule 3 above.

**Suggested reading order for a slice:** §1.3 → §2.5 → §3 → §4 → §6 → the §5 sections for that slice → §11 → §12–§13.

**Document map:** 1. Introduction · 2. Product overview · 3. Constraints, assumptions, and temporary defaults · 4. Architecture requirements · 5. Functional requirements · 6. Cross-cutting requirements · 7. External integrations · 8. Non-functional requirements · 9. Data requirements · 10. Testing and acceptance · 11. Delivery plan · 12. Decision and ADR index · 13. Open questions · Appendix A. Glossary · Appendix B. Conceptual diagram alignment · Appendix C. Grouped requirement-ID aliases

---
