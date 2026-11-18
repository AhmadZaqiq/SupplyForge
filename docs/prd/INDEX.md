# SupplyForge PRD — Index (read this first)

This is a **navigation aid**. The requirement text lives in the part files next to this one, split verbatim from `SupplyForge-PRD-v0_8-AI.md` (PRD v0.8, AI edition). Nothing was reworded. If this index and a part file disagree, the part file wins. Authority rules are in `00-how-to-use.md`.

## Reading protocol

1. Read `00-how-to-use.md` once per task (~0.8K tokens).
2. Identify the slice (A0…B4, see below). If the task does not say which one, ask.
3. For any task that changes code or data, read the **Core set**. For a read-only question, read only what the question needs.
4. Read the **slice files** for your slice. Read nothing else unless a requirement ID sends you there.
5. Look up one requirement with grep: `grep -rn "INV-014" docs/prd/`, then read only the section around the hit. If a grep for one ID finds nothing, check `appendix-c-id-aliases.md` (grouped ranges).
6. Never read all files. Never open a file “to be safe”. Before you write code, state which files you read.
7. A dependency on a proposed ADR (ADR-017…019), text marked `NEEDS OWNER APPROVAL`, an `OD-n` item, or an `Open-PO` question without a default: stop and ask (PRD §0 rule 3).

## Core set (code or data changes, ~8K tokens)

| File | Why |
|---|---|
| `00-how-to-use.md` | authority, scope, stop-and-ask |
| `03-constraints-assumptions-defaults.md` | tech baseline, TD-n defaults |
| `04a-module-map-and-rules.md` | module boundaries MOD-1…10 |
| `04b-credit-contracts-and-events.md` | credit contract, events |
| `04c-idempotency-transactions-outbox.md` | idempotency, outbox, transactions |
| `06a-global-invariants-location-negative-stock.md` | global invariants, negative stock |
| `02b-users-roles-branches.md` | branch isolation, BRN-011…014 (404/403) |

First time on the project, also read once: `01-introduction.md` (§1.3 conventions) and `02a-product-overview.md` (§2.5 scope).

## Slice → files (in addition to the Core set)

Derived from PRD §11.2. For the gate of your slice also read its row in `11-delivery-plan.md` and the matching themes in `10-testing-acceptance.md`.

| Slice | Read | Notes |
|---|---|---|
| A0 Foundations | `05-01-platform-core-PLT.md`, `06d-security-and-privacy.md`, `06b-document-lifecycle-numbering.md`, `07-external-integrations.md`, `08-non-functional.md` | Auth, branch, audit, outbox, idempotency, sequences; notifications PLT-050…052; fake email INT-030; API keys INT-050; arch tests NFR-040. |
| A1 Master + Inventory core | `05-02-master-data-MD.md`, `05-03-inventory-INV.md`, `05-01-platform-core-PLT.md`, `06d-security-and-privacy.md`, `09-data.md` | Approval engine PLT-030…034 is in 05-01; logo upload SEC-40 is in 06d. WAC reversal eligibility and correction path are pending OD-03 (TD-5): ask before costing code. |
| A2 Buy & receive | `05-04-procurement-PRC.md`, `05-05-inbound-INB.md`, `05-03-inventory-INV.md`, `06b-document-lifecycle-numbering.md` | From 05-03 only the incoming projection and receipt/putaway posting (grep INV-063). |
| A3 Sell & reserve | `05-06-sales-SAL.md`, `05-03-inventory-INV.md`, `06c-financial-calculation-rules.md`, `05-09-finance-FIN.md` | From 05-03 reservations and availability. From 05-09 only credit exposure (grep `ICreditExposureQuery`, `IReserveCredit`). Sales must not read Finance tables (MOD-9, SAL-074). |
| A4 Fulfil & dispatch | `05-07-fulfillment-FUL.md`, `05-08-shipping-SHP.md`, `05-10-dispatch-invoice-status.md`, `05-03-inventory-INV.md`, `02c-end-to-end-workflow.md` | Stock leaves on-hand at dispatch (D-4, FUL-031). Shipment creation is a second, idempotent TXN (TD-10). |
| A5 Money stub | — | Default: SKIP (TD-6, PA-17). Do not start without an explicit task. |
| B1 Invoice generation | `05-09-finance-FIN.md`, `05-10-dispatch-invoice-status.md`, `05-07-fulfillment-FUL.md`, `06c-financial-calculation-rules.md` | FIN-027…039, FIN-029a, FUL-036…038. Billing policy per ADR-013. |
| B2 AP/AR depth | `05-09-finance-FIN.md`, `06c-financial-calculation-rules.md`, `05-04-procurement-PRC.md`, `05-05-inbound-INB.md` | From PRC/INB only what the three-way match needs, via contracts. Cross-branch payments need `finance.payment.cross_branch` (TD-8). |
| B3 Thin insight | `05-11-reporting-RPT.md`, `08-non-functional.md` | Reporting is event-only and owns no operational writes (MOD-6). NFR-025 workload W5 in 08. |
| B4 Hardening | `08-non-functional.md`, `10-testing-acceptance.md`, `06d-security-and-privacy.md`, `09-data.md` | Performance W1–W5, security pass, docs, demo data, traceability. |

