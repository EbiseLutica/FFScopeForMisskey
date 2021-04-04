using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MisskeyDotNet;

var sessions = LoadSessions();

var session = ChooseSession(sessions);

Misskey mi;
try
{
    mi = await GetMisskeyAsync(session);
}
catch (MisskeyApiException)
{
    // エラーオブジェクトがサーバーから返ってきた場合
    Console.WriteLine("認可に失敗しました。");
    return;
}
// TODO: https://github.com/Xeltica/Misskey.NET/issues/3 が治ったら HttpException に書き換える
catch (Exception e)
{
    // サーバーにて問題が発生している場合
    Console.WriteLine(e.Message);
    return;
}

var me = await mi.ApiAsync<User>("i");

Console.WriteLine("アカウント情報を取得しました:");
Console.WriteLine($" {me.Name ?? me.Username} @{GetAcct(me)}");
Console.WriteLine($" {me.NotesCount} ノート");
Console.WriteLine($" id: {me.Id}");

// Console.WriteLine(mi.Token);

Console.WriteLine("1. 片思いフォロー検出");
Console.WriteLine("2. 片思いフォロワー検出");
Console.WriteLine("3. Detect dead remote-following (TBD)");
Console.WriteLine("4. Detect dead remote-followers (TBD)");
Console.WriteLine("5. Detect inactive following (TBD)");
Console.WriteLine("何をしますか？モードを番号で選んでください");

var mode = int.Parse(Console.ReadLine());

switch (mode)
{
    case 1:
        await CheckKataomoiFolloweeAsync(mi, me);
        break;
    case 2:
        await CheckKataomoiFollowerAsync(mi, me);
        break;
    default:
        Console.WriteLine("不適切な番号が指定されました。中止します。");
        break;
}

if (session == null) ConfirmSave(mi);

Console.WriteLine("ENTER キーを押して終了");
Console.ReadLine();

// -----------------------------------------------------------------

static string GetAcct(User user)
{
    return user.Host != null
        ? $"{user.Username}@{user.Host}"
        : user.Username;
}

static string GetAcctUrl(User user, string host)
{
    var h = user.Host ?? host;
    return $"https://{h}/@{user.Username}";
}

static async Task RetryAsync(Exception e)
{
    Console.WriteLine($"例外がスローされました: {e.Message}");
    Console.WriteLine("15分後に再試行します");
    await Task.Delay(899000);
}

static (string name, string body)[] LoadSessions()
{
    if (!Directory.Exists("sessions")) return Array.Empty<(string, string)>();

    return Directory
        .EnumerateFiles("sessions")
        .Select(p => (Path.GetFileNameWithoutExtension(p), File.ReadAllText(p)))
        .ToArray();
}

static string ChooseSession((string name, string body)[] sessions)
{
    if (sessions.Length == 0) return null;

    sessions.Select((t, i) => $"{i}: {t.name}").ToList().ForEach(Console.WriteLine);
    Console.WriteLine(sessions.Length + ": 新規作成");
    Console.WriteLine("セッションが保存されています。使用するセッションを番号で入力してください");

    string numStr = null;
    do
    {
        Console.Write("> ");
        numStr = Console.ReadLine().Trim();
    } while (string.IsNullOrWhiteSpace(numStr));

    if (!int.TryParse(numStr, out var num))
    {
        return null;
    }
    if (num < sessions.Length)
    {
        return sessions[num].body;
    }
    return null;
}

static async ValueTask<Misskey> CreateNewMisskeyClientAsync()
{
    string host;

    Console.WriteLine("インスタンスのホスト名を入力してください。例: misskey.io");
    do
    {
        Console.Write("> ");
        host = Console.ReadLine().Trim();
    } while (string.IsNullOrWhiteSpace(host));

    if (host.StartsWith("http://"))
        host = host.Remove(0, "http://".Length);
    if (host.StartsWith("https://"))
        host = host.Remove(0, "https://".Length);
    if (host.EndsWith("/"))
        host = host.Remove(host.Length - 1);

    var miAuth = new MiAuth(host, "FFScopeForMisskey", null, null, new Permission[]{
        Permission.ReadAccount,
        Permission.WriteAccount,
        Permission.ReadFollowing,
        Permission.WriteFollowing,
    });
    if (!miAuth.TryOpenBrowser())
    {
        Console.WriteLine("次のURLをお使いのウェブブラウザーで開き、認可を完了させてください。");
        Console.WriteLine(miAuth.Url);
    }
    Console.WriteLine("認可が完了したら、ENTER キーを押してください。");
    Console.ReadLine();

    return await miAuth.CheckAsync();
}

