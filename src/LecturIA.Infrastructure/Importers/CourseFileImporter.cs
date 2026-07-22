using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using CsvHelper;
using CsvHelper.Configuration;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Importers;

/// <summary>
/// Imports a course from an Excel (<c>.xlsx</c> / <c>.xls</c>) or CSV file.
/// </summary>
/// <remarks>
/// The importer expects student columns named <c>RUT sin puntos y con guion</c>,
/// <c>Nombre</c> and <c>Apellido</c>, plus three columns describing the
/// course: <c>Establecimiento</c>, <c>Nivel</c> and <c>Sección</c>. Header
/// lookups are case-insensitive and tolerant of extra qualifiers in
/// parentheses. Course metadata is taken from the first row that has a
/// non-empty value for each field; rows that disagree are ignored on the
/// assumption that the planilla represents a single course. Duplicate
/// students are removed by RUT (or full name when RUT is missing) and the
/// order of first appearance is preserved.
/// </remarks>
public sealed class CourseFileImporter : ICourseImporter
{
    private static readonly CultureInfo ChileanSpanish = new("es-CL");

    private static readonly string[] RutHeaders =
        ["rut sin puntos y con guion", "rut", "run"];

    private static readonly string[] FirstNameHeaders =
        ["nombre", "nombres", "first name", "firstname"];

    private static readonly string[] LastNameHeaders =
        ["apellido", "apellidos", "last name", "lastname"];

    private static readonly string[] SchoolHeaders =
        ["establecimiento", "colegio", "escuela", "school"];

    private static readonly string[] LevelHeaders =
        ["nivel", "curso", "grado", "level", "grade"];

    private static readonly string[] SectionHeaders =
        ["seccion", "sección", "section", "letra"];

    /// <inheritdoc />
    public Task<Course> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The file to import was not found.", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        List<RawRow> rows = extension switch
        {
            ".xlsx" or ".xls" => ReadFromExcel(filePath),
            ".csv" => ReadFromCsv(filePath),
            _ => throw new NotSupportedException(
                "Unsupported format. Only .xlsx, .xls and .csv files can be imported."),
        };

        var students = NormalizeStudents(rows);
        if (students.Count == 0)
        {
            throw new InvalidDataException("No valid student records were found in the file.");
        }

