using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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

    class Options
    {
        /// <summary>
        /// Stop at view count, 0 is no limit
        /// </summary>
        public int UpperLimit { get; set; }
        public int LowerLimit { get; set; }
        public int DayInterval { get; set; }
        /// <summary>
        /// Flag for download
        /// </summary>
        public bool Download { get; set; }

        /// <summary>
        /// Flag for deleting
        /// </summary>
        public bool Delete { get; set; }

        /// <summary>
        /// Auth token
        /// </summary>
        public string Token { get; set; }

        public DateTime CurrentDate { get; set; }
    }

    class Program
    {
        static string TwitchClientID = "kimne78kx3ncx6brgo4mv6wki5h1ko";
        static string TwitchToken;
        static string UserId;
        static string Login;
        static string RootPath = Environment.CurrentDirectory;
        static void Main(string[] args)
        {
            var options = LoadConfig();
            TwitchToken = options.Token;

            GetUserInfo();
            var folder = Path.Combine(RootPath, "downloads");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var firstClip = GetFirstClip(Login);
            if (firstClip.Count < 1)
            {
                Console.WriteLine("No clips found");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }
            var lastDate = DateTime.Parse(firstClip[0].created_at).Date;
            options.CurrentDate = DateTime.Now.Date;

            Console.WriteLine($"Oldest Clip on {lastDate}");
            do
            {
                List<ClipInfo> clips = new List<ClipInfo>();
                string cursor = null;
                Console.WriteLine($"Getting batched clips for {options.CurrentDate} to {options.CurrentDate.AddDays(options.DayInterval)}");
                int count = 0;
                do
                {
                    var newClips = GetClipsApi(UserId, options.CurrentDate, options.DayInterval, ref cursor);
                    clips.AddRange(newClips);
                    count += newClips.Count;

                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"Count {count}...");
                } while (!string.IsNullOrEmpty(cursor));
                Console.WriteLine("Complete.");

                var deleteClips = new List<string>();
                foreach (var clip in clips)
                {
                    if ((options.UpperLimit != 0 && options.UpperLimit < clip.view_count)
                        || (options.LowerLimit != 0 && options.LowerLimit > clip.view_count))
                    {
                        Console.WriteLine($"{clip.id} with views {clip.view_count} out of bounds.");
                        continue;
                    }

                    var fileName = SanitizeFile($"v{clip.view_count:00000000}[{clip.created_at}] {clip.title} by {clip.creator_name}-{clip.id}.mp4");
                    var savePath = Path.Combine(folder, fileName);
                    try
                    {
                        if (options.Download)
                        {
                            Console.Write($"Downloading {clip.id}.");
                            string sourceUrl = GetClipUri(clip.id);
                            Console.Write(".");
                            DownloadClip(sourceUrl, savePath);
                            Console.Write(".");
                            Console.WriteLine("Complete");
                        }
                        if (options.Delete)
                            deleteClips.Add(clip.id);
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{clip.id} download failed: {ex.Message}" + Environment.NewLine);
                        Console.WriteLine("Failed");
                    }
                    if (options.Delete && deleteClips.Count > 10)
                    {
                        del();
                    }
                }
                if (options.Delete && deleteClips.Count > 0)
                {
                    del();
                }

                void del()
                {
                    try
                    {
                        Console.Write($"Deleting {string.Join(',', deleteClips)}...");
                        DeleteClips(deleteClips);
                        Console.WriteLine("Complete.");
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{string.Join(',', deleteClips)} deleting failed: {ex.Message}" + Environment.NewLine);
                        Console.WriteLine("Failed.");
                    }
                    deleteClips.Clear();
                }

                options.CurrentDate = options.CurrentDate.AddDays(-2);
                SaveConfig(options);
            } while (options.CurrentDate > lastDate);

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }

        static Options LoadConfig()
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            try
            {
                if (File.Exists(configPath))
                {
                    Console.WriteLine("Session found resume? (y or n):");
                    string input = Console.ReadLine();
                    if (input.ToLower().StartsWith('y'))
                    {
                        using var fsr = new StreamReader(File.OpenRead(configPath));
                        var config = JObject.Parse(fsr.ReadToEnd());
                        return config.ToObject<Options>();
                    }
                }
            }
            catch
            {
                Console.WriteLine("There was a problem loading the configuration");
            }

            if (File.Exists(configPath)) File.Delete(configPath);
            return GetConfig();
        }

        static Options GetConfig()
        {
            var options = new Options();
            Console.WriteLine("Paste in auth token:");
            options.Token = Console.ReadLine().Trim();

            Console.WriteLine("Download (y or n):");
            string input = Console.ReadLine();
            options.Download = input.ToLower().StartsWith('y');

            Console.WriteLine("Delete (y or n):");
            input = Console.ReadLine();
            options.Delete = input.ToLower().StartsWith('y');

            Console.WriteLine("Upper limit of view count processing (enter 0 for no limit):");
            input = Console.ReadLine();
            if (int.TryParse(input, out int ul))
            {
                options.UpperLimit = ul;
            }
            else
            {
                options.UpperLimit = 0;
            }

            Console.WriteLine("Lower limit of view count processing (enter 0 for no limit):");
            input = Console.ReadLine();
            if (int.TryParse(input, out int ll))
            {
                options.LowerLimit = ll;
            }
            else
            {
                options.LowerLimit = 0;
            }

            Console.WriteLine("Day intervals (amount of days to batch):");
            input = Console.ReadLine();
            if (int.TryParse(input, out int d))
            {
                options.DayInterval = d;
            }
            else
            {
                options.DayInterval = 7;
            }

            options.CurrentDate = DateTime.Now.Date;

            SaveConfig(options);

            return options;
        }

        static void SaveConfig(Options options)
        {
            var configPath = Path.Combine(RootPath, "appsettings.json");
            var config = JObject.FromObject(options);

            try
            {
                using var sw = new StreamWriter(File.OpenWrite(configPath));
                sw.Write(config.ToString());
                sw.Close();
            }
            catch
            {
                Console.WriteLine("There was a problem saving configuration");
            }
        }

        static void GetUserInfo()
        {
            try
            {
                var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TwitchToken);

                var res = http.GetStringAsync($"https://api.twitch.tv/helix/users").GetAwaiter().GetResult();
                var jtok = JToken.Parse(res);
                UserId = jtok["data"][0]["id"].ToString();
                Login = jtok["data"][0]["login"].ToString();
            }
            catch (Exception ex)
            {
                throw new Exception($"[{DateTime.Now}] GetUserInfo failed.", ex);
            }
        }

        static void GetUserInfo(string login)
        {
            try
            {
                var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TwitchToken);

                var res = http.GetStringAsync($"https://api.twitch.tv/helix/users?login={login}").GetAwaiter().GetResult();
                var jtok = JToken.Parse(res);
                UserId = jtok["data"][0]["id"].ToString();
                Login = jtok["data"][0]["login"].ToString();
            }
            catch (Exception ex)
            {
                throw new Exception($"[{DateTime.Now}] GetUserInfo failed.", ex);
            }
        }

        static string GetClipUri(string clipId)
        {
            var gql = new JArray
            {
                new JObject()
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
                }
            };
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var http = GetHttpClient();
            var result = http.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JArray.Parse(result);

            var nullcheck = json.SelectToken("[0].data.clip")?.Type == JTokenType.Null;
            if (nullcheck)
            {
                File.AppendAllText(Path.Combine(RootPath, "error.log"), $"[{DateTime.Now}] {clipId} clip missing: payload: {result}" + Environment.NewLine);
                throw new Exception("Clip not found");
            }

            var sourceUrl = json.SelectToken("[0].data.clip.videoQualities[0].sourceURL")?.ToString();
            if (sourceUrl == null)
            {
                File.AppendAllText(Path.Combine(RootPath, "error.log"), $"[{DateTime.Now}] {clipId} download failed: payload: {result}" + Environment.NewLine);
                throw new Exception("Download failed");
            }
            return sourceUrl;
        }

        static void DeleteClips(IList<string> clips)
        {
            var gql = new JArray
            {
                new JObject()
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
                }
            };
            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var http = GetHttpClient(true);
            var result = http.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JArray.Parse(result);
            if (result.Contains("error"))
            {
                File.AppendAllText(Path.Combine(RootPath, "error.log"), $"[{DateTime.Now}] {string.Join(", ", clips)} deleting failed: payload: {result}" + Environment.NewLine);
                throw new Exception("Delete Clips Failed");
            }
        }

        static void DownloadClip(string sourceUrl, string savePath)
        {
            try
            {
                using var http = new HttpClient();
                var stream = http.GetStreamAsync(sourceUrl).GetAwaiter().GetResult();
                if (File.Exists(savePath)) File.Delete(savePath);
                using var fs = new FileStream(savePath, FileMode.CreateNew);
                stream.CopyTo(fs);
                fs.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"[{DateTime.Now}] DownloadClip: There was a problem downloading {sourceUrl} to {savePath}", ex);
            }
        }

        static string SanitizeFile(string origFileName)
        {
            var invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", origFileName.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        static HttpClient GetHttpClient(bool authorize = false)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            if (authorize)
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", TwitchToken);
            return http;
        }

        #region "Unused"

        static IList<ClipInfo> GetClipsApi(string userId, DateTime start, int days, ref string cursor)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TwitchToken);

            var end = start.AddDays(days);
            var uri = new UriBuilder($"https://api.twitch.tv/helix/clips");
            uri.Query = $"?broadcaster_id={userId}&first={100}&started_at={start:s}Z&ended_at={end:s}Z";
            if (!string.IsNullOrWhiteSpace(cursor))
                uri.Query += $"&after={cursor}";
            var result = http.GetAsync(uri.Uri).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
            var json = JToken.Parse(result);

            cursor = json.SelectToken("pagination.cursor")?.ToString();
            return json.SelectToken("data")?.ToObject<List<ClipInfo>>();
        }

        static IList<ClipInfo> GetFirstClip(string login)
        {
            Console.WriteLine($"Getting First Clip");
            var gql = new JArray()
            {
                new JObject()
                {
                    ["extensions"] = new JObject()
                    {
                        ["persistedQuery"] = new JObject()
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = "b73ad2bfaecfd30a9e6c28fada15bd97032c83ec77a0440766a56fe0bd632777"
                        }
                    },
                    ["operationName"] = "ClipsCards__User",
                    ["variables"] = new JObject()
                    {
                        ["login"] = login,
                        ["limit"] = 1,
                        ["criteria"] = new JObject()
                        {
                            ["sort"] = "CREATED_AT_ASC",
                            ["filter"] = "ALL_TIME"
                        }
                    }
                }
            };

            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", TwitchToken);

            var result = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(result);
            var clips = new List<ClipInfo>();
            var edges = json.SelectToken("[0].data.user.clips.edges");
            if (edges == null)
            {
                File.AppendAllText(Path.Combine(RootPath, "error.log"), $"[{DateTime.Now}] getting clips failed: payload: {result}" + Environment.NewLine);
            }
            foreach (var edge in edges)
            {
                clips.Add(new ClipInfo
                {
                    id = edge.SelectToken("node.slug")?.ToString(),
                    title = edge.SelectToken("node.title")?.ToString(),
                    creator_name = edge.SelectToken("node.curator.login")?.ToString(),
                    created_at = edge.SelectToken("node.createdAt")?.ToString(),
                    view_count = edge.SelectToken("node.viewCount")?.ToObject<int>() ?? 0
                });
            }
            return clips;
        }

        #endregion
    }
}
