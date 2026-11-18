# AGENTS.md — [Project name]

> Copy this to the root of each project. Keep it short. Only include what an agent cannot reliably discover from the code.

## What this project is
[One or two sentences.]

## Stack
[.NET version, database, key packages, hosting.]

## Commands
```
Build:    dotnet build
Test:     dotnet test
Run:      dotnet run --project src/[Api]
Format:   dotnet format
Migrate:  [command, and who may run it]
```

## Architecture
[Projects and layers, e.g. src/Domain, src/Application, src/Infrastructure, src/Api. Note any deviation from Clean Architecture.]

## Conventions that differ from defaults
- [e.g. Result pattern instead of exceptions for expected failures]
- [e.g. MediatR/CQRS or plain services]

## Gotchas
- [Things that break builds, fragile areas, required environment variables, local setup traps]

## Permissions
- May run: build, tests, formatters.
- Must ask first: migrations against shared databases, package upgrades, commits, pushes, deployments.