P2 and P3 requirements (including AI §5.12 and advanced integrations) are never implemented unless the task says so.

## ID prefix → file

| Prefix | File |
|---|---|
| BRN | `02b-users-roles-branches.md` |
| PLT, PLT-ROLE (roles in 02b) | `05-01-platform-core-PLT.md` |
| MD | `05-02-master-data-MD.md` |
| INV, WAC | `05-03-inventory-INV.md` |
| PRC | `05-04-procurement-PRC.md` |
| INB | `05-05-inbound-INB.md` |
| SAL | `05-06-sales-SAL.md` |
| FUL (036…038 in 05-10) | `05-07-fulfillment-FUL.md` |
| SHP | `05-08-shipping-SHP.md` |
| FIN | `05-09-finance-FIN.md` |
| FIN-CALC | `06c-financial-calculation-rules.md` |
| RPT | `05-11-reporting-RPT.md` |
| AI | `05-12-ai-AI.md` |
| INT | `07-external-integrations.md` |
| SEC | `06d-security-and-privacy.md` |
| NFR | `08-non-functional.md` |
| MOD | `04a-module-map-and-rules.md` |
| MOD-TXN, IDEM, OUT | `04c-idempotency-transactions-outbox.md` |
| DM | `09-data.md` |
| TD-n, A-n | `03-constraints-assumptions-defaults.md` |
| D-n | `12-decisions-adr-index.md` |
| Q-n | `13-open-questions.md` |

`ADR-nnn` → the `ADR/` folder at the project root, not this folder. `OD-n` = decision awaiting owner approval; mentioned in `00-how-to-use.md`, `03-…`, `05-03-…`, `05-06-…`; treat as stop-and-ask. PA/PC items → `docs/Decision Register.md`.

## File map

