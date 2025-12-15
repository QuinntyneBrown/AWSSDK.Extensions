#!/bin/bash
# Version Increment Script for AWSSDK.Extensions
# Usage: ./increment-version.sh [major|minor|patch]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# File paths
PROPS_FILE="Directory.Build.props"
CHANGELOG_FILE="CHANGELOG.md"

# Function to extract current version
get_current_version() {
    # Use sed for better cross-platform compatibility
    sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$PROPS_FILE" | head -1
}

# Function to increment version
increment_version() {
    local version=$1
    local increment_type=$2
    
    IFS='.' read -ra VERSION_PARTS <<< "$version"
    local major=${VERSION_PARTS[0]}
    local minor=${VERSION_PARTS[1]}
    local patch=${VERSION_PARTS[2]}
    
    case $increment_type in
        major)
            major=$((major + 1))
            minor=0
            patch=0
            ;;
        minor)
            minor=$((minor + 1))
            patch=0
            ;;
        patch)
            patch=$((patch + 1))
            ;;
        *)
            echo -e "${RED}Error: Invalid increment type. Use 'major', 'minor', or 'patch'${NC}"
            exit 1
            ;;
    esac
    
    echo "$major.$minor.$patch"
}

# Main script
main() {
    if [ $# -ne 1 ]; then
        echo -e "${RED}Usage: $0 [major|minor|patch]${NC}"
        echo ""
        echo "Examples:"
        echo "  $0 patch  # Increment patch version (1.0.0 -> 1.0.1)"
        echo "  $0 minor  # Increment minor version (1.0.0 -> 1.1.0)"
        echo "  $0 major  # Increment major version (1.0.0 -> 2.0.0)"
        exit 1
    fi
    
    local increment_type=$1
    
    # Check if files exist
    if [ ! -f "$PROPS_FILE" ]; then
        echo -e "${RED}Error: $PROPS_FILE not found${NC}"
        exit 1
    fi
    
    if [ ! -f "$CHANGELOG_FILE" ]; then
        echo -e "${RED}Error: $CHANGELOG_FILE not found${NC}"
        exit 1
    fi
    
    # Get current version
    local current_version=$(get_current_version)
    echo -e "${YELLOW}Current version: $current_version${NC}"
    
    # Calculate new version
    local new_version=$(increment_version "$current_version" "$increment_type")
    echo -e "${GREEN}New version: $new_version${NC}"
    
    # Update Directory.Build.props
    echo "Updating $PROPS_FILE..."
    sed -i "s|<VersionPrefix>$current_version</VersionPrefix>|<VersionPrefix>$new_version</VersionPrefix>|" "$PROPS_FILE"
    
    # Get today's date
    local today=$(date +%Y-%m-%d)
    
    # Update CHANGELOG.md
    echo "Updating $CHANGELOG_FILE..."
    # This is a simplified update - you should manually edit the changelog to add actual changes
    sed -i "s/## \[Unreleased\]/## [Unreleased]\n\n## [$new_version] - $today/" "$CHANGELOG_FILE"
    
    echo -e "${GREEN}Version updated successfully!${NC}"
    echo ""
    echo "Next steps:"
    echo "1. Review and update CHANGELOG.md with actual changes"
    echo "2. Review changes: git diff"
    echo "3. Commit changes: git add $PROPS_FILE $CHANGELOG_FILE && git commit -m 'Bump version to $new_version'"
    echo "4. Create tag: git tag -a v$new_version -m 'Release version $new_version'"
    echo "5. Push changes: git push && git push origin v$new_version"
    echo "6. Create a GitHub release at https://github.com/QuinntyneBrown/AWSSDK.Extensions/releases/new"
}

main "$@"
