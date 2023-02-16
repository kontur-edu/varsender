using System.Text;

namespace varsender.App;

public static class TdClientExtensions
{
    public static Action RegisterMiddlewares(this TdClient client, params ITdMiddleware[] middlewares)
    {
        Func<TdApi.Update, Task> processUpdates = update => Task.CompletedTask;
        foreach (var middleware in middlewares.Reverse().ToArray())
        {
            var next = processUpdates;
            processUpdates = async update => await middleware.Run(update, next);
        }
        EventHandler<TdApi.Update> handler = async (_, update) => { await processUpdates(update); };

        client.UpdateReceived += handler;

        return () => client.UpdateReceived -= handler;
    }

    public static async Task<TdApi.FormattedText> ParseMarkdownAsync(this TdClient client, string text)
    {
        return await client.ParseTextEntitiesAsync(text, new TdApi.TextParseMode.TextParseModeMarkdown());
    }

    public static TdApi.FormattedText Trim(this TdApi.FormattedText formattedText)
    {
        var textCopy = formattedText.ShallowCopy();

        var tempText = textCopy.Text.TrimStart();
        var trimmedAtStart = textCopy.Text.Length - tempText.Length;
        textCopy.Text = tempText.TrimEnd();

        var filteredEntities = new List<TdApi.TextEntity>();
        foreach (var entity in formattedText.Entities)
        {
            var entityCopy = entity.ShallowCopy();

            bool changed = trimmedAtStart != 0;
            entityCopy.Offset -= trimmedAtStart;

            if (entityCopy.Offset + entityCopy.Length <= 0)
            {
                continue;
            }
            else if (entityCopy.Offset < 0)
            {
                changed = true;
                entityCopy.Length += entityCopy.Offset;
                entityCopy.Offset = 0;
            }

            if (entityCopy.Offset >= textCopy.Text.Length)
            {
                continue;
            }
            else if (entityCopy.Offset + entityCopy.Length > textCopy.Text.Length)
            {
                changed = true;
                entityCopy.Length = textCopy.Text.Length - entityCopy.Offset;
            }

            filteredEntities.Add(changed ? entityCopy : entityCopy);
        }

        textCopy.Entities = filteredEntities.ToArray();
        return textCopy;
    }

    public static TdApi.FormattedText CleanupHashtags(this TdApi.FormattedText formattedText)
    {
        var textCopy = formattedText.ShallowCopy();

        var variables = formattedText.Entities
            .Where(e => e.Type is TdApi.TextEntityType.TextEntityTypeHashtag)
            .Select(e => formattedText.Text.Substring(e.Offset, e.Length))
            .Distinct()
            .ToDictionary(it => it, _ => string.Empty);

        textCopy.Entities = formattedText.Entities
            .Where(e => !(e.Type is TdApi.TextEntityType.TextEntityTypeHashtag))
            .ToArray();

        return Substitute(textCopy, variables);
    }

    public static TdApi.FormattedText Interpolate(this TdApi.FormattedText formattedText, IDictionary<string, string> variables)
    {
        var handlebarsVariables = variables.ToDictionary(it => "{{" + it.Key + "}}", it => it.Value);
        return Substitute(formattedText, handlebarsVariables);
    }

    public static TdApi.FormattedText Substitute(this TdApi.FormattedText formattedText, IDictionary<string, string> variables)
    {
        var textCopy = formattedText.ShallowCopy();

        foreach (var pair in variables)
        {
            (textCopy.Text, textCopy.Entities) = Substitute(textCopy.Text, textCopy.Entities, pair.Key, pair.Value);
        }

        if (variables.Count > 0)
        {
            var entities = new List<TdApi.TextEntity>();
            foreach (var entity in textCopy.Entities)
            {
                entities.Add(SubstituteUrl(entity, variables));
            }
            textCopy.Entities = entities.ToArray();
        }

        return textCopy;
    }

