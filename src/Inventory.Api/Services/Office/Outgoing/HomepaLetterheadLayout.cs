namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Homepa value coordinates relative to QuestPDF's page-header origin.
/// The Homepa artwork is used by company 1003 in the main database and by company 3 in the
/// sample one (Companies/3 and Companies/1003 embed the same image); both ids therefore
/// share this layout — see Reporting:OutgoingLetters:HomepaCompanyIds.
/// Physical artwork coordinates come from the MRT: page margin + ClientRectangle, in points.
/// Header content begins after Page.MarginTop (24pt on A4 / 16pt on A5), so subtract that
/// origin to keep values on the matching raster-label row rather than one row too low.
/// Date/attachment retain their calibrated 4pt (A4) / 3pt (A5) left shift.
/// Keep each field independent; text height/wrapping must not move the following fields.
/// </summary>
internal static class HomepaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    // Header field placement and body start spacing are deliberately independent.
    // These fixed header bounds keep values on company 1003's printed labels;
    // only the body content offset may change without moving those values.
    internal static float HeaderHeight(bool isA5) => isA5 ? 150f : 160f;
    // The art ends at about 232pt on A4 and scales to about 163pt on A5.
    // Keep roughly 32pt (11mm) clear below it in both formats.
    internal static float ReceiverTopPadding(bool isA5) => isA5 ? 28f : 80f;
    internal static float SubjectBodyGap(bool isA5) => isA5 ? 8f : 12f;
    internal static float FooterReserve(bool isA5) => isA5 ? 68f : 90f;
    internal static float HeaderValueFontSize(bool isA5) => isA5 ? 8.5f : 10.5f;

    internal static Field Number(bool isA5) => isA5
        ? new(7.2f, 25.04f, 59.04f, 12.96f)
        : new(14.4f, 36.48f, 82.08f, 14.4f);

    internal static Field Date(bool isA5) => isA5
        ? new(4.2f, 36.56f, 59.04f, 12.96f)
        : new(10.4f, 53.04f, 82.08f, 14.4f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(4.2f, 48.08f, 59.04f, 12.96f)
        : new(10.4f, 69.6f, 82.08f, 14.4f);
}
