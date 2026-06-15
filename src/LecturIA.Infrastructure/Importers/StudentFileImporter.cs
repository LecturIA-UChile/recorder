using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using CsvHelper;
using CsvHelper.Configuration;

using LecturIA.Core.Abstractions;
using LecturIA.Core.Models;

namespace LecturIA.Infrastructure.Importers;

/// <summary>
/// Imports a student list from an Excel (<c>.xlsx</c> / <c>.xls</c>) or CSV file.
/// </summary>
/// <remarks>
/// The importer prefers a column called <c>nombres</c> (case-insensitive).
/// When that column is not present, the first column of the file is used.
/// Names are normalized to upper case using Chilean Spanish rules,
/// duplicates are removed, and the order of first appearance is preserved.
/// </remarks>
public sealed class StudentFileImporter : IStudentImporter
{
    private const string PreferredColumnName = "nombres";

    private static readonly CultureInfo ChileanSpanish = new("es-CL");

    /// <inheritdoc />
    public Task<IReadOnlyList<Student>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The file to import was not found.", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        List<string> rawNames = extension switch
        {
            ".xlsx" or ".xls" => ReadFromExcel(filePath),
            ".csv" => ReadFromCsv(filePath),
            _ => throw new NotSupportedException(
                "Unsupported format. Only .xlsx, .xls and .csv files can be imported."),
        };

        var students = NormalizeNames(rawNames);
        if (students.Count == 0)
        {
            throw new InvalidDataException("No valid student names were found in the file.");
        }

        return Task.FromResult<IReadOnlyList<Student>>(students);
    }

    private static List<string> ReadFromExcel(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("The Excel file does not contain any worksheet.");

        var firstRow = sheet.FirstRowUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");

        var columnNumber = firstRow.Cells()
            .FirstOrDefault(c => string.Equals(
                c.GetString().Trim(), PreferredColumnName, StringComparison.OrdinalIgnoreCase))
            ?.Address.ColumnNumber ?? firstRow.FirstCellUsed().Address.ColumnNumber;

        var names = new List<string>();
        var dataRange = sheet.RangeUsed()
            ?? throw new InvalidDataException("The Excel file is empty.");

        foreach (var row in dataRange.RowsUsed().Skip(1))
        {
            names.Add(row.Cell(columnNumber).GetString());
        }

        return names;
    }

    private static List<string> ReadFromCsv(string filePath)
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
        var preferred = headers.FirstOrDefault(h =>
            string.Equals(h?.Trim(), PreferredColumnName, StringComparison.OrdinalIgnoreCase));

        var names = new List<string>();
        while (csv.Read())
        {
            var value = preferred is not null
                ? csv.GetField(preferred)
                : csv.GetField(0);
            if (!string.IsNullOrWhiteSpace(value))
            {
                names.Add(value);
            }
        }

        return names;
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // Spanish CSV exports from Excel are commonly produced in Latin-1.
        // Probing the file with a strict UTF-8 decoder lets us detect that
        // case without relying on a BOM.
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

    private static List<Student> NormalizeNames(IEnumerable<string> rawNames)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Student>();
        foreach (var raw in rawNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var normalized = raw.Trim().ToUpper(ChileanSpanish);
            if (seen.Add(normalized))
            {
                result.Add(new Student(normalized));
            }
        }

        return result;
    }
}
