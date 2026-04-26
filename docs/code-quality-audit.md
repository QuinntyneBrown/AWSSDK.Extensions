# Code Quality Audit

Date: 2026-04-26

Scope: `src/AWSSDK.Extensions`, solution-level build/test configuration, tests, docs/package metadata, and repository hygiene. The playground applications were reviewed only for repository hygiene and build artifact/data issues.

## Executive Summary

The project has useful coverage around S3-compatible behavior, but the current quality baseline is not clean. `dotnet test AWSSDK.Extensions.sln --configuration Release` fails in the SQLite acceptance suite, `dotnet format --verify-no-changes` fails on formatting/analyzer issues, and the public `IAmazonS3` surface still exposes many `NotImplementedException` paths.

The largest maintainability risk is concentration of behavior in two large client classes:

- `src/AWSSDK.Extensions/CouchbaseS3Client.cs`: 5,986 lines.
- `src/AWSSDK.Extensions/SqlLiteS3Client.cs`: 2,602 lines.

These classes mix AWS request handling, storage schema details, versioning rules, object-lock rules, metadata serialization, error mapping, stream handling, and disposal. That makes behavioral fixes risky because many features share mutable storage state and duplicated code paths.

## Verification Results

### `dotnet test AWSSDK.Extensions.sln --configuration Release`

Result: failed.

Observed results:

- `AWSSDK.Extensions.AcceptanceTests`: 172 passed, 11 skipped.
- `AWSSDK.Extensions.Tests`: 117 passed.
- `AWSSDK.Extensions.SqlLite.AcceptanceTests`: 173 passed, 2 skipped, 5 failed.

SQLite failures:

- `DeleteMarkerBehaviorAcceptanceTests.DeleteMarker_MultipleDeleteMarkers_OnlyMostRecentIsLatest`: two delete markers are reported as latest.
- `ConditionalReadsAcceptanceTests.GetObjectAsync_ReturnsLastModifiedTimestamp`: returned timestamp is outside the expected UTC range.
- `ConditionalDeletesAcceptanceTests.DeleteObjectAsync_DeleteMarker_RemovingItRestoresObject`: deleting the delete marker does not restore normal object access.
- `DeleteMarkerBehaviorAcceptanceTests.DeleteMarker_HasCorrectLastModifiedTimestamp`: delete marker timestamp is outside the expected UTC range.
- `VersioningErrorHandlingAcceptanceTests.PutObjectAsync_ConcurrentWrites_AllSucceedWithSeparateVersions`: only 2 versions were listed after 5 concurrent writes.

Other build observations:

- NuGet warning `NU1603`: requested `AWSSDK.S3` version `3.7.403.10` was not found; `3.7.404` was resolved.
- Many Couchbase Lite APIs used by `CouchbaseS3Client` are obsolete.
- `GeneratePackageOnBuild` causes package creation during test/build verification and produces packaging warnings for Couchbase native DLL content.

### `dotnet format AWSSDK.Extensions.sln --verify-no-changes --verbosity minimal`

Result: failed.

Observed issues:

- Whitespace formatting failures in `CouchbaseS3Client.cs` and `CouchbaseS3ClientTests.cs`.
- xUnit analyzer warning `xUnit2031` in acceptance tests using `Where(...)` before `Assert.Single(...)`.

## High Priority Findings

### 1. SQLite version/delete-marker behavior is not reliable

Evidence:

- Failing SQLite acceptance tests listed above.
- `SqlLiteS3Client.CreateDeleteMarker` inserts a new marker with `is_latest = 1` without first clearing prior latest markers for the same bucket/key.
- `DeleteObjectAsync` removes a delete marker by version ID, but the current object is not restored when that marker was hiding an archived version.
- Concurrent writes to the same key lose versions under load.

Suggested improvements:

- Add a single version-state transition component for `PutObject`, `DeleteObject`, `DeleteObjects`, and `CopyObject`.
- Use SQLite transactions for multi-step versioning changes: archive current object, clear previous latest flags, insert marker/version, and update current object should commit atomically.
- Add constraints/indexes that make invalid state hard to persist, for example one latest delete marker per bucket/key.
- When deleting a latest delete marker, promote the newest non-delete version back to visible current-object state or mark it latest consistently.
- Add a focused regression test file for version-state invariants: exactly one latest item per key, marker deletion restores access, repeated deletes produce one latest marker, and concurrent writes retain all versions.

