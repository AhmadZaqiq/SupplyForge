# SupplyForge — Handoff (read this first)

Written: 2026-11-16. Last updated: 2026-11-24. Put this file at `docs/HANDOFF.md` in the project.
If you are an AI picking this work up: read this file fully, then follow section 10. Do not assume any step in section 2 "Pending" has been done. Check the disk.

## 1. What the owner is doing

The owner is building **SupplyForge**, a modular-monolith backend (.NET 10) for the goods lifecycle (supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report), using **OpenCode** as the coding agent, inside **VS Code**, on **Ubuntu**.

Work done so far was **setup, not coding**. The goal was to make OpenCode load the right context on demand:
- a reusable engineering knowledge library (`my-ai-knowledge/`, written by the owner),
- a large PRD (~122 KB, ~30K tokens) that was **split verbatim into 34 section files** so the agent reads only what a task needs,
- a project `AGENTS.md`, an `opencode.json`, and two slash commands that tie it together.

Owner preferences: informal, short and concrete. Code, config and project files stay in English. The library assumes an early-career developer with solid fundamentals: explain trade-offs briefly, skip tutorials.

## 2. Status

### Current handoff (refresh at every handoff; read before any edit)
- Last refreshed: 2026-11-26T15:55:00Z
- Active agent: none (Kilo session; A1-4 Part 2 + A1-5 + A1-6 + A1-7 done, NOT committed)
- Last commit: 8208234 (docs: handoff sync after owner re-run). A1-4 Part 1 code is 93b28c0.
- Git status: all A1-4 Part 2 + A1-5 + A1-6 + A1-7 code + tests complete, NOT committed; 0 staged
- Uncommitted scope: A1-4 Part 2 (branch scope) + A1-5 (inventory ledger + WAC core) + A1-6 (WAC core) + A1-7 (transfer orchestration + corrections) — code + tests complete, NOT committed
- Changed/new/deleted files:
  - modified: HANDOFF.md (root + docs), A1-PROGRESS.md, Program.cs, ServiceCollectionExtensions.cs, SupplyForge.Host.csproj, MasterDataDbContext.cs, MasterDataDbContextFactory.cs, MasterDataSeed.cs, MasterData.Infrastructure.csproj, ICurrentBranchScope.cs, PlatformSharedServices.cs, IIdempotencyStore.cs, AmbientSessionThreeModuleTests.cs, MasterDataSeedTests.cs, PartnersAndSharedMasterTests.cs, ProductCatalogueTests.cs, WarehouseLocationTests.cs, README.md
  - new: BranchScopeHolder.cs, IBranchScopeResolver.cs, HttpBranchScopeResolver.cs, BranchScopeMiddleware.cs, BranchScopeDefaults.cs, NoScopeImpl.cs, BranchScopeProvider.cs, SystemBranchScope.cs, RequestBranchScope.cs, NoBranchScope.cs, BranchScopeArchitectureTests.cs, WarehouseBranchScopeTests.cs, BranchScopeMiddlewareTests.cs, Inventory module (Domain, Contracts, Infrastructure), IWarehouseBranchResolver.cs, WarehouseBranchResolver.cs, 16 Inventory integration tests (A1-5), Inventory.UnitTests project, TransferOrchestrator.cs, AdjustmentOrchestrator.cs, OpeningImportOrchestrator.cs, InventoryDbContext model config, migration 20261126004151_A1TransferOrchestration, Transfer/Adjustment/OpeningImport domain entities, 32 new A1-7 tests (unit + integration), README.md rewrite
  - deleted: BranchScopeAccessor.cs, BranchScopeContext.cs
- Decisions: Q-A…Q-D accepted; Q-E decided 2026-11-25 (option a) and IMPLEMENTED; blocked⇒inactive check removed; A1-5: no var, idempotency via ambient TXN rollback (FailAsync removed), branch validation fail-closed via MasterData.Contracts; A1-7 corrections: CloseTransferAsync rejects Cancelled/Closed/Draft/Approved, CreateTransferShortfallAdjustmentAsync validates transfer existence/status/shortfall with HOLDLOCK, PendingApproval state reachable via SubmitForApprovalAsync, OpeningImport warehouse existence validation
- Build: 0 errors / 0 warnings — verified
- Test: Inventory integration 32/32 (A1-7 new) + 40/40 (existing), unit 45/45, architecture 20/20, format clean — all verified
- Format: clean — verified (`dotnet format --verify-no-changes` exit 0)
- Migration: A1-4 Part 1 `20261124181017_A1WarehousesLocations` created, NOT applied; A1-5 `20261125171326_A1InventoryLedger` created, NOT applied; A1-7 `20261126004151_A1TransferOrchestration` created, NOT applied; Q-C `platform.User.IsCompanyWide` migration file only, awaiting owner approval
- Next step: owner review of all uncommitted A1 work; commit after approval

| Item | State |
|---|---|
| PRD split into `docs/prd/` (34 files + `INDEX.md`) | Done. Verified: concatenating the parts reproduces the original line by line, and all 817 requirement-ID occurrences are preserved |
| Project `AGENTS.md`, `opencode.json`, `.opencode/commands/{slice,req}.md` | Written and in use. Both `AGENTS.md` files load in current sessions; the slash commands exist but were not exercised in the last session |
| `docs/prd/INDEX.md` (slice → files table, ID prefix → file table, token sizes) | Written by an AI from PRD §11.2. It is a navigation aid, not requirements. Review the slice table |
| Library `my-ai-knowledge/AGENTS.md` patched (2 lines: library root + "open files with Read tool") | Done. The root line now reads `/home/ahmad/SupplyForge/my-ai-knowledge/`; verified no `D:/` remains |
| OpenCode loads the library rules through `instructions` | **Verified** in live sessions (library rules present in session instructions) |
| `ADR/`, `docs/Decision Register.md` | **Present.** ADR-001…006, 008, 009 drafted and **Accepted by the owner 2026-11-16**. Decision Register created. ADR-007 and ADR-010…016 **not drafted yet** (write them when their slice needs them). `adr-review-report.md` reflects the acceptance |
| `SF.png` | **Missing.** The owner will add it later; references stay as they are |
| Application code | **M0 + A0 foundations + A1-1…A1-5.** M0-0…M0-5, A0-1 (JWT), A0-2/A0-2b (rate limiting + defect fix), A0-3 (permission catalogue/role bundles, create-user bootstrap, deny-default authorization — 9/9 steps), A0-4 (BRN HTTP matrix, PLT-044 redaction, INT-052 OpenAPI security schemes), A0-5 (CI workflow + README; **CI green on GitHub** since run `44439119956`, 2026-11-19), A0-6 (ADR-003 ambient session S1–S6 incl. HTTP notification round-trip + `AuthService` on the session), A0-7 (notification delivery state — PLT-051 visible pending, PLT-052 retry worker → dead-letter `Failed`, TD-2 audited mark-sent, delivery fields on the read API; owner-approved migration `20261119033923_AddNotificationDeliveryState` **applied to the dev DB**) — all **done and pushed in `48bd9df`**; sweep **169/169 ×2**. **A0 gate: closed** — all five PRD §11.2 items evidenced in `docs/A0-PROGRESS.md`. PLT-013 deferred by owner decision (migration not approved). **A1-1 done 2026-11-20** (committed in `704b665`): `MasterData` module per ADR-001, `master_data` schema + company singleton + 3-branch seed (MD-001/MD-002), migration file `20261119232720_A1InitialMasterData`, ambient session generalised 2 → N contexts (ADR-003), MOD-10 arch-test extension, CI migration line, new `tests/MasterData.IntegrationTests` (10 tests, throwaway databases); sweep **179/179**, format clean. **A1-2 done 2026-11-22** (committed in `704b665`): product catalogue — `Product`/`ProductStatus`/`Category`/`UnitOfMeasure`/`UnitOfMeasureConversion` (MD-010…MD-013, decisions D-a…D-j in `docs/A1-PROGRESS.md` §1a), `master_data` model extended + migration `20261120110027_A1ProductCatalogue` (all five FKs `Restrict`, zero cascades per PRD "deactivate not delete"), new `tests/MasterData.UnitTests` (6 tests) + 9 catalogue integration tests, one A1-1 test de-coupled from the growing table count; sweep **194/194**, format clean. **A0-8 hardening done 2026-11-22** (6 items: SEC-20 startup guard fails outside Development on the placeholder secret, PLT-073b `NextAsync` via compare-and-swap, SEC-05/PLT-012 password verified before `user_inactive`/`locked_out`, PLT-011 refresh returns the real `userId`, OUT-01/OUT-06 `IOutbox` correlationId + active-transaction guard + `IInbox` fixed to operate on `InboxMessage`, MD-004 README accuracy) — one commit per item pushed: `6085e9c`, `da012a6`, `7eedfb9`, `18040d7`, `d4accf0`, `8d87159`; sweep **201/201**, format clean. **A1-3 done 2026-11-24** (DB-first style: plan first, then code): partners + shared master — `Supplier`/`Customer` (+ statuses, `CreditLimit` decimal 18,2 NOT NULL default 0, negative rejected by CHECK), `SupplierAddress`/`CustomerAddress` (two default flags, each with a filtered unique index), `TaxRate` (Rate decimal 9,6, CHECK 0–100), `PaymentTerm` (DueDays CHECK ≥ 0), `ReasonCode` (one global list, no purpose column) — MD-020/MD-030/MD-031/MD-050/PLT-075, decisions D1–D17 + Q1–Q7 in `docs/A1-PROGRESS.md` §1b; migration file `20261124011532_A1PartnersSharedMaster` (**file only, never applied to the dev DB**), +11 unit +17 integration tests (28 new), one A1-2 test de-coupled by name; sweep **230/230 ×2**, build 0/0, format clean. Status detail: `docs/A0-PROGRESS.md` (incl. §A0-8), `docs/A1-PROGRESS.md` |
| `docs/DATABASE-GUIDE.md` (increment **D0**, updated by **A1-3**) | **Done 2026-11-23; A1-3 update 2026-11-24.** All **32 tables** across the 3 modules (14 `platform`, **15 `master_data`**, 3 `integrations`) — one sentence of purpose, key columns and relationships each — plus one Mermaid ER diagram per module, a "life of a login request" walkthrough naming every table it touches, 2 example rows for each of the main tables (incl. `Supplier`/`Customer`/addresses), and a glossary that defines every term at first use. Read-only test-parallelism investigation recorded in §12. **Docs only: no source, test or config changed by D0** |
| **A1-4 Part 1 (warehouses + locations)** | **Done 2026-11-24 (committed 93b28c0).** Entities `Warehouse`, `StorageLocation`, `LocationType`; port `ILocationDeactivationGuard` + public placeholder guard; migration file `20261124181017_A1WarehousesLocations`; 7 unit + 12 integration tests on throwaway DBs; build 0/0; 249/249 ×2 verified; format clean. **Committed and pushed.** |
| Git | Initial commit on `main`; owner-approved publish to `https://github.com/AhmadZaqiq/SupplyForge` (2026-11-18). The earlier "do not commit for now" instruction is superseded |
| Pre-quota checkpoint workflow | **Done.** `scripts/quota-status.sh` reads the official `GET /api/experimental/usage/limits`. On this machine it exits **3 (unknown)** — signed in to workspace `Personal`, but no Go meters and no budgets, so **no number is guessed**; the fallback trigger is a usage-limit response from the provider. `scripts/checkpoint.sh` runs verify → `git status` + full diff → explicit-path staging → secret/artifact guards → both-HANDOFF guard → commit → push (never force) → `ls-remote` SHA proof. Docs `docs/pre-quota-checkpoint.md`; commands `/quota`, `/checkpoint`. Verified 2026-11-19: `--verify-only` PASS (11.4 s); `--dry-run` staged exactly the intended files; **live run pushed `94b1263` and `ls-remote` matched the local SHA**. That run also exposed a real bug — bash `test` compares `[ "$x" != VERIFIED* ]` **literally**, so the success branch was skipped and a verified push was reported as a failure; fixed with `case` and locked in by `scripts/checkpoint-selftest.sh` (**19/19**: every guard plus the SKIPPED / VERIFIED / failed-push branches, in throwaway repos with a local bare `origin`). Still code-reviewed only, not executed: exit 10/20 (no meter data exists on this machine) and exit 7 (branch behind origin). Detail: section 11 |

