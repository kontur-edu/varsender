using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace varsender.App;

public class GAuth
{
    public static async Task<BaseClientService.Initializer> AuthAsync(Settings settings)
    {
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = settings.GoogleClientId,
                ClientSecret = settings.GoogleClientSecret,
            },
            new[] { SheetsService.Scope.Spreadsheets },
            "user",
            CancellationToken.None);

        return new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = settings.GoogleApplicationName
        };
    }
}