### 2. The public `IAmazonS3` surface is only partially safe to call

Evidence:

- `CouchbaseS3Client` has many `NotImplementedException` members, including `Paginators`.
- `SqlLiteS3Client` throws `NotImplementedException` from `Config`, `Paginators`, multipart APIs, and many bucket/object APIs.

Suggested improvements:

- Publish an explicit support matrix for both clients and make docs match it.
- Prefer `NotSupportedException` with operation-specific messages for unsupported S3 features.
- Add reflection-based tests that enumerate `IAmazonS3` members and assert each member is either implemented, documented as unsupported, or intentionally excluded by a compatibility policy.
- Consider smaller capability interfaces for local-storage scenarios instead of presenting the entire `IAmazonS3` surface as production-equivalent.

### 3. Async APIs wrap synchronous work in `Task.Run`

Evidence:

- `CouchbaseS3Client` uses `Task.Run` around most operations.
- `SqlLiteS3Client` uses `Task.Run` around synchronous SQLite operations and synchronous stream copies.

Risks:

- Cancellation tokens usually only cancel task scheduling, not the storage operation already in progress.
- Blocking storage work is shifted to the thread pool instead of being made truly asynchronous.
- Shared mutable state is accessed concurrently without an explicit concurrency model.

Suggested improvements:

- Choose a clear async strategy: either synchronous internals returning completed tasks for local in-process storage, or true async database/stream APIs where available.
- For SQLite, use per-operation connections or a dedicated write gate plus explicit transactions.
- Pass cancellation tokens into long-running loops and stream operations.
- Avoid `Task.Run(async () => ...)`; it complicates exception and cancellation behavior without solving storage concurrency.

### 4. The core clients are too large and duplicated

Evidence:

- `CouchbaseS3Client.cs` is 5,986 lines.
- `SqlLiteS3Client.cs` is 2,602 lines.
- Error mapping, metadata serialization, versioning, object-lock behavior, and list/copy/delete logic are duplicated across storage implementations.

Suggested improvements:

- Split the public client facade from domain behavior and persistence.
- Introduce shared services/helpers for:
  - S3 exception creation and status/error-code mapping.
  - Metadata serialization/deserialization.
  - ETag/content hashing.
  - Bucket versioning and object-lock rules.
  - Version/delete-marker state transitions.
- Keep Couchbase and SQLite code focused on persistence adapters.
- Move tests toward shared behavior-contract tests that can run against both storage backends.

### 5. Quality gates are too permissive

Evidence:

- `Directory.Build.props` sets `TreatWarningsAsErrors` to `false`.
- Product and acceptance projects suppress broad warning sets with `NoWarn`.
- The test/build output contains obsolete API warnings, NuGet resolution warnings, analyzer warnings, and package warnings.

Suggested improvements:

- Remove malformed duplicate commas from `NoWarn` values.
- Replace broad warning suppression with narrow, justified suppressions.
- Enable .NET analyzers and enforce warnings as errors for new code first.
- Add CI steps for `dotnet format --verify-no-changes` and tests without package generation side effects.
- Move package creation out of normal build/test by disabling `GeneratePackageOnBuild` and using an explicit `dotnet pack` release step.

## Medium Priority Findings

### Tests contain simulated success/failure paths

Some acceptance tests throw `AmazonS3Exception` inside the test body instead of invoking the client behavior. That can make acceptance criteria appear covered while the implementation remains incomplete.

Suggested improvements:

- Remove simulated exceptions from acceptance tests.
- Use real request headers and real client calls for conditional read/write/delete behavior.
- Keep skipped tests, but track each skip against a work item and review skips in CI output.

### Package and documentation metadata drift

Evidence:

- `README.md` and `docs/NUGET.md` refer to package ID `AWSSDK.Extensions`.
- `src/AWSSDK.Extensions/AWSSDK.Extensions.csproj` uses `PackageId` `Quinntyne.AWSSDK.Extensions`.
- The README project structure references an older `CouchbaseS3Implementation.cs` name.
- `user-guide/couchbase-s3-client-guide.md` links to `github.com/anthropics/AWSSDK.Extensions`.

