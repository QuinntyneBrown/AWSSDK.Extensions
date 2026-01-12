# IAmazonS3 Interface - Versioning Implementation Acceptance Criteria

Comprehensive acceptance criteria for implementing versioning-related operations in the `IAmazonS3` .NET interface based on AWS SDK for .NET V3 and Amazon S3 API documentation.

---

## Table of Contents

1. [GetBucketVersioning](#1-getbucketversioning)
2. [PutBucketVersioning](#2-putbucketversioning)
3. [ListVersions / ListObjectVersions](#3-listversions--listobjectversions)
4. [GetObject with VersionId](#4-getobject-with-versionid)
5. [GetObjectMetadata with VersionId](#5-getobjectmetadata-with-versionid)
6. [PutObject Versioning Behavior](#6-putobject-versioning-behavior)
7. [DeleteObject Versioning Behavior](#7-deleteobject-versioning-behavior)
8. [Delete Markers](#8-delete-markers)
9. [CopyObject with Versioning](#9-copyobject-with-versioning)
10. [MFA Delete](#10-mfa-delete)
11. [Error Handling](#11-error-handling)

---

## 1. GetBucketVersioning

### 1.1 GetBucketVersioningAsync

```gherkin
Feature: Get Bucket Versioning Configuration
  As an S3 client
  I want to retrieve the versioning state of a bucket
  So that I can understand how objects are being versioned

  Scenario: Get versioning status for a bucket that has never had versioning set
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" that has never had versioning configured
    When I call GetBucketVersioningAsync with bucket name "my-bucket"
    Then the response should have HTTP status code 200
    And the VersioningConfig.Status should be null or empty
    And the MFADelete should be null or not present

  Scenario: Get versioning status for a versioning-enabled bucket
    Given I have valid AWS credentials
    And I own a bucket "versioned-bucket" with versioning enabled
    When I call GetBucketVersioningAsync with bucket name "versioned-bucket"
    Then the response should have HTTP status code 200
    And the VersioningConfig.Status should be "Enabled"

  Scenario: Get versioning status for a versioning-suspended bucket
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    When I call GetBucketVersioningAsync with bucket name "suspended-bucket"
    Then the response should have HTTP status code 200
    And the VersioningConfig.Status should be "Suspended"

  Scenario: Get versioning status for a bucket with MFA Delete enabled
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    When I call GetBucketVersioningAsync with bucket name "mfa-bucket"
    Then the response should have HTTP status code 200
    And the VersioningConfig.Status should be "Enabled"
    And the MFADelete should be "Enabled"

  Scenario: Fail to get versioning status for non-existent bucket
    Given I have valid AWS credentials
    And no bucket named "non-existent-bucket" exists
    When I call GetBucketVersioningAsync with bucket name "non-existent-bucket"
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchBucket"
    And the HTTP status code should be 404

  Scenario: Fail to get versioning status without bucket owner permission
    Given I have valid AWS credentials
    And the bucket "other-owner-bucket" is owned by another account
    And I do not have s3:GetBucketVersioning permission
    When I call GetBucketVersioningAsync with bucket name "other-owner-bucket"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: Get versioning status with ExpectedBucketOwner validation - success
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" with account ID "123456789012"
    When I call GetBucketVersioningAsync with bucket name "my-bucket" and ExpectedBucketOwner "123456789012"
    Then the response should have HTTP status code 200

  Scenario: Get versioning status with ExpectedBucketOwner validation - failure
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" with account ID "123456789012"
    When I call GetBucketVersioningAsync with bucket name "my-bucket" and ExpectedBucketOwner "999999999999"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403
```

---

## 2. PutBucketVersioning

### 2.1 PutBucketVersioningAsync

```gherkin
Feature: Set Bucket Versioning Configuration
  As an S3 client
  I want to enable or suspend versioning on a bucket
  So that I can control how object versions are managed

  Scenario: Enable versioning on a bucket for the first time
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" that has never had versioning configured
    When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled"
    Then the response should have HTTP status code 200
    And subsequent GetBucketVersioningAsync should return Status "Enabled"

  Scenario: Enable versioning on a previously versioning-suspended bucket
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    When I call PutBucketVersioningAsync with bucket name "suspended-bucket" and Status "Enabled"
    Then the response should have HTTP status code 200
    And subsequent GetBucketVersioningAsync should return Status "Enabled"

  Scenario: Suspend versioning on a versioning-enabled bucket
    Given I have valid AWS credentials
    And I own a bucket "versioned-bucket" with versioning enabled
    When I call PutBucketVersioningAsync with bucket name "versioned-bucket" and Status "Suspended"
    Then the response should have HTTP status code 200
    And subsequent GetBucketVersioningAsync should return Status "Suspended"
    And existing object versions should be preserved

  Scenario: Attempt to disable versioning completely (not possible)
    Given I have valid AWS credentials
    And I own a bucket "versioned-bucket" with versioning enabled
    When I attempt to completely disable versioning (return to unversioned state)
    Then it is not possible - the bucket can only be suspended, never fully disabled

  Scenario: Fail to set versioning on non-existent bucket
    Given I have valid AWS credentials
    And no bucket named "non-existent-bucket" exists
    When I call PutBucketVersioningAsync with bucket name "non-existent-bucket" and Status "Enabled"
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchBucket"
    And the HTTP status code should be 404

  Scenario: Fail to set versioning without proper permissions
    Given I have valid AWS credentials
    And I do not have s3:PutBucketVersioning permission on bucket "restricted-bucket"
    When I call PutBucketVersioningAsync with bucket name "restricted-bucket" and Status "Enabled"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: Set versioning with invalid status value
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Invalid"
    Then the response should throw AmazonS3Exception
    And the error code should be "MalformedXML" or "InvalidArgument"
    And the HTTP status code should be 400

  Scenario: Enable versioning with ExpectedBucketOwner validation - success
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" with account ID "123456789012"
    When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled" and ExpectedBucketOwner "123456789012"
    Then the response should have HTTP status code 200

  Scenario: Enable versioning with ExpectedBucketOwner validation - failure
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" with account ID "123456789012"
    When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled" and ExpectedBucketOwner "999999999999"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: Versioning propagation delay after first enable
    Given I have valid AWS credentials
    And I own a bucket "my-bucket" that has never had versioning configured
    When I call PutBucketVersioningAsync with bucket name "my-bucket" and Status "Enabled"
    Then the response should have HTTP status code 200
    And I should wait approximately 15 minutes before performing write operations
    And during propagation period, PUT or DELETE operations may encounter HTTP 404 NoSuchKey errors
```

---

## 3. ListVersions / ListObjectVersions

### 3.1 ListVersionsAsync

```gherkin
Feature: List Object Versions
  As an S3 client
  I want to list all versions of objects in a bucket
  So that I can see the version history

  Scenario: List versions in a versioning-enabled bucket with multiple object versions
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains object "file.txt" with versions "v1", "v2", "v3"
    When I call ListVersionsAsync with bucket name "versioned-bucket"
    Then the response should have HTTP status code 200
    And the response should contain 3 versions for key "file.txt"
    And each version should have a unique VersionId
    And each version should have an IsLatest property
    And only one version should have IsLatest set to true
    And versions should be ordered with the latest first

  Scenario: List versions in a bucket with delete markers
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted-file.txt" has been deleted (has a delete marker as current version)
    When I call ListVersionsAsync with bucket name "versioned-bucket"
    Then the response should contain Versions collection
    And the response should contain DeleteMarkers collection
    And the delete marker for "deleted-file.txt" should have IsLatest set to true
    And the delete marker should have a VersionId
    And the delete marker should not have Size or ETag

  Scenario: List versions with prefix filter
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains objects "folder1/file1.txt", "folder1/file2.txt", "folder2/file3.txt"
    When I call ListVersionsAsync with bucket name "versioned-bucket" and Prefix "folder1/"
    Then the response should only contain versions for keys starting with "folder1/"
    And the response should not contain versions for "folder2/file3.txt"

  Scenario: List versions with delimiter for folder-like structure
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains objects "folder1/file1.txt", "folder1/file2.txt", "folder2/file3.txt"
    When I call ListVersionsAsync with bucket name "versioned-bucket" and Delimiter "/"
    Then the response should contain CommonPrefixes for "folder1/" and "folder2/"
    And the response may contain root-level object versions

  Scenario: List versions with pagination using MaxKeys
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains more than 100 object versions
    When I call ListVersionsAsync with bucket name "versioned-bucket" and MaxKeys 50
    Then the response should contain at most 50 versions
    And the response IsTruncated should be true
    And the response should contain NextKeyMarker
    And the response should contain NextVersionIdMarker

  Scenario: List versions with pagination continuation
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains more than 100 object versions
    And I have the NextKeyMarker and NextVersionIdMarker from a previous response
    When I call ListVersionsAsync with KeyMarker and VersionIdMarker set to the previous values
    Then the response should contain the next page of versions
    And the response should not duplicate versions from the previous page

  Scenario: List versions in a non-versioned bucket
    Given I have valid AWS credentials
    And I own a bucket "non-versioned-bucket" without versioning enabled
    And the bucket contains object "file.txt"
    When I call ListVersionsAsync with bucket name "non-versioned-bucket"
    Then the response should have HTTP status code 200
    And the object "file.txt" should have VersionId of "null"

  Scenario: List versions in a versioning-suspended bucket
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    And the bucket contains objects uploaded before and after suspension
    When I call ListVersionsAsync with bucket name "suspended-bucket"
    Then the response should contain versions with unique VersionIds (uploaded when enabled)
    And the response should contain versions with null VersionId (uploaded when suspended)

  Scenario: List versions for empty bucket
    Given I have valid AWS credentials
    And I own an empty versioning-enabled bucket "empty-bucket"
    When I call ListVersionsAsync with bucket name "empty-bucket"
    Then the response should have HTTP status code 200
    And the Versions collection should be empty
    And the DeleteMarkers collection should be empty

  Scenario: Fail to list versions on non-existent bucket
    Given I have valid AWS credentials
    And no bucket named "non-existent-bucket" exists
    When I call ListVersionsAsync with bucket name "non-existent-bucket"
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchBucket"
    And the HTTP status code should be 404

  Scenario: Fail to list versions without proper permissions
    Given I have valid AWS credentials
    And I do not have s3:ListBucketVersions permission on bucket "restricted-bucket"
    When I call ListVersionsAsync with bucket name "restricted-bucket"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: List versions response structure validation
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And the bucket contains object "file.txt" with multiple versions
    When I call ListVersionsAsync with bucket name "versioned-bucket"
    Then each S3ObjectVersion should contain:
      | Property       | Description                                      |
      | Key            | The object key name                              |
      | VersionId      | Unique version identifier                        |
      | IsLatest       | Boolean indicating if this is the current version|
      | LastModified   | DateTime when version was created                |
      | ETag           | Entity tag of the object                         |
      | Size           | Size in bytes                                    |
      | StorageClass   | Storage class of the object                      |
      | Owner          | Owner information (ID and DisplayName)           |
```

---

## 4. GetObject with VersionId

### 4.1 GetObjectAsync with VersionId

```gherkin
Feature: Get Specific Object Version
  As an S3 client
  I want to retrieve a specific version of an object
  So that I can access historical versions

  Scenario: Get current version of object without specifying VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" (older) and "v2" (current)
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 200
    And the response should contain the content of version "v2"
    And the response header x-amz-version-id should be "v2"

  Scenario: Get specific older version of object by VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" (older) and "v2" (current)
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    Then the response should have HTTP status code 200
    And the response should contain the content of version "v1"
    And the response header x-amz-version-id should be "v1"

  Scenario: Get object when current version is a delete marker (without VersionId)
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted-file.txt" current version is a delete marker
    When I call GetObjectAsync with bucket "versioned-bucket" and key "deleted-file.txt" without VersionId
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchKey"
    And the HTTP status code should be 404
    And the response header x-amz-delete-marker should be "true"

  Scenario: Get specific version when current version is a delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted-file.txt" has version "v1" and current version is a delete marker
    When I call GetObjectAsync with bucket "versioned-bucket" and key "deleted-file.txt" and VersionId "v1"
    Then the response should have HTTP status code 200
    And the response should contain the content of version "v1"
    And the response header x-amz-version-id should be "v1"

  Scenario: Attempt to GET a delete marker by its VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has a delete marker with VersionId "dm-123"
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 405 (Method Not Allowed)
    And the response header x-amz-delete-marker should be "true"

  Scenario: Get object with non-existent VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists with version "v1"
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "non-existent-version"
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchVersion"
    And the HTTP status code should be 404

  Scenario: Get object from non-versioned bucket with VersionId parameter
    Given I have valid AWS credentials
    And I own a bucket "non-versioned-bucket" without versioning enabled
    And object "file.txt" exists in the bucket
    When I call GetObjectAsync with bucket "non-versioned-bucket" and key "file.txt" and VersionId "null"
    Then the response should have HTTP status code 200
    And the response should contain the object content

  Scenario: Get object requires s3:GetObjectVersion permission for specific version
    Given I have valid AWS credentials
    And I have s3:GetObject permission but not s3:GetObjectVersion
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has version "v1"
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: Get current version only requires s3:GetObject permission
    Given I have valid AWS credentials
    And I have s3:GetObject permission but not s3:GetObjectVersion
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists with current version
    When I call GetObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 200
    And the response should contain the object content
```

---

## 5. GetObjectMetadata with VersionId

### 5.1 GetObjectMetadataAsync with VersionId

```gherkin
Feature: Get Object Metadata for Specific Version
  As an S3 client
  I want to retrieve metadata for a specific version of an object
  So that I can inspect version properties without downloading content

  Scenario: Get metadata for current version without VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" and "v2" (current)
    When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 200
    And the response VersionId should be "v2"
    And the response should include ContentLength, ContentType, ETag, and LastModified

  Scenario: Get metadata for specific older version
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" (older) and "v2" (current)
    When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    Then the response should have HTTP status code 200
    And the response VersionId should be "v1"

  Scenario: Get metadata when current version is a delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted-file.txt" current version is a delete marker
    When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "deleted-file.txt" without VersionId
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 404
    And the response header x-amz-delete-marker should be "true"

  Scenario: Get metadata for a delete marker by VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has a delete marker with VersionId "dm-123"
    When I call GetObjectMetadataAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    Then the response should have HTTP status code 200
    And the response header x-amz-delete-marker should be "true"
    And the response should include LastModified

  Scenario: Get metadata with non-existent VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists
    When I call GetObjectMetadataAsync with VersionId "non-existent-version"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 404
```

---

## 6. PutObject Versioning Behavior

### 6.1 PutObjectAsync Versioning Behavior

```gherkin
Feature: Put Object Version Behavior
  As an S3 client
  I want to understand how PutObject behaves with versioning
  So that I can correctly manage object versions

  Scenario: Put object in versioning-enabled bucket creates new version
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" already exists with version "v1"
    When I call PutObjectAsync with bucket "versioned-bucket" and key "file.txt" with new content
    Then the response should have HTTP status code 200
    And the response should contain a new VersionId different from "v1"
    And both version "v1" and the new version should exist
    And the new version should be marked as IsLatest

  Scenario: Put object in versioning-enabled bucket - first upload
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "new-file.txt" does not exist
    When I call PutObjectAsync with bucket "versioned-bucket" and key "new-file.txt"
    Then the response should have HTTP status code 200
    And the response should contain a unique VersionId (not null)
    And the object should be created as the first and current version

  Scenario: Put object in non-versioned bucket has null VersionId
    Given I have valid AWS credentials
    And I own a bucket "non-versioned-bucket" without versioning enabled
    When I call PutObjectAsync with bucket "non-versioned-bucket" and key "file.txt"
    Then the response should have HTTP status code 200
    And the response VersionId should be null or not present
    And subsequent PutObject with same key overwrites the object

  Scenario: Put object in versioning-suspended bucket has null VersionId
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    When I call PutObjectAsync with bucket "suspended-bucket" and key "new-file.txt"
    Then the response should have HTTP status code 200
    And the response VersionId should be null

  Scenario: Put object in versioning-suspended bucket overwrites existing null version
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    And object "file.txt" exists with null VersionId
    When I call PutObjectAsync with bucket "suspended-bucket" and key "file.txt" with new content
    Then the response should have HTTP status code 200
    And the object with null VersionId should be overwritten
    And there should still be only one version with null VersionId

  Scenario: Put object in versioning-suspended bucket does not overwrite versioned objects
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    And object "file.txt" has versions "v1" and "v2" from when versioning was enabled
    When I call PutObjectAsync with bucket "suspended-bucket" and key "file.txt" with new content
    Then the response should have HTTP status code 200
    And a new version with null VersionId should be created as current
    And versions "v1" and "v2" should still exist as noncurrent versions

  Scenario: Verify VersionId format characteristics
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    When I call PutObjectAsync with bucket "versioned-bucket" and key "file.txt"
    Then the response VersionId should be:
      | Characteristic | Description                                    |
      | Encoding       | Unicode, UTF-8 encoded, URL-ready              |
      | Max Length     | No more than 1024 bytes                        |
      | Format         | Opaque string (implementation-specific)        |
      | Uniqueness     | Unique across all versions of the object       |
      | Source         | Generated only by Amazon S3 (cannot be edited) |
```

---

## 7. DeleteObject Versioning Behavior

### 7.1 DeleteObjectAsync Versioning Behavior

```gherkin
Feature: Delete Object Version Behavior
  As an S3 client
  I want to understand how DeleteObject behaves with versioning
  So that I can correctly manage object deletions

  Scenario: Delete object in versioning-enabled bucket creates delete marker (simple delete)
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists with version "v1"
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 204
    And the response should contain a new VersionId for the delete marker
    And the response header x-amz-delete-marker should be "true"
    And version "v1" should still exist as a noncurrent version
    And GetObject without VersionId should return 404 NoSuchKey

  Scenario: Delete specific version permanently removes that version
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" and "v2"
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    Then the response should have HTTP status code 204
    And the response header x-amz-version-id should be "v1"
    And version "v1" should be permanently deleted
    And version "v2" should still exist

  Scenario: Delete the current (latest) version makes previous version current
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has versions "v1" (noncurrent) and "v2" (current)
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v2"
    Then the response should have HTTP status code 204
    And version "v2" should be permanently deleted
    And version "v1" should become the current version

  Scenario: Delete a delete marker permanently removes it
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has a delete marker "dm-123" as current version
    And there is a previous version "v1"
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "dm-123"
    Then the response should have HTTP status code 204
    And the response header x-amz-delete-marker should be "true"
    And the delete marker should be removed
    And version "v1" should become the current version
    And GetObject without VersionId should succeed and return version "v1"

  Scenario: Simple delete when current version is already a delete marker creates another delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" current version is a delete marker "dm-1"
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 204
    And a new delete marker "dm-2" should be created
    And both delete markers should exist in version history

  Scenario: Delete object in non-versioned bucket permanently removes object
    Given I have valid AWS credentials
    And I own a bucket "non-versioned-bucket" without versioning enabled
    And object "file.txt" exists
    When I call DeleteObjectAsync with bucket "non-versioned-bucket" and key "file.txt"
    Then the response should have HTTP status code 204
    And the object "file.txt" should be permanently deleted
    And GetObject should return 404 NoSuchKey

  Scenario: Delete object in versioning-suspended bucket - removes null version and creates delete marker
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    And object "file.txt" exists with null VersionId
    When I call DeleteObjectAsync with bucket "suspended-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 204
    And the null version should be removed
    And a delete marker with null VersionId should be created

  Scenario: Delete object in versioning-suspended bucket - no null version exists
    Given I have valid AWS credentials
    And I own a bucket "suspended-bucket" with versioning suspended
    And object "file.txt" only has versioned objects "v1", "v2" (no null version)
    When I call DeleteObjectAsync with bucket "suspended-bucket" and key "file.txt" without VersionId
    Then the response should have HTTP status code 204
    And a delete marker with null VersionId should be created
    And versions "v1" and "v2" should still exist

  Scenario: Delete object with non-existent VersionId
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists with version "v1"
    When I call DeleteObjectAsync with VersionId "non-existent-version"
    Then the response should have HTTP status code 204
    And no object should be deleted (operation is idempotent)

  Scenario: Delete object requires s3:DeleteObjectVersion permission for versioned delete
    Given I have valid AWS credentials
    And I have s3:DeleteObject permission but not s3:DeleteObjectVersion
    And I own a versioning-enabled bucket "versioned-bucket"
    When I call DeleteObjectAsync with bucket "versioned-bucket" and key "file.txt" and VersionId "v1"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403
```

---

## 8. Delete Markers

### 8.1 Delete Marker Behavior

```gherkin
Feature: Delete Marker Behavior
  As an S3 client
  I want to understand delete marker behavior
  So that I can correctly manage deleted objects

  Scenario: Delete marker properties
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has been deleted creating a delete marker
    When I call ListVersionsAsync with bucket "versioned-bucket"
    Then the delete marker should appear in DeleteMarkers collection
    And the delete marker should have:
      | Property     | Value/Behavior                                  |
      | Key          | Same as the deleted object key                  |
      | VersionId    | Unique identifier                               |
      | IsLatest     | true (if current delete marker)                 |
      | LastModified | Timestamp when delete marker was created        |
      | Owner        | Owner information                               |
    And the delete marker should NOT have:
      | Property     | Reason                                          |
      | ETag         | Delete markers have no data                     |
      | Size         | Delete markers have no data                     |
      | StorageClass | Delete markers have no storage class            |

  Scenario: ListObjects does not return objects with delete marker as current version
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted-file.txt" current version is a delete marker
    And object "active-file.txt" exists without delete marker
    When I call ListObjectsAsync with bucket "versioned-bucket"
    Then the response should contain "active-file.txt"
    And the response should NOT contain "deleted-file.txt"

  Scenario: Expired delete marker (only remaining version)
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has only a delete marker (all other versions deleted)
    Then the delete marker is considered an "expired delete marker"
    And it can be automatically removed by lifecycle configuration with ExpiredObjectDeleteMarker set to true

  Scenario: Multiple delete markers for same object
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has been deleted twice without specifying VersionId
    When I call ListVersionsAsync with bucket "versioned-bucket"
    Then the response should contain two delete markers for "file.txt"
    And each delete marker should have a unique VersionId
    And only the most recent delete marker should have IsLatest set to true
```

---

## 9. CopyObject with Versioning

### 9.1 CopyObjectAsync with Versioning

```gherkin
Feature: Copy Object with Versioning
  As an S3 client
  I want to copy specific versions of objects
  So that I can duplicate historical versions

  Scenario: Copy specific version to new key
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "source.txt" has versions "v1" and "v2"
    When I call CopyObjectAsync from "source.txt" version "v1" to "dest.txt"
    Then the response should have HTTP status code 200
    And "dest.txt" should be created with content from "source.txt" version "v1"
    And "dest.txt" should have a new unique VersionId (if bucket is versioned)

  Scenario: Copy without specifying version copies current version
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "source.txt" has versions "v1" (older) and "v2" (current)
    When I call CopyObjectAsync from "source.txt" without VersionId to "dest.txt"
    Then "dest.txt" should be created with content from "source.txt" version "v2"

  Scenario: Copy creates new version in destination bucket
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "dest.txt" already exists with version "d1"
    When I call CopyObjectAsync from "source.txt" to "dest.txt"
    Then a new version should be created for "dest.txt"
    And version "d1" should become a noncurrent version

  Scenario: Copy from versioning-enabled to non-versioned bucket
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "source-bucket"
    And I own a non-versioned bucket "dest-bucket"
    And object "source.txt" has version "v1"
    When I call CopyObjectAsync from "source-bucket/source.txt" version "v1" to "dest-bucket/dest.txt"
    Then "dest.txt" should be created in "dest-bucket"
    And "dest.txt" should have null VersionId

  Scenario: Copy cannot copy a delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted.txt" current version is a delete marker
    When I call CopyObjectAsync from "deleted.txt" without VersionId
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchKey"
    And the HTTP status code should be 404
```

---

## 10. MFA Delete

### 10.1 MFA Delete Configuration

```gherkin
Feature: MFA Delete
  As an S3 bucket owner
  I want to enable MFA Delete
  So that version deletions require multi-factor authentication

  Scenario: Enable MFA Delete on a bucket
    Given I have valid AWS credentials
    And I am the bucket owner
    And I have a valid MFA device configured
    And I own a versioning-enabled bucket "mfa-bucket"
    When I call PutBucketVersioningAsync with Status "Enabled" and MfaDelete "Enabled" and MFA header
    Then the response should have HTTP status code 200
    And subsequent GetBucketVersioning should show MFADelete "Enabled"

  Scenario: Fail to enable MFA Delete without MFA header
    Given I have valid AWS credentials
    And I am the bucket owner
    And I own a versioning-enabled bucket "my-bucket"
    When I call PutBucketVersioningAsync with MfaDelete "Enabled" without MFA header
    Then the response should throw AmazonS3Exception
    And the error code should be "InvalidRequest" or "AccessDenied"

  Scenario: Fail to enable MFA Delete when not bucket owner
    Given I have valid AWS credentials
    And I am NOT the bucket owner
    And I have PutBucketVersioning permission
    When I call PutBucketVersioningAsync with MfaDelete "Enabled"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"

  Scenario: Delete specific version requires MFA when MFA Delete is enabled
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" has version "v1"
    When I call DeleteObjectAsync with VersionId "v1" without MFA header
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"

  Scenario: Delete specific version succeeds with valid MFA when MFA Delete is enabled
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" has version "v1"
    When I call DeleteObjectAsync with VersionId "v1" with valid MFA header
    Then the response should have HTTP status code 204
    And version "v1" should be permanently deleted

  Scenario: Simple delete (creates delete marker) does not require MFA
    Given I have valid AWS credentials
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    And object "file.txt" exists
    When I call DeleteObjectAsync without VersionId and without MFA header
    Then the response should have HTTP status code 204
    And a delete marker should be created

  Scenario: Change versioning state requires MFA when MFA Delete is enabled
    Given I have valid AWS credentials
    And I am the bucket owner
    And I own a bucket "mfa-bucket" with MFA Delete enabled and versioning enabled
    When I call PutBucketVersioningAsync with Status "Suspended" without MFA header
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"

  Scenario: Disable MFA Delete requires MFA
    Given I have valid AWS credentials
    And I am the bucket owner
    And I own a bucket "mfa-bucket" with MFA Delete enabled
    When I call PutBucketVersioningAsync with MfaDelete "Disabled" without MFA header
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
```

---

## 11. Error Handling

### 11.1 Version-Specific Errors

```gherkin
Feature: Versioning Error Handling
  As an S3 client
  I want proper error responses for versioning operations
  So that I can handle errors appropriately

  Scenario: NoSuchVersion error
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" exists
    When I request a version that does not exist
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchVersion"
    And the HTTP status code should be 404
    And the error message should indicate the version does not exist

  Scenario: MethodNotAllowed when GET on delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has a delete marker with VersionId "dm-123"
    When I call GetObjectAsync with VersionId "dm-123"
    Then the response should throw AmazonS3Exception
    And the HTTP status code should be 405 (Method Not Allowed)
    And the response header x-amz-delete-marker should be "true"

  Scenario: NoSuchKey when object current version is delete marker
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "deleted.txt" current version is a delete marker
    When I call GetObjectAsync without VersionId
    Then the response should throw AmazonS3Exception
    And the error code should be "NoSuchKey"
    And the HTTP status code should be 404
    And the response header x-amz-delete-marker should be "true"

  Scenario: AccessDenied for version operations without permission
    Given I have valid AWS credentials
    And I do not have s3:GetObjectVersion permission
    And object "file.txt" has version "v1"
    When I call GetObjectAsync with VersionId "v1"
    Then the response should throw AmazonS3Exception
    And the error code should be "AccessDenied"
    And the HTTP status code should be 403

  Scenario: InvalidArgument for invalid versioning status
    Given I have valid AWS credentials
    And I own a bucket "my-bucket"
    When I call PutBucketVersioningAsync with invalid Status value
    Then the response should throw AmazonS3Exception
    And the error code should be "InvalidArgument" or "MalformedXML"
    And the HTTP status code should be 400

  Scenario: NotImplemented for directory buckets
    Given I have valid AWS credentials
    And I own a directory bucket "dir-bucket--usw2-az1--x-s3"
    When I call GetBucketVersioningAsync on the directory bucket
    Then the response should throw AmazonS3Exception
    And versioning operations are not supported for directory buckets

  Scenario: Concurrent write handling with versioning
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    When multiple simultaneous PutObject requests are made for the same key
    Then all requests should succeed
    And each request should create a separate version
    And Amazon S3 stores all versions (no data loss)

  Scenario: HTTP 503 errors with many versions
    Given I have valid AWS credentials
    And I own a versioning-enabled bucket "versioned-bucket"
    And object "file.txt" has millions of versions
    When I perform PUT or DELETE operations
    Then I may receive HTTP 503 Service Unavailable errors
    And I should implement exponential backoff retry logic
    And I should consider using lifecycle policies to manage version count
```

---

## Summary of Key Versioning Behaviors

| Operation | Non-Versioned | Versioning Enabled | Versioning Suspended |
|-----------|---------------|--------------------|--------------------|
| PUT Object | Overwrites | Creates new version | Creates/overwrites null version |
| GET Object | Returns object | Returns current version | Returns current version |
| GET Object (VersionId) | Returns null version | Returns specified version | Returns specified version |
| DELETE Object | Permanently deletes | Creates delete marker | Removes null version, creates delete marker |
| DELETE Object (VersionId) | N/A | Permanently deletes version | Permanently deletes version |
| ListVersions | Returns with null VersionId | Returns all versions | Returns all versions |

---

## References

- [AWS SDK for .NET V3 - IAmazonS3 Interface](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/TIS3.html)
- [How S3 Versioning Works](https://docs.aws.amazon.com/AmazonS3/latest/userguide/versioning-workflows.html)
- [Enabling Versioning on Buckets](https://docs.aws.amazon.com/AmazonS3/latest/userguide/manage-versioning-examples.html)
- [Working with Delete Markers](https://docs.aws.amazon.com/AmazonS3/latest/userguide/DeleteMarker.html)
- [Deleting Object Versions](https://docs.aws.amazon.com/AmazonS3/latest/userguide/DeletingObjectVersions.html)
- [Adding Objects to Versioning-Suspended Buckets](https://docs.aws.amazon.com/AmazonS3/latest/userguide/AddingObjectstoVersionSuspendedBuckets.html)
- [S3 API Error Responses](https://docs.aws.amazon.com/AmazonS3/latest/API/ErrorResponses.html)