    public static (string, TdApi.TextEntity[]) Substitute(string inputText, TdApi.TextEntity[] entities, string key, string value)
    {
        if (inputText == string.Empty)
            return (string.Empty, entities);
        if (key == string.Empty)
            return (inputText, entities);

        var resultText = new StringBuilder();
        var resultEntities = entities.Select(ShallowCopy).ToArray();
        var currentIndex = 0;
        while (true)
        {
            var foundIndex = inputText.IndexOf(key, currentIndex, StringComparison.OrdinalIgnoreCase);
            if (foundIndex == -1)
            {
                resultText.Append(inputText.Substring(currentIndex));
                return (resultText.ToString(), resultEntities);
            }
            else
            {
                resultText.Append(inputText.Substring(currentIndex, foundIndex - currentIndex));
                var foundIndexInResult = resultText.Length;
                resultText.Append(value);

                foreach (var entity in resultEntities)
                {
                    var delta = value.Length - key.Length;

                    var left = entity.Offset;
                    var right = entity.Offset + entity.Length;

                    if (left >= foundIndexInResult + key.Length)
                    {
                        left = left + delta;
                    }
                    else
                    {
                        left = Math.Min(foundIndexInResult + value.Length, left);
                    }

                    if (right >= foundIndexInResult + key.Length)
                    {
                        right = right + delta;
                    }
                    else if (right > foundIndexInResult)
                    {
                        right = Math.Max(foundIndexInResult, Math.Min(foundIndexInResult + value.Length, right));
                    }

                    entity.Offset = left;
                    entity.Length = right - left;
                }

                currentIndex = foundIndex + key.Length;
            }
        }
    }

