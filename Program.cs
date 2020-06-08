using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;

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
        static string TwitchClientID;
        static string TwitchToken;
        static string UserId;
        static string Cursor;
        static bool Delete;
        static string RootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
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
                        Console.WriteLine($"Downloading {clip.id} - {clip.title} by {clip.creator_name}");
                        DownloadClip(sourceUrl, savePath);
                        if (Delete)
                            DeleteClips(new List<string>() { clip.id });
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{clip.id} download failed: {ex.Message}");
                    }
                }
                if (Cursor == null)
                    break;
                UpdateCursor();
                clips = GetClips();
            }
        }

        static void LoadConfig()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            using var fs = File.OpenRead(configPath);
            using var fsr = new StreamReader(fs);
            var config = JObject.Parse(fsr.ReadToEnd());
            TwitchClientID = config["twitchclientid"]?.ToString();
            TwitchToken = config["twitchtoken"]?.ToString();
            Delete = config["delete"]?.ToObject<bool>() == true;
            Cursor = config["cursor"]?.ToString();
        }

        static void UpdateCursor()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            using var fsr = File.OpenRead(configPath);
            using var sr = new StreamReader(fsr);
            var config = JObject.Parse(sr.ReadToEnd());
            config["cursor"] = Cursor;
            fsr.Close();

            File.Delete(configPath);
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
        }

        static IList<ClipInfo> GetClips()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TwitchToken);

            var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={UserId}&first=100";
            if (!string.IsNullOrEmpty(Cursor))
                url += $"&after={Cursor}";
            var res = http.GetStringAsync(url).GetAwaiter().GetResult();
            var jtok = JToken.Parse(res);
            Cursor = jtok["pagination"]["cursor"]?.ToString();
            return jtok["data"].ToObject<List<ClipInfo>>();
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
            Console.WriteLine($"Deleting clip at {clips[0]}");
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
            var res = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var jtok = JArray.Parse(res);

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