| File | PRD part | Size | Read when |
|---|---|---|---|
| `00-how-to-use.md` | Title + §0 | ~0.8K | Every task, first. Authority, scope, stop-and-ask rules, invariants. |
| `01-introduction.md` | §1 | ~0.5K | Once per session if new: ID and priority conventions (§1.3), authority order (§1.4). |
| `02a-product-overview.md` | §2.1–2.5 | ~0.9K | Scope questions: in/out of scope (§2.5), goals G-n, success criteria. |
| `02b-users-roles-branches.md` | §2.6–2.7 | ~1.4K | Roles, permissions, branch scoping, cross-branch rules. **BRN-011…014 (404/403 policy) live here.** |
| `02c-end-to-end-workflow.md` | §2.8 | ~0.6K | Features that span several modules; failure and edge scenarios. |
| `03-constraints-assumptions-defaults.md` | §3 | ~0.8K | Fixed tech baseline, assumptions A-n, temporary defaults TD-n (implement a TD-n default instead of asking). |
| `04a-module-map-and-rules.md` | §4.1–4.2 | ~1.9K | Module map, dependency kinds C/R/E, MOD-1…10, which module owns what. |
| `04b-credit-contracts-and-events.md` | §4.3–4.4 | ~1.4K | Credit-reservation contract (§4.3, Sales↔Finance); event catalog (§4.4) when publishing or consuming events. |
| `04c-idempotency-transactions-outbox.md` | §4.5 | ~1.6K | Anything that writes data: idempotency, outbox, TXN boundaries. |
| `05-01-platform-core-PLT.md` | §5 intro + §5.1 | ~0.8K | Auth, branch scope, audit, approval engine (PLT-030…034), notifications (PLT-050…052), sequences. |
| `05-02-master-data-MD.md` | §5.2 | ~0.4K | Company singleton, branches, products, partners, warehouses, locations, tax, terms. |
| `05-03-inventory-INV.md` | §5.3 | ~2.6K | Stock ledger, balances, reservations, transfers, adjustments, opening, incoming projection, WAC. |
| `05-04-procurement-PRC.md` | §5.4 | ~0.7K | Requisitions, RFQ, quotes, awards, POs, revisions. |
| `05-05-inbound-INB.md` | §5.5 | ~1.5K | Expected deliveries, goods receipt, inspection, discrepancies, putaway, supplier returns. |
| `05-06-sales-SAL.md` | §5.6 | ~0.8K | Sales orders, pricing inputs, backorders, credit-check orchestration. |
| `05-07-fulfillment-FUL.md` | §5.7 | ~0.7K | Pick, pack, dispatch, shipment creation trigger. |
| `05-08-shipping-SHP.md` | §5.8 | ~0.5K | Carriers, shipments, tracking, delivery confirmation. |
| `05-09-finance-FIN.md` | §5.9 | ~2.4K | AP/AR invoices, payments, allocations, credit notes, three-way match, credit exposure, invoice generation, cross-branch payments. |
| `05-10-dispatch-invoice-status.md` | §5.10 | ~0.2K | FUL-036…038: dispatch status vs invoice status separation. |
| `05-11-reporting-RPT.md` | §5.11 | ~0.2K | Read models, dashboards, reports. |
| `05-12-ai-AI.md` | §5.12–5.13 | ~0.2K | Only to confirm AI is NOT P1. §5.13 just points to §7. |
| `06a-global-invariants-location-negative-stock.md` | §6.1–6.3 | ~0.4K | Global invariants §6.1, location eligibility §6.2, negative stock §6.3. |
| `06b-document-lifecycle-numbering.md` | §6.4–6.5 | ~0.7K | Document states, document numbering, inventory/reservation cross-reference. |
| `06c-financial-calculation-rules.md` | §6.6 | ~0.7K | Anything with money: scale, pricing→discount→tax order, rounding, snapshots, credit-note caps. |
| `06d-security-and-privacy.md` | §6.7 | ~0.5K | Authentication, authorization, secrets, audit integrity, SEC-n. |
| `07-external-integrations.md` | §7 | ~0.4K | INT-n: adapters, fakes, email, API keys, payment/carrier integrations. |
| `08-non-functional.md` | §8 | ~1.6K | NFR-n: tech baseline, performance targets, reliability, maintainability, testing, CI, delivery. |
| `09-data.md` | §9 | ~1.0K | Data rules and main entities (DM-n). |
| `10-testing-acceptance.md` | §10 | ~0.8K | Acceptance themes §10.1, traceability §10.2; check before declaring a slice done. |
| `11-delivery-plan.md` | §11 | ~1.2K | Slices A0–B4, gates, priority-to-gate mapping. Read the row of your slice. |
| `12-decisions-adr-index.md` | §12 | ~0.7K | Decision lookup D-n; ADR references. |
| `13-open-questions.md` | §13 | ~0.8K | Open questions Q-n with defaults; change control. |
| `appendix-a-glossary.md` | Appendix A | ~0.2K | Unknown term. |
| `appendix-b-diagram-alignment.md` | Appendix B | ~0.2K | Only when SF.png is involved. |
| `appendix-c-id-aliases.md` | Appendix C | ~0.6K | Grouped ID ranges (e.g. “INV-020…025”): look here when a grep for one ID misses. |

Sizes are rough token estimates (bytes ÷ 4). The whole PRD is ~30K tokens; a typical slice needs 12–18K.

## Maintenance

- These files are a mechanical split of PRD v0.8. If the PRD changes, re-split from the new version and update the table above; do not hand-edit requirement text here.
- Section numbers (§5.3 etc.) and requirement IDs are unchanged, so cross-references inside the PRD still work.
