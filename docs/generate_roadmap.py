#!/usr/bin/env python3
"""
Generate S3 Couchbase Implementation Roadmap as PNG
"""

from PIL import Image, ImageDraw, ImageFont
import os

# Configuration
WIDTH = 1600
HEIGHT = 1200
BACKGROUND_COLOR = (255, 255, 255)
TITLE_COLOR = (26, 26, 46)
SUBTITLE_COLOR = (102, 102, 102)

# Phase colors (fill, border)
PHASE_COLORS = {
    'completed': ((213, 232, 212), (130, 179, 102)),
    'high': ((255, 242, 204), (214, 182, 86)),
    'medium_blue': ((218, 232, 252), (108, 142, 191)),
    'medium_purple': ((225, 213, 231), (150, 115, 166)),
    'medium_red': ((248, 206, 204), (184, 84, 80)),
    'low_orange': ((255, 230, 204), (215, 155, 0)),
    'low_green': ((213, 232, 212), (86, 167, 100)),
    'optional': ((245, 245, 245), (102, 102, 102)),
}

def get_font(size, bold=False):
    """Get a font, falling back to default if needed"""
    try:
        # Try to find a TrueType font
        font_paths = [
            '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf' if bold else '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf',
            '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf' if bold else '/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf',
            '/usr/share/fonts/TTF/DejaVuSans-Bold.ttf' if bold else '/usr/share/fonts/TTF/DejaVuSans.ttf',
        ]
        for path in font_paths:
            if os.path.exists(path):
                return ImageFont.truetype(path, size)
    except:
        pass
    return ImageFont.load_default()

def draw_rounded_rect(draw, xy, radius, fill, outline, width=2):
    """Draw a rounded rectangle"""
    x1, y1, x2, y2 = xy
    draw.rounded_rectangle([x1, y1, x2, y2], radius=radius, fill=fill, outline=outline, width=width)

