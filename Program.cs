using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
    static string endpoint;

    static string GetAcct(User user)
    {
        return user.Host != null
            ? $"{user.Username}@{user.Host}"
            : user.Username;
    }

    static async Task Main(string[] args)
    {
        string token;
        string host;

        async Task RetryAsync(Exception e)
        {
            Console.WriteLine($"例外がスローされました: {e.Message}");
            Console.WriteLine("15分後に再試行します");
            await Task.Delay(899000);
        }

        // -- 認証
        Console.WriteLine("Write your token. It's at Settings > API.");
        do
        {
            Console.Write("> ");
            token = Console.ReadLine();
        } while (string.IsNullOrWhiteSpace(token));

        Console.WriteLine("Write your server's host. e.g: misskey.io");
        do
        {
            Console.Write("> ");
            host = Console.ReadLine();
        } while (string.IsNullOrWhiteSpace(host));

        if (host.StartsWith("http://"))
            host = host.Remove(0, "http://".Length);
        if (host.StartsWith("https://"))
            host = host.Remove(0, "https://".Length);
        if (host.EndsWith("/"))
            host = host.Remove(host.Length - 1);

        endpoint = $"https://{host}/api/";

        // -- ユーザー情報をフェッチ

        var me = await PostAsync<User>("i", new { i = token });

        Console.WriteLine("アカウント情報を取得しました:");
        Console.WriteLine($" {me.Name ?? me.Username} @{GetAcct(me)}");
        Console.WriteLine($" {me.NotesCount} ノート");
        Console.WriteLine($" id: {me.Id}");

        Console.WriteLine("フォローしているユーザー全員の情報を取得中...");

        var kataomoi = (await PostPaginationAsync<Following>("users/following", new { i = token, userId = me.Id }))
            .Select(f => f.Followee)
            .Where(f => !f.IsFollowed);
        var kataomoiCount = kataomoi.Count();

        foreach (var u in kataomoi)
        {
            Console.WriteLine($"{u.Name ?? u.Username} @{GetAcct(u)}");
        }

        Console.WriteLine($"フォローしているうち {kataomoiCount} 人のユーザーがあなたをフォローしていません。全てフォロー解除しますか？ (y/N) ? ");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            Console.WriteLine("希望するモードを次の数字で選んでください:\n1. 一括で全てフォローを外す\n2. ひとりひとり確認してフォローを外す");
            var isAllMode = Console.ReadLine().Trim() == "1";
            foreach (var u in kataomoi)
            {
                try
                {
                    var delete = isAllMode;
                    if (!isAllMode)
                    {
                        Console.WriteLine($"@{GetAcct(u)} をフォロー解除しますか？ (y/N)");
                        delete = Console.ReadLine().ToLowerInvariant() == "y";
                    }

                    if (delete)
                        await PostAsync("following/delete", new { i = token, userId = u.Id });

                    if (isAllMode)
                        Console.WriteLine($"{GetAcct(u)} をフォロー解除しました");
                }
                catch (ApiErrorException e)
                {
                    await RetryAsync(e);
                }
                catch (HttpRequestException e)
                {
                    await RetryAsync(e);
                }
            }
        }

        Console.WriteLine("片思いフォローの検出をノートしますか？ (y/N) ? ");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            await PostAsync("notes/create", new { i = token, visibility = "home", text = $"{kataomoi.Count()}人に片思いされていました。 #FFScopeForMisskey" });
        }

        Console.WriteLine("ENTER キーを押して終了");
        Console.ReadLine();
    }

    static async Task<List<T>> PostPaginationAsync<T>(string api, object args)
        where T : IIdentificatedModel
    {
        string untilId = null;
        var jArgs = JObject.FromObject(args);
        var list = new List<T>();

        while (true)
        {
            var a = jArgs.DeepClone() as JObject;
            a.Add("limit", 100);
            if (untilId != null)
                a.Add("untilId", untilId);

            var fetched = await PostAsync<List<T>>(api, a.ToString());
            if (!fetched.Any())
                break;

            list.AddRange(fetched);
            untilId = fetched.Last().Id;
        }
        return list;
    }

    static async Task PostAsync(string api, object args)
    {
        await PostAsync<object>(api, args);
    }

    static Task<T> PostAsync<T>(string api, object args)
    {
        return PostAsync<T>(api, JsonConvert.SerializeObject(args));
    }

    static async Task<T> PostAsync<T>(string api, string body)
    {
        var res = await cli.PostAsync(endpoint + api, new StringContent(body));
        var jsonString = await res.Content.ReadAsStringAsync();
        try
        {
            // エラーオブジェクトであれば例外発生
            var err = JsonConvert.DeserializeObject<Error>(jsonString);
            if (err != null && new[] { err.Code, err.Message }.All(el => el != null))
            {
                throw new ApiErrorException(err);
            }
        }
        catch (JsonSerializationException)
        {
            // JSON解析エラーが出るということはエラーオブジェクトではないので無視
        }
        if ((int)res.StatusCode >= 400)
        {
            throw new HttpRequestException(jsonString);
        }
        return JsonConvert.DeserializeObject<T>(jsonString);
    }

    private static HttpClient cli = new HttpClient();
}
