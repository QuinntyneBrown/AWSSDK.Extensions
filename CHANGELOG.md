# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2024-12-15

### Added
- Initial release of AWSSDK.Extensions
- CouchbaseS3Client: Local Couchbase Lite implementation of Amazon S3 interfaces
- CouchbaseS3ClientWithTransactions: Enhanced version with transactional support
- Bucket operations: Create, delete, and list buckets
- Object operations: Put, get, delete, and list objects
- Metadata support for storing and retrieving custom metadata
- Prefix-based object listing
- ETag generation using MD5 for object integrity
- Atomic multi-object operations in transactional client
- Copy operations with atomic support
- Comprehensive unit test coverage using NUnit
- CI/CD pipeline with GitHub Actions

### Features
- Offline-first development and testing without AWS infrastructure
- Compatible with IAmazonS3 interface for seamless integration
- Support for .NET 9.0
- Full documentation and usage examples

[Unreleased]: https://github.com/QuinntyneBrown/AWSSDK.Extensions/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/QuinntyneBrown/AWSSDK.Extensions/releases/tag/v1.0.0
