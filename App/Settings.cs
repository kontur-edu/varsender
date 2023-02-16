using dotenv.net;

namespace varsender.App;

public class Settings
{
    public static Settings Instance => instance.Value;

    public int TelegramApiId;
    public string TelegramApiHash = string.Empty;
    public string TelegramApplicationVersion = string.Empty;
    public string GoogleClientId = string.Empty;
    public string GoogleClientSecret = string.Empty;
    public string GoogleApplicationName = string.Empty;

    private static Lazy<Settings> instance = new Lazy<Settings>(LoadSettingsFromEnv);

    private static Settings LoadSettingsFromEnv()
    {
        LoadEnv();

        var env = Environment.GetEnvironmentVariables();

        var settings = new Settings
        {
            TelegramApiId = int.TryParse(GetFromEnv("TELEGRAM_API_ID"), out var id)
                ? id
                : throw new InvalidOperationException($"Can't get TELEGRAM_API_ID from settings."),
            TelegramApiHash = GetFromEnv("TELEGRAM_API_HASH"),
            TelegramApplicationVersion = GetFromEnv("TELEGRAM_APPLICATION_VERSION"),
            GoogleClientId = GetFromEnv("GOOGLE_CLIENT_ID"),
            GoogleClientSecret = GetFromEnv("GOOGLE_CLIENT_SECRET"),
            GoogleApplicationName = GetFromEnv("GOOGLE_APPLICATION_NAME")
        };

        return settings;

        string GetFromEnv(string key)
        {
            if (!env.Contains(key))
                throw new InvalidOperationException($"Can't get {key} from settings.");
            return (string)env[key]!;
        }
    }

    private static void LoadEnv()
    {
        const string envPath = "./.env";
        DotEnv.Load(new DotEnvOptions().WithoutExceptions().WithEnvFiles(envPath));
    }
}
