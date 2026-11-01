namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Company 1005 (Aria) outgoing-letter layout, calibrated against its A4 letterhead PDF.
/// Field coordinates are points from the page-header origin, independent of body spacing.
/// A5 coordinates follow the registered letterhead's proportional scale.
/// </summary>
internal static class AriaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    // Header height controls only where flowing content begins; field rectangles below are absolute.
    internal static float HeaderHeight(bool isA5) => isA5 ? 70f : 95f;
    internal static float BodyTopPadding(bool isA5) => isA5 ? 13f : 12f;
    internal static float FieldFontSize(bool isA5) => isA5 ? 6f : 8f;

    // Dotted line regions, read from left to right: attachment, number, date.
    // The text is right-aligned inside each dotted region so it does not overlap its caption.
    internal static Field Date(bool isA5) => isA5
        ? new(142.2f, 47f, 35.9f, 10f)
        : new(202f, 66f, 51f, 14f);

    internal static Field Number(bool isA5) => isA5
        ? new(77.4f, 47f, 34.5f, 10f)
        : new(110f, 66f, 49f, 14f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(7f, 47f, 35.2f, 10f)
        : new(10f, 66f, 50f, 14f);
}