**A1-4 Part 1 done 2026-11-24** (warehouses + storage locations: entities, mappings, migration **file only** `20261124181017_A1WarehousesLocations`, unit + 12 integration tests on throwaway DBs; build 0/0; full suite green ×2; **committed and pushed as 93b28c0**; Part 2 branch scope not started). **A1-4 Part 2 (branch scope) + A1-5 (inventory ledger) done 2026-11-25** (code + tests complete, NOT committed). Pending, in order: **D0 documentation + read-only investigation closed 2026-11-23** (`docs/DATABASE-GUIDE.md`; parallelism findings in §12; **docs only — no source, test or config changed**) → **A0-9 login-enumeration hardening closed 2026-11-23** (`3270b93`) → **R0 repository tidy-up closed 2026-11-23** (`8dbf1ab`) → **A1-3 partners + shared master closed 2026-11-24** (DB-first working style: plan → owner approval → code → tests → docs) → **A1-4 Part 1 committed 2026-11-24 (93b28c0); Part 2 + A1-5 done 2026-11-25, awaiting owner review** …A1-9 per `docs/A1-PROGRESS.md` §1 (A1-1/A1-2 committed in `704b665`). Owner decisions still open before their slices: **ADR-007** (approval engine → the INV-041 gate item), **ADR-011** (logo-upload gate item), **OD-03/TD-5** (WAC reversal eligibility), PLT-013 migration approval, Q-5 email-in-P1 before B4, **Q7 placement of MD-003 (proposal: A1-9 — awaiting the owner, `docs/A1-PROGRESS.md` §1b)**. **B4/DM-3 is settled:** the owner accepted Guid surrogate keys on 2026-11-24 (Decision Register **PA-18**, DM-3 amendment). Q-3 branch codes/names are placeholders until answered.

