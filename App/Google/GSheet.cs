using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace varsender.App;

public class GSheet
{
    public GSheet(string spreadsheetId, string sheetName, int sheetId, SheetsService sheetsService)
    {
        SpreadsheetId = spreadsheetId;
        SheetName = sheetName;
        SheetId = sheetId;
        SheetsService = sheetsService;
    }

    public readonly string SpreadsheetId;
    public readonly string SheetName;
    public readonly int SheetId;
    public readonly SheetsService SheetsService;

    public string ReadCell((int, int) cellCoords)
    {
        var (top, left) = cellCoords;
        var range = $"R{top + 1}C{left + 1}";
        var values = ReadRange(range);
        return values.First().First();
    }

    public List<List<string>> ReadRange((int top, int left) rangeStart, (int top, int left) rangeEnd)
    {
        var (top, left) = rangeStart;
        var (bottom, right) = rangeEnd;
        var range = $"R{top+1}C{left+1}:R{bottom+1}C{right+1}";
        return ReadRange(range);
    }

    public GSheetEditsBuilder Edit()
    {
        return new GSheetEditsBuilder(SheetsService, SpreadsheetId, SheetId);
    }

    public void ClearRange(string sheetName, (int top, int left) rangeStart, (int top, int left) rangeEnd)
    {
        var (top, left) = rangeStart;
        var (bottom, right) = rangeEnd;
        var range = $"R{top+1}C{left+1}:R{bottom+1}C{right+1}";
        var fullRange = $"{sheetName}!{range}";
        var requestBody = new ClearValuesRequest();
        var deleteRequest = SheetsService.Spreadsheets.Values.Clear(requestBody, SpreadsheetId, fullRange);
        var deleteResponse = deleteRequest.Execute();
    }

    private List<List<string>> ReadRange(string range)
    {
        var fullRange = $"{SheetName}!{range}";
        var request = SheetsService.Spreadsheets.Values.Get(SpreadsheetId, fullRange);
        var response = request.Execute();
        var values = response.Values ?? new List<IList<object>>();
        var result = values.Select(l => l?.Select(o => o?.ToString() ?? "").ToList() ?? new List<string>()).ToList();
        return result;
    }
}