    private static TdApi.TextEntity SubstituteUrl(TdApi.TextEntity entity, IDictionary<string, string> variables)
    {
        if (entity.Type is TdApi.TextEntityType.TextEntityTypeTextUrl textUrlType && textUrlType.Url != string.Empty)
        {
            foreach (var pair in variables.Where(it => !string.IsNullOrEmpty(it.Key)))
            {
                if (textUrlType.Url.IndexOf(pair.Key, 0, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    var entityCopy = entity.ShallowCopy();
                    var typeCopy = textUrlType.ShallowCopy();
                    typeCopy.Url = pair.Value;
                    entityCopy.Type = typeCopy;
                    return entityCopy;
                }
            }
        }
        return entity;
    }


    public static async Task<TdApi.FormattedText?> GetTemplateAsync(this TdClient client, string hashtag)
    {
        var favChat = await client.GetFavoritesChatAsync();

        var tag = hashtag.StartsWith('#') ? hashtag : $"#{hashtag}";
        var message = (await client.SearchChatMessagesAsync(favChat.Id, tag, limit: 1))
            .Messages_.FirstOrDefault();
        if (message == null) return null;
        
        var messageText = message.Content as TdApi.MessageContent.MessageText;
        if (messageText == null) return null;
        
        return messageText.Text;
    }

    public static async Task<TdApi.Chat> GetFavoritesChatAsync(this TdClient client)
    {
        var currentUser = await client.GetMeAsync();
        return await client.CreatePrivateChatAsync(currentUser.Id);
    }

    public static async Task<TdApi.Chat?> SmartSearchChatAsync(this TdClient client, string query)
    {
        query = FixPrefix(query);

        if (string.IsNullOrWhiteSpace(query))
            return null;

        //{
        //    var contacts = await client.SearchContactsAsync(query, 5);
        //    if (contacts != null)
        //    {
        //        var suitableChats = new List<TdApi.Chat>();
        //        foreach (var userId in contacts.UserIds)
        //        {
        //            var chat = await ChoosePrivateChatByUserId(userId, query);
        //            if (chat != null)
        //                suitableChats.Add(chat);
        //        }
        //        var singleChat = suitableChats.SingleOrDefault();
        //        if (singleChat != null) return singleChat;
        //    }
        //}

        {
            var chats = await client.SearchChatsAsync(query, 5);
            var chat = await ChooseChatAsync(chats, query);
            if (chat != null) return chat;
        }

        {
            var chats = await client.SearchChatsOnServerAsync(query, 5);
            var chat = await ChooseChatAsync(chats, query);
            if (chat != null) return chat;
        }

        {
            var chats = await client.SearchPublicChatsAsync(query);
            var chat = await ChooseChatAsync(chats, query);
            if (chat != null) return chat;
        }

        return null;


        string FixPrefix(string q)
        {
            const string prefix1 = "https://t.me/";
            if (q.StartsWith(prefix1)) return "@" + q.Substring(prefix1.Length);

            const string prefix2 = "http://t.me/";
            if (q.StartsWith(prefix2)) return "@" + q.Substring(prefix2.Length);

            return q;
        }

        async Task<TdApi.Chat?> ChoosePrivateChatByUserId(long userId, string q)
        {
            if (!q.StartsWith("@"))
            {
                var chat = await client.CreatePrivateChatAsync(userId);
                return chat != null && chat.Title.Trim().StartsWith(q.Trim()) ? chat : null;
            }

            var user = await client.GetUserAsync(userId);
            var userName = q.TrimStart('@');
            return user?.Usernames?.ActiveUsernames?.Contains(userName) == true
                ? await client.CreatePrivateChatAsync(userId)
                : null;
        }

        async Task<TdApi.Chat?> ChooseChatAsync(TdApi.Chats chats, string q)
        {
            if (chats == null)
                return null;

            var suitableChats = new List<TdApi.Chat>();
            for (var i = 0; i < chats.TotalCount; i++)
            {
                var chat = await ChooseChatWithChatId(chats.ChatIds[i], q);
                if (chat != null)
                    suitableChats.Add(chat);
            }
            return suitableChats.SingleOrDefault();
        }

        async Task<TdApi.Chat?> ChooseChatWithChatId(long chatId, string q)
        {
            var chat = await client.GetChatAsync(chatId);
            if (!q.StartsWith("@"))
            {
                return chat != null && chat.Title.Trim().StartsWith(q.Trim()) ? chat : null;
            }

            var name = q.TrimStart('@');

            switch (chat.Type)
            {
                case TdApi.ChatType.ChatTypePrivate privateType:
                    var user = await client.GetUserAsync(privateType.UserId);
                    return user?.Usernames?.ActiveUsernames?.Contains(name) == true
                        ? chat
                        : null;
                case TdApi.ChatType.ChatTypeSupergroup supergroupType:
                    var superGroup = await client.GetSupergroupAsync(supergroupType.SupergroupId);
                    return superGroup?.Usernames?.ActiveUsernames?.Contains(name) == true
                        ? chat
                        : null;
                default:
                    return null;
            }
        }
    }

    public static async Task<string?> GetUsernameByUserId(this TdClient client, long userId)
    {
        var user = await client.GetUserAsync(userId);
        return user.GetFirstUsername();
    }

    public static string? GetFirstUsername(this TdApi.User user) =>
        user?.Usernames?.ActiveUsernames?.FirstOrDefault();

    public static string? GetFullName(this TdApi.User user) =>
        $"{user.FirstName.Trim()} {user.LastName.Trim()}";

    public static async Task<TdApi.Message> SendPostponedMessageAsync(this TdClient client, TdApi.Chat chat, DateTime utcSendTime,
        TdApi.FormattedText text, bool enableWebPagePreview = false)
    {
        var content = new TdApi.InputMessageContent.InputMessageText
        {
            DisableWebPagePreview = !enableWebPagePreview,
            Text = text
        };

        var result = await client.ExecuteAsync(new TdApi.SendMessage
        {
            ChatId = chat.Id,
            InputMessageContent = content,
            Options = new TdApi.MessageSendOptions
            {
                SchedulingState = new TdApi.MessageSchedulingState.MessageSchedulingStateSendAtDate()
                {
                    SendDate = (int)((DateTimeOffset)utcSendTime).ToUnixTimeSeconds()
                }
            }
        });
        return result;
    }

    public static TdApi.FormattedText ShallowCopy(this TdApi.FormattedText input) =>
        new TdApi.FormattedText
        {
            DataType = input.DataType,
            Entities = input.Entities,
            Extra = input.Extra,
            Text = input.Text
        };

    public static TdApi.TextEntity ShallowCopy(this TdApi.TextEntity input) =>
        new TdApi.TextEntity
        {
            DataType = input.DataType,
            Extra = input.Extra,
            Length = input.Length,
            Offset = input.Offset,
            Type = input.Type,
        };

    public static TdApi.TextEntityType.TextEntityTypeTextUrl ShallowCopy(this TdApi.TextEntityType.TextEntityTypeTextUrl input) =>
        new TdApi.TextEntityType.TextEntityTypeTextUrl
        {
            DataType = input.DataType,
            Extra = input.Extra,
            Url = input.Url,
        };
}