def draw_phase_box(draw, x, y, w, h, title, subtitle, items, color_key, fonts):
    """Draw a phase box with title and items"""
    fill, border = PHASE_COLORS[color_key]

    # Main container
    draw_rounded_rect(draw, (x, y, x+w, y+h), 10, fill, border, 2)

    # Header
    header_h = 35
    draw_rounded_rect(draw, (x+5, y+5, x+w-5, y+5+header_h), 8, border, border, 1)

    # Title text (centered)
    title_font = fonts['bold_12']
    title_bbox = draw.textbbox((0, 0), title, font=title_font)
    title_w = title_bbox[2] - title_bbox[0]
    draw.text((x + (w - title_w) // 2, y + 8), title, fill=(255, 255, 255), font=title_font)

    # Subtitle
    if subtitle:
        sub_font = fonts['normal_10']
        sub_bbox = draw.textbbox((0, 0), subtitle, font=sub_font)
        sub_w = sub_bbox[2] - sub_bbox[0]
        draw.text((x + (w - sub_w) // 2, y + 22), subtitle, fill=(255, 255, 255), font=sub_font)

    # Items
    item_y = y + header_h + 15
    item_font = fonts['normal_10']

    for item_title, item_methods in items:
        # Item box
        item_h = 15 + len(item_methods) * 12 + 8
        draw_rounded_rect(draw, (x+10, item_y, x+w-10, item_y+item_h), 5, (255, 255, 255), border, 1)

        # Item title
        draw.text((x+15, item_y+3), item_title, fill=(0, 0, 0), font=fonts['bold_10'])

        # Methods
        method_y = item_y + 16
        for method in item_methods:
            draw.text((x+15, method_y), f"- {method}", fill=(80, 80, 80), font=fonts['normal_9'])
            method_y += 12

        item_y += item_h + 5

    return item_y

def draw_arrow(draw, start, end, color=(100, 100, 100)):
    """Draw an arrow from start to end"""
    x1, y1 = start
    x2, y2 = end
    draw.line([x1, y1, x2, y2], fill=color, width=2)
    # Arrowhead
    arrow_size = 8
    if x2 > x1:  # Horizontal arrow pointing right
        draw.polygon([(x2, y2), (x2-arrow_size, y2-arrow_size//2), (x2-arrow_size, y2+arrow_size//2)], fill=color)
    elif y2 > y1:  # Vertical arrow pointing down
        draw.polygon([(x2, y2), (x2-arrow_size//2, y2-arrow_size), (x2+arrow_size//2, y2-arrow_size)], fill=color)

def main():
    # Create image
    img = Image.new('RGB', (WIDTH, HEIGHT), BACKGROUND_COLOR)
    draw = ImageDraw.Draw(img)

    # Load fonts
    fonts = {
        'title': get_font(22, bold=True),
        'subtitle': get_font(13),
        'bold_12': get_font(11, bold=True),
        'bold_10': get_font(10, bold=True),
        'normal_10': get_font(9),
        'normal_9': get_font(8),
        'normal_8': get_font(7),
    }

    # Title
    title = "IAmazonS3 Interface Implementation Roadmap"
    title_bbox = draw.textbbox((0, 0), title, font=fonts['title'])
    title_w = title_bbox[2] - title_bbox[0]
    draw.text(((WIDTH - title_w) // 2, 15), title, fill=TITLE_COLOR, font=fonts['title'])

    subtitle = "Couchbase Lite Backend | Total: 156+ Methods | Implemented: 16 (~10%) | Remaining: 140+"
    sub_bbox = draw.textbbox((0, 0), subtitle, font=fonts['subtitle'])
    sub_w = sub_bbox[2] - sub_bbox[0]
    draw.text(((WIDTH - sub_w) // 2, 42), subtitle, fill=SUBTITLE_COLOR, font=fonts['subtitle'])

    # Phase dimensions
    phase_w = 245
    phase_h = 280
    row1_y = 75
    row2_y = 380
    gap = 15
    start_x = 30

    # Row 1: Phases 1-5
    phases_row1 = [
        ('PHASE 1: FOUNDATION', '[COMPLETED]', 'completed', [
            ('Bucket Operations', ['PutBucketAsync', 'DeleteBucketAsync', 'ListBucketsAsync']),
            ('Object Operations', ['PutObjectAsync', 'GetObjectAsync', 'DeleteObjectAsync', 'DeleteObjectsAsync']),
            ('Listing & Config', ['ListObjectsV2Async', 'IClientConfig', 'IDisposable']),
        ]),
        ('PHASE 2: ESSENTIAL OPS', 'Priority: HIGH', 'high', [
            ('Metadata Operations', ['GetObjectMetadataAsync', 'HeadBucketAsync', 'DoesS3BucketExistAsync']),
            ('Copy Operations', ['CopyObjectAsync (3)', 'CopyPartAsync']),
            ('Pre-signed URLs', ['GetPreSignedURL', 'GetPreSignedURLAsync']),
        ]),
        ('PHASE 3: VERSIONING', 'Priority: MEDIUM', 'medium_blue', [
            ('Schema Updates', ['Version ID generation', 'Version chain tracking', 'Delete markers']),
            ('Versioning Config', ['GetBucketVersioningAsync', 'PutBucketVersioningAsync']),
            ('Version Operations', ['ListVersionsAsync', 'GetObject w/ VersionId']),
        ]),
        ('PHASE 4: MULTIPART', 'Priority: MEDIUM', 'medium_purple', [
            ('Schema Updates', ['Upload tracking docs', 'Part storage schema', 'State management']),
            ('Upload Lifecycle', ['InitiateMultipartUploadAsync', 'AbortMultipartUploadAsync', 'CompleteMultipartUploadAsync']),
            ('Part Operations', ['UploadPartAsync', 'ListPartsAsync']),
        ]),
        ('PHASE 5: ACCESS CTRL', 'Priority: MEDIUM', 'medium_red', [
            ('ACL Operations', ['GetACLAsync', 'PutACLAsync', 'MakeObjectPublicAsync']),
            ('Bucket Policies', ['GetBucketPolicyAsync', 'PutBucketPolicyAsync', 'DeleteBucketPolicyAsync']),
            ('Public Access', ['GetPublicAccessBlockAsync', 'PutPublicAccessBlockAsync']),
        ]),
    ]

    # Draw Row 1
    x = start_x
    for i, (title, subtitle_text, color, items) in enumerate(phases_row1):
        draw_phase_box(draw, x, row1_y, phase_w, phase_h, title, subtitle_text, items, color, fonts)

        # Draw arrow to next phase (except last)
        if i < len(phases_row1) - 1:
            arrow_y = row1_y + phase_h // 2
            draw_arrow(draw, (x + phase_w + 2, arrow_y), (x + phase_w + gap - 2, arrow_y))

        x += phase_w + gap

    # Row 2: Phases 6-8 + Legend
    phase_w2 = 330  # Wider phases for row 2

    phases_row2 = [
        ('PHASE 6: BUCKET CONFIG', 'Priority: LOW', 'low_orange', [
            ('Encryption', ['GetBucketEncryptionAsync', 'PutBucketEncryptionAsync']),
            ('Lifecycle Rules', ['GetLifecycleConfigurationAsync', 'PutLifecycleConfigurationAsync']),
            ('CORS & Website', ['GetCORSConfigurationAsync', 'PutBucketWebsiteAsync']),
        ]),
        ('PHASE 7: OBJECT FEATURES', 'Priority: LOW', 'low_green', [
            ('Object Tagging', ['GetObjectTaggingAsync', 'PutObjectTaggingAsync']),
            ('Object Lock', ['GetObjectLockConfigurationAsync', 'PutObjectRetentionAsync']),
            ('Legal Hold', ['GetObjectLegalHoldAsync', 'PutObjectLegalHoldAsync']),
        ]),
        ('PHASE 8: ADVANCED', 'Priority: OPTIONAL', 'optional', [
            ('S3 Select', ['SelectObjectContentAsync', 'SQL-like query support']),
            ('Object Lambda', ['WriteGetObjectResponseAsync']),
            ('Directory Buckets', ['ListDirectoryBucketsAsync', 'CreateSessionAsync']),
        ]),
    ]

    x = start_x
    for i, (title, subtitle_text, color, items) in enumerate(phases_row2):
        draw_phase_box(draw, x, row2_y, phase_w2, phase_h, title, subtitle_text, items, color, fonts)

        # Draw arrow to next phase (except last)
        if i < len(phases_row2) - 1:
            arrow_y = row2_y + phase_h // 2
            draw_arrow(draw, (x + phase_w2 + 2, arrow_y), (x + phase_w2 + gap - 2, arrow_y))

        x += phase_w2 + gap

    # Legend box
    legend_x = x + 10
    legend_y = row2_y
    legend_w = 240
    legend_h = phase_h

    draw_rounded_rect(draw, (legend_x, legend_y, legend_x + legend_w, legend_y + legend_h), 10, (255, 255, 255), (51, 51, 51), 1)

    # Legend header
    draw_rounded_rect(draw, (legend_x + 5, legend_y + 5, legend_x + legend_w - 5, legend_y + 30), 5, (51, 51, 51), (51, 51, 51), 1)
    draw.text((legend_x + 40, legend_y + 10), "LEGEND & STATISTICS", fill=(255, 255, 255), font=fonts['bold_10'])

    # Legend items
    legend_items = [
        ('completed', 'Completed (16 methods)'),
        ('high', 'High Priority (~15 methods)'),
        ('medium_blue', 'Medium Priority (~35 methods)'),
        ('low_orange', 'Low Priority (~50 methods)'),
        ('optional', 'Optional (~40 methods)'),
    ]

    item_y = legend_y + 40
    for color_key, label in legend_items:
        fill, border = PHASE_COLORS[color_key]
        draw.rectangle([legend_x + 15, item_y, legend_x + 30, item_y + 15], fill=fill, outline=border)
        draw.text((legend_x + 40, item_y), label, fill=(0, 0, 0), font=fonts['normal_10'])
        item_y += 22

    # Stats
    item_y += 10
    draw.line([legend_x + 15, item_y, legend_x + legend_w - 15, item_y], fill=(180, 180, 180), width=1)
    item_y += 10

    stats = [
        "Implementation Stats:",
        "",
        "Total Methods: ~156",
        "Implemented: 16",
        "Remaining: ~140",
        "",
        "Current: ~10%",
        "After Phase 2: ~20%",
        "After Phase 4: ~40%",
        "After Phase 6: ~70%",
        "Complete: 100%",
    ]

    for i, stat in enumerate(stats):
        font = fonts['bold_10'] if i == 0 else fonts['normal_9']
        draw.text((legend_x + 15, item_y), stat, fill=(60, 60, 60), font=font)
        item_y += 14

    # Technical notes at bottom
    notes_y = 690
    notes_h = 100
    draw_rounded_rect(draw, (start_x, notes_y, WIDTH - start_x, notes_y + notes_h), 8, (240, 240, 240), (153, 153, 153), 1)

    draw.text((start_x + 15, notes_y + 8), "TECHNICAL IMPLEMENTATION NOTES", fill=(51, 51, 51), font=fonts['bold_12'])

    notes = [
        "Storage Backend: Couchbase Lite (Document DB) | Framework: .NET 9 | Package: Couchbase.Lite 3.1.9",
        "Document Schema: Buckets (bucket::{name}), Objects (object::{bucket}::{key}), Versions (version::{bucket}::{key}::{versionId})",
        "Key Decisions: Use InBatch() for transactions | MD5-based ETags | Blob storage for content | Indexes for queries",
    ]

    note_y = notes_y + 30
    for note in notes:
        draw.text((start_x + 15, note_y), note, fill=(100, 100, 100), font=fonts['normal_10'])
        note_y += 18

    # Draw connecting arrow from row 1 to row 2
    arrow_start_x = start_x + 4 * (phase_w + gap) + phase_w // 2
    draw_arrow(draw, (arrow_start_x, row1_y + phase_h + 5), (arrow_start_x, row2_y - 10))
    draw.line([arrow_start_x, row2_y - 10, start_x + phase_w2 // 2, row2_y - 10], fill=(100, 100, 100), width=2)
    draw_arrow(draw, (start_x + phase_w2 // 2, row2_y - 10), (start_x + phase_w2 // 2, row2_y - 2))

    # Save the image
    output_path = '/home/user/AWSSDK.Extensions/docs/S3-Couchbase-Implementation-Roadmap.png'
    img.save(output_path, 'PNG', quality=95)
    print(f"Roadmap saved to: {output_path}")
    print(f"Image size: {WIDTH}x{HEIGHT}")

if __name__ == '__main__':
    main()