        var (school, level, section) = ExtractCourseInfo(rows);
        return Task.FromResult(Course.CreateNew(school, level, section, students));
    }

    // -------------------------------------------------------------------------
    // Excel reader
    // -------------------------------------------------------------------------

    private static List<RawRow> ReadFromExcel(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The Excel file does not contain any worksheet.");

        var headerRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");

        var columnMap = BuildColumnMap(
            headerRow.Cells()
                     .ToDictionary(
                         c => c.Address.ColumnNumber,
                         c => c.GetString().Trim()));

        var rows = new List<RawRow>();
        var dataRange = sheet.RangeUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");

        foreach (var row in dataRange.RowsUsed().Skip(1))
        {
            rows.Add(new RawRow(
                Rut: ReadCell(row, columnMap.Rut),
                FirstName: ReadCell(row, columnMap.FirstName),
                LastName: ReadCell(row, columnMap.LastName),
                School: ReadCell(row, columnMap.School),
                Level: ReadCell(row, columnMap.Level),
                Section: ReadCell(row, columnMap.Section)));
        }

        return rows;
    }

    private static string ReadCell(IXLRangeRow row, int? columnIndex) =>
        columnIndex.HasValue ? row.Cell(columnIndex.Value).GetString() : string.Empty;

    // -------------------------------------------------------------------------
    // CSV reader
    // -------------------------------------------------------------------------

    private static List<RawRow> ReadFromCsv(string filePath)
    {
        var encoding = DetectEncoding(filePath);
        using var reader = new StreamReader(filePath, encoding);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
        });

        if (!csv.Read() || !csv.ReadHeader())
        {
            throw new InvalidDataException("The CSV file is empty or has no header row.");
        }

        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        var headerDict = headers
            .Select((h, i) => (Header: h?.Trim() ?? string.Empty, Index: i))
            .ToDictionary(x => x.Index, x => x.Header);

        var columnMap = BuildColumnMap(headerDict);

        var rows = new List<RawRow>();
        while (csv.Read())
        {
            rows.Add(new RawRow(
                Rut: GetCsvField(csv, headers, columnMap.Rut),
                FirstName: GetCsvField(csv, headers, columnMap.FirstName),
                LastName: GetCsvField(csv, headers, columnMap.LastName),
                School: GetCsvField(csv, headers, columnMap.School),
                Level: GetCsvField(csv, headers, columnMap.Level),
                Section: GetCsvField(csv, headers, columnMap.Section)));
        }

        return rows;
    }

    private static string GetCsvField(CsvReader csv, string[] headers, int? columnIndex)
    {
        if (!columnIndex.HasValue || columnIndex.Value >= headers.Length)
        {
            return string.Empty;
        }

        return csv.GetField(columnIndex.Value) ?? string.Empty;
    }

    // -------------------------------------------------------------------------
    // Column mapping
    // -------------------------------------------------------------------------

    private static ColumnMap BuildColumnMap(Dictionary<int, string> headers)
    {
        int? FindColumn(string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                foreach (var (index, header) in headers)
                {
                    var normalizedHeader = StripDiacritics(header);
                    var normalizedCandidate = StripDiacritics(candidate);

                    // StartsWith allows headers like "RUT sin puntos y con guion (12345678-9)"
                    // to still match the candidate "rut sin puntos y con guion".
                    if (normalizedHeader.StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedHeader, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }
            }

            return null;
        }

        return new ColumnMap(
            Rut: FindColumn(RutHeaders),
            FirstName: FindColumn(FirstNameHeaders),
            LastName: FindColumn(LastNameHeaders),
            School: FindColumn(SchoolHeaders),
            Level: FindColumn(LevelHeaders),
            Section: FindColumn(SectionHeaders));
    }

    private static string StripDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // -------------------------------------------------------------------------
    // Normalization
    // -------------------------------------------------------------------------

    private static List<Student> NormalizeStudents(IEnumerable<RawRow> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Student>();
        var rowIndex = 0;

        foreach (var row in rows)
        {
            rowIndex++;
            var rut = row.Rut.Trim();
            var firstName = NormalizeName(row.FirstName);
            var lastName = NormalizeName(row.LastName);

            if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rut))
            {
                throw new InvalidDataException(
                    $"Row {rowIndex} ({firstName} {lastName}) does not have a RUT. "
                    + "Every student must have a RUT.");
            }

            if (seen.Add(rut))
            {
                result.Add(new Student(rut, firstName, lastName));
            }
        }

        return result;
    }

    private static (string School, string Level, string Section) ExtractCourseInfo(IReadOnlyList<RawRow> rows)
    {
        // Course metadata is repeated on every row. The first non-empty
        // value wins; we trim but otherwise preserve the original casing
        // because school names and levels are user-facing strings.
        string FirstNonEmpty(Func<RawRow, string> selector) =>
            rows.Select(selector)
                .Select(static v => v?.Trim() ?? string.Empty)
                .FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v))
                ?? string.Empty;

        return (
            FirstNonEmpty(static r => r.School),
            FirstNonEmpty(static r => r.Level),
            FirstNonEmpty(static r => r.Section));
    }

    private static string NormalizeName(string value) =>
        value.Trim().ToUpper(ChileanSpanish);

    // -------------------------------------------------------------------------
    // Encoding detection
    // -------------------------------------------------------------------------

    private static Encoding DetectEncoding(string filePath)
    {
        // Spanish CSV exports from Excel are commonly produced in Latin-1.
        // Probing with a strict UTF-8 decoder detects that case without a BOM.
        try
        {
            using var sr = new StreamReader(filePath, new UTF8Encoding(false, true));
            _ = sr.ReadToEnd();
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    // -------------------------------------------------------------------------
    // Private types
    // -------------------------------------------------------------------------

    private sealed record RawRow(
        string Rut,
        string FirstName,
        string LastName,
        string School,
        string Level,
        string Section);

    private sealed record ColumnMap(
        int? Rut,
        int? FirstName,
        int? LastName,
        int? School,
        int? Level,
        int? Section);
}
