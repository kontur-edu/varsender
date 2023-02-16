namespace varsender.App;

public interface ITdMiddleware
{
    public Task Run(TdApi.Update update, Func<TdApi.Update, Task> next);
}