static async ValueTask<Misskey> GetMisskeyAsync(string session)
{
    return session == null ? await CreateNewMisskeyClientAsync() : Misskey.Import(session);
}

static async ValueTask CheckKataomoiFolloweeAsync(Misskey mi, User me)
{
    Console.WriteLine("フォローしているユーザー全員の情報を取得中...");


    string untilId = null;
    var following = new List<Following>();

    while (true)
    {
        object args = untilId == null ? new 
        {
            userId = me.Id,
            limit = 100,
        } : new
        {
            userId = me.Id,
            limit = 100,   
            untilId,
        };

        var fetched = await mi.ApiAsync<List<Following>>("users/following", args);
        if (!fetched.Any())
            break;

        following.AddRange(fetched);
        untilId = fetched.Last().Id;
    }

    var kataomoi = following
        .Select(f => f.Followee)
        .Where(f => !f.IsFollowed);
    var kataomoiCount = kataomoi.Count();

    foreach (var u in kataomoi)
    {
        Console.WriteLine($"{u.Name ?? u.Username} {GetAcctUrl(u, mi.Host)}");
    }

    if (kataomoiCount == 0)
    {
        Console.WriteLine("片思いフォローはありませんでした。");
    }
    else
    {
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
                        Console.WriteLine($"@{GetAcctUrl(u, mi.Host)} をフォロー解除しますか？ (y/N)");
                        delete = Console.ReadLine().ToLowerInvariant() == "y";
                    }

                    if (delete)
                        await mi.ApiAsync("following/delete", new { userId = u.Id });

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

        Console.WriteLine("片思いフォローの検出をノートしますか？(y/N)");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            await mi.ApiAsync("notes/create", new
            {
                visibility = "home",
                text = $"{kataomoi.Count()}人を片思いしていました。 #FFScopeForMisskey"
            });
        }
    }
}

static async ValueTask CheckKataomoiFollowerAsync(Misskey mi, User me)
{
    Console.WriteLine("フォローされているユーザー全員の情報を取得中...");


    string untilId = null;
    var following = new List<Following>();

    while (true)
    {
        object args = untilId == null ? new
        {
            userId = me.Id,
            limit = 100,
        } : new
        {
            userId = me.Id,
            limit = 100,
            untilId,
        };

        var fetched = await mi.ApiAsync<List<Following>>("users/followers", args);
        if (!fetched.Any())
            break;

        following.AddRange(fetched);
        untilId = fetched.Last().Id;
    }

    var kataomoi = following
        .Select(f => f.Follower)
        .Where(f => !f.IsFollowing);
    var kataomoiCount = kataomoi.Count();

    foreach (var u in kataomoi)
    {
        Console.WriteLine($"{u.Name ?? u.Username} {GetAcctUrl(u, mi.Host)}");
    }

    if (kataomoiCount == 0)
    {
        Console.WriteLine("片思いフォロワーはありませんでした。");
    }
    else
    {
        Console.WriteLine($"フォローされている {kataomoiCount} 人のユーザーをフォローしていません。フォロー作業に入りますか (y/N) ? ");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            Console.WriteLine("希望するモードを次の数字で選んでください:\n1. 一括で全てフォローする\n2. ひとりひとり確認してフォローを外す");
            var isAllMode = Console.ReadLine().Trim() == "1";
            foreach (var u in kataomoi)
            {
                try
                {
                    var create = isAllMode;
                    if (!isAllMode)
                    {
                        Console.WriteLine($"@{GetAcctUrl(u, mi.Host)} をフォローしますか？ (y/N)");
                        create = Console.ReadLine().ToLowerInvariant() == "y";
                    }

                    if (create)
                        await mi.ApiAsync("following/create", new { userId = u.Id });

                    if (isAllMode)
                        Console.WriteLine($"{GetAcct(u)} をフォローしました");
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

        Console.WriteLine("片思いフォロワーの検出をノートしますか？(y/N)");

        if (Console.ReadLine().ToLowerInvariant() == "y")
        {
            await mi.ApiAsync("notes/create", new
            {
                visibility = "home",
                text = $"{kataomoi.Count()}人に片思いされていました。 #FFScopeForMisskey"
            });
        }
    }
}

static void ConfirmSave(Misskey mi)
{
    Console.WriteLine("今回使用したトークンを保存しますか？(Y/n)");

    if (Console.ReadLine().ToLowerInvariant() == "n") return;

    string name;
    do
    {
        Console.Write("名前 > ");
        name = Console.ReadLine().Trim();
    } while (string.IsNullOrWhiteSpace(name));
    
    if (!Directory.Exists("sessions")) Directory.CreateDirectory("sessions");
    var serialized = mi.Export();
    File.WriteAllText("sessions/" + name + ".ini", serialized);
}