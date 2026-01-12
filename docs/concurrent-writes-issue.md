# Concurrent Writes Issue - Technical Design Document

## Problem Statement

The `PutObjectAsync_ConcurrentWrites_AllSucceedWithSeparateVersions` acceptance test is failing because the current implementation does not properly handle concurrent write operations to the same object key in a versioned bucket.

### Expected Behavior (Per AWS S3 Specification)
When multiple simultaneous PutObject requests are made for the same key in a versioning-enabled bucket:
- All requests should succeed with HTTP 200
- Each request should create a separate, unique version
- No data loss should occur - all versions must be preserved

### Current Behavior
- Only 2-3 versions are created instead of the expected 5
- Some versions are lost due to race conditions

### Root Cause Analysis

The race condition occurs in `PutObjectAsync` (lines 245-303 of `CouchbaseS3Implementation.cs`):

```
Timeline of Race Condition:

Thread A                          Thread B                          Thread C
────────                          ────────                          ────────
Read object (V1)
                                  Read object (V1)
                                                                    Read object (V1)
Archive V1 → version::V1
Save new object (V2)
                                  Archive V1 → version::V1 (OVERWRITES!)
                                  Save new object (V3)
                                                                    Archive V1 → version::V1 (OVERWRITES!)
                                                                    Save new object (V4)

Result: Only V1 archived once, V2/V3 lost, only V4 remains as current
```

The core issue is that the read-archive-write sequence is not atomic:
1. Multiple threads read the same "current" object document
2. Each thread tries to archive the same version to the same `version::` document ID
3. Later archives overwrite earlier ones
4. Intermediate versions are never archived because they're overwritten before the next thread reads them

---

## Design Option 1: Pessimistic Locking with Named Mutex

### Description
Implement a per-key locking mechanism using named mutexes or semaphores to serialize all write operations to the same object key.

### Implementation
```csharp
private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, ...)
{
    var lockKey = $"{request.BucketName}::{request.Key}";
    var semaphore = _keyLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

    await semaphore.WaitAsync(cancellationToken);
    try
    {
        // Existing put logic - now serialized per key
        return await PutObjectInternalAsync(request, cancellationToken);
    }
    finally
    {
        semaphore.Release();
    }
}
```

### Pros
- Simple to implement and understand
- Guarantees correctness - no race conditions possible
- No changes needed to database schema or document structure
- Works with existing Couchbase Lite without special features

### Cons
- Serializes all writes to the same key, reducing throughput
- In-memory locks don't survive process restarts
- Potential for lock contention hotspots on frequently-written keys
- Memory overhead from storing many SemaphoreSlim instances
- Does not work in distributed/multi-process scenarios

### Risks
- **Medium**: Lock cleanup logic needed to prevent memory leaks
- **Low**: Deadlock potential if locks are acquired in wrong order across methods

### Effort
**Low** (1-2 days)
- Add lock dictionary and acquisition logic
- Wrap PutObjectAsync, DeleteObjectAsync, CopyObjectAsync
- Add lock cleanup mechanism (WeakReference or periodic cleanup)

---

## Design Option 2: Optimistic Concurrency with Retry

### Description
Use Couchbase Lite's document revision tracking for optimistic concurrency control. Detect conflicts during save and retry with exponential backoff.

### Implementation
```csharp
public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, ...)
{
    const int maxRetries = 5;
    var baseDelay = TimeSpan.FromMilliseconds(10);

    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            return await Task.Run(() =>
            {
                var objectId = $"object::{request.BucketName}::{request.Key}";
                var existingDoc = _database.GetDocument(objectId);

                if (isVersioningEnabled && existingDoc != null)
                {
                    // Archive with unique version ID based on document sequence
                    var existingVersionId = existingDoc.GetString("versionId")
                        ?? $"{existingDoc.Sequence}-{GenerateVersionId()}";
                    ArchiveVersion(existingDoc, existingVersionId);
                }

                // Create new document - will fail if document was modified
                var doc = existingDoc?.ToMutable() ?? new MutableDocument(objectId);
                // ... set properties ...

                _database.Save(doc, ConcurrencyControl.FailOnConflict);
                return new PutObjectResponse { ... };
            });
        }
        catch (CouchbaseLiteException ex) when (ex.Error == CouchbaseLiteError.Conflict)
        {
            if (attempt == maxRetries - 1) throw;
            await Task.Delay(baseDelay * Math.Pow(2, attempt));
        }
    }
}
```

