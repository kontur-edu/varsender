using System.Text.RegularExpressions;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace varsender.App;

public class GSheetClient
{
    public static Regex UrlRegex = new Regex("https://docs.google.com/spreadsheets/d/(.+)/edit#gid=(.+)", RegexOptions.Compiled);

    public GSheetClient(BaseClientService.Initializer initializer)
    {
        SheetsService = new SheetsService(initializer);
    }

    public GSpreadsheet GetSpreadsheet(string spreadsheetId) =>
        new GSpreadsheet(spreadsheetId, SheetsService);

    public GSheet GetSheetByUrl(string url)
    {
        var match = UrlRegex.Match(url);
        var spreadsheetId = match.Groups[1].Value;
        var sheetId = int.Parse(match.Groups[2].Value);
        return GetSpreadsheet(spreadsheetId).GetSheetById(sheetId);
    }

    private SheetsService SheetsService { get; }
}
