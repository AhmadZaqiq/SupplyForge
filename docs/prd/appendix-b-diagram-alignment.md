<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of Appendix B · nothing changed · navigation: docs/prd/INDEX.md -->

## Appendix B. Conceptual diagram alignment

Authority: conceptual only (D-20). **Not modified.**

### B.1 Aligned

Master data, procurement path, inbound inspect accept/reject, inventory hub, sales→reserve, fulfill pick/pack/dispatch, shipping track/confirm, finance AP/AR, foundation (auth, audit, notifications, integrations — **minus Multi-Tenancy**), analytics/AI band.

### B.2 Differences

| --- | --- | --- | --- |
| 1 | Multi-Tenancy | In foundation | **Superseded**; Company singleton |
| 2 | Branches | Absent | Seed three + auth |
| 3 | AI band | Equal prominence | P2/P3 |
| 4 | Partial inspection | Binary accept/reject | Sessions + PendingQty |
| 5 | Invoice vs dispatch | Not shown | Split statuses |
| 6 | Customer returns | Not shown | P2 workflow; Returns location P1 guarded |

### B.3 Possible future image update

Replace Multi-Tenancy with “Single Company · Three Branches”; optional AI “Phase 2” badge.

---
