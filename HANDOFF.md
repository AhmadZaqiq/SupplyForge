# SupplyForge — Handoff (read this first)

Written: 2026-11-16. Last updated: 2026-11-18. Put this file at `docs/HANDOFF.md` in the project.
If you are an AI picking this work up: read this file fully, then follow section 10. Do not assume any step in section 2 "Pending" has been done. Check the disk.

## 1. What the owner is doing

The owner is building **SupplyForge**, a modular-monolith backend (.NET 10) for the goods lifecycle (supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report), using **OpenCode** as the coding agent, inside **VS Code**, on **Ubuntu**.

Work done so far was **setup, not coding**. The goal was to make OpenCode load the right context on demand:
- a reusable engineering knowledge library (`my-ai-knowledge/`, written by the owner),
- a large PRD (~122 KB, ~30K tokens) that was **split verbatim into 34 section files** so the agent reads only what a task needs,
- a project `AGENTS.md`, an `opencode.json`, and two slash commands that tie it together.

Owner preferences: informal, short and concrete. Code, config and project files stay in English. The library assumes an early-career developer with solid fundamentals: explain trade-offs briefly, skip tutorials.

## 2. Status

| Item | State |
|---|---|
| PRD split into `docs/prd/` (34 files + `INDEX.md`) | Done. Verified: concatenating the parts reproduces the original line by line, and all 817 requirement-ID occurrences are preserved |
| Project `AGENTS.md`, `opencode.json`, `.opencode/commands/{slice,req}.md` | Written and in use. Both `AGENTS.md` files load in current sessions; the slash commands exist but were not exercised in the last session |
| `docs/prd/INDEX.md` (slice → files table, ID prefix → file table, token sizes) | Written by an AI from PRD §11.2. It is a navigation aid, not requirements. Review the slice table |
| Library `my-ai-knowledge/AGENTS.md` patched (2 lines: library root + "open files with Read tool") | Done. The root line now reads `/home/ahmad/SupplyForge/my-ai-knowledge/`; verified no `D:/` remains |
| OpenCode loads the library rules through `instructions` | **Verified** in live sessions (library rules present in session instructions) |
| `ADR/`, `docs/Decision Register.md` | **Present.** ADR-001…006, 008, 009 drafted and **Accepted by the owner 2026-11-16**. Decision Register created. ADR-007 and ADR-010…016 **not drafted yet** (write them when their slice needs them). `adr-review-report.md` reflects the acceptance |
| `SF.png` | **Missing.** The owner will add it later; references stay as they are |
| Application code | **M0 + A0 foundations.** M0-0…M0-5, A0-1 (JWT), A0-2/A0-2b (rate limiting + defect fix), A0-3 (permission catalogue/role bundles, create-user bootstrap, deny-default authorization — 9/9 steps), A0-4 (BRN HTTP matrix, PLT-044 redaction, INT-052 OpenAPI security schemes), A0-5 (CI workflow + README) — all **done**. **A0-6 (ADR-003 ambient session) in progress:** S1 `AmbientDbSession`/`AmbientDbSessionFactory`, S2 session-then-email `NotificationRaiseService`, S3 core test table green (6/6, incl. IDEM-05 race fix) — S3 HTTP round-trip test + S4–S6 remaining. PLT-013 deferred by owner decision (migration not approved). Slices A1…B4 not started. Status detail: `docs/A0-PROGRESS.md` |
| Git | Initial commit on `main`; owner-approved publish to `https://github.com/AhmadZaqiq/SupplyForge` (2026-11-18). The earlier "do not commit for now" instruction is superseded |

Pending, in order: finish A0-6 (S3 HTTP round-trip test → S4 adopt the session in `AuthService.CreateUserAsync` → S5 sweep: build / test ×2 / format / null-byte scan → S6 close-out + remove the throwaway `spike-tx-sql` container) → close the A0 gate evidence → `/slice A1` (ask about OD-03/TD-5 before any costing code; PLT-013 waits on migration approval).

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
│   └─ req.md                     /req INV-014 → grep + read only the section, no edits
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
├─ src/                           Host + Modules (PlatformCore, Integrations) + Shared
├─ tests/                         ArchitectureTests, PlatformCore.UnitTests, PlatformCore.IntegrationTests
├─ docs/
│   ├─ HANDOFF.md                 this file
│   ├─ A0-PROGRESS.md             increment log: status + evidence
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

Inside OpenCode: `/slice A0` … `/slice B4`, `/req <ID>`. Built-ins include `/undo`, `/redo`, `/share`, `/help`. **Do not run `/init`**: it generates an `AGENTS.md` and this project already has a curated one.

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

## 10. How to continue (instructions to the next AI)

1. Reply to the owner briefly. Lead with the result.
2. Do not trust this file over the disk. Run `pwd`, `git status`, `ls`, and the checks in section 9 #1–#2 to learn what is actually done. Ask the owner for the result of #3–#4 if you cannot run them yourself.
3. Fix whatever section 9 shows broken before starting any slice.
4. Then start with `/slice A0` (or follow its steps manually if slash commands are unavailable): read the files listed in `docs/prd/INDEX.md`, propose a plan, **wait for approval**.
5. Follow the project `AGENTS.md` and `my-ai-knowledge/AGENTS.md`. In particular: do only what was asked; never commit, push, deploy or delete data without permission; run build and tests after changing code and never claim a check passed unless you ran it; do not invent APIs, files, requirements or results.
6. Do not read the whole PRD or the whole library. State which files you read before you write code.
7. Do not claim anything about OpenCode behaviour that you did not verify; the official docs are at https://opencode.ai/docs (rules, permissions, commands, ide, skills) and VS Code Linux setup at https://code.visualstudio.com/docs/setup/linux.
8. Optional later improvement, not required: turn the library workflows into OpenCode skills (`SKILL.md` under `.opencode/skills/` or `~/.config/opencode/skills/`) so the agent discovers them by description. Verify the current skills docs first.

### Paste this into another AI or a fresh OpenCode session

```
Read docs/HANDOFF.md fully, then AGENTS.md. Check the disk for what is actually done (section 9 #1-#2).
Tell me what is done, what is broken, and the next step. Do not write code yet.
```
