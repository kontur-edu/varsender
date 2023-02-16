using Google.Apis.Http;

#nullable enable

namespace varsender.App;

public class ErrorsHandler : IHttpUnsuccessfulResponseHandler
{
    public Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
    {
        Console.WriteLine("Не могу выполнить запись в Google Таблицу.");
        Console.WriteLine(args.Response.StatusCode + " " + args.Response.ReasonPhrase);
        Console.WriteLine(args.Response.Content.ReadAsStringAsync().Result);
        return Task.FromResult(false);
    }
}
