using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace ClipManager
{
    class ClipInfo
    {
        public string id { get; set; }
        public int view_count { get; set; }
        public string creator_name { get; set; }
        public string created_at { get; set; }
        public string title { get; set; }
    }
    class Program
    {
        static string TwitchClientID = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        static string TwitchToken;
        static string UserId;
        static string Login;
        static string Cursor;
        static bool Download = false;
        static bool Delete = false;
        static string RootPath = Environment.CurrentDirectory;
        static void Main(string[] args)
        {
            LoadConfig();
            GetUserID();
            var folder = Path.Combine(RootPath, "downloads");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            var clips = GetClips();
            while (clips.Count > 0)
            {
                foreach (var clip in clips)
                {
                    var fileName = SanitizeFile($"v{clip.view_count:00000000}[{clip.created_at}] {clip.title} by {clip.creator_name}-{clip.id}.mp4");
                    var savePath = Path.Combine(folder, fileName);
                    try
                    {
                        var sourceUrl = GetClipUri(clip.id);
                        if (Download)
                        {
                            Console.WriteLine($"Downloading {clip.id} - {clip.title} by {clip.creator_name}");
                            DownloadClip(sourceUrl, savePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{clip.id} download failed: {ex.Message}");
                    }
                }
                if (Delete)
                {
                    Console.WriteLine($"Deleting {string.Join(',', clips.Select(c => c.id))}");
                    DeleteClips(clips.Select(c => c.id).ToList());
                }
                UpdateCursor();
                if (!Delete && Cursor == null)
                    break;
                clips = GetClips();
            }
        }

        static void LoadConfig()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            bool resume = false;
            if (File.Exists(configPath))
            {
                Console.WriteLine("Session found resume? (y or n):");
                var resumeResp = Console.ReadLine();
                resume = resumeResp.ToLower().StartsWith('y');
                if (resume)
                {
                    using var fs = File.OpenRead(configPath);
                    using var fsr = new StreamReader(fs);
                    var config = JObject.Parse(fsr.ReadToEnd());
                    Cursor = config["cursor"]?.ToString();

                    TwitchToken = config["twitchtoken"]?.ToString();
                    Download = config["download"]?.ToObject<bool>() == true;
                    Delete = config["delete"]?.ToObject<bool>() == true;
                }
            }
            if (!resume)
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);
                GetConfig();
            }
        }

        static void GetConfig()
        {
            Console.WriteLine("Paste in auth token:");
            TwitchToken = Console.ReadLine().Trim();

            Console.WriteLine("Download (y or n):");
            var downloadResp = Console.ReadLine();
            Download = downloadResp.ToLower().StartsWith('y');
            Console.WriteLine("Delete (y or n):");
            var deleteResp = Console.ReadLine();
            Delete = deleteResp.ToLower().StartsWith('y');

            var configPath = Path.Combine(RootPath, "appsettings.json");
            var config = new JObject()
            {
                ["twitchtoken"] = TwitchToken,
                ["download"] = Download,
                ["delete"] = Delete
            };
            using var fsw = File.OpenWrite(configPath);
            using var sw = new StreamWriter(fsw);
            sw.Write(config.ToString());
            sw.Close();
        }

        static void UpdateCursor()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            JObject config = new JObject();
            if (File.Exists(configPath))
            {
                using var fsr = File.OpenRead(configPath);
                using var sr = new StreamReader(fsr);
                config = JObject.Parse(sr.ReadToEnd());
                fsr.Close();

                File.Delete(configPath);
            }
            config["cursor"] = Cursor;

            using var fsw = File.OpenWrite(configPath);
            using var sw = new StreamWriter(fsw);
            sw.Write(config.ToString());
            sw.Close();
        }

        static void GetUserID()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TwitchToken);

            var res = http.GetStringAsync($"https://api.twitch.tv/helix/users").GetAwaiter().GetResult();
            var jtok = JToken.Parse(res);
            UserId = jtok["data"][0]["id"].ToString();
            Login = jtok["data"][0]["login"].ToString();
        }

        static IList<ClipInfo> GetClips()
        {
            var gql = new JArray()
            {
                new JObject()
                {
                    ["extensions"] = new JObject()
                    {
                        ["persistedQuery"] = new JObject()
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = "b300f79444fdcf2a1a76c101f466c8c9d7bee49b643a4d7878310a4e03944232"
                        }
                    },
                    ["operationName"] = "ClipsManagerTable_User",
                    ["variables"] = new JObject()
                    {
                        ["login"] = Login,
                        ["limit"] = 5,
                        ["criteria"] = new JObject()
                        {
                            ["sort"] = "VIEWS_DESC",
                            ["period"] = "ALL_TIME",
                            ["broadcasterID"] = UserId
                        }
                    }
                }
            };
            if (!Delete && !string.IsNullOrWhiteSpace(Cursor))
            {
                gql[0]["variables"]["cursor"] = Cursor;
            }
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", TwitchToken);
            var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var jtok = JArray.Parse(res);
            var retVal = new List<ClipInfo>();
            bool hasNextPage = jtok[0]["data"]["user"]["clips"]["pageInfo"]["hasNextPage"]?.ToObject<bool>() == true;
            foreach (dynamic e in jtok[0]["data"]["user"]["clips"]["edges"])
            {
                dynamic node = e.node;
                string creator = node.broadcaster.login;
                try
                {
                    creator = node.curator.login;
                }
                catch
                {

                }
                retVal.Add(new ClipInfo
                {
                    id = node.slug,
                    title = node.title,
                    creator_name = creator,
                    created_at = node.createdAt,
                    view_count = node.viewCount
                });
                if (!Delete && e.cursor != null && hasNextPage)
                {
                    Cursor = e.cursor;
                }
            }
            if (!Delete && !hasNextPage) Cursor = null;
            return retVal;
        }

        static string GetClipUri(string clipId)
        {
            var gql = new JArray();
            gql.Add(new JObject()
            {
                ["extensions"] = new JObject()
                {
                    ["persistedQuery"] = new JObject()
                    {
                        ["version"] = 1,
                        ["sha256Hash"] = "9bfcc0177bffc730bd5a5a89005869d2773480cf1738c592143b5173634b7d15"
                    }
                },
                ["operationName"] = "VideoAccessToken_Clip",
                ["variables"] = new JObject()
                {
                    ["slug"] = clipId
                }
            });
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var jtok = JArray.Parse(res);
            return jtok[0]["data"]["clip"]["videoQualities"][0]["sourceURL"].ToString();
        }

        static void DeleteClips(IList<string> clips)
        {
            var gql = new JArray();
            gql.Add(new JObject()
            {
                ["extensions"] = new JObject()
                {
                    ["persistedQuery"] = new JObject()
                    {
                        ["version"] = 1,
                        ["sha256Hash"] = "df142a7eec57c5260d274b92abddb0bd1229dc538341434c90367cf1f22d71c4"
                    }
                },
                ["operationName"] = "Clips_DeleteClips",
                ["variables"] = new JObject()
                {
                    ["input"] = new JObject()
                    {
                        ["slugs"] = new JArray(clips.ToArray())
                    }
                }
            });
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", TwitchToken);
            while (true)
            {
                var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var jtok = JArray.Parse(res);
                if (!res.Contains("error"))
                    break;
                else
                    Thread.Sleep(10000);
            }
        }

        static void DownloadClip(string sourceUrl, string savePath)
        {
            using var http = new HttpClient();
            var stream = http.GetStreamAsync(sourceUrl).GetAwaiter().GetResult();
            if (File.Exists(savePath))
                File.Delete(savePath);
            using var fs = new FileStream(savePath, FileMode.CreateNew);
            stream.CopyTo(fs);
            fs.Close();
        }

        static string SanitizeFile(string origFileName)
        {
            var invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", origFileName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }
    }
}