Suggested improvements:

- Choose the canonical package ID and update README, badges, NuGet docs, and publish commands.
- Update project structure docs to include both `CouchbaseS3Client` and `SqlLiteS3Client`.
- Correct stale repository links.

### Runtime data is tracked under playground

Evidence:

- `git ls-files` shows tracked Couchbase Lite/SQLite data under `Playground/Enterprise/src/FileStorage/data/...`, including `db.sqlite3`, WAL/SHM files, and blob attachments.

Suggested improvements:

- Remove generated runtime data from source control unless it is an intentional fixture.
- Add explicit ignores for playground runtime data such as `playground/**/data/`, `*.cblite2/`, `*.sqlite3`, `*.sqlite3-shm`, and `*.sqlite3-wal`.
- If sample data is required, move it under a named fixtures directory with a README explaining how it is regenerated.

### Timestamp handling should use round-trip UTC semantics

Evidence:

- SQLite writes timestamps with `DateTimeOffset.UtcNow.ToString("o")`.
- SQLite reads timestamps with `DateTime.Parse(...)`.
- Timestamp-based tests currently fail in the SQLite suite.

Suggested improvements:

- Read persisted timestamps with `DateTimeOffset.Parse(..., CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)` and convert explicitly to UTC.
- Standardize response `DateTime` kind and precision across both clients.
- Add timezone-independent timestamp tests.

### Naming should be corrected carefully

`SqlLiteS3Client` is misspelled. The correct spelling is `SQLite`. Renaming a public type is breaking, so introduce `SQLiteS3Client` as the preferred type and keep `SqlLiteS3Client` as an obsolete compatibility alias until the next major version.

## Lower Priority Findings

- Request validation is inconsistent. Many methods assume non-null `request`, `BucketName`, and `Key`; add guard clauses that return AWS-like exceptions where appropriate.
- Local object operations load full object content into memory for hashing and storage. This is acceptable for small test objects, but it should be documented or replaced with streaming/chunked handling for larger local workloads.
- Test frameworks are mixed: NUnit for `AWSSDK.Extensions.Tests`, xUnit for acceptance tests. That is workable, but shared fixtures and naming conventions would reduce duplicate setup and cleanup code.
- Cleanup code swallows exceptions in tests. Prefer logging cleanup failures or using framework temp-directory helpers so intermittent file-locking issues are visible.

## Suggested Improvement Plan

1. Make verification green.
   - Fix the 5 SQLite acceptance failures.
   - Stabilize concurrent writes with transactions or a write gate.
   - Fix timestamp parsing/UTC handling.

2. Define and enforce the public contract.
   - Create a support matrix for Couchbase and SQLite clients.
   - Replace undocumented `NotImplementedException` paths with documented unsupported behavior or full implementations.
   - Add reflection tests over `IAmazonS3`.

3. Reduce implementation risk.
   - Extract shared S3 behavior from persistence code.
   - Centralize exception mapping and version-state transitions.
   - Add backend-agnostic behavior tests for both clients.

4. Raise quality gates.
   - Fix formatting.
   - Reduce warning suppressions.
   - Update obsolete Couchbase APIs.
   - Move package generation to release-only `dotnet pack`.

5. Clean repository and docs.
   - Remove tracked runtime database/blob files.
   - Align package ID, badges, guides, and release docs.
   - Add playground runtime-data ignore rules.

## Recommended Near-Term PRs

1. SQLite version-state fix:
   - Transactional `PutObject`/`DeleteObject`/`DeleteObjects`.
   - Clear old latest markers before adding a new one.
   - Restore visible object after deleting the latest delete marker.

2. Verification cleanup:
   - Fix timestamp parsing.
   - Fix `dotnet format --verify-no-changes`.
   - Update `AWSSDK.S3` package reference to an available version.

3. Contract/documentation cleanup:
   - Add a generated `IAmazonS3` support matrix.
   - Update README/NuGet docs for the actual package ID and SQLite client.
   - Remove or rewrite simulated acceptance tests.
