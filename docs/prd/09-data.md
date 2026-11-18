<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §9 · nothing changed · navigation: docs/prd/INDEX.md -->

## 9. Data requirements

### 9.1 Data rules

- **DM-1 (P1A)** `BranchId` on branch-owned tables; **Company** is singleton without multi-tenant `CompanyId` mandate; no speculative tenancy columns (D-2).
- **DM-2 (P1A)** Money decimal per base currency scale; qty decimal base unit (scale 4 default).
- **DM-3 (P1A)** Surrogate keys (**BIGINT**); human numbers displayed.
- **DM-4 (P1A)** No cross-schema FKs; store other module ids + snapshots.
- **DM-5 (P1A)** Explicit status enums stored as **string status codes**; transitions tested.
- **DM-6 (P1A)** History via audit/status/immutable ledgers.
- **DM-7 (P1A)** Optimistic concurrency; rowversion alone ≠ stock concurrency solution (INV-021/D-14).
- **DM-8 (P1A)** Finance tables never referenced from Sales (MOD-9); contract DTOs live in **`Finance.Contracts` — a per-module leaf contract assembly (ADR-001; no shared contracts assembly)** — **interface owned by Finance**.

### 9.2 Main entities

| Module | Entities |
| --- | --- |
| Platform Core | **User, Role, Permission, UserRole, RolePermission, UserBranchScope, UserWarehouseScope, RefreshToken, ApiKey, ApprovalPolicy, ApprovalPolicyStep, ApprovalRequest, ApprovalDecision, AuditLog, Notification, DocumentSequence, IdempotencyKey, JobDefinition/JobRun** |
| Master Data | **Company (singleton), Branch, Employee, Product, ProductCategory, UnitOfMeasure, UnitConversion, Supplier, SupplierContact, SupplierAddress, Customer, CustomerContact, CustomerAddress, Warehouse, StorageLocation, TaxRate, PaymentTerm, ReasonCode** |
| Inventory | **StockBalance, StockMovement, Reservation, Transfer, TransferLine, Adjustment, AdjustmentLine, ProductCost, IncomingProjection (per product/warehouse open-PO qty — INV-063)**, Lot/Serial (P2), StockCount (P2) |
| Procurement | **PurchaseRequisition, RequisitionLine, QuotationRequest, QuotationRequestLine, QuotationRequestSupplier, SupplierQuote, SupplierQuoteLine, QuoteAward, PurchaseOrder, PurchaseOrderLine, PurchaseOrderRevision**; Agreement* (P2) |
| Inbound | **ExpectedDelivery, ExpectedDeliveryLine, DeliveryRegistration, GoodsReceipt, GoodsReceiptLine, Inspection, InspectionSession, InspectionLine, Discrepancy, SupplierReturn, SupplierReturnLine**; InspectionPlan* (P2) |
| Sales | **SalesOrder, SalesOrderLine, Backorder**; Quotation*, PriceList*, CustomerGroup* (P2), CustomerReturn* (P2) |
| Fulfillment | **FulfillmentOrder, FulfillmentOrderLine, PickList, PickListLine, Package, PackageItem, Dispatch, DispatchLine** |
| Shipping | **Carrier, Shipment, ShipmentPackage, TrackingEvent, DeliveryConfirmation**; RateTable* (P2) |
| Finance | **SupplierInvoice, SupplierInvoiceLine, InvoiceMatchResult, SupplierPayment (RecordingBranchId), SupplierPaymentAllocation, CustomerInvoice, CustomerInvoiceLine, CreditNote, CreditNoteLine, DebitNote, CustomerPayment, CustomerPaymentAllocation, PaymentReversal, **CustomerCreditExposure (authoritative credit-exposure record — MOD-12), CustomerBalance, SupplierBalance (FIN-040 projections)**, InvoiceGeneration (DispatchId unconditional unique; Status NotStarted/Pending/Succeeded/Failed/Skipped; CustomerInvoiceId nullable; AttemptCount; LastAttemptAt; LastSucceededAt; LastErrorCode; LastErrorMessageRedacted; NextRetryAt; CreatedAt; UpdatedAt; RowVersion; CorrelationId), InvoiceGenerationAttempt (optional retry history: Id, InvoiceGenerationId, AttemptNumber, StartedAt, CompletedAt, Result, RedactedErrorCode, CorrelationId — no DispatchId uniqueness)** |
| Reporting | **StockSnapshot, SalesFact, KpiSnapshot, DashboardLayout**, other facts P2 |
| Integrations | **OutboxMessage, InboxMessage/WebhookEvent, IntegrationLog, IntegrationConnection/Credential** |

Also **DM-8:** Finance contract interfaces (`ICreditExposureQuery` etc.) owned by Finance; Sales references contracts only — never Finance tables.

Table counts are estimates for design, not feature targets (G-8).

---
