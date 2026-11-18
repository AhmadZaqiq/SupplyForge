<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of Appendix A · nothing changed · navigation: docs/prd/INDEX.md -->

## Appendix A. Glossary

| Term | Meaning |
|---|---|
| Available | Eligible Storage on-hand less unpicked reservations and blocked eligible stock (INV-002). |
| Branch scope | The branches and warehouses a principal is authorized to access (BRN-001…010). |
| C/R dependency | Compile-time contract reference and/or in-process runtime command/query dependency (Section 4.1). |
| E subscription | Asynchronous event consumption through the outbox; not a compile-time dependency (Section 4.1). |
| Incoming | Open purchase-order quantity not yet received; planning signal, not stock (INV-002/063). |
| On-hand | Physical quantity in warehouse locations (INV-002). |
| Outbox | Durable post-commit delivery record written atomically with the owning business transaction (OUT-01…07). |
| Picked-reserved | Reserved quantity moved to Staging and awaiting dispatch or release (INV-002/024/026). |
| Unpicked-reserved | Reserved quantity still in eligible Storage and subtracted from eligible on-hand (INV-002). |
| WAC | Weighted-average cost, scoped by product and warehouse (WAC-01…11). |


