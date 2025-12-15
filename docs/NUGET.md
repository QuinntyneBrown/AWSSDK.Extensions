# NuGet Package Documentation

This document provides information about creating, versioning, and publishing the AWSSDK.Extensions NuGet package.

## Package Information

- **Package ID**: AWSSDK.Extensions
- **Current Version**: 1.0.0
- **License**: MIT
- **NuGet Repository**: https://www.nuget.org/packages/AWSSDK.Extensions

## Building the Package

The NuGet package is automatically generated when building the project in Release configuration:

```bash
dotnet build --configuration Release
```

The package files will be created in:
- `src/AWSSDK.Extensions/bin/Release/AWSSDK.Extensions.<version>.nupkg`
- `src/AWSSDK.Extensions/bin/Release/AWSSDK.Extensions.<version>.snupkg` (symbols package)

## Version Management

### Versioning Strategy

This project follows [Semantic Versioning](https://semver.org/) (SemVer):

- **MAJOR** version (X.0.0): Incompatible API changes
- **MINOR** version (0.X.0): New functionality in a backward-compatible manner
- **PATCH** version (0.0.X): Backward-compatible bug fixes

### Updating the Version

#### Option 1: Using the Automated Script

The easiest way to increment the version is using the provided script:

```bash
# Increment patch version (1.0.0 -> 1.0.1)
./scripts/increment-version.sh patch

# Increment minor version (1.0.0 -> 1.1.0)
./scripts/increment-version.sh minor

# Increment major version (1.0.0 -> 2.0.0)
./scripts/increment-version.sh major
```

The script will:
- Update `Directory.Build.props` with the new version
- Update `CHANGELOG.md` with a new version section
- Provide next steps for committing and tagging

#### Option 2: Manual Update

1. **Update Directory.Build.props**:
   ```xml
   <VersionPrefix>1.1.0</VersionPrefix>
   ```

2. **Update CHANGELOG.md**:
   - Move changes from `[Unreleased]` section to a new version section
   - Add the release date
   - Update the comparison links at the bottom

3. **Commit the changes**:
   ```bash
   git add Directory.Build.props CHANGELOG.md
   git commit -m "Bump version to 1.1.0"
   git push
   ```

4. **Create a Git tag**:
   ```bash
   git tag -a v1.1.0 -m "Release version 1.1.0"
   git push origin v1.1.0
   ```

5. **Create a GitHub Release**:
   - Go to GitHub repository → Releases → Draft a new release
   - Choose the tag you just created
   - Copy release notes from CHANGELOG.md
   - Publish the release

## Automated Publishing

### GitHub Actions Workflow

The package is automatically published to NuGet.org when a new GitHub release is created:

1. **Configure NuGet API Key** (one-time setup):
   - Generate an API key at https://www.nuget.org/account/apikeys
   - Add it as a repository secret named `NUGET_API_KEY`:
     - Go to GitHub repository → Settings → Secrets and variables → Actions
     - Click "New repository secret"
     - Name: `NUGET_API_KEY`
     - Value: Your NuGet API key

2. **Publish Process**:
   - Create and push a new tag (e.g., `v1.1.0`)
   - Create a GitHub release using that tag
   - The workflow automatically:
     - Builds the project
     - Runs tests
     - Creates the NuGet package
     - Publishes to NuGet.org

### Manual Publishing

To manually publish the package:

```bash
# Build the package
dotnet pack --configuration Release

# Publish to NuGet.org
dotnet nuget push src/AWSSDK.Extensions/bin/Release/AWSSDK.Extensions.*.nupkg \
    --api-key YOUR_API_KEY \
    --source https://api.nuget.org/v3/index.json
```

## Package Contents

The NuGet package includes:

- **Library**: `lib/net9.0/AWSSDK.Extensions.dll`
- **XML Documentation**: `lib/net9.0/AWSSDK.Extensions.xml`
- **Symbols**: Separate symbols package (.snupkg) for debugging
- **Documentation**: README.md, CHANGELOG.md, LICENSE
- **Native Dependencies**: Couchbase Lite native libraries

## Package Metadata

The package metadata is defined in `src/AWSSDK.Extensions/AWSSDK.Extensions.csproj`:

- **Title**: AWSSDK Extensions - Local S3 Implementation
- **Authors**: Quinntyne Brown
- **Description**: A powerful extension library for AWS SDK with local Couchbase Lite S3 implementation
- **Tags**: AWS, S3, Couchbase, Local, Testing, Development, Offline
- **Repository**: GitHub repository URL
- **License**: MIT

## Best Practices

1. **Always update CHANGELOG.md** before releasing
2. **Run all tests** before publishing: `dotnet test`
3. **Test the package locally** before publishing to NuGet.org
4. **Follow semantic versioning** strictly
5. **Include release notes** in GitHub releases
6. **Never delete published packages** - unpublish only if absolutely necessary
7. **Use pre-release versions** for testing: `1.1.0-beta.1`

## Pre-release Versions

To create a pre-release version:

1. Update `Directory.Build.props`:
   ```xml
   <VersionPrefix>1.1.0</VersionPrefix>
   <VersionSuffix>beta.1</VersionSuffix>
   ```

2. This creates version: `1.1.0-beta.1`

3. Pre-release versions are not shown by default in NuGet searches

## Testing the Package Locally

Before publishing, test the package locally:

```bash
# Create a test project
mkdir /tmp/test-package
cd /tmp/test-package
dotnet new console

# Add the local package
dotnet add package AWSSDK.Extensions --source /path/to/AWSSDK.Extensions/src/AWSSDK.Extensions/bin/Release

# Test the functionality
dotnet run
```

## Troubleshooting

### Package not appearing on NuGet.org

- It can take a few minutes for packages to be indexed
- Check the package validation status on NuGet.org

### Version conflicts

- Ensure the version number is unique and not already published
- Use `--skip-duplicate` flag when pushing to avoid errors on republishing

### Build errors

- Ensure all dependencies are restored: `dotnet restore`
- Clean the solution: `dotnet clean`
- Rebuild: `dotnet build --configuration Release`

## Additional Resources

- [NuGet Documentation](https://docs.microsoft.com/en-us/nuget/)
- [Semantic Versioning](https://semver.org/)
- [Keep a Changelog](https://keepachangelog.com/)
- [.NET Pack Command](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-pack)