## 3. Environment

- OS: Ubuntu. VS Code installed from the official `.deb` (this adds Microsoft's apt repository, so updates arrive through apt).
- Project folder: assumed `~/SupplyForge`. Verify with `pwd`; it may differ.
- OpenCode is run from the VS Code integrated terminal. The VS Code extension (`sst-dev.opencode`) installs itself the first time `opencode` runs there. It shares the open tab/selection with OpenCode. Per its docs it does **not** render diffs by itself; diffs come from git via VS Code's Source Control panel.
- OpenCode docs say there are V1 and V2 versions with differences (for example the permission syntax). Check `opencode --version` before changing permission config.

## 4. What is on disk

```
SupplyForge/
├─ opencode.json                  instructions: ["my-ai-knowledge/AGENTS.md"] → always-on rules
├─ AGENTS.md                      project rules: stack, architecture, reading protocol, stop-and-ask
├─ .opencode/commands/
│   ├─ slice.md                   /slice A2  → reads the right PRD files, proposes a plan, waits
│   ├─ req.md                     /req INV-014 → grep + read only the section, no edits
│   ├─ quota.md                   /quota → scripts/quota-status.sh, report the exit code
│   └─ checkpoint.md              /checkpoint → guarded pre-quota checkpoint sequence
├─ scripts/
│   ├─ quota-status.sh            official OpenCode usage-limit query (exit 0/10/20/3)
│   ├─ checkpoint.sh              verify → inspect → stage explicit paths → guards → commit → push → prove
│   └─ checkpoint-selftest.sh     19 guard/regression cases in throwaway repos (run after editing checkpoint.sh)
├─ my-ai-knowledge/               the owner's library (generic, reusable)
│   ├─ AGENTS.md                  17 always-on rules + loading index (task → file)
│   ├─ general/                   principles, security, testing, git-workflow, observability-and-operations
│   ├─ workflows/                 discovery, planning, implementation, debugging, review, verification, release-and-incident
│   ├─ expertise/                 DOTNET_ENGINEERING, CLEAN_ARCHITECTURE, SOLID_AND_DI, WEB_API, API_SECURITY,
│   │                             EF_CORE, SQL_SERVER_TSQL, DOCKER, DOTNET_TESTING, ASPNET_CORE_CONFIG_AND_LOGGING
│   ├─ templates/                 discovery-report, implementation-plan, decision-record, bug-report, review-report, project-AGENTS
│   ├─ evals/smoke-tests.md       run after changing the library
│   └─ README.md, SOURCES.md
├─ README.md                      architecture overview + runbook (NFR-061/062)
├─ src/                           Host + Modules + Shared
│   └─ Modules/                   built: PlatformCore, Integrations, MasterData, Inventory
│       ├─ Inventory/             built (A1-5 ledger + WAC + A1-7 documents): stock movements, balances, WAC, transfers, adjustments, opening imports
│       ├─ Procurement/           planned, not built (A2)
│       ├─ Inbound/               planned, not built (A2)
│       ├─ Sales/                 planned, not built (A3)
│       ├─ Fulfillment/           planned, not built (A4)
│       ├─ Shipping/              planned, not built (A4)
│       ├─ Finance/               planned, not built (B1)
│       └─ Reporting/             planned, not built (B3)
├─ tests/                         ArchitectureTests, PlatformCore.UnitTests, PlatformCore.IntegrationTests, MasterData.UnitTests, MasterData.IntegrationTests, Inventory.UnitTests, Inventory.IntegrationTests
├─ docs/
│   ├─ HANDOFF.md                 this file
│   ├─ A0-PROGRESS.md             increment log: status + evidence
│   ├─ A1-PROGRESS.md             A1 plan, owner blockers B1–B4, A1-1/A1-2/A1-3 evidence
│   ├─ pre-quota-checkpoint.md    quota triggers, checkpoint sequence, guards
│   ├─ Decision Register.md, adr-review-report.md
│   └─ prd/
│       ├─ INDEX.md               start here for requirements
│       ├─ 00-how-to-use.md … 13-open-questions.md, appendix-a/b/c   (verbatim PRD parts)
│       └─ _archive/              old single-file PRD. Never read it
└─ ADR/                           expected: ADR-001 … ADR-016 accepted, 017–019 proposals (not provided yet)
```

PRD part files are named by section: `05-03-inventory-INV.md` is §5.3, `06c-financial-calculation-rules.md` is §6.6, and so on. Section numbers and requirement IDs are unchanged, so cross-references still work.

## 5. How context loading works (the mechanism)

1. `opencode.json` → `instructions` loads `my-ai-knowledge/AGENTS.md` into every session. The project `AGENTS.md` at the root is loaded by OpenCode as the project rules file. Both are combined.
2. OpenCode does **not** follow file paths mentioned inside those files. Everything else is read **on demand** by the agent with its Read tool, guided by the index tables and the reading protocol. That is why both files tell the agent to open matching files and to report which files it read.
3. The PRD is never placed in `instructions` (too big). The agent reads `docs/prd/INDEX.md`, then `00-how-to-use.md`, then the **Core set** (~8K tokens: 00, 03, 04a, 04b, 04c, 06a, 02b), then the slice files. One requirement at a time: `grep -rn "INV-014" docs/prd/`, then read around the hit. Grouped ranges: `appendix-c-id-aliases.md`.
4. Precedence: the owner's explicit request and safety constraints → project instructions (PRD, ADRs, project `AGENTS.md`) → library rules. The library's default architecture (Clean Architecture layers) is **overridden** by the PRD's modular-monolith rules (library rule 4 allows this).

## 6. Command cheat sheet (Ubuntu)

Install or update VS Code:
```bash
sudo apt update && sudo apt install -y wget git
cd ~/Downloads
wget -O code.deb https://update.code.visualstudio.com/latest/linux-deb-x64/stable   # ARM: replace x64 with arm64
sudo apt install -y ./code.deb
# later updates:
sudo apt update && sudo apt install --only-upgrade code
```

Prepare the project:
```bash
unzip -o ~/Downloads/SupplyForge-opencode-setup.zip -d ~/        # merges into ~/SupplyForge
cd ~/SupplyForge
mkdir -p docs/prd/_archive ADR
mv SupplyForge-PRD-v0_8-AI.md docs/prd/_archive/                  # keep a backup, not at project root
sed -i "s|D:/SupplyForge/my-ai-knowledge/|$PWD/my-ai-knowledge/|" my-ai-knowledge/AGENTS.md
git init && git add -A && git commit -m "baseline"
cp ~/Downloads/HANDOFF.md docs/HANDOFF.md
```

Run:
```bash
cd ~/SupplyForge
code .          # then Ctrl+` for the integrated terminal, and type: opencode
```

Inside OpenCode: `/slice A0` … `/slice B4`, `/req <ID>`, plus this project's `/quota` (read the usage limit) and `/checkpoint` (run the safe pre-quota checkpoint, section 11). Built-ins include `/undo`, `/redo`, `/share`, `/help`. **Do not run `/init`**: it generates an `AGENTS.md` and this project already has a curated one.

VS Code shortcuts (OpenCode extension): `Ctrl+Esc` open OpenCode in a split terminal, `Ctrl+Shift+Esc` new session, `Alt+Ctrl+K` insert a file reference like `@File#L37-42`. `Ctrl+Shift+G` opens Source Control (diffs).

## 7. Project essentials (so you rarely need to open the whole PRD)

- Stack: C#, .NET 10, ASP.NET Core Web API, EF Core 10, SQL Server, xUnit with real SQL Server integration tests, Docker Compose, GitHub Actions, OpenAPI. Not by default: microservices, Redis, RabbitMQ, MediatR, CQRS frameworks (MOD-8; needs an ADR).
- One company (singleton config, `BranchId` on data, no speculative `CompanyId`), **three seeded branches** (seed, not a schema maximum). Single base currency in P1. API-first.
- Modules own their tables. Cross-module reads through public query contracts; writes through public commands or events via the outbox (hybrid sync + async, no broker). Only Inventory posts stock movements. Only Finance owns amounts and credit exposure. Sales never reads Finance tables (acyclic: Sales → Finance by query contract, Finance → Sales by events only). Reporting is event-only.
- Stock leaves on-hand at **dispatch** (D-4). Customer invoices are generated at dispatch, per dispatch, in a separate transaction with a separate status (D-5, D-22, FIN-027). WAC costing in P1. No Picking location type. Invoice PDF is P2. AI is P2/P3 only.
- 404 vs 403 policy: BRN-011…014 (`02b-users-roles-branches.md`).
- Invariants that always apply: §6.1, §6.3 (negative stock), §4.1–4.2, §4.3 (credit reservation), §4.5 (idempotency, transactions, outbox), BRN-011…014.
- Traceability: put the requirement ID in test names (NFR-051) and in commit/PR titles.

Slices (PRD §11.2; files per slice in `docs/prd/INDEX.md`):
A0 Foundations · A1 Master + Inventory core · A2 Buy & receive · A3 Sell & reserve · A4 Fulfil & dispatch · A5 Money stub (optional, **default skip**, TD-6/PA-17) · B1 Invoice generation · B2 AP/AR depth · B3 Thin insight · B4 Hardening. P2/P3 requirements are never implemented unless the owner says so.

Stop and ask (PRD §0 rule 3), do not decide: text marked `NEEDS OWNER APPROVAL`; `OD-n` items; anything depending on proposed ADR-017…019; `Open-PO` questions without a default. If a `TD-n` default or a `Default` in §13.1 exists, implement the default. If an ADR or Decision Register entry a requirement relies on was not provided, ask; do not invent the mechanism.

## 8. Known issues and traps

1. **Path:** the delivered `my-ai-knowledge/AGENTS.md` contains `D:/SupplyForge/my-ai-knowledge/`. On Ubuntu it must be replaced (the `sed` line in section 6). Check with `grep -n "D:/" my-ai-knowledge/AGENTS.md` (expect no output).
2. `ADR/`, `docs/Decision Register.md`, `SF.png` are missing. A0 produces the M0 ADRs (ADR-001…006); the agent should ask the owner whether to draft them or whether the owner already has them. Solution/project layout depends on them: do not create projects or layers before they exist.
3. **OD-03 / TD-5:** WAC reversal eligibility and correction path are pending owner approval. Ask before touching costing reversal code (slice A1).
4. `instructions` uses a **relative** path (documented form). An absolute path in a global `~/.config/opencode/opencode.json` was **not verified** against the docs. If the rules do not load, fallback: create `~/.config/opencode/AGENTS.md` (the documented global rules file) containing one line: "Read <absolute path>/my-ai-knowledge/AGENTS.md at the start of every task."
5. External directories: files outside the project folder may trigger permission prompts (V1 default is to ask). Keep the library inside the project folder to avoid this.
6. The `Commands` section of the project `AGENTS.md` has placeholders (`Run`, `Migrate`) to fill once slice A0 creates the solution.
7. The library's smoke tests (`my-ai-knowledge/evals/smoke-tests.md`) were never run in OpenCode.
8. Do not hand-edit requirement text in `docs/prd/`. If the PRD changes, re-split from the new version and update `INDEX.md`.

## 9. Verification checklist (run after setup)

| # | Do | Expected |
|---|---|---|
| 1 | `ls docs/prd/INDEX.md .opencode/commands opencode.json AGENTS.md` | all exist |
| 2 | `grep -n "D:/" my-ai-knowledge/AGENTS.md` | no output |
| 3 | In OpenCode: "Without reading any file, state rule 6 and rule 12 of your instructions" | Rule 6: never commit/push/deploy/delete data/run destructive commands without explicit permission. Rule 12: run build/tests/linters after changes, never claim a check passed unless run |
| 4 | `/slice A0` | It reads INDEX and 00, the Core set, the A0 files, `11-delivery-plan.md` row, `workflows/planning.md`; replies with files read, requirement IDs in scope, open items (ADRs missing, etc.), a plan; writes **no code**; waits |
| 5 | `/req FIN-027` | Requirement text, priority/slice, dependencies (D-22 etc.); modifies nothing |
| 6 | Ask: "I want an endpoint that returns a customer's orders. Which files will you read first? No code." | PRD `00` and `INDEX`, SAL parts, `workflows/implementation.md`, `expertise/WEB_API.md` and similar. It must not open the whole PRD |
| 7 | `evals/smoke-tests.md` | Run and report honestly what passed and what was not run |

If #3 fails: section 8 item 4. If the VS Code extension does not install: run `opencode` from the **integrated** terminal, check the `code` command exists (`which code`), restart the terminal.

**Test-failure rule (owner, 2026-11-23 — applies to every future run).** Whenever
tests fail, save the **full** output to a file *first*, before reading or
re-running: `dotnet test 2>&1 | tee /tmp/test-run-N.txt`. Never tail and discard.
A failure message that was not captured cannot be diagnosed later, and re-running
overwrites the evidence. Capture, then read, then decide.

8. **Several AI tools use this repo (OpenCode, Kilo).** Trust the disk, git log and HANDOFF, never chat memory. End every session by updating section 2 and committing, or by listing every uncommitted file.

## 10. How to continue (instructions to the next AI)

1. Reply to the owner briefly. Lead with the result.
2. Do not trust this file over the disk. Run `pwd`, `git status`, `ls`, and the checks in section 9 #1–#2 to learn what is actually done. Ask the owner for the result of #3–#4 if you cannot run them yourself.
3. Fix whatever section 9 shows broken before starting any slice.
4. Then continue the current slice. **A0 is closed** (`docs/A0-PROGRESS.md`, including the **A0-8** hardening record of 2026-11-22 — do not re-run it); **A1-1 through A1-7 are complete** — read `docs/A1-PROGRESS.md` first (increment plan §1, owner blockers §0, evidence §5–§10), then the slice files listed in `docs/prd/INDEX.md`. A1-4 Part 2, A1-5, A1-6, and A1-7 are all completed and verified but NOT committed. Next step: owner review and commit of all completed A1 work, then A1-8 (Availability + replenishment + returns guard) — keep the DB-first style (plan → owner approval → code → tests → docs).
5. Follow the project `AGENTS.md` and `my-ai-knowledge/AGENTS.md`. In particular: do only what was asked; never commit, push, deploy or delete data without permission; run build and tests after changing code and never claim a check passed unless you ran it; do not invent APIs, files, requirements or results.
6. Do not read the whole PRD or the whole library. State which files you read before you write code.
7. Do not claim anything about OpenCode behaviour that you did not verify; the official docs are at https://opencode.ai/docs (rules, permissions, commands, ide, skills) and VS Code Linux setup at https://code.visualstudio.com/docs/setup/linux.
8. Optional later improvement, not required: turn the library workflows into OpenCode skills (`SKILL.md` under `.opencode/skills/` or `~/.config/opencode/skills/`) so the agent discovers them by description. Verify the current skills docs first.
9. Before starting any substantial task, run `scripts/quota-status.sh` (or `/quota`) and report the exit code. If it warns (10), is exhausted (20), or a usage-limit response arrives, **stop starting tasks** and follow section 11 — never leave the working tree uncommitted.

### Paste this into another AI or a fresh OpenCode session

```
Read docs/HANDOFF.md fully, then AGENTS.md. Check the disk for what is actually done (section 9 #1-#2).
Tell me what is done, what is broken, and the next step. Do not write code yet.
```

## 11. Pre-quota checkpoint (protect the work)

Full detail: [`docs/pre-quota-checkpoint.md`](docs/pre-quota-checkpoint.md). Commands: `scripts/quota-status.sh`, `scripts/checkpoint.sh`, `/quota`, `/checkpoint`. This is tooling and documentation only — it changes no application code and no PRD requirement.

**Where the number comes from.** Official endpoint: `opencode api GET /api/experimental/usage/limits` (operation `experimental.usage.limits`) returns the OpenCode Go allowance meters (`rolling`/`weekly`/`monthly`, each `percent` + `limited`) and Console budgets. On this machine it returns workspace `Personal` with **no meters and no budgets**, so `scripts/quota-status.sh` exits **3 (unknown)** and prints no number. **Do not estimate one** from `opencode stats`, token counts or elapsed time. The fallback trigger is a usage-limit response from the provider (HTTP 429 / a "usage limit" error).

**When a trigger fires** (exit 10 warning, exit 20 exhausted, or a usage-limit response):

1. Stop starting new tasks. Bring the current operation to a consistent point (nothing half-edited, project still builds).
2. `scripts/checkpoint.sh --verify-only` — fastest relevant verification (`dotnet build`, ~11 s incremental; `--verify-cmd "dotnet test ..."` if tests are wanted).
3. Update **both** `HANDOFF.md` and `docs/HANDOFF.md`: completed work, verification result, current state, exact next steps. The script refuses to run if either is unstaged or unchanged.
4. Agree the exact path list with the owner. Never `git add -A`.
5. `scripts/checkpoint.sh --message "<title with the requirement ID>" -- <paths>` — prints `git status --porcelain` and the complete diff, stages only those paths, refuses `.env`/`secrets.json`/keys/databases/build artifacts/archives/`SupplyForge.zip`, commits, pushes **without force**, then proves with `git ls-remote` that the remote SHA equals the local SHA.
6. Only call it a checkpoint when the report says `VERIFIED — remote SHA matches local commit`. Otherwise report exactly where it stopped (exit codes are in `scripts/checkpoint.sh --help`).

**Safety.** Never force-push, never rewrite history, never reset/restore/delete to make a checkpoint pass. Commit and push still need owner approval (project `AGENTS.md`) — the script automates the sequence, not the permission. After any change to `checkpoint.sh`, run `scripts/checkpoint-selftest.sh` (19 cases in throwaway repos; exit 0 = every guard still behaves). Last-run artefacts are in `.git/checkpoint/` (inside `.git`, never committable).

**If the quota runs out mid-task:** `git fetch origin && git status`, read these HANDOFF files, confirm the tree is clean, and continue from the last verified GitHub checkpoint. Nothing is lost as long as that checkpoint was verified.

## 12. Known gaps backlog (recorded by D0, 2026-11-23)

Documentation only — **nothing below is implemented, and nothing here is approved work**. Each
item needs an owner decision before the slice that would touch it. Verified against source on
2026-11-23.

1. **Password storage and policy.** `PasswordHasher` uses PBKDF2 with **100,000 iterations**
   (`PasswordHasher.Iterations`), and there is **no password policy anywhere** — no minimum
   length, complexity, expiry or history rule.
2. **Refresh-token rotation is not atomic.** `RefreshToken` carries **no concurrency token**
   (no `RowVersion` / `IsConcurrencyToken`, unlike `User`, `Branch`, `Product`), so
   `RefreshAsync`'s revoke-then-insert has no optimistic-concurrency guard and two concurrent
   refreshes can both succeed.
3. **Outbox is under-specified and unowned.** `integrations.OutboxMessage` has **no `BranchId`,
   no `CausationId`, no retry-scheduling column** (only `AttemptCount` + `LastError`), and
   **nothing owns the outbox dispatcher** — rows are written but never dispatched
   (OUT-03/OUT-07, PLT-071).
4. **Container privileges.** The `Dockerfile` runs as **root** (no `USER` directive), and the
   application connects to SQL Server as **sa**.
5. **API-key auth is not an authentication scheme.** It is a **manual `X-Api-Key` header check
   inside one endpoint** (`GET /api/v1/api-keys/me`, `.AllowAnonymous()`); the only registered
   scheme is JWT Bearer.
6. **No integration test proves branch isolation yet.** BRN-012 (the foreign-branch leg) lands
   with the **first branch-owned entity in A1**; current tests only cover status discrimination.

### D0 findings (read-only): do the two integration projects run in parallel?

**Yes.** `dotnet test` starts one VSTest host per test project concurrently (five interleaved `Test run for …` banners in a saved log), and with no `xunit.runner.json` / `.runsettings` / `[CollectionBehavior]` the xUnit 2.9.3 defaults apply — classes run in parallel up to 12 threads (12 classes in PlatformCore.IntegrationTests, 4 of which each spawn a real Host).
Measured on a green run (202/202, 109 s wall): **4 testhost + 4 Host processes alive at once**, SQL container peaked at **110 % CPU**, available RAM fell **4.2 → 1.6 GB** with swap-out up to **22 MB/s**, while `iowait` stayed **0 %** and CPU reached **99 % busy** (run queue 34 on 12 CPUs).
Both projects use `Server=localhost,1433`: PlatformCore integration tests hit the **shared `SupplyForge` dev DB** while MasterData integration tests **CREATE/DROP throwaway DBs on the same server**; the container has **no CPU/memory limits** and WSL2 has no `.wslconfig` (≈8 GB of 16 GB).
`D:` holds both VHDX files and is a **SATA SSD** (`Get-PhysicalDisk`), so WSL's `ROTA=1` is **not** evidence of a spinning disk.
**Verdict: contention is a plausible contributor to the recurring `Execution Timeout Expired`, not yet proven as the cause — nothing was changed.**
Options, none applied: **A** run the two integration projects sequentially (no repo change) · **B** cap xUnit parallelism for the integration projects · **C** give the SQL container CPU/memory limits · **D** capture full output + `iostat`/`docker stats` on the next red run · **E** raise the SqlClient command timeout (masks the symptom, not recommended).
