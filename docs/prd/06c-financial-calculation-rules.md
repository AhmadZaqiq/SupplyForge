<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §6.6 · nothing changed · navigation: docs/prd/INDEX.md -->

### 6.6 Financial calculation rules

#### 6.6.1 Currency and precision

- **FIN-CALC-01 (P1A)** Single **company base currency** for all P1 documents and balances. Multi-currency deferred (D-12).
- **FIN-CALC-02 (P1A)** Money uses SQL `decimal` (exact numeric), **never float/double**.
- **FIN-CALC-03 (P1A)** Scale follows base currency minor unit. Demo default **A-1: USD, scale 2**. Other currency via Q-1 changes scale (0/2/3). Not a universal legal rule.
- **FIN-CALC-04 (P1A)** Quantity decimal scale default 4 in base unit (ADR may adjust).

#### 6.6.2 Pricing, discount, tax order

- **FIN-CALC-05 (P1A)** **Tax-exclusive** line prices in P1.
- **FIN-CALC-06 (P1A)** Per line: (1) unit price × qty → gross; (2) line discount → net; (3) tax on **net** → tax amount; (4) store net and tax separately; line total = net + tax for display.
- **FIN-CALC-07 (P1A)** Order-level discount: prefer line-level only in P1; order-level P2 if needed.
- **FIN-CALC-08 (P1A)** Document totals: `subtotal = Σ line net`; `tax_total = Σ line tax`; `grand_total = subtotal + tax_total` (+ non-tax charges if defined) = **sum of rounded line components** (no second rounding pass).

#### 6.6.3 Rounding

- **FIN-CALC-09 (P1A)** Round each monetary line component **half away from zero** to currency scale at line time; totals = Σ rounded components. *(Other rounding only if PO mandates — Q-2.)*
- **FIN-CALC-10 (P1A)** Percentages stored ~6 dp; not rounded before multiply; only money outputs round.

#### 6.6.4 Immutable snapshots

- **FIN-CALC-11 (P1A)** Posted financial docs freeze line amounts, tax, totals, currency — later master-data changes do not alter them.
- **FIN-CALC-12 (P1A)** SO prices frozen at confirm (SAL-024); invoice copies frozen values for dispatched qty.

#### 6.6.5 Credit notes and payment allocation caps

*(Consumed by P1B money operations — credit notes (FIN-025/FIN-007), payment allocation (FIN-011/FIN-030/FIN-052), supplier payments (FIN-012); tagged **P1B** since . General money rules needed earlier (currency, scale, tax, rounding, snapshots) stay P1A as FIN-CALC-01…12.)*

- **FIN-CALC-13 (P1B)** Credit notes against an invoice **≤ original invoice total**.
- **FIN-CALC-14 (P1B)** Payment allocations to an invoice **≤ invoice open balance** (total − payments − credits). Excess = unallocated credit on payment (not silent overpay on invoice).
- **FIN-CALC-15 (P1B)** Σ allocations **≤ payment amount**.
- **FIN-CALC-16 (P1B)** Supplier payment same caps on AP side.

#### 6.6.6 Explicit non-claims

- No double-entry GL, chart of accounts, or statutory reporting in P1.
- Tax compliance for any jurisdiction **unresolved** (Q-2); rates are configuration only.

---
