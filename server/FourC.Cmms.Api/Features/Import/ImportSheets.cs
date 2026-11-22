using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Assets;
using FourC.Cmms.Core.Locations;
using FourC.Cmms.Core.Pm;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Api.Features.Import;

public enum ValueKind { Text, Number, Integer, Date, YesNo, Choice, CodeList, ChoiceList }

/// <summary>"Week of month" on a PM schedule: the first to fourth week, or the last.</summary>
public enum WeekOfMonth { First = 1, Second = 2, Third = 3, Fourth = 4, Last = 5 }

/// <summary>A column of an import sheet: its key, its heading in English and Arabic, how its cells read.</summary>
public sealed record ColumnDef(string Key, string English, string Arabic, ValueKind Kind, bool RequiredOnNew = false, Type? Choices = null)
{
    /// <summary>The heading in the request's language.</summary>
    public string Heading => Translator.CurrentLanguage == "ar" ? Arabic : English;
}

/// <summary>A sheet of the import workbook: its key, its name in English and Arabic, and its columns.</summary>
/// <param name="Aliases">Older names the sheet is still read under (e.g. "PM tasks" before 0.12.0's "Work templates").</param>
public sealed record SheetDef(string Key, string English, string Arabic, IReadOnlyList<ColumnDef> Columns, IReadOnlyList<string>? Aliases = null)
{
    public string Name => Translator.CurrentLanguage == "ar" ? Arabic : English;

    /// <summary>The column whose English or Arabic heading this is, ignoring case and spaces.</summary>
    public ColumnDef? FindColumn(string? heading)
    {
        var h = ImportSheets.Normal(heading);
        return h.Length == 0 ? null : Columns.FirstOrDefault(c => ImportSheets.Normal(c.English) == h || ImportSheets.Normal(c.Arabic) == h);
    }

    public ColumnDef Column(string key) => Columns.First(c => c.Key == key);

    /// <summary>The same sheet with more columns (the organization's custom fields on Assets).</summary>
    public SheetDef With(IEnumerable<ColumnDef> more) => this with { Columns = [.. Columns, .. more] };
}

/// <summary>The import workbook's sheets, in the order they are loaded (a sheet may refer to codes from the ones before it).</summary>
public static class ImportSheets
{
    private static ColumnDef T(string key, string en, string ar, bool req = false) => new(key, en, ar, ValueKind.Text, req);
    private static ColumnDef N(string key, string en, string ar, bool req = false) => new(key, en, ar, ValueKind.Number, req);
    private static ColumnDef I(string key, string en, string ar, bool req = false) => new(key, en, ar, ValueKind.Integer, req);
    private static ColumnDef D(string key, string en, string ar, bool req = false) => new(key, en, ar, ValueKind.Date, req);
    private static ColumnDef Y(string key, string en, string ar) => new(key, en, ar, ValueKind.YesNo);
    private static ColumnDef C<TEnum>(string key, string en, string ar) where TEnum : struct, Enum => new(key, en, ar, ValueKind.Choice, false, typeof(TEnum));
    private static ColumnDef L(string key, string en, string ar) => new(key, en, ar, ValueKind.CodeList);
    private static ColumnDef CL<TEnum>(string key, string en, string ar) where TEnum : struct, Enum => new(key, en, ar, ValueKind.ChoiceList, false, typeof(TEnum));

    private static ColumnDef Code => T("code", "Code", "الرمز", true);
    private static ColumnDef Name => T("name", "Name", "الاسم", true);
    private static ColumnDef Active => Y("active", "Active", "نشط");
    private static ColumnDef Description => T("description", "Description", "الوصف");
    private static ColumnDef Notes => T("notes", "Notes", "ملاحظات");
    private static ColumnDef SiteCode => T("siteCode", "Site code", "رمز الموقع", true);

