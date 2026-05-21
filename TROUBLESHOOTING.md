# Troubleshooting Vidly Builds, Tests & CI

This document captures the recurring sharp edges of the Vidly codebase. If your
build is failing in a way that looks weird, **read this first** — chances are
the root cause is one of the dual-project-layout footguns below, not your code.

> **TL;DR**: `dotnet build Vidly.sln` will fail on every machine that doesn't
> have Visual Studio (or the VS Build Tools) installed. The main project is a
> legacy ASP.NET MVC 5 (`.NET Framework 4.7.2`) `.csproj` that requires
> MSBuild + the WebApplication targets. **Always build via
> `Vidly.Tests/Vidly.Tests.csproj`**, which is SDK-style and pulls in every
> source file from the main project.

---

## Table of Contents

- [Project Layout Recap](#project-layout-recap)
- [Symptom Guide](#symptom-guide)
  - [1. `MSB4019: Microsoft.WebApplication.targets was not found`](#1-msb4019-microsoftwebapplicationtargets-was-not-found)
  - [2. `CS0101` / `CS0104`: duplicate or ambiguous type definitions](#2-cs0101--cs0104-duplicate-or-ambiguous-type-definitions)
  - [3. `CS0117`: `Genre` / `Movie` does not contain a definition for X](#3-cs0117-genre--movie-does-not-contain-a-definition-for-x)
  - [4. `CS1737`: default parameter value must come at the end](#4-cs1737-default-parameter-value-must-come-at-the-end)
  - [5. xUnit and MSTest both in one assembly](#5-xunit-and-mstest-both-in-one-assembly)
  - [6. `dotnet format` fails on `Vidly.sln`](#6-dotnet-format-fails-on-vidlysln)
  - [7. CI is red on `master` but my local build looks fine](#7-ci-is-red-on-master-but-my-local-build-looks-fine)
- [Pre-Push Verification Checklist](#pre-push-verification-checklist)
- [Tracked Build-Health Issues](#tracked-build-health-issues)

---

## Project Layout Recap

Vidly is **two coexisting csproj styles** living in one repo:

| Project | Style | Target | Built by |
|---|---|---|---|
| `Vidly/Vidly.csproj` | Legacy non-SDK MSBuild | `.NET Framework 4.7.2` (ASP.NET MVC 5) | **MSBuild only** (needs `Microsoft.WebApplication.targets`) |
| `Vidly.Tests/Vidly.Tests.csproj` | SDK-style (`Microsoft.NET.Sdk`) | `net472` | `dotnet build` |

The test csproj uses `<Compile Include="..\Vidly\**\*.cs" Link="…" />` to pull
*every* source file from the main project into the test assembly. That means:

- All source-level compile errors in the main project surface as test-project
  errors.
- You can build/test the entire codebase with `dotnet`, without installing
  Visual Studio — **as long as you target `Vidly.Tests.csproj`, not the
  solution.**
- Conversely, packaging the deployable web app (NuGet/Docker) still needs
  MSBuild, because only it understands `<ProjectTypeGuids>` and the
  WebApplication targets import.

---

## Symptom Guide

### 1. `MSB4019: Microsoft.WebApplication.targets was not found`

```
error MSB4019: The imported project
  "…\sdk\10.0.300\Microsoft\VisualStudio\v18.0\WebApplications\Microsoft.WebApplication.targets"
  was not found.
```

**Cause.** You ran `dotnet build Vidly.sln` (or `dotnet build Vidly/Vidly.csproj`)
on a machine without Visual Studio. The legacy csproj does
`<Import Project="$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets" />`,
which only resolves under MSBuild from VS / VS Build Tools.

**Fix.** Build the test project instead — it compiles every source file from
the main project:

```bash
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet build   Vidly.Tests/Vidly.Tests.csproj -c Release
dotnet test    Vidly.Tests/Vidly.Tests.csproj -c Release --no-build
```

If you genuinely need the *deployable web app*, use MSBuild from VS Build
Tools:

```pwsh
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
  -latest -requires Microsoft.Component.MSBuild `
  -find MSBuild\**\Bin\MSBuild.exe
& $msbuild Vidly\Vidly.csproj /p:Configuration=Release /p:DeployOnBuild=true
```

> **Do not "fix" this by adding `<PropertyGroup><VSToolsPath>…</VSToolsPath>`
> with a hard-coded path** — it only papers over the symptom and breaks CI.

### 2. `CS0101` / `CS0104`: duplicate or ambiguous type definitions

```
error CS0101: The namespace 'Vidly.Services' already contains a definition for 'X'
error CS0104: 'InterventionType' is an ambiguous reference between
              'Vidly.Models.InterventionType' and 'Vidly.Services.InterventionType'
```

**Cause.** The test project includes *every* `.cs` file under
`Vidly/Controllers`, `Vidly/Services`, `Vidly/Models`, `Vidly/Repositories`,
`Vidly/ViewModels`, `Vidly/Filters`, and `Vidly/Utilities`. If you defined a
helper type (a stub repo, a small enum, a request DTO) in a test fixture **and**
in the main project — or in two main-project files — the duplicate definitions
collide in the test assembly even though they don't collide in the web app.

**Fix.**

- For enums duplicated across `Vidly.Models` and `Vidly.Services`: pick one
  canonical home. The convention is **domain-shared enums live in `Models/`**;
  service-internal enums should be `internal` and live in the service file.
- For test-helper repository stubs (`FakeMovieRepository`, etc.): put them in
  the test project under a unique namespace, e.g.
  `Vidly.Tests.Fakes`, and **never** name them the same as a main-project type.
- Pre-push: run `dotnet build Vidly.Tests/Vidly.Tests.csproj --warnaserror`.
  The CI does this with `--warnaserror`; reproducing it locally avoids the
  surprise.

This pattern bit us hard in 2026 — see #159 and the chain of `fix(CS0101)`
commits. CONTRIBUTING.md has a dedicated "test-build namespace collisions"
section that's worth re-reading before adding a new service.

### 3. `CS0117`: `Genre` / `Movie` does not contain a definition for X

```
error CS0117: 'Genre' does not contain a definition for 'Name'
error CS0117: 'Movie' does not contain a definition for 'NumberInStock'
```

**Cause.** Tests are pointing at a property that was renamed or removed in the
main project. Because the test project consumes source files directly (not a
compiled DLL), there's no API-shim layer to catch these — the *test* file
becomes the authority on what shape the model has.

**Fix.** Always grep the main project for the property name before writing or
modifying a test:

```bash
grep -r "public.*NumberInStock" Vidly/Models/
```

If you renamed `Genre.Name` → `Genre.GenreName`, you must update every test
file that touches it in the same PR. The test-source-link arrangement
means there's no compatibility window.

### 4. `CS1737`: default parameter value must come at the end

```
error CS1737: Optional parameters must appear after all required parameters
```

**Cause.** A service constructor / method added a required parameter
(typically `IClock clock`) *before* an existing optional parameter. The
existing call sites (and tests) used the implicit defaults; the new required
param invalidated them.

**Fix.** Make the new dependency optional with a sensible default, e.g.
`IClock clock = null` and `clock ??= SystemClock.Instance;` in the body. This
is the same pattern used in `0a2a8ca fix(services): resolve CS1737 by making
IClock parameter optional`.

### 5. xUnit and MSTest both in one assembly

`Vidly.Tests.csproj` references both `MSTest.TestAdapter` and `xunit` because
~4 test files (`AnomalyWatchdog`, `CatalogVelocity`, `CulturalMoment`,
`ProcurementAdvisor`) were authored with `[Fact]`/`[Theory]` and the rest use
`[TestClass]`/`[TestMethod]`. They coexist fine **as long as no single file
imports both `Microsoft.VisualStudio.TestTools.UnitTesting` and `Xunit`** —
that would collide on the `Assert` symbol.

**Convention for new tests.** Use **MSTest**. The xUnit files are legacy and
will be migrated as we touch them.

### 6. `dotnet format` fails on `Vidly.sln`

CI's lint step runs `dotnet format Vidly.sln --verify-no-changes`. On machines
without VS Build Tools this dies inside the legacy csproj for the same reason
as #1. Either install VS Build Tools, or run formatting against the test
project only:

```bash
dotnet format Vidly.Tests/Vidly.Tests.csproj --verify-no-changes --verbosity diagnostic
```

(That covers every `.cs` file via the linked-compile globs.)

### 7. CI is red on `master` but my local build looks fine

Cross-check these in order:

1. The CI workflow may be running against a newer .NET SDK than your local
   one. The hosted `windows-latest` image has shipped SDK 10.x, and
   `dotnet build Vidly.sln` against that SDK trips #1 above.
2. Your local build might be MSBuild + VS, masking errors that only show up
   with `dotnet build` (e.g. nullable warnings, analyzer differences).
3. You may have a packages-folder restore that CI can't reproduce. Delete
   `bin/`, `obj/`, and `packages/`; restore from clean.
4. Check #159 for the current known-red test suite errors before opening a
   new issue.

---

## Pre-Push Verification Checklist

Whether you're a human or an agent, run these **before** pushing:

```bash
# 1. Restore + build the test project (which builds all source)
dotnet restore Vidly.Tests/Vidly.Tests.csproj
dotnet build   Vidly.Tests/Vidly.Tests.csproj -c Release --warnaserror

# 2. Run the tests
dotnet test    Vidly.Tests/Vidly.Tests.csproj -c Release --no-build

# 3. Verify formatting matches CI's lint step
dotnet format  Vidly.Tests/Vidly.Tests.csproj --verify-no-changes --verbosity diagnostic
```

If you can't run these locally (e.g. on Linux without `mono`/`net472`
support), say so explicitly in the commit body — don't claim "tests pass"
when you haven't run them.

---

## Tracked Build-Health Issues

- **#159** — `Vidly.Tests` does not compile (xUnit packages, stale stub repos,
  `CS1737`). This is the umbrella issue for the current test-suite breakage.
  Sub-PRs should reference it and chip away at the buckets.
- **CI workflow** (`.github/workflows/ci.yml`) — uses `dotnet build Vidly.sln`,
  which fails immediately on `windows-latest` runners since SDK 10.x removed
  the `v18.0` WebApplication targets shim. Tracked as the recurring CI failure
  streak; the fix is to switch the workflow to build/test/format
  `Vidly.Tests/Vidly.Tests.csproj` (see Symptom 1 + Symptom 6 above).

---

If you hit a build sharp edge that isn't here, **please add it** — that's how
the next contributor avoids the half-day rabbit hole you just escaped from.
