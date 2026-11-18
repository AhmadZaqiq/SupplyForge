<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.10 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.10 Dispatch and invoice processing status (Fulfillment)

Authoritative definitions of FUL-036…038 are stated here; Section 5.7 cross-references this section.

- **FUL-036 (P1B)** Dispatch status shall transition only from Draft to Dispatched; a committed dispatch shall not be reversed.
- **FUL-037 (P1B)** API responses for dispatch return dispatch status immediately; invoice generation is reported via **separate** status endpoint `GET .../dispatches/{id}/invoice-generation` (served by **Finance**, not by Fulfillment reading Finance tables) or embedded read-model field **labeled clearly** as non-authoritative for finance.
- **FUL-038 (P1B)** UI/API must not show “invoiced” solely because dispatch succeeded.