    public static readonly IReadOnlyList<SheetDef> All =
    [
        new("Sites", "Sites", "المواقع", [Code, Name, T("address", "Address", "العنوان"), T("city", "City", "المدينة"), T("country", "Country", "الدولة"),
            T("timeZone", "Time zone", "المنطقة الزمنية"), Active]),
        new("Locations", "Locations", "الأماكن", [SiteCode, Code, Name, T("parentCode", "Parent code", "رمز المكان الأعلى"),
            C<LocationType>("type", "Type", "النوع"), C<Criticality>("criticality", "Criticality", "الأهمية"), Description,
            T("notesToTechnician", "Notes to technician", "ملاحظات للفني"), Active]),
        new("Categories", "Categories", "الفئات", [Code, Name, T("color", "Color", "اللون"), Active]),
        new("Trades", "Trades", "التخصصات", [Code, Name, T("color", "Color", "اللون"), Active]),
        new("PartGroups", "Part groups", "مجموعات القطع", [Code, Name, Active]),
        new("Suppliers", "Suppliers", "الموردون", [Code, Name, T("contact", "Contact", "جهة الاتصال"), T("email", "Email", "البريد الإلكتروني"),
            T("phone", "Phone", "الهاتف"), T("address", "Address", "العنوان"), T("city", "City", "المدينة"), T("country", "Country", "الدولة"),
            T("taxNumber", "Tax number", "الرقم الضريبي"), T("paymentTerms", "Payment terms", "شروط الدفع"), T("currency", "Currency", "العملة"),
            Y("contractor", "Contractor", "مقاول"), Notes, Active]),
        new("Stores", "Stores", "المخازن", [Code, Name, SiteCode, Description, Active]),
        new("Assets", "Assets", "الأصول", [T("assetNumber", "Asset number", "رقم الأصل"), Name, SiteCode, T("locationCode", "Location code", "رمز المكان"),
            T("parentAssetNumber", "Parent asset number", "رقم الأصل الأعلى"), T("categoryCode", "Category code", "رمز الفئة"),
            C<AssetStatus>("status", "Status", "الحالة"), C<Criticality>("criticality", "Criticality", "الأهمية"),
            T("manufacturer", "Manufacturer", "الشركة المصنعة"), T("model", "Model", "الطراز"), T("serialNumber", "Serial number", "الرقم التسلسلي"),
            T("barcode", "Barcode", "الباركود"), T("operationNumber", "Operation number", "رقم التشغيل"),
            D("commissionedOn", "Commissioned on", "تاريخ التشغيل"), D("disposedOn", "Disposed on", "تاريخ الاستبعاد"),
            N("purchasePrice", "Purchase price", "سعر الشراء"), N("currentValue", "Current value", "القيمة الحالية"), N("salvageValue", "Salvage value", "قيمة الخردة"),
            N("expectedLifeYears", "Expected life (years)", "العمر المتوقع (سنوات)"), N("downtimeCostPerHour", "Downtime cost per hour", "تكلفة التوقف للساعة"),
            C<WarrantyType>("warrantyType", "Warranty type", "نوع الضمان"), D("warrantyExpiresOn", "Warranty expires on", "انتهاء الضمان"),
            T("warrantyNotes", "Warranty notes", "ملاحظات الضمان"), Description, Notes, T("notesToTechnician", "Notes to technician", "ملاحظات للفني"),
            N("runsHoursPerDay", "Runs hours a day", "ساعات التشغيل في اليوم"), CL<DayOfWeek>("runsOn", "Runs on", "يعمل في")]),
        new("People", "People", "الأفراد", [Code, T("fullName", "Full name", "الاسم الكامل", true), T("jobTitle", "Job title", "المسمى الوظيفي"),
            T("email", "Email", "البريد الإلكتروني"), T("phone", "Phone", "الهاتف"), L("trades", "Trades", "التخصصات"), L("sites", "Sites", "المواقع"),
            N("hourlyRate", "Hourly rate", "أجر الساعة"), N("overtimeRate", "Overtime rate", "أجر الساعة الإضافية"), Y("contractor", "Contractor", "مقاول"),
            T("company", "Company", "الشركة"), Notes, Active]),
        new("Parts", "Parts", "القطع", [T("partNumber", "Part number", "رقم القطعة", true), Name, T("unit", "Unit", "الوحدة", true),
            T("groupCode", "Group code", "رمز المجموعة"), T("specification", "Specification", "المواصفات"), T("manufacturer", "Manufacturer", "الشركة المصنعة"),
            T("manufacturerPartNumber", "Manufacturer part number", "رقم القطعة لدى المصنع"), N("standardPrice", "Standard price", "السعر القياسي"),
            I("leadTimeDays", "Lead time (days)", "مدة التوريد (أيام)"), Y("critical", "Critical", "حرجة"), Y("shelfLife", "Shelf-life item", "صنف له صلاحية"),
            Y("allowNegative", "Allow negative stock", "السماح بالرصيد السالب"), T("preferredSupplierCode", "Preferred supplier code", "رمز المورد المفضل"),
            T("barcode", "Barcode", "الباركود"), Notes, Active]),
        new("AssetParts", "Asset parts", "قطع الأصول", [T("assetNumber", "Asset number", "رقم الأصل", true), T("partNumber", "Part number", "رقم القطعة", true),
            N("quantity", "Quantity", "الكمية", true), T("note", "Note", "ملاحظة")]),
        new("Stock", "Stock", "المخزون", [T("partNumber", "Part number", "رقم القطعة", true), T("storeCode", "Store code", "رمز المخزن", true),
            T("bin", "Bin", "الموضع"), N("min", "Min", "الحد الأدنى"), N("reorderLevel", "Re-order level", "حد إعادة الطلب"), N("max", "Max", "الحد الأقصى"),
            N("reorderQuantity", "Re-order quantity", "كمية إعادة الطلب"), N("openingQuantity", "Opening quantity", "الرصيد الافتتاحي"),
            T("batch", "Batch", "الدفعة"), D("expiry", "Expiry", "تاريخ الانتهاء")]),
        new("PmTasks", "Work templates", "قوالب العمل", [Code, Name, Description, T("tradeCode", "Trade code", "رمز التخصص"),
            N("estimatedHours", "Estimated hours", "الساعات التقديرية"), T("safetyNotes", "Safety notes", "ملاحظات السلامة"),
            T("title", "Title", "العنوان"), C<WorkType>("workType", "Work type", "نوع العمل"), C<WorkPriority>("priority", "Priority", "الأولوية"),
            N("expectedStopHours", "Expected stop hours", "ساعات التوقف المتوقعة"), Active], ["PM tasks", "مهام الصيانة الوقائية"]),
        new("PmSteps", "PM steps", "خطوات الصيانة الوقائية", [T("taskCode", "Task code", "رمز المهمة", true), I("step", "Step", "الخطوة", true),
            T("description", "Description", "الوصف", true)]),
        new("PmParts", "PM parts", "قطع الصيانة الوقائية", [T("taskCode", "Task code", "رمز المهمة", true), T("partNumber", "Part number", "رقم القطعة", true),
            N("quantity", "Quantity", "الكمية", true)]),
        new("PmSchedules", "PM schedules", "جداول الصيانة الوقائية", [Code, Name, T("taskCode", "Task code", "رمز المهمة", true), SiteCode,
            T("locationCode", "Location code", "رمز المكان"), T("assetNumber", "Asset number", "رقم الأصل"), T("tradeCode", "Trade code", "رمز التخصص"),
            C<WorkPriority>("priority", "Priority", "الأولوية"), C<PmScheduleType>("schedule", "Schedule", "الجدول"), I("every", "Every", "كل"),
            C<FrequencyUnit>("unit", "Unit", "الوحدة"), C<PmBasis>("countFrom", "Count from", "الحساب من"),
            C<WeekOfMonth>("weekOfMonth", "Week of month", "أسبوع الشهر"), C<DayOfWeek>("weekday", "Weekday", "يوم الأسبوع"),
            I("dayOfMonth", "Day of month", "يوم الشهر"), D("startDate", "Start date", "تاريخ البدء", true), I("leadDays", "Lead days", "أيام التقديم"),
            I("workPeriodDays", "Work period days", "أيام فترة العمل"), L("assignees", "Assignees", "المكلفون"),
            C<MultiAssetMode>("multiAssetMode", "Multi-asset mode", "وضع الأصول المتعددة"), Description, Active]),
    ];

