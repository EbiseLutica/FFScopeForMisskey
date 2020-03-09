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
    static async Task Main(string[] args)
    {
        string token;
        string host;

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

        Console.WriteLine("Fetched your account:");
        Console.WriteLine($" {me.Name ?? me.Username} @{me.Name}");
        Console.WriteLine($" {me.NotesCount} Notes");
        Console.WriteLine($" id: {me.Id}");

        Console.WriteLine("Fetching all users you follow...");

        var kataomoi = (await PostPaginationAsync<Following>("users/following", new { i = token, userId = me.Id }))
            .Select(f => f.Followee)
            .Where(f => !f.IsFollowed);
        var kataomoiCount = kataomoi.Count();

        foreach (var u in kataomoi)
        {
            Console.WriteLine($"{u.Name ?? u.Username} @{u.Username}@{u.Host ?? host}");
        }


        Console.WriteLine($"These {kataomoiCount} users don't follow you but you follow them.\nDo you want to unfollow them (y/N) ? ");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            Console.WriteLine("Which mode do you prefer? Please select a number:\n1. Unfollow all\n2. Unfollow one by one");
            var isAllMode = Console.ReadLine().Trim() == "1";
            foreach (var u in kataomoi)
            {
                try
                {
                    var delete = isAllMode;
                    if (!isAllMode)
                    {
                        Console.WriteLine($"Do you want to unfollow {u.Name ?? u.Username}? (y/N)");
                        delete = Console.ReadLine().ToLowerInvariant() == "y";
                    }

                    if (delete)
                        await PostAsync("following/delete", new { i = token, userId = u.Id });

                    if (isAllMode)
                        Console.WriteLine($"Unfollowed {u.Name ?? u.Username}");
                }
                catch (ApiErrorException e)
                {
                    Console.WriteLine($"Exception thrown: {e.Message}");
                    Console.WriteLine("Retry after 15 minutes");
                    await Task.Delay(899000);
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Exception thrown: {e.Message}");
                    Console.WriteLine("Retry after 15 minutes");
                    await Task.Delay(899000);
                }
            }
        }

        Console.WriteLine("Do you want to note that detect one-sided followees (y/N) ? ");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            await PostAsync("notes/create", new { i = token, visibility = "home", text = $"I have followed {kataomoi.Count()} users one-sidedly. #FFScopeForMisskey" });
        }

        Console.WriteLine("Press ENTER to exit");
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