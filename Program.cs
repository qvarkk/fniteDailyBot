using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

Dictionary<long, UserState> userStates = new();

HttpClient sharedClient = new HttpClient();
sharedClient.DefaultRequestHeaders.Add("Authorization", Environment.GetEnvironmentVariable("API_TOKEN"));

using var cts = new CancellationTokenSource();
var bot = new TelegramBotClient(Environment.GetEnvironmentVariable("BOT_TOKEN")!, cancellationToken: cts.Token);
var me = await bot.GetMeAsync();
bot.OnMessage += OnMessage;
bot.OnUpdate += OnUpdate;
bot.OnError += OnError;

Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
Console.ReadLine();
cts.Cancel();

async Task OnMessage(Message msg, UpdateType type)
{

    if (!userStates.ContainsKey(msg.Chat.Id))
    {
        userStates[msg.Chat.Id] = new UserState();
    }

    var userState = userStates[msg.Chat.Id];

    if (msg.Text == "/start")
    {
        await bot.SendTextMessageAsync(msg.Chat, "Welcome, choose an option below",
            replyMarkup: new InlineKeyboardMarkup().AddButtons("Stats", "News"));
    } else if (userState.state == State.AwaitingUsername)
    {
        await ProcessStatsAPICall(msg, userState.platform);
    } else
    {
        await bot.SendTextMessageAsync(msg.Chat, "❌ No such command");
    }
}

async Task OnUpdate(Update update)
{
    if (update is { CallbackQuery: { } query })
    {
        long chatId = query.Message!.Chat.Id;

        if (!userStates.ContainsKey(chatId))
        {
            userStates[chatId] = new UserState();
        }

        var userState = userStates[chatId];

        await bot.AnswerCallbackQueryAsync(query.Id, $"You picked {query.Data}");

        switch (query.Data)
        {
            case "News":
                await ProcessNewsAPICall(query);
                break;
            case "Stats":
                await bot.SendTextMessageAsync(query.Message!.Chat, "Pick platform",
                    replyMarkup: new InlineKeyboardMarkup().AddButtons("Epic", "Xbox", "Playstation"));
                break;
            case "Epic":
                userState.platform = "epic";
                userState.state = State.AwaitingUsername;
                await bot.SendTextMessageAsync(query.Message!.Chat, "Send your username");
                break;
            case "Xbox":
                userState.platform = "xbl";
                userState.state = State.AwaitingUsername;
                await bot.SendTextMessageAsync(query.Message!.Chat, "Send your username");
                break;
            case "Playstation":
                userState.platform = "psn";
                userState.state = State.AwaitingUsername;
                await bot.SendTextMessageAsync(query.Message!.Chat, "Send your username");
                break;
            default:
                await bot.SendTextMessageAsync(query.Message!.Chat, "❌ Wrong Chat Option");
                break;
        }  
    }
}


async Task OnError(Exception e, HandleErrorSource h)
{
    Console.WriteLine(e);
}

async Task ProcessStatsAPICall(Message message, String platform)
{
    try
    {
        string endpoint = $"https://fortnite-api.com/v2/stats/br/v2?name={Uri.EscapeDataString(message.Text!)}&platform={platform}";        

        // Send request
        HttpResponseMessage response = await sharedClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        // Process response
        string responseBody = await response.Content.ReadAsStringAsync();
        JObject json = JObject.Parse(responseBody);

        var data = json["data"];

        var name = data!["account"]!["name"];
        var bpLvl = data!["battlePass"]!["level"];
        var stats = data!["stats"]!["all"];

        var soloWins = stats!["solo"]!["wins"];
        var soloKills = stats!["solo"]!["kills"];
        var soloKd = stats!["solo"]!["kd"];
        var soloMatches = stats!["solo"]!["matches"];

        var duoWins = stats!["duo"]!["wins"];
        var duoKills = stats!["duo"]!["kills"];
        var duoKd = stats!["duo"]!["kd"];
        var duoMatches = stats!["duo"]!["matches"];

        var squadWins = stats!["squad"]!["wins"];
        var squadKills = stats!["squad"]!["kills"];
        var squadKd = stats!["squad"]!["kd"];
        var squadMatches = stats!["squad"]!["matches"];

        string textMessage = $"""
            🧏  {name}

            🌟 Battle Pass level: {bpLvl} 🔥

            👤 Solo stats:
            {soloWins} Wins 🏆
            {soloKills} Kills 🔫
            {soloKd} Kd 🥶
            {soloMatches} Matches 🏝️

            👥 Duo stats:
            {duoWins} Wins 🏆
            {duoKills} Kills 🔫
            {duoKd} Kd 🥶
            {duoMatches} Matches 🏝️

            ⚔️ Squads stats:
            {squadWins} Wins 🏆
            {squadKills} Kills 🔫
            {squadKd} Kd 🥶
            {squadMatches} Matches 🏝️
            """;

        await bot.SendTextMessageAsync(message.Chat, textMessage);
        userStates[message.Chat.Id].state = State.Other;

    } catch (HttpRequestException e)
    {
        Console.WriteLine($"Request error: {e.Message}");
        await bot.SendTextMessageAsync(message.Chat, "❌ No player with this name\n❗ Check if your nickname is correct");
        userStates[message.Chat.Id].state = State.Other;
    }
}

async Task ProcessNewsAPICall(CallbackQuery query)
{    
    try
    {
        string endpoint = "https://fortnite-api.com/v2/news";

        // Send request
        HttpResponseMessage response = await sharedClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        // Process response
        string responseBody = await response.Content.ReadAsStringAsync();
        JObject json = JObject.Parse(responseBody);

        if (json["data"]!["br"] == null)
        {
            await bot.SendTextMessageAsync(query.Message!.Chat, "❌ No news for today it seems\n❗ Check if servers are up");
            return;
        }

        var motds = json["data"]!["br"]!["motds"];
        string message = "🔥 Battle Royale News\n\n";

        // Setup message media
        List<InputMediaPhoto> newsPhotos = new List<InputMediaPhoto>();

        // Form message and media list
        foreach (var news in motds!)
        {
            message += $"❗{news["tabTitle"]} - {news["body"]}\n\n";
            var newsPhotoUri = news["image"]!.ToString();
            newsPhotos.Add(new InputMediaPhoto(InputFile.FromString(newsPhotoUri)));
        }
        newsPhotos[0].Caption = message;

        // Send formed message with media
        await bot.SendMediaGroupAsync(query.Message!.Chat, newsPhotos.ToArray());

    } catch (HttpRequestException e)
    {
        Console.WriteLine($"Request error: {e.Message}");
        await bot.SendTextMessageAsync(query.Message!.Chat, "❌ Some error occured on Epic side\n❗ Check if servers are up");

    }
}

enum State { AwaitingUsername, Other }

class UserState
{
    public State state { get; set; } = State.Other;
    public string platform { get; set; } = "";
}