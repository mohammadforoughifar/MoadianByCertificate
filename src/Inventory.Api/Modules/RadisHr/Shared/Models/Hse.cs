namespace RadisHr.Shared.Models;

/// <summary>حادثه HSE — معادل state.incidents در hse.js</summary>
public class Incident
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string CaseNo { get; set; } = "";        // HSE-1405-0001
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Shift { get; set; } = "صبح";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "جزئی";
    public string EmployeeCode { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";

    public string Status { get; set; } = "ثبت اولیه";
    public int LostDays { get; set; }
    public string MedicalNotes { get; set; } = "";
    public bool LeaveRequired { get; set; }
    public string LeaveType { get; set; } = "";
    public string RootCause { get; set; } = "";

    public List<CorrectiveAction> CorrectiveActions { get; set; } = new();
    public List<MedicalDocument> MedicalDocuments { get; set; } = new();

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CorrectiveAction
{
    public int Id { get; set; }
    public int IncidentId { get; set; }
    public string Uid { get; set; } = "";
    public string Title { get; set; } = "";
    public string Owner { get; set; } = "";
    public string DueDate { get; set; } = "";
    public string Status { get; set; } = "باز";
}

public class MedicalDocument
{
    public int Id { get; set; }
    public int IncidentId { get; set; }
    public string Uid { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string ArchivedBy { get; set; } = "";
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    /// <summary>اسناد پزشکی غیرقابل حذف هستند (immutable در نسخهٔ اصلی)</summary>
    public bool Immutable { get; set; } = true;
    public string? StoredFileId { get; set; }
}

/// <summary>تحویل تجهیزات حفاظت فردی — state.ppe</summary>
public class PpeDelivery
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public string EquipmentType { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string BrandModel { get; set; } = "";
    public string Size { get; set; } = "";
    public string Serial { get; set; } = "";
    public string DeliveryDate { get; set; } = "";
    public int ReplacementIntervalDays { get; set; } = 180;
    public string Condition { get; set; } = "نو";

    public bool EarlyReplacement { get; set; }
    public string EarlyReplacementReason { get; set; } = "";
    public string Notes { get; set; } = "";

    public string HseApproval { get; set; } = "در انتظار";        // در انتظار | تأیید | رد
    public string ProductionApproval { get; set; } = "در انتظار";
    public string? HseApprovedBy { get; set; }
    public string? ProductionApprovedBy { get; set; }

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>کپسول اطفای حریق — state.extinguishers</summary>
public class Extinguisher
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string Type { get; set; } = "";
    public string AssetCode { get; set; } = "";
    public string Location { get; set; } = "";
    public string ExpiryDate { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>اعلان HSE (تحویل زودهنگام) — state.notifications</summary>
public class HseNotification
{
    public int Id { get; set; }
    public string Uid { get; set; } = "";
    public string? PpeId { get; set; }
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Seen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>مجوز تجهیزات هر ایستگاه کاری — state.ppeAuthorizations["واحد::ایستگاه"]</summary>
public class PpeAuthorization
{
    public int Id { get; set; }
    public string StationKey { get; set; } = "";    // «تولید::خط تولید»
    public string EquipmentType { get; set; } = "";
}

/// <summary>تعاریف پایه HSE (نوع رویداد، نوع کپسول، نوع تجهیز)</summary>
public class HseDefinition
{
    public int Id { get; set; }
    public string Group { get; set; } = "";   // incidentTypes | extinguisherTypes | ppeTypes
    public string Title { get; set; } = "";
    public int Ordinal { get; set; }
}
