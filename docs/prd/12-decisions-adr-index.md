<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §12 · nothing changed · navigation: docs/prd/INDEX.md -->

## 12. Decision and ADR index

| ID | Decision | Options | Status |
| --- | --- | --- | --- |
| D-1 | Frontend | API only; thin UI; full app | API-first; optional thin UI in hardening |
| D-2 | Company data model | Multi-tenant; singleton company + BranchId; schema/company | **Singleton Company config + BranchId; no speculative CompanyId (corrected)** |
| D-3 | Module communication | In-process+outbox; direct only; broker | **Hybrid sync R + async outbox E; no broker (expanded §4.5)** |
| D-4 | When stock leaves on-hand | Pick; dispatch | **Dispatch** — referenced by FUL-031 |
| D-5 | Customer invoice trigger | Confirm; dispatch; delivery | **Dispatch only, per-dispatch invoices** |
| D-6 | External providers | Fakes only; fakes+real | Fakes P1; optional real later |
| D-7 | Forecasting | Stats; ML; LLM | P2+ stats (not P1) |
| D-8 | AI data exposure | Full; aggregates; none | Aggregates/minimal when AI exists |
| D-9 | Costing | WAC; FIFO | **WAC P1** (scope WAC-01); FIFO P3 |
| D-10 | Approvals | Fixed; configurable | Configurable; seed two levels |
| D-11 | Lot/serial | P1; P2 | **P2** |
| D-12 | Currencies | Multi; single | **Single base currency P1** |
| D-13 | Auth | Built-in; IdP | Built-in P1; IdP P3 |
| D-14 | Stock concurrency | Various SQL strategies | **Invariants+tests in PRD; SQL Server strategy in Inventory ADR** |
| D-15 | Numbering scope | Global; branch; fiscal | Per type + optional branch/fiscal; gaps allowed |
| D-16 | Location types | With/without Picking | **No Picking type** |
| D-17 | RFQ | P1; P2 | **P1 simplified** |
| D-18 | AI phases | P1; P2/P3 | **P2/P3 only** |
| D-19 | Base currency demo | USD 2dp; other | **A-1 until Q-1** |
| D-20 | SF.png authority | Binding; conceptual | **Conceptual; Multi-Tenancy superseded** |
| D-21 | Sales–Finance dependency | Cycle; tables; contracts | **Acyclic: Sales→Finance query contracts; Finance→Sales events only (MOD-5/9/11)** |
| D-22 | Dispatch vs invoice coupling | Same TXN; same status; separate | **Separate TXNs + separate statuses (FIN-027)** |
| D-23 | Branch count | Hard-coded 3; seed 3 | **Seed three; not schema max (MD-002)** |
| D-24 | 404 vs 403 | Always 403; always 404; hybrid | **Hybrid BRN-011…014** |
| D-25 | Invoice PDF | P1; P2 | **P2 (FIN-024)** |

*Mechanism-level decisions are recorded in `ADR/ADR-001` through `ADR/ADR-019`; see `docs/Decision Register.md` for decision tracking.*

---

### 12.1 References

- `ADR/ADR-001…019` — architecture decisions (authoritative for mechanism-level choices).  
- `SF.png` conceptual only (Appendix B).  
- Stack: .NET 10, ASP.NET Core, EF Core 10, SQL Server, xUnit, Docker Compose, GitHub Actions, OpenAPI.
