# IAmazonS3 Interface - Transaction and Transaction Management Implementation Acceptance Criteria

Comprehensive acceptance criteria for implementing transaction and transaction management related operations in the `IAmazonS3` .NET interface based on AWS SDK for .NET V3 and Amazon S3 API documentation.

> **Note:** Amazon S3 is an object storage service and does not support traditional ACID transactions like relational databases. However, S3 provides several features that enable transaction-like behavior including batch operations, conditional requests (optimistic concurrency), and Object Lock for WORM (Write Once Read Many) compliance.

---

## Table of Contents

1. [Batch Delete Operations (DeleteObjects)](#1-batch-delete-operations-deleteobjects)
2. [Conditional Writes](#2-conditional-writes)
3. [Conditional Reads](#3-conditional-reads)
4. [Conditional Deletes](#4-conditional-deletes)
5. [Object Lock - Retention](#5-object-lock---retention)
6. [Object Lock - Legal Hold](#6-object-lock---legal-hold)
7. [Object Lock Configuration](#7-object-lock-configuration)
8. [Concurrency and Conflict Handling](#8-concurrency-and-conflict-handling)
9. [Error Handling and Partial Failures](#9-error-handling-and-partial-failures)

---

## 1. Batch Delete Operations (DeleteObjects)

### 1.1 DeleteObjectsAsync - Basic Operations

```gherkin
Feature: Batch Delete Multiple Objects
  As an S3 client
  I want to delete multiple objects in a single request
  So that I can efficiently manage object deletion with reduced overhead

  Scenario: Successfully delete multiple objects in a single request
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And the bucket contains objects "file1.txt", "file2.txt", "file3.txt"
    When I call DeleteObjectsAsync with keys ["file1.txt", "file2.txt", "file3.txt"]
    Then the response should have HTTP status code 200
    And the response DeletedObjects collection should contain 3 items
    And each DeletedObject should have the corresponding Key
    And the response Errors collection should be empty

  Scenario: Delete up to 1000 objects in a single request (maximum allowed)
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And the bucket contains 1000 objects
    When I call DeleteObjectsAsync with 1000 object keys
    Then the response should have HTTP status code 200
    And the response should process all 1000 objects

  Scenario: Fail when attempting to delete more than 1000 objects
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    When I call DeleteObjectsAsync with 1001 object keys
    Then the response should throw AmazonS3Exception
    And the error should indicate maximum limit exceeded

  Scenario: Delete non-existent objects returns success (idempotent)
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And no object with key "non-existent.txt" exists
    When I call DeleteObjectsAsync with key "non-existent.txt"
    Then the response should have HTTP status code 200
    And the response DeletedObjects should contain "non-existent.txt"
    And the operation confirms deletion even though object did not exist

  Scenario: Partial success - mixed results with some failures
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And I have permission to delete "file1.txt" but not "file2.txt"
    When I call DeleteObjectsAsync with keys ["file1.txt", "file2.txt"]
    Then the response should have HTTP status code 200
    And the response DeletedObjects should contain "file1.txt"
    And the response Errors should contain an error for "file2.txt"
    And the error Code should be "AccessDenied"
    And the error Message should indicate access denied

  Scenario: Delete objects with Quiet mode enabled
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And the bucket contains objects "file1.txt", "file2.txt", "file3.txt"
    When I call DeleteObjectsAsync with Quiet mode set to true
    Then the response should have HTTP status code 200
    And the response DeletedObjects should be empty (quiet mode)
    And only errors (if any) are returned in the response

  Scenario: Delete objects with Quiet mode - only errors returned
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And I have permission to delete "file1.txt" but not "file2.txt"
    When I call DeleteObjectsAsync with keys ["file1.txt", "file2.txt"] and Quiet mode true
    Then the response should have HTTP status code 200
    And the response DeletedObjects should be empty
    And the response Errors should contain only "file2.txt" error
```

### 1.2 DeleteObjectsAsync - Versioned Buckets

```gherkin
Feature: Batch Delete in Versioned Buckets
  As an S3 client
  I want to delete objects and versions in versioned buckets
  So that I can manage version history efficiently

  Scenario: Delete objects without version ID creates delete markers
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And objects "file1.txt", "file2.txt" exist with versions
    When I call DeleteObjectsAsync with keys ["file1.txt", "file2.txt"] without VersionIds
    Then the response should have HTTP status code 200
    And each DeletedObject should have DeleteMarker set to true
    And each DeletedObject should have a DeleteMarkerVersionId
    And the original object versions should still exist

  Scenario: Delete specific object versions permanently
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1", "v2", "v3"
    When I call DeleteObjectsAsync with key "file.txt" and VersionId "v1"
    Then the response should have HTTP status code 200
    And the response DeletedObject should have Key "file.txt" and VersionId "v1"
    And version "v1" should be permanently deleted
    And versions "v2" and "v3" should still exist

  Scenario: Delete a delete marker makes object reappear
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has a delete marker with VersionId "dm-123"
    And there is a previous version "v1"
    When I call DeleteObjectsAsync with key "file.txt" and VersionId "dm-123"
    Then the response should have HTTP status code 200
    And the response DeletedObject should have DeleteMarker set to true
    And the response DeletedObject should have DeleteMarkerVersionId "dm-123"
    And the delete marker should be removed
    And version "v1" should become the current version

  Scenario: Mixed versioned and non-versioned deletes in single request
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file1.txt" exists with version "v1"
    And object "file2.txt" exists with version "v2"
    When I call DeleteObjectsAsync with:
      | Key       | VersionId |
      | file1.txt | (none)    |
      | file2.txt | v2        |
    Then the response should have HTTP status code 200
    And "file1.txt" should have a delete marker created
    And "file2.txt" version "v2" should be permanently deleted
```

### 1.3 DeleteObjectsAsync - MFA Delete

```gherkin
Feature: Batch Delete with MFA Delete Enabled
  As an S3 bucket owner
  I want to require MFA for versioned deletes
  So that I can prevent accidental permanent deletions

  Scenario: Fail to delete versioned objects without MFA token when MFA Delete enabled
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" has version "v1"
    When I call DeleteObjectsAsync with key "file.txt" and VersionId "v1" without MFA header
    Then the entire request should fail
    And the error code should be "AccessDenied"

  Scenario: Successfully delete versioned objects with valid MFA token
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" has version "v1"
    When I call DeleteObjectsAsync with key "file.txt" and VersionId "v1" with valid MFA header
    Then the response should have HTTP status code 200
    And version "v1" should be permanently deleted

  Scenario: Invalid MFA token fails entire request
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And objects exist with versions
    When I call DeleteObjectsAsync with versioned deletes and invalid MFA token
    Then the entire request should fail
    And no objects should be deleted (even non-versioned deletes)

  Scenario: Non-versioned deletes succeed without MFA
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" exists
    When I call DeleteObjectsAsync with key "file.txt" without VersionId and without MFA header
    Then the response should have HTTP status code 200
    And a delete marker should be created (no MFA required for this)
```

### 1.4 DeleteObjectsAsync - Conditional Deletes

```gherkin
Feature: Batch Delete with Conditional Preconditions
  As an S3 client
  I want to conditionally delete objects based on ETag
  So that I can prevent accidental deletions of modified objects

  Scenario: Conditional delete succeeds when ETag matches
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call DeleteObjectsAsync with key "file.txt" and ETag "abc123"
    Then the response should have HTTP status code 200
    And the object should be deleted

  Scenario: Conditional delete fails when ETag does not match
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call DeleteObjectsAsync with key "file.txt" and ETag "different-etag"
    Then the response should have HTTP status code 200
    And the response Errors should contain "file.txt"
    And the error Code should be "PreconditionFailed"
    And the object should NOT be deleted

  Scenario: Conditional delete with wildcard ETag (object exists check)
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists
    When I call DeleteObjectsAsync with key "file.txt" and ETag "*"
    Then the response should have HTTP status code 200
    And the object should be deleted (exists check passed)

  Scenario: Mixed conditional and non-conditional deletes
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file1.txt" exists with ETag "etag1"
    And object "file2.txt" exists with ETag "etag2"
    When I call DeleteObjectsAsync with:
      | Key       | ETag   |
      | file1.txt | etag1  |
      | file2.txt | wrong  |
    Then the response should have HTTP status code 200
    And "file1.txt" should be deleted (ETag matched)
    And "file2.txt" should have PreconditionFailed error
```

---

## 2. Conditional Writes

### 2.1 PutObjectAsync with If-None-Match

```gherkin
Feature: Conditional Write - Prevent Overwrites
  As an S3 client
  I want to upload objects only if they don't exist
  So that I can prevent accidental overwrites in concurrent scenarios

  Scenario: Successfully upload object when key does not exist
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And no object with key "new-file.txt" exists
    When I call PutObjectAsync with key "new-file.txt" and IfNoneMatch "*"
    Then the response should have HTTP status code 200
    And the object should be created
    And the response should contain ETag and VersionId (if versioned)

  Scenario: Fail to upload when object already exists
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "existing-file.txt" already exists
    When I call PutObjectAsync with key "existing-file.txt" and IfNoneMatch "*"
    Then the response should throw AmazonS3Exception
    And the error code should be "PreconditionFailed"
    And the HTTP status code should be 412
    And the existing object should remain unchanged

  Scenario: Concurrent conditional writes - first write wins
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And no object with key "race-file.txt" exists
    When two concurrent PutObjectAsync requests are made for "race-file.txt" with IfNoneMatch "*"
    Then the first request to complete should succeed
    And the second request should fail with 412 Precondition Failed
    And only one version of the object should exist

  Scenario: Conditional write requires HTTPS or SigV4
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    When I call PutObjectAsync with IfNoneMatch "*" using HTTPS
    Then the conditional write should be processed correctly

  Scenario: Handle 409 Conflict during conditional write
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And a delete request succeeds before the conditional write completes
    When I call PutObjectAsync with IfNoneMatch "*"
    Then the response may return 409 Conflict
    And the client should retry the upload
```

### 2.2 PutObjectAsync with If-Match (ETag validation)

```gherkin
Feature: Conditional Write - Update Only If Unchanged
  As an S3 client
  I want to update objects only if they haven't been modified
  So that I can implement optimistic concurrency control

  Scenario: Successfully update object when ETag matches
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call PutObjectAsync with key "file.txt" and IfMatch "abc123"
    Then the response should have HTTP status code 200
    And the object should be updated
    And the response should contain a new ETag

  Scenario: Fail to update when ETag does not match
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call PutObjectAsync with key "file.txt" and IfMatch "different-etag"
    Then the response should throw AmazonS3Exception
    And the error code should be "PreconditionFailed"
    And the HTTP status code should be 412
    And the existing object should remain unchanged

  Scenario: If-Match with non-existent object fails
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And no object with key "missing.txt" exists
    When I call PutObjectAsync with key "missing.txt" and IfMatch "any-etag"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 412 or 404

  Scenario: If-Match requires s3:PutObject and s3:GetObject permissions
    Given I have valid AWS credentials
    And I have s3:PutObject permission but not s3:GetObject
    When I call PutObjectAsync with IfMatch header
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
```

### 2.3 CompleteMultipartUploadAsync with Conditional Headers

```gherkin
Feature: Conditional Multipart Upload Completion
  As an S3 client
  I want to conditionally complete multipart uploads
  So that I can prevent overwrites during large file uploads

  Scenario: Complete multipart upload with If-None-Match succeeds
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And I have initiated a multipart upload for "large-file.bin"
    And I have uploaded all parts
    And no object with key "large-file.bin" exists
    When I call CompleteMultipartUploadAsync with IfNoneMatch "*"
    Then the response should have HTTP status code 200
    And the object should be created

  Scenario: Complete multipart upload fails when object exists
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And I have initiated a multipart upload for "existing-file.bin"
    And I have uploaded all parts
    And object "existing-file.bin" already exists
    When I call CompleteMultipartUploadAsync with IfNoneMatch "*"
    Then the response should throw AmazonS3Exception
    And the error code should be "PreconditionFailed"
    And the multipart upload must be re-initiated to retry

  Scenario: Concurrent multipart upload during conditional write
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And Client 1 is uploading "file.bin" using multipart upload
    And during the upload, Client 2 successfully writes "file.bin" with conditional write
    When Client 1 calls CompleteMultipartUploadAsync with IfNoneMatch "*"
    Then the response should return 412 Precondition Failed
    And Client 1 must initiate a new multipart upload to retry
```

---

## 3. Conditional Reads

### 3.1 GetObjectAsync with Conditional Headers

```gherkin
Feature: Conditional Read Operations
  As an S3 client
  I want to conditionally retrieve objects
  So that I can optimize bandwidth and avoid unnecessary downloads

  Scenario: Get object with If-Match - ETag matches
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call GetObjectAsync with key "file.txt" and IfMatch "abc123"
    Then the response should have HTTP status code 200
    And the response should contain the object content

  Scenario: Get object with If-Match - ETag does not match
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call GetObjectAsync with key "file.txt" and IfMatch "different-etag"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 412 Precondition Failed

  Scenario: Get object with If-None-Match - ETag matches (not modified)
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call GetObjectAsync with key "file.txt" and IfNoneMatch "abc123"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 304 Not Modified
    And no object content should be returned (bandwidth saved)

  Scenario: Get object with If-None-Match - ETag does not match
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call GetObjectAsync with key "file.txt" and IfNoneMatch "different-etag"
    Then the response should have HTTP status code 200
    And the response should contain the object content

  Scenario: Get object with If-Modified-Since - object was modified
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" was last modified "2025-01-10T12:00:00Z"
    When I call GetObjectAsync with IfModifiedSince "2025-01-09T00:00:00Z"
    Then the response should have HTTP status code 200
    And the response should contain the object content

  Scenario: Get object with If-Modified-Since - object was not modified
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" was last modified "2025-01-01T12:00:00Z"
    When I call GetObjectAsync with IfModifiedSince "2025-01-10T00:00:00Z"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 304 Not Modified

  Scenario: Get object with If-Unmodified-Since - object was not modified
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" was last modified "2025-01-01T12:00:00Z"
    When I call GetObjectAsync with IfUnmodifiedSince "2025-01-10T00:00:00Z"
    Then the response should have HTTP status code 200
    And the response should contain the object content

  Scenario: Get object with If-Unmodified-Since - object was modified
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" was last modified "2025-01-10T12:00:00Z"
    When I call GetObjectAsync with IfUnmodifiedSince "2025-01-05T00:00:00Z"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 412 Precondition Failed

  Scenario: Combined conditional headers - If-None-Match false and If-Modified-Since true
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123" modified "2025-01-10T12:00:00Z"
    When I call GetObjectAsync with IfNoneMatch "abc123" and IfModifiedSince "2025-01-01T00:00:00Z"
    Then the response should have HTTP status code 304 Not Modified
    And S3 returns 304 because If-None-Match takes precedence

  Scenario: Combined conditional headers - If-Match true and If-Unmodified-Since false
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123" modified "2025-01-10T12:00:00Z"
    When I call GetObjectAsync with IfMatch "abc123" and IfUnmodifiedSince "2025-01-01T00:00:00Z"
    Then the response should have HTTP status code 200
    And the object content is returned because If-Match takes precedence
```

---

## 4. Conditional Deletes

### 4.1 DeleteObjectAsync with If-Match

```gherkin
Feature: Conditional Delete Operations
  As an S3 client
  I want to conditionally delete objects
  So that I can prevent accidental deletions of modified objects

  Scenario: Delete object when ETag matches
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call DeleteObjectAsync with key "file.txt" and IfMatch "abc123"
    Then the response should have HTTP status code 204
    And the object should be deleted

  Scenario: Fail to delete when ETag does not match
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When I call DeleteObjectAsync with key "file.txt" and IfMatch "different-etag"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 412 Precondition Failed
    And the object should NOT be deleted

  Scenario: Delete object with wildcard ETag (object exists check)
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists
    When I call DeleteObjectAsync with key "file.txt" and IfMatch "*"
    Then the response should have HTTP status code 204
    And the object should be deleted

  Scenario: Conditional delete with If-Match requires s3:DeleteObject and s3:GetObject
    Given I have valid AWS credentials
    And I have s3:DeleteObject permission but not s3:GetObject
    When I call DeleteObjectAsync with IfMatch header and ETag value
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"

  Scenario: Concurrent delete wins over conditional delete
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And object "file.txt" exists with ETag "abc123"
    When a DELETE request succeeds before a conditional delete completes
    Then the conditional delete may return 409 Conflict or 404 Not Found
    And the object is already deleted
```

---

## 5. Object Lock - Retention

### 5.1 PutObjectRetentionAsync

```gherkin
Feature: Object Lock Retention Management
  As an S3 client
  I want to set retention periods on objects
  So that I can implement WORM (Write Once Read Many) compliance

  Scenario: Set Governance mode retention on an object
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "file.txt" exists
    When I call PutObjectRetentionAsync with:
      | Property        | Value                    |
      | Mode            | GOVERNANCE               |
      | RetainUntilDate | 2026-01-01T00:00:00Z     |
    Then the response should have HTTP status code 200
    And the object should be locked in Governance mode until the specified date

  Scenario: Set Compliance mode retention on an object
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "file.txt" exists
    When I call PutObjectRetentionAsync with:
      | Property        | Value                    |
      | Mode            | COMPLIANCE               |
      | RetainUntilDate | 2026-01-01T00:00:00Z     |
    Then the response should have HTTP status code 200
    And the object should be locked in Compliance mode
    And no user including root can delete the object before retention expires

  Scenario: Extend retention period succeeds
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "file.txt" has Governance retention until "2025-06-01"
    When I call PutObjectRetentionAsync with RetainUntilDate "2025-12-01"
    Then the response should have HTTP status code 200
    And the retention period should be extended

  Scenario: Fail to shorten Compliance mode retention
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "file.txt" has Compliance retention until "2026-01-01"
    When I call PutObjectRetentionAsync with RetainUntilDate "2025-06-01"
    Then the response should throw AmazonS3Exception
    And the error should indicate retention cannot be shortened

  Scenario: Fail to change Compliance mode to Governance
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "file.txt" has Compliance retention
    When I call PutObjectRetentionAsync with Mode "GOVERNANCE"
    Then the response should throw AmazonS3Exception
    And the error should indicate mode cannot be changed from Compliance

  Scenario: Bypass Governance mode with permission
    Given I have valid AWS credentials
    And I have s3:BypassGovernanceRetention permission
    And object "file.txt" has Governance retention
    When I call PutObjectRetentionAsync with empty retention and BypassGovernanceRetention header
    Then the response should have HTTP status code 200
    And the retention should be removed

  Scenario: Fail to bypass Governance mode without permission
    Given I have valid AWS credentials
    And I do NOT have s3:BypassGovernanceRetention permission
    And object "file.txt" has Governance retention
    When I call PutObjectRetentionAsync with shorter retention period
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"

  Scenario: Set retention on object during upload
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    When I call PutObjectAsync with:
      | Property              | Value                |
      | ObjectLockMode        | GOVERNANCE           |
      | ObjectLockRetainUntilDate | 2026-01-01T00:00:00Z |
    Then the response should have HTTP status code 200
    And the object should be created with retention settings
```

### 5.2 GetObjectRetentionAsync

```gherkin
Feature: Get Object Retention Information
  As an S3 client
  I want to retrieve retention settings for objects
  So that I can verify WORM compliance status

  Scenario: Get retention settings for a locked object
    Given I have valid AWS credentials
    And I have s3:GetObjectRetention permission
    And object "file.txt" has Governance retention until "2026-01-01"
    When I call GetObjectRetentionAsync for "file.txt"
    Then the response should have HTTP status code 200
    And the response Mode should be "GOVERNANCE"
    And the response RetainUntilDate should be "2026-01-01T00:00:00Z"

  Scenario: Get retention for specific version
    Given I have valid AWS credentials
    And object "file.txt" version "v1" has Compliance retention
    And object "file.txt" version "v2" has no retention
    When I call GetObjectRetentionAsync for "file.txt" with VersionId "v1"
    Then the response should return the retention for version "v1"

  Scenario: Get retention for object without retention
    Given I have valid AWS credentials
    And object "file.txt" has no retention settings
    When I call GetObjectRetentionAsync for "file.txt"
    Then the response should indicate no retention is set

  Scenario: Fail to get retention without permission
    Given I have valid AWS credentials
    And I do NOT have s3:GetObjectRetention permission
    When I call GetObjectRetentionAsync for any object
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
```

---

## 6. Object Lock - Legal Hold

### 6.1 PutObjectLegalHoldAsync

```gherkin
Feature: Object Lock Legal Hold Management
  As an S3 client
  I want to place legal holds on objects
  So that I can prevent deletion indefinitely until hold is removed

  Scenario: Enable legal hold on an object
    Given I have valid AWS credentials
    And I have s3:PutObjectLegalHold permission
    And I own a bucket "lock-bucket" with Object Lock enabled
    And object "evidence.txt" exists
    When I call PutObjectLegalHoldAsync with Status "ON"
    Then the response should have HTTP status code 200
    And the object should have legal hold enabled
    And the object cannot be deleted until legal hold is removed

  Scenario: Remove legal hold from an object
    Given I have valid AWS credentials
    And I have s3:PutObjectLegalHold permission
    And object "evidence.txt" has legal hold enabled
    When I call PutObjectLegalHoldAsync with Status "OFF"
    Then the response should have HTTP status code 200
    And the object legal hold should be removed
    And the object can be deleted (if no retention period)

  Scenario: Legal hold with retention period - both must be cleared
    Given I have valid AWS credentials
    And object "file.txt" has both legal hold and Governance retention until "2026-01-01"
    When retention period expires on "2026-01-01"
    Then the object still cannot be deleted because legal hold is active
    When I remove the legal hold
    Then the object can be deleted

  Scenario: Legal hold prevents deletion even with BypassGovernanceRetention
    Given I have valid AWS credentials
    And I have s3:BypassGovernanceRetention permission
    And object "file.txt" has Governance retention AND legal hold
    When I try to delete "file.txt" with BypassGovernanceRetention header
    Then the deletion should fail because legal hold is still active

  Scenario: Set legal hold on specific version
    Given I have valid AWS credentials
    And object "file.txt" has versions "v1", "v2"
    When I call PutObjectLegalHoldAsync for "file.txt" with VersionId "v1" and Status "ON"
    Then only version "v1" should have legal hold
    And version "v2" can be deleted

  Scenario: Fail to set legal hold on non-Object-Lock bucket
    Given I have valid AWS credentials
    And I own a bucket "regular-bucket" without Object Lock enabled
    When I call PutObjectLegalHoldAsync for any object
    Then the response should throw AmazonS3Exception
    And the error should indicate Object Lock is not enabled
```

### 6.2 GetObjectLegalHoldAsync

```gherkin
Feature: Get Object Legal Hold Status
  As an S3 client
  I want to check legal hold status
  So that I can verify object protection state

  Scenario: Get legal hold status - hold is enabled
    Given I have valid AWS credentials
    And I have s3:GetObjectLegalHold permission
    And object "evidence.txt" has legal hold enabled
    When I call GetObjectLegalHoldAsync for "evidence.txt"
    Then the response should have HTTP status code 200
    And the response Status should be "ON"

  Scenario: Get legal hold status - hold is not enabled
    Given I have valid AWS credentials
    And object "file.txt" does not have legal hold
    When I call GetObjectLegalHoldAsync for "file.txt"
    Then the response should have HTTP status code 200
    And the response Status should be "OFF"

  Scenario: Fail to get legal hold without permission
    Given I have valid AWS credentials
    And I do NOT have s3:GetObjectLegalHold permission
    When I call GetObjectLegalHoldAsync for any object
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
```

---

## 7. Object Lock Configuration

### 7.1 PutObjectLockConfigurationAsync

```gherkin
Feature: Bucket Object Lock Configuration
  As an S3 bucket owner
  I want to configure default Object Lock settings
  So that all objects inherit retention automatically

  Scenario: Set default retention configuration on bucket
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    When I call PutObjectLockConfigurationAsync with:
      | Property             | Value      |
      | DefaultRetention.Mode | GOVERNANCE |
      | DefaultRetention.Days | 30         |
    Then the response should have HTTP status code 200
    And new objects should automatically have 30-day Governance retention

  Scenario: Set default retention in years
    Given I have valid AWS credentials
    And I own a bucket "lock-bucket" with Object Lock enabled
    When I call PutObjectLockConfigurationAsync with:
      | Property              | Value      |
      | DefaultRetention.Mode | COMPLIANCE |
      | DefaultRetention.Years | 7          |
    Then the response should have HTTP status code 200
    And new objects should have 7-year Compliance retention

  Scenario: Remove default retention configuration
    Given I have valid AWS credentials
    And bucket "lock-bucket" has default retention configured
    When I call PutObjectLockConfigurationAsync with empty retention
    Then the response should have HTTP status code 200
    And new objects will not have automatic retention

  Scenario: Fail to set Object Lock config on bucket without Object Lock
    Given I have valid AWS credentials
    And I own a bucket "regular-bucket" without Object Lock enabled
    When I call PutObjectLockConfigurationAsync
    Then the response should throw AmazonS3Exception
    And the error should indicate Object Lock is not enabled
```

### 7.2 GetObjectLockConfigurationAsync

```gherkin
Feature: Get Bucket Object Lock Configuration
  As an S3 client
  I want to retrieve Object Lock configuration
  So that I can verify default retention settings

  Scenario: Get Object Lock configuration with defaults
    Given I have valid AWS credentials
    And I have s3:GetBucketObjectLockConfiguration permission
    And bucket "lock-bucket" has default Governance retention of 30 days
    When I call GetObjectLockConfigurationAsync for "lock-bucket"
    Then the response should have HTTP status code 200
    And the response ObjectLockEnabled should be "Enabled"
    And the response DefaultRetention.Mode should be "GOVERNANCE"
    And the response DefaultRetention.Days should be 30

  Scenario: Get Object Lock configuration without defaults
    Given I have valid AWS credentials
    And bucket "lock-bucket" has Object Lock enabled but no default retention
    When I call GetObjectLockConfigurationAsync for "lock-bucket"
    Then the response should have HTTP status code 200
    And the response ObjectLockEnabled should be "Enabled"
    And the response DefaultRetention should be null or not present

  Scenario: Fail to get config for non-Object-Lock bucket
    Given I have valid AWS credentials
    And bucket "regular-bucket" does not have Object Lock enabled
    When I call GetObjectLockConfigurationAsync for "regular-bucket"
    Then the response should throw AmazonS3Exception
    And the error code should be "ObjectLockConfigurationNotFoundError"
```

---

## 8. Concurrency and Conflict Handling

### 8.1 Optimistic Concurrency Patterns

```gherkin
Feature: Optimistic Concurrency Control
  As an S3 client
  I want to handle concurrent modifications safely
  So that I can build reliable distributed applications

  Scenario: Read-Modify-Write pattern with ETag validation
    Given I have valid AWS credentials
    And object "config.json" exists with content "v1" and ETag "etag1"
    When Client reads object and gets ETag "etag1"
    And Client modifies content locally
    And Client writes with IfMatch "etag1"
    Then the write should succeed
    And the object should have new content and new ETag

  Scenario: Read-Modify-Write pattern - conflict detected
    Given I have valid AWS credentials
    And object "config.json" exists with ETag "etag1"
    When Client A reads object and gets ETag "etag1"
    And Client B reads object and gets ETag "etag1"
    And Client A writes with IfMatch "etag1" (succeeds, ETag becomes "etag2")
    And Client B writes with IfMatch "etag1" (outdated)
    Then Client B's write should fail with 412 Precondition Failed
    And Client B should re-read, re-modify, and retry

  Scenario: Create-if-not-exists pattern
    Given I have valid AWS credentials
    And no object with key "lock.txt" exists
    When multiple clients simultaneously try to create "lock.txt" with IfNoneMatch "*"
    Then exactly one client should succeed
    And all other clients should receive 412 Precondition Failed
    And the winning client effectively acquires a "lock"

  Scenario: Versioning as alternative concurrency control
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket
    And object "data.txt" exists with version "v1"
    When Client A uploads new content (creates version "v2")
    And Client B uploads new content (creates version "v3")
    Then both uploads succeed (no conflict)
    And both versions exist in the bucket
    And version "v3" is the current version
```

### 8.2 Conflict Response Handling

```gherkin
Feature: Conflict Response Handling
  As an S3 client
  I want to properly handle conflict responses
  So that I can recover gracefully from concurrent operations

  Scenario: Handle 409 Conflict response
    Given I have valid AWS credentials
    And a delete request to "file.txt" succeeds during a conditional write
    When the conditional write completes
    Then the response should be 409 Conflict
    And the client should retry the upload

  Scenario: Handle 412 Precondition Failed response
    Given I have valid AWS credentials
    And object "file.txt" was modified by another client
    When I attempt conditional write with outdated ETag
    Then the response should be 412 Precondition Failed
    And the client should fetch current ETag and retry

  Scenario: Handle 304 Not Modified response
    Given I have valid AWS credentials
    And object "file.txt" has not changed since last download
    When I call GetObjectAsync with IfNoneMatch matching current ETag
    Then the response should be 304 Not Modified
    And the client should use cached version

  Scenario: Handle 404 Not Found during conditional operation
    Given I have valid AWS credentials
    And object "file.txt" was deleted by another client
    When I attempt conditional operation on "file.txt"
    Then the response may be 404 Not Found
    And the client should handle missing object appropriately
```

---

## 9. Error Handling and Partial Failures

### 9.1 Batch Operation Error Handling

```gherkin
Feature: Batch Operation Error Handling
  As an S3 client
  I want to properly handle partial failures in batch operations
  So that I can implement reliable error recovery

  Scenario: Handle partial failure in DeleteObjects
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    And I have permission to delete some but not all objects
    When I call DeleteObjectsAsync with mixed accessible and inaccessible objects
    Then the response should have HTTP status code 200
    And the response should contain both Deleted and Errors collections
    And each error should have:
      | Property | Description                          |
      | Key      | The object key that failed           |
      | Code     | Error code (e.g., AccessDenied)      |
      | Message  | Human-readable error message         |
      | VersionId | Version ID if applicable            |

  Scenario: All items fail in batch delete
    Given I have valid AWS credentials
    And I have no permission to delete any objects
    When I call DeleteObjectsAsync with multiple objects
    Then the response should have HTTP status code 200
    And the response DeletedObjects should be empty
    And the response Errors should contain all objects

  Scenario: Handle malformed XML in batch request
    Given I have valid AWS credentials
    When I send DeleteObjectsAsync with malformed XML
    Then the response should throw AmazonS3Exception
    And the error code should be "MalformedXML"
    And no objects should be deleted

  Scenario: Handle Content-MD5 mismatch
    Given I have valid AWS credentials
    When I send DeleteObjectsAsync with incorrect Content-MD5 header
    Then the response should throw AmazonS3Exception
    And the error code should be "BadDigest" or "InvalidDigest"
    And no objects should be deleted

  Scenario: Retry strategy for transient failures
    Given I have valid AWS credentials
    When a DeleteObjects request fails with 503 Service Unavailable
    Then the client should implement exponential backoff retry
    And the retry should succeed when service recovers
```

### 9.2 Object Lock Error Handling

```gherkin
Feature: Object Lock Error Handling
  As an S3 client
  I want to properly handle Object Lock related errors
  So that I understand why operations are blocked

  Scenario: Attempt to delete object under retention
    Given I have valid AWS credentials
    And object "protected.txt" has Compliance retention until "2026-01-01"
    When I call DeleteObjectAsync for "protected.txt" with VersionId
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the error message should indicate object is locked

  Scenario: Attempt to overwrite object under retention
    Given I have valid AWS credentials
    And object "protected.txt" has Governance retention
    When I call PutObjectAsync for "protected.txt" (same key)
    Then the upload should succeed and create a new version
    And the protected version remains unchanged (versioning required for Object Lock)

  Scenario: Attempt to delete object with legal hold
    Given I have valid AWS credentials
    And object "evidence.txt" has legal hold enabled
    When I call DeleteObjectAsync for "evidence.txt" with VersionId
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the error message should indicate object has legal hold

  Scenario: Object Lock requires versioning
    Given I have valid AWS credentials
    And bucket "lock-bucket" has Object Lock enabled
    When I try to suspend versioning on "lock-bucket"
    Then the response should throw AmazonS3Exception
    And the error should indicate versioning cannot be suspended

  Scenario: Cannot enable Object Lock on existing bucket (legacy)
    Given I have valid AWS credentials
    And bucket "existing-bucket" was created without Object Lock
    When I try to enable Object Lock on "existing-bucket"
    Then the response may succeed (newer feature) or fail
    And Object Lock was traditionally only enabled at bucket creation
```

---

## Summary of Transaction-Like Features

| Feature | Purpose | Atomicity | Isolation |
|---------|---------|-----------|-----------|
| DeleteObjects | Batch delete up to 1000 objects | Partial (per-object) | None |
| Conditional Write (If-None-Match) | Prevent overwrites | Single object | Optimistic |
| Conditional Write (If-Match) | Update only if unchanged | Single object | Optimistic |
| Conditional Read | Return only if modified | Single object | N/A |
| Conditional Delete | Delete only if unchanged | Single object | Optimistic |
| Object Lock Retention | WORM compliance | Single object | Enforced |
| Object Lock Legal Hold | Indefinite protection | Single object | Enforced |
| Versioning | Maintain history | Per version | None |

---

## Key Behaviors Summary

| Scenario | Response Code | Behavior |
|----------|---------------|----------|
| Conditional write, key exists (If-None-Match) | 412 | Precondition Failed |
| Conditional write, ETag mismatch (If-Match) | 412 | Precondition Failed |
| Conditional read, ETag matches (If-None-Match) | 304 | Not Modified |
| Conditional read, not modified since (If-Modified-Since) | 304 | Not Modified |
| Conditional delete, ETag mismatch | 412 | Precondition Failed |
| Concurrent conflict during conditional write | 409 | Conflict |
| Delete locked object (retention/legal hold) | 403 | Access Denied |
| Batch delete partial failure | 200 | Mixed Deleted/Errors |

---

## References

- [AWS SDK for .NET V3 - IAmazonS3 Interface](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/TIS3.html)
- [DeleteObjects API](https://docs.aws.amazon.com/AmazonS3/latest/API/API_DeleteObjects.html)
- [Conditional Requests](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-requests.html)
- [Conditional Writes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-writes.html)
- [Conditional Deletes](https://docs.aws.amazon.com/AmazonS3/latest/userguide/conditional-deletes.html)
- [Object Lock](https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-lock.html)
- [Object Lock Configuration](https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-lock-configure.html)
- [S3 API Error Responses](https://docs.aws.amazon.com/AmazonS3/latest/API/ErrorResponses.html)