    /// <summary>
    /// The sheet with this name: English, Arabic or its key, ignoring case and spaces. A CSV file's name may carry more
    /// after the sheet's name ("assets-november"): then the longest sheet name it starts with wins.
    /// </summary>
    public static SheetDef? Find(string? name)
    {
        var n = Normal(name);
        if (n.Length == 0) return null;
        var exact = All.FirstOrDefault(s => Names(s).Contains(n));
        if (exact is not null) return exact;
        return All.SelectMany(s => Names(s).Select(x => (Sheet: s, Name: x)))
            .Where(x => n.StartsWith(x.Name, StringComparison.Ordinal) && !char.IsLetter(n[x.Name.Length]))
            .OrderByDescending(x => x.Name.Length).Select(x => x.Sheet).FirstOrDefault();
    }

    private static IEnumerable<string> Names(SheetDef s) => [Normal(s.English), Normal(s.Arabic), Normal(s.Key), .. (s.Aliases ?? []).Select(Normal)];

    /// <summary>Lower case, trimmed, inner runs of spaces made one.</summary>
    public static string Normal(string? s) =>
        string.Join(' ', (s ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The English word a choice value goes by: the screens' word, or the import's own for schedule terms.</summary>
    public static string ChoiceWord(Enum value) => value switch
    {
        PmScheduleType.FixedInterval => "Every",
        PmScheduleType.DayOfWeekInMonth => "Weekday of month",
        PmScheduleType.DateOfMonth => "Day of month",
        PmBasis.Scheduled => "Due date",
        PmBasis.Actual => "Completion",
        WeekOfMonth w when w != WeekOfMonth.Last => ((int)w).ToString(),
        _ => TableExport.Words(value.ToString()),
    };

    /// <summary>The choice word in the request's language.</summary>
    public static string ChoiceText(Enum value) => Translator.T(ChoiceWord(value));
}
