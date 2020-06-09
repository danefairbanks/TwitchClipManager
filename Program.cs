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
        /// Sort Order
        /// </summary>
        public bool Ascending { get; set; }

        /// <summary>
        /// Stop at view count, 0 is no limit
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Flag for download
        /// </summary>
        public bool Download { get; set; }

        /// <summary>
        /// Flag for deleting
        /// </summary>
        public bool Delete { get; set; }

        /// <summary>
        /// Flag for clip types
        /// </summary>
        public bool MyClips { get; set; }

        /// <summary>
        /// Auth token
        /// </summary>
        public string Token { get; set; }
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

            bool quit = false;
            do
            {
                List<ClipInfo> clips = new List<ClipInfo>();
                string cursor = null;
                Console.Write("Getting batched clip info");
                do
                {
                    Console.Write(".");
                    clips.AddRange(GetClipsGraph(options.MyClips, options.Ascending, ref cursor));
                } while (!string.IsNullOrEmpty(cursor));
                Console.WriteLine("Complete.");

                var deleteClips = new List<string>();
                foreach (var clip in clips)
                {
                    if (options.Limit != 0 &&
                        ((options.Ascending && options.Limit < clip.view_count) ||
                        (!options.Ascending && options.Limit > clip.view_count)))
                    {
                        Console.WriteLine($"Clip view count exceeds limit {options.Limit}");
                        quit = true;
                        break;
                    }
                    var fileName = SanitizeFile($"v{clip.view_count:00000000}[{clip.created_at}] {clip.title} by {clip.creator_name}-{clip.id}.mp4");
                    var savePath = Path.Combine(folder, fileName);
                    try
                    {
                        if (options.Download)
                        {
                            Console.WriteLine($"Downloading {clip.id} - {clip.title} by {clip.creator_name}");
                            string sourceUrl = GetClipUri(clip.id);
                            DownloadClip(sourceUrl, savePath);
                            if (options.Delete)
                                deleteClips.Add(clip.id);
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(RootPath, "error.log"), $"{clip.id} download failed: {ex.Message}" + Environment.NewLine);
                    }
                    if (options.Delete && deleteClips.Count > 10)
                    {
                        Console.WriteLine($"Deleting {string.Join(',', deleteClips)}");
                        DeleteClips(deleteClips);
                        deleteClips.Clear();
                    }
                }
                if (options.Delete && deleteClips.Count > 10)
                {
                    Console.WriteLine($"Deleting {string.Join(',', deleteClips)}");
                    DeleteClips(deleteClips);
                    deleteClips.Clear();
                }
                if (clips.Count < 900) break;
            } while (options.Delete && !quit);
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

            Console.WriteLine("Types of clips");
            Console.WriteLine("1. My Clips (clips youve taken)");
            Console.WriteLine("2. Channel Clips (clips of your channel)");
            Console.WriteLine("Type 1 or 2:");
            string input = Console.ReadLine();
            options.MyClips = input.StartsWith("1");

            Console.WriteLine("Download (y or n):");
            input = Console.ReadLine();
            options.Download = input.ToLower().StartsWith('y');

            Console.WriteLine("Delete (y or n):");
            input = Console.ReadLine();
            options.Delete = input.ToLower().StartsWith('y');

            Console.WriteLine("Sort order by view count");
            Console.WriteLine("1. Low to high");
            Console.WriteLine("2. High to low");
            Console.WriteLine("Type 1 or 2:");
            input = Console.ReadLine();
            options.Ascending = input.StartsWith("1");

            Console.WriteLine("Limit processing at view count (enter 0 for no limit):");
            input = Console.ReadLine();
            if (int.TryParse(input, out int result))
            {
                options.Limit = result;
            }
            else
            {
                options.Limit = 0;
            }

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

            return options;
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

        static IList<ClipInfo> GetClipsGraph(bool myclips, bool ascending, ref string cursor)
        {
            var query = myclips ? "curatorID" : "broadcasterID";
            var sort = ascending ? "VIEWS_ASC" : "VIEWS_DESC";
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
                            ["sort"] = sort,
                            ["period"] = "ALL_TIME",
                            [query] = UserId
                        }
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(cursor))
                gql[0]["variables"]["cursor"] = cursor;

            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var http = GetHttpClient(true);
            var result = http.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(result);

            var clips = new List<ClipInfo>();
            bool hasNextPage = json.SelectToken("[0].data.user.clips.pageInfo.hasNextPage")?.ToObject<bool>() == true;
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
                if (hasNextPage && edge.SelectToken("cursor")?.ToString() != null)
                {
                    cursor = edge.SelectToken("cursor")?.ToString();
                }
            }
            if (!hasNextPage) cursor = null;
            return clips;
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
                if (File.Exists(savePath))
                    File.Delete(savePath);
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

        static IList<ClipInfo> GetClipsApi(ref string cursor)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TwitchToken);
            var url = $"https://api.twitch.tv/helix/clips?broadcaster_id={UserId}&first={100}";
            if (!string.IsNullOrWhiteSpace(cursor))
                url += $"&after={cursor}";
            Console.WriteLine($"Getting clips, cursor: {cursor}");
            var result = http.GetAsync(url).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
            var json = JToken.Parse(result);

            cursor = json.SelectToken("pagination.cursor")?.ToString();
            return json.SelectToken("data")?.ToObject<List<ClipInfo>>();
        }

        static IList<ClipInfo> GetClipCardsGraph(ref string cursor)
        {
            Console.WriteLine($"Getting clips, cursor: {cursor}");
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
                        ["login"] = Login,
                        ["limit"] = 10,
                        ["criteria"] = new JObject()
                        {
                            ["filter"] = "ALL_TIME"
                        }
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(cursor))
                gql[0]["variables"]["cursor"] = cursor;

            var content = gql.ToString(Newtonsoft.Json.Formatting.None);
            var ghttp = new HttpClient();
            ghttp.DefaultRequestHeaders.Add("Client-ID", TwitchClientID);
            ghttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", TwitchToken);

            var result = ghttp.PostAsync("https://gql.twitch.tv/gql", new StringContent(content)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = JToken.Parse(result);
            var clips = new List<ClipInfo>();
            bool hasNextPage = json.SelectToken("[0].data.user.clips.pageInfo.hasNextPage")?.ToObject<bool>() == true;
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
                if (edge.SelectToken("cursor")?.ToString() != null && hasNextPage)
                {
                    cursor = edge.SelectToken("cursor")?.ToString();
                }
            }
            if (!hasNextPage) cursor = null;
            return clips;
        }

        #endregion
    }
}