### Pros
- No blocking - maintains high throughput under low contention
- Uses built-in Couchbase Lite conflict detection
- Stateless - works across process restarts
- Natural fit for database concurrency model

### Cons
- Requires careful handling of archive versioning to avoid duplicates
- Retry storms possible under very high contention
- More complex error handling and testing
- Each retry re-reads document, adding latency

### Risks
- **High**: Archive versioning must use unique IDs to prevent overwrites
- **Medium**: Retry exhaustion under extreme load
- **Low**: Increased latency due to retries

### Effort
**Medium** (3-5 days)
- Implement retry loop with exponential backoff
- Modify version ID generation to include sequence numbers
- Add comprehensive conflict handling tests
- Update DeleteObjectAsync and CopyObjectAsync similarly

---

## Design Option 3: Write-Ahead Log (WAL) with Background Processor

### Description
Instead of directly modifying documents, write operations append to a per-key write-ahead log. A background processor serializes log entries into actual document changes.

### Implementation
```csharp
// Write operation appends to WAL
public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, ...)
{
    var versionId = GenerateVersionId();
    var walEntry = new MutableDocument($"wal::{request.BucketName}::{request.Key}::{versionId}");
    walEntry.SetString("type", "wal_put");
    walEntry.SetString("versionId", versionId);
    walEntry.SetDate("timestamp", DateTimeOffset.UtcNow);
    walEntry.SetBlob("content", ...);
    // ... other properties ...

    _database.Save(walEntry);

    // Trigger async processing
    _walProcessor.Signal(request.BucketName, request.Key);

    return new PutObjectResponse { VersionId = versionId, ... };
}

// Background processor (runs periodically or on signal)
private void ProcessWalEntries(string bucketName, string key)
{
    lock (GetKeyLock(bucketName, key))
    {
        var entries = QueryWalEntries(bucketName, key).OrderBy(e => e.Timestamp);
        foreach (var entry in entries)
        {
            ApplyWalEntry(entry);
            _database.Delete(entry);
        }
    }
}
```

### Pros
- Writes never block - immediate acknowledgment
- Naturally orders operations by timestamp
- Audit trail of all operations
- Can batch multiple operations for efficiency

### Cons
- Eventually consistent - reads may not see latest write immediately
- Additional storage for WAL entries
- Complex background processor with failure handling
- Query operations need to merge WAL with current state

### Risks
- **High**: Read-after-write consistency expectations may not be met
- **High**: WAL processor failure could cause data lag
- **Medium**: Complexity in merging WAL state for queries

### Effort
**High** (1-2 weeks)
- Design and implement WAL schema
- Build background processor with error handling
- Modify all read operations to consider pending WAL entries
- Add monitoring and recovery mechanisms

---

## Design Option 4: Atomic Version Chain with CAS Operations

### Description
Restructure the versioning model to use a linked list of versions with Compare-And-Swap (CAS) operations for atomic updates.

### Implementation
```csharp
// Document structure:
// object::bucket::key -> { currentVersionId: "v3", type: "object", ... }
// version::bucket::key::v3 -> { previousVersionId: "v2", content: ..., ... }
// version::bucket::key::v2 -> { previousVersionId: "v1", content: ..., ... }
// version::bucket::key::v1 -> { previousVersionId: null, content: ..., ... }

public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, ...)
{
    var newVersionId = GenerateVersionId();

    // First, create the new version document (always succeeds, unique ID)
    var versionDoc = new MutableDocument($"version::{request.BucketName}::{request.Key}::{newVersionId}");
    versionDoc.SetString("type", "version");
    versionDoc.SetBlob("content", ...);
    _database.Save(versionDoc);

    // Then, atomically update the head pointer
    while (true)
    {
        var headDoc = _database.GetDocument($"object::{request.BucketName}::{request.Key}");
        var previousVersionId = headDoc?.GetString("currentVersionId");

        // Update our version to point to previous
        var mutableVersion = _database.GetDocument(versionDoc.Id).ToMutable();
        mutableVersion.SetString("previousVersionId", previousVersionId);
        _database.Save(mutableVersion);

        // Try to update head pointer
        var newHead = headDoc?.ToMutable() ?? new MutableDocument($"object::{request.BucketName}::{request.Key}");
        newHead.SetString("currentVersionId", newVersionId);
        newHead.SetString("type", "object");

        try
        {
            _database.Save(newHead, ConcurrencyControl.FailOnConflict);
            break; // Success
        }
        catch (CouchbaseLiteException ex) when (ex.Error == CouchbaseLiteError.Conflict)
        {
            // Another write won - our version is still saved, just retry head update
            continue;
        }
    }

    return new PutObjectResponse { VersionId = newVersionId, ... };
}
```

