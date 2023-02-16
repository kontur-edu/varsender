using System.Text.RegularExpressions;

namespace varsender.App;

public class TdAuthMiddleware : ITdMiddleware
{
    private readonly TdClient client;
    private readonly Settings settings;
    private readonly ManualResetEventSlim readyToAuthenticate = new();
    private bool authNeeded;
    private bool passwordNeeded;

    public TdAuthMiddleware(TdClient client, Settings settings)
    {
        this.client = client;
        this.settings = settings;
    }

    public async Task Run(TdApi.Update update, Func<TdApi.Update, Task> next)
    {
        switch (update)
        {
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters }:
                // TdLib creates database in the current directory.
                // so create separate directory and switch to that dir.
                var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                await client.SetTdlibParametersAsync(
                    apiId: settings.TelegramApiId,
                    apiHash: settings.TelegramApiHash,
                    deviceModel: "PC",
                    systemLanguageCode: "en",
                    applicationVersion: settings.TelegramApplicationVersion,
                    databaseDirectory: filesLocation,
                    filesDirectory: filesLocation
                );
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber }:
            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitCode }:
                authNeeded = true;
                readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPassword }:
                authNeeded = true;
                passwordNeeded = true;
                readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateUser:
                readyToAuthenticate.Set();
                break;

            case TdApi.Update.UpdateConnectionState { State: TdApi.ConnectionState.ConnectionStateReady }:
                // You may trigger additional event on connection state change
                break;

            default:
                // ReSharper disable once EmptyStatement
                ;
                // Add a breakpoint here to see other events
                break;
        }

        await next(update);
    }

    public async Task WaitAsync()
    {
        // Waiting until we get enough events to be in 'authentication ready' state
        readyToAuthenticate.Wait();

        // We may not need to authenticate since TdLib persists session in 'td.binlog' file.
        // See 'TdlibParameters' class for more information, or:
        // https://core.telegram.org/tdlib/docs/classtd_1_1td__api_1_1tdlib_parameters.html
        if (authNeeded)
        {
            // Interactively handling authentication
            await HandleAuthentication(client);
        }
    }

    private async Task HandleAuthentication(TdClient client)
    {
        await AskAndSetPhoneNumberAsync(client);
        await AskAndSetLoginCodeAsync(client);

        if (!passwordNeeded)
            return;
        await AskAndSetPasswordAsync(client);
    }

    private async Task AskAndSetPhoneNumberAsync(TdClient client)
    {
        var phoneRegex = new Regex("\\+7[0-9]{10}");
        string phoneNumber;

        while (true)
        {
            ConsoleHelper.WriteCommandLinePrompt("Введите номер телефона в формте +7 xxx xxxxxxxx:");
            phoneNumber = Console.ReadLine() ?? string.Empty;

            if (!string.IsNullOrEmpty(phoneNumber))
            {
                phoneNumber = phoneNumber.Replace(" ", "").Trim();
                if (phoneRegex.IsMatch(phoneNumber))
                    break;
            }
        }

        await client.SetAuthenticationPhoneNumberAsync(phoneNumber);
    }

    private async Task AskAndSetLoginCodeAsync(TdClient client)
    {
        ConsoleHelper.WriteCommandLinePrompt("Введите подтверждающий код, пришедший в Telegram или SMS:");
        var code = Console.ReadLine()?.Trim() ?? string.Empty;
        await client.CheckAuthenticationCodeAsync(code);
    }

    private async Task AskAndSetPasswordAsync(TdClient client)
    {
        ConsoleHelper.WriteCommandLinePrompt("Введите пароль:");
        var password = Console.ReadLine() ?? string.Empty;
        await client.CheckAuthenticationPasswordAsync(password);
    }
}
