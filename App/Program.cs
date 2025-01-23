using System.Text.RegularExpressions;
using varsender.App;


var tdClient = await ConfigureTdClientAsync();
var sheetClient = await ConfigureGSheetClientAsync();

// await ExtractReportsAsync(tdClient); return;



string sheetUrl;
while (true)
{
    ConsoleHelper.WriteCommandLinePrompt("Введите ссылку на лист Google Таблицы с сообщениями:");
    sheetUrl = Console.ReadLine()?.Trim() ?? "";
    if (GSheetClient.UrlRegex.IsMatch(sheetUrl))
        break;
    ConsoleHelper.WriteInfoLine("Неверный формат. Предполагается ссылка вида: https://docs.google.com/spreadsheets/d/ИДЕНТИФИКАТОР-ТАБЛИЦЫ/edit#gid=ИДЕНТИФИКАТОР-ЛИСТА.");
}

Console.WriteLine();

ConsoleHelper.WriteCommandLinePrompt("Введите символ режима работы и нажмите ENTER. Пустая строка или \"s\" для отправки, \"d\" для удаления, \"x\" для экспериментальной функции");
Console.WriteLine();
var approveCommand = Console.ReadLine()?.Trim()?.ToLowerInvariant();
if (approveCommand == "d")
{
    await ProcessDeletePostponedMessagesAsync();
}
else if (approveCommand == "" || approveCommand == "s")
{
    await ProcessSendingLettersAsync();
}
else
{
    ConsoleHelper.WriteCommandLinePrompt("Неизвеестный режим работы. Нажмите ENTER, чтобы выйти из приложения");
    Console.ReadLine();
}

async Task ProcessDeletePostponedMessagesAsync()
{
    var letters = ReadLetters(sheetClient, sheetUrl);
    var tos = letters.Select(l => l.To).Distinct().ToList();
    if (tos.Count == 0)
    {
        ConsoleHelper.WriteInfoLine("Не удалось зачитать ни одного отправителя...");
        ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы выйти из приложения");
        Console.ReadLine();
        return;
    }

    foreach (var to in tos)
    {
        ConsoleHelper.WriteInfoLine($"Обработка отложенных сообщений для {to}");

        var chat = await tdClient.SmartSearchChatAsync(to);

        if (chat != null)
        {
            var username = chat.Type is TdApi.ChatType.ChatTypePrivate chatType ? await tdClient.GetUsernameByUserId(chatType.UserId) : null;
            ConsoleHelper.WriteInfo("Чат: ");
            if (username != null)
                Console.WriteLine($"{chat.Title} / {username}");
            else
                Console.WriteLine($"{chat.Title}");

            var messages = await tdClient.SearchPostponedMessageAsync(chat, string.Empty);
            var deletingMessages = new List<TdApi.Message>();
            foreach (var message in messages)
            {
                if (message.Content is TdApi.MessageContent.MessageText messageText)
                {
                    long sendTimestamp = message.Date;
                    if (message.SchedulingState is TdApi.MessageSchedulingState.MessageSchedulingStateSendAtDate sendAtDate)
                        sendTimestamp = sendAtDate.SendDate;

                    var sendTime = DateTimeOffset.FromUnixTimeSeconds(sendTimestamp).DateTime;
                    ConsoleHelper.WriteInfo("Время отправки: ");
                    Console.WriteLine($"{sendTime.ToString("ddd dd.MM.yyyy HH:mm")} (локальное)");
                    Console.WriteLine();
                    Console.WriteLine(messageText.Text.Text);
                    Console.WriteLine();
                    Console.WriteLine();

                    deletingMessages.Add(message);
                }
            }

            ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы продолжить без удаления сообщений, либо введите символ \"d\" или слово \"delete\", чтобы удалить сообщения для этого получателя.");
            Console.WriteLine();
            var command = Console.ReadLine()?.Trim()?.ToLowerInvariant();
            if (command == "d" || command == "delete")
            {
                await tdClient.DeleteMessagesForAllAsync(chat, deletingMessages);
                ConsoleHelper.WriteInfoLine("Сообщения были удалены");
                Console.WriteLine();
            }
        }
        else
        {
            ConsoleHelper.WriteErrorLine($"Не удалось определить адресата...");
            Console.WriteLine();
        }
    }

    ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы выйти из приложения");
    Console.ReadLine();
}

async Task ProcessSendingLettersAsync()
{
    var letters = ReadLetters(sheetClient, sheetUrl);
    if (letters.Count == 0)
    {
        ConsoleHelper.WriteInfoLine("Не удалось зачитать ни одного сообщения для отправки...");
        ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы выйти из приложения");
        Console.ReadLine();
        return;
    }

    ConsoleHelper.WriteInfoLine($"Было зачитано {letters.Count} {letters.Count.Pluralize("сообщение", "сообщения", "сообщений")} для отправки...");
    ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы перейти к просмотру сообщений");
    Console.ReadLine();


    var tdLetters = new List<TdLetter>();
    var withoutPreview = false;
    foreach (var letter in letters)
    {
        var tdLetter = await PrepareLetterAsync(tdClient, letter, withoutPreview);
        if (tdLetter != null)
            tdLetters.Add(tdLetter);

        if (!withoutPreview)
        {
            ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы продолжить, либо введите символ \"a\" или слово \"approve\", чтобы подготовить остальные сообщения без предварительного просмотра.");
            Console.WriteLine();
            var approveCommand = Console.ReadLine()?.Trim()?.ToLowerInvariant();
            if (approveCommand == "a" || approveCommand == "approve")
                withoutPreview = true;
        }
        Thread.Sleep(100);
    }


    ConsoleHelper.WriteInfoLine($"Было подготовлено и проверено {tdLetters.Count} {tdLetters.Count.Pluralize("сообщение", "сообщения", "сообщений")} для отправки...");
    ConsoleHelper.WriteCommandLinePrompt("Введите символ \"s\" или слово \"send\" и нажмите ENTER, чтобы отправить сообщения, либо нажмите ENTER, чтобы выйти из приложения.");
    var sendCommand = Console.ReadLine()?.Trim()?.ToLowerInvariant();
    Console.WriteLine();
    if (sendCommand != "s" && sendCommand != "send")
        return;

    foreach (var letter in tdLetters)
    {
        await SendLetterAsync(tdClient, letter);
        Thread.Sleep(300);
    }

    ConsoleHelper.WriteCommandLinePrompt("Нажмите ENTER, чтобы выйти из приложения");
    Console.ReadLine();
}

async Task<TdClient> ConfigureTdClientAsync()
{
    var client = new TdClient();
    client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);
    Console.Clear();

    ConsoleHelper.WriteInfoLine("Выполняется вход в Telegram...");

    var authMiddleware = new TdAuthMiddleware(client, Settings.Instance);
    client.RegisterMiddlewares(authMiddleware);
    await authMiddleware.WaitAsync();

    var currentUser = await client.GetMeAsync();
    ConsoleHelper.WriteSuccessLine($"Выполнен вход в Telegram в качестве @{currentUser.GetFirstUsername()} / {currentUser.GetFullName()} / {currentUser.Id}");
    Console.WriteLine();

    return client;
}

async Task<GSheetClient> ConfigureGSheetClientAsync()
{
    ConsoleHelper.WriteInfoLine("Выполняется вход в Google Таблицы...");
    var sheetClient = new GSheetClient(await GAuth.AuthAsync(Settings.Instance));
    ConsoleHelper.WriteSuccessLine($"Выполнен вход в Google Таблицы");
    Console.WriteLine();
    return sheetClient;
}


IList<Letter> ReadLetters(GSheetClient sheetClient, string url)
{
    var spreadsheet = sheetClient.GetSheetByUrl(url);
    var range = spreadsheet.ReadRange((0, 0), (1000, 1000));

    var letters = new List<Letter>();
    var head = range.FirstOrDefault();
    if (head == null)
        return letters;

    var variableToIndex = new Dictionary<string, int>();
    var indexToVariable = new Dictionary<int, string>();
    foreach (var it in head
        .Select((v, i) => (index: i, value: v.Trim().ToLowerInvariant()))
        .Where(it => !string.IsNullOrEmpty(it.value)))
    {
        variableToIndex[it.value] = it.index;
        indexToVariable[it.index] = it.value;
    }

    if (!variableToIndex.TryGetValue("to", out var toIndex)
        || !variableToIndex.TryGetValue("sendtime", out var sendTimeIndex)
        || !variableToIndex.TryGetValue("template", out var templateIndex))
        return letters;

    foreach (var row in range.Skip(1))
    {
        var template = row.ElementAtOrDefault(templateIndex)?.Trim();
        var to = row.ElementAtOrDefault(toIndex)?.Trim();
        var sendTime = row.ElementAtOrDefault(sendTimeIndex)?.Trim();

        if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(sendTime))
            continue;

        var letter = new Letter(to, sendTime, template);

        for (int i = 0; i < row.Count; i++)
        {
            if (indexToVariable.TryGetValue(i, out var variableName))
                letter.AddVariable(variableName, row[i].Trim());
        }

        letters.Add(letter);
    }

    return letters;
}

async Task<TdLetter?> PrepareLetterAsync(TdClient tdClient, Letter letter, bool withoutPreview = false)
{
    ConsoleHelper.WriteInfoLine($"Подготовка сообщения для {letter.To}");

    var chat = await FindChatForAsync(letter, tdClient);
    var sendTime = BuildSendTimeFor(letter);
    var message = await BuildLetterTextForAsync(letter, tdClient);

    if (chat != null)
    {
        if (!withoutPreview)
        {

            var username = chat.Type is TdApi.ChatType.ChatTypePrivate chatType ? await tdClient.GetUsernameByUserId(chatType.UserId) : null;
            ConsoleHelper.WriteInfo("Чат: ");
            if (username != null)
                Console.WriteLine($"{chat.Title} / {username}");
            else
                Console.WriteLine($"{chat.Title}");
            ConsoleHelper.WriteInfo("Время отправки: ");
            Console.WriteLine($"{sendTime.ToString("ddd dd.MM.yyyy HH:mm")} (локальное)");
            Console.WriteLine();
            Console.WriteLine(message.Text);
            Console.WriteLine();
            var urlEntities = message.Entities.Where(it => it.Type is TdApi.TextEntityType.TextEntityTypeTextUrl).ToList();
            if (urlEntities.Count > 0)
            {
                ConsoleHelper.WriteInfoLine("Ссылки в сообщении:");
                foreach (var urlEntity in urlEntities)
                {
                    var textUrlType = (TdApi.TextEntityType.TextEntityTypeTextUrl)urlEntity.Type;
                    Console.WriteLine($"- {message.Text.Substring(urlEntity.Offset, urlEntity.Length)}: {textUrlType.Url}");
                }
            }
            else
            {
                ConsoleHelper.WriteInfoLine("Ссылок в сообщении нет");
            }
        }
        else
        {
            var username = chat.Type is TdApi.ChatType.ChatTypePrivate chatType ? await tdClient.GetUsernameByUserId(chatType.UserId) : null;
            ConsoleHelper.WriteInfo("Чат: ");
            if (username != null)
                Console.WriteLine($"{chat.Title} / {username}");
            else
                Console.WriteLine($"{chat.Title}");
            ConsoleHelper.WriteInfo("Время отправки: ");
            Console.WriteLine($"{sendTime.ToString("ddd dd.MM.yyyy HH:mm")} (локальное)");
            ConsoleHelper.WriteSuccessLine($"Сообщение готово");
            Console.WriteLine();
        }

        return new TdLetter(chat, sendTime.ToUniversalTime(), message);
    }
    else
    {
        ConsoleHelper.WriteErrorLine($"Не удалось определить адресата...");
        Console.WriteLine();

        return null;
    }
}

async Task<TdApi.Chat?> FindChatForAsync(Letter letter, TdClient tdClient) =>
    await tdClient.SmartSearchChatAsync(letter.To);

DateTime BuildSendTimeFor(Letter letter) =>
    DateTime.TryParse(letter.SendTime, out var time) ? time : new DateTime().AddMinutes(10);

async Task<TdApi.FormattedText> BuildLetterTextForAsync(Letter letter, TdClient tdClient)
{
    var template = letter.Template.IsHashtag()
        ? await tdClient.GetTemplateAsync(letter.Template)
        : await tdClient.ParseMarkdownAsync(letter.Template);

    var message = template!.CleanupHashtags();
    message = message.Trim();
    var variables = letter.GetVariables();
    message = message.Interpolate(variables);

    return message;
}

async Task SendLetterAsync(TdClient client, TdLetter letter)
{
    ConsoleHelper.WriteInfoLine($"Отправка сообщения для {letter.To.Title}");
    await client.SendPostponedMessageAsync(letter.To, letter.UtcDateTime, letter.Text, false);
    ConsoleHelper.WriteSuccessLine($"Сообщение отправлено");
    Console.WriteLine();
}

async Task ExtractReportsAsync(TdClient client)
{
    var chatName = "Чат Проектный практикум ФИИТ";
    var startDateTime = new DateTime(2024, 09, 10);
    var outputFileName = "reports.csv";
    var outputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), outputFileName);
    
    var messages = await client.SelectChatMessagesAsync(chatName, startDateTime);
    var reports = ExtractReportMessages(messages);

    var goodReports = reports.Where(r => r.Ready).OrderBy(r => r.Team).ToList();
    var badReports = reports.Where(r => !r.Ready).ToList();
    var lines = new List<string>();
    foreach (var r in goodReports)
    {
        Console.WriteLine(r);
        lines.Add(r.ToCsv());
    }

    Console.WriteLine();
    Console.WriteLine();

    foreach (var r in badReports)
    {
        Console.WriteLine(r);
    }

    File.WriteAllLines(outputPath, lines);
}

IList<ReportMessage> ExtractReportMessages(List<TdApi.Message> messages)
{
    var texts = messages.ExtractTexts();

    var reports = new List<ReportMessage>();
    var reportRegex = new Regex("^.{0,2}([#отчеёт]{5})", RegexOptions.IgnoreCase);
    var teamRegex = new Regex("^.{0,2}(([команда\\:]{6})|([team\\:]{4}))", RegexOptions.IgnoreCase);
    var projectRegex = new Regex("^.{0,2}(([проект\\:]{5})|([project\\:]{5}))", RegexOptions.IgnoreCase);
    var sprintRegex = new Regex("^.{0,2}(([спринт\\:]{5})|([sprint\\:]{5}))", RegexOptions.IgnoreCase);
    foreach (var t in texts)
    {
        var lines = t.Text.Text.Split('\n');
        var report = FoundValue(lines, reportRegex);
        var team = FoundValue(lines, teamRegex);
        var project = FoundValue(lines, projectRegex);
        var sprint = FoundValue(lines, sprintRegex);
        var ready = report != null && (team != null || project != null) && sprint != null;
        var reportMessage = new ReportMessage(t.Text.Text,
            report ?? string.Empty,
            team ?? string.Empty,
            project ?? string.Empty,
            sprint ?? string.Empty,
            t.Time,
            ready);
        reports.Add(reportMessage);
    }
    return reports;

    string? FoundValue(string[] lines, Regex regex) =>
        ExtractValue(lines.FirstOrDefault(l => regex.IsMatch(l)));

    string? ExtractValue(string? line)
    {
        if (line == null)
            return null;
        var parts = line.Split(':');
        if (parts.Length < 2)
            return line.Trim();
        return parts[1].Trim();
    }
}

record ReportMessage(
    string Text,
    string Report, string Team, string Project, string Sprint,
    DateTime Time,
    bool Ready)
{
    public override string ToString()
    {
        var r = Ready ? "v" : "x";
        return $"{r} r={Report}, t={Team}, p={Project}, s={Sprint}, t={Time}";
    }

    public string ToCsv()
    {
        return $"{Team};{Project};{Sprint};{Time.ToShortDateString()}";
    }
}