### Pros
- Lock-free algorithm with guaranteed progress
- All versions are always preserved (written before head update)
- Clean separation between version storage and ordering
- Naturally supports version traversal

### Cons
- Requires schema redesign for version storage
- More complex read logic to follow version chain
- Additional document for head pointer
- GetObject needs to follow chain or maintain index

### Risks
- **High**: Significant refactoring of existing code
- **Medium**: Performance impact of chained reads
- **Low**: Orphaned versions if process crashes between version save and head update

### Effort
**High** (1-2 weeks)
- Redesign document schema
- Implement CAS-based head pointer updates
- Refactor all version-related operations
- Add garbage collection for orphaned versions
- Migration strategy for existing data

---

## Design Option 5: Database Transaction with Batch Operations

### Description
Use Couchbase Lite's batch/transaction support to make the read-archive-write sequence atomic.

### Implementation
```csharp
public async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, ...)
{
    return await Task.Run(() =>
    {
        string versionId = null;
        PutObjectResponse response = null;

        _database.InBatch(() =>
        {
            var objectId = $"object::{request.BucketName}::{request.Key}";
            var existingDoc = _database.GetDocument(objectId);

            if (isVersioningEnabled)
            {
                if (existingDoc != null)
                {
                    // Archive existing version
                    var existingVersionId = existingDoc.GetString("versionId") ?? GenerateVersionId();
                    var versionDocId = $"version::{request.BucketName}::{request.Key}::{existingVersionId}";

                    var versionDoc = new MutableDocument(versionDocId);
                    // ... copy all properties ...
                    _database.Save(versionDoc);
                }

                versionId = GenerateVersionId();
            }

            // Create/update current object
            var doc = new MutableDocument(objectId);
            doc.SetString("versionId", versionId);
            // ... set all properties ...
            _database.Save(doc);

            response = new PutObjectResponse { VersionId = versionId, ... };
        });

        return response;
    });
}
```

### Pros
- Uses native database transaction support
- Atomic read-modify-write semantics
- Minimal code changes to existing logic
- Familiar transaction pattern

### Cons
- Couchbase Lite's `InBatch` provides atomicity but not isolation
- Concurrent batches may still see intermediate states
- Batch scope is entire database, not per-key
- Still requires additional conflict handling

### Risks
- **High**: `InBatch` may not provide required isolation level
- **Medium**: Need to verify Couchbase Lite transaction semantics
- **Low**: Performance impact of larger transaction scope

### Effort
**Low-Medium** (2-3 days)
- Wrap existing logic in InBatch calls
- Test concurrent behavior thoroughly
- Add conflict detection if needed
- Document transaction semantics

---

## Recommendation

### Short-term (Immediate Fix)
**Option 1: Pessimistic Locking** is recommended for immediate implementation because:
- Lowest risk and effort
- Guarantees correctness
- Can be implemented and tested quickly
- Performance impact is acceptable for most use cases

### Long-term (If Performance Becomes Critical)
**Option 4: Atomic Version Chain** provides the best long-term architecture because:
- Lock-free design scales better
- Clean separation of concerns
- Aligns with how S3 versioning conceptually works
- No version data loss possible

### Hybrid Approach
Consider implementing Option 1 immediately, then refactoring to Option 4 if:
- Performance profiling shows lock contention issues
- High-frequency writes to same keys become a pattern
- Distributed deployment is required

---

## Appendix: Test Case Details

```csharp
// Acceptance Criteria 11.1 - Scenario: Concurrent write handling with versioning
[Fact]
public async Task PutObjectAsync_ConcurrentWrites_AllSucceedWithSeparateVersions()
{
    // Arrange: Create versioned bucket
    // Act: 5 concurrent PutObject requests to same key
    // Assert:
    //   - All 5 requests return HTTP 200
    //   - All 5 version IDs are unique
    //   - ListVersions returns exactly 5 versions
}
```

Current failure: `Assert.Equal(5, fileVersions.Count)` - Expected: 5, Actual: 2
