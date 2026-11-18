<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.2 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.2 Company and Master Data (MD)

- **MD-001 (P1A)** **Company singleton** profile: legal name, tax id, address, logo, base currency, timezone, fiscal year start, default language. Exactly one active company row; creation only at first-run migration/seed (not a tenant API).
- **MD-002 (P1A)** **Branches: seed three** (codes/names Q-3). Schema supports N branches; v1 seed = 3; not a hard-coded max (A-2). Warehouses require branch.
- **MD-003 (P1A)** Employee directory; optional user link.
- **MD-004 (P1A)** Admin user lifecycle + role/branch assignment; last admin protected.
- **MD-010 (P1A)** Product fields incl. **RequiresInspection**, reorder, lead time, default price, standard cost.
- **MD-011 (P1A)** Category hierarchy.
- **MD-012 (P1A)** UoM conversions; store base unit.
- **MD-013 (P1A)** Product statuses; no archive with on-hand > 0.
- **MD-014 (P2)** Lot/serial. **MD-015 (P2)** Multi-barcode. **MD-016 (P3)** Variants/images.
- **MD-020 (P1A)** Supplier + status (no multi-currency — D-12).
- **MD-021 (P2)** Supplier catalogue. **MD-022 (P2)** Performance score.
- **MD-030 (P1A)** Customer + credit limit + status.
- **MD-031 (P1A)** Addresses with defaults. **MD-032 (P2)** Groups.
- **MD-040 (P1A)** Warehouse + **branch id**.
- **MD-041 (P1A)** Locations; types **Receiving, Quarantine, Storage, Returns, Staging** (D-16).
- **MD-042 (P1A)** Block location; no deactivate while holding stock.
- **MD-043 (P3)** Capacity.
- **MD-050 (P1A)** Tax rates, payment terms, reason codes.
- **MD-051 (P3)** FX. **MD-060/061 (P2)** Import/duplicates.

**Rules:** deactivate not delete; Blocked supplier/customer rules; price changes never mutate existing docs.
