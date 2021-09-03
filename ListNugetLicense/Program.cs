using Microsoft.Extensions.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ListNugetLicense
{
    class Program
    {
        static readonly string appPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        static readonly string nugetUrl = "https://api.nuget.org/v3-flatcontainer/";
        static readonly string gitHubApiRepoUrl = "https://api.github.com/repos/";

        // Get-Package | Select-Object Id, Version | Foreach-Object { "$($_.Id)`t$($_.Version)" }
        static readonly string InputFileName = "NugetPackageList.txt";

        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("* Loading the configuration file.");
                var outputFolderPath = appPath;
                //
                // Install the following packages
                // - Microsoft.Extensions.Configuration
                // - Microsoft.Extensions.Configuration.FileExtension
                // - Microsoft.Extensions.Configuration.Json
                // 
                var currentDirectoryPath = Directory.GetCurrentDirectory();

                var appSettingBuilder = new ConfigurationBuilder()
                    .SetBasePath(currentDirectoryPath)
                    .AddJsonFile("appsettings.json", optional: true);
                var appSettingConfiguration = appSettingBuilder.Build();

                Console.WriteLine($"");
                Console.WriteLine("* Check the proxy settings.");

                HttpClient client = null;
                var proxySetting = appSettingConfiguration.GetSection("Proxy");
                var proxyHost = proxySetting["Host"]?.ToString();

                if (!string.IsNullOrEmpty(proxyHost))
                {
                    Console.WriteLine("* Load the proxy settings.");

                    var proxyPort = proxySetting["Port"]?.ToString();
                    var proxyUser = proxySetting["UserName"]?.ToString();
                    var proxyPass = proxySetting["UserPassword"]?.ToString();

                    HttpClientHandler clientHandler = new HttpClientHandler();

                    clientHandler.Proxy = new WebProxy(proxyHost + ":" + proxyPort);
                    clientHandler.Proxy.Credentials = new NetworkCredential(proxyUser, proxyPass);
                    clientHandler.UseProxy = true;

                    Console.WriteLine("* Generate a client with proxy settings...");

                    client = new HttpClient(clientHandler);
                }
                else
                {
                    Console.WriteLine("* No proxy settings...");

                    client = new HttpClient();
                }

                var token = appSettingConfiguration.GetChildren().Where(r => r.Key == "GithubToken").Single().Value;

                if (!string.IsNullOrEmpty(token))
                {
                    Console.WriteLine($"");
                    Console.WriteLine("* Load github Token.");
                    client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
                }

                var outputFolderPathSetting = appSettingConfiguration.GetChildren().Where(r => r.Key == "OutputFolderPath").Single().Value;

                if (!string.IsNullOrEmpty(outputFolderPathSetting))
                {
                    Console.WriteLine($"");
                    Console.WriteLine($"* Load outputFolderPath {outputFolderPath}.");
                    outputFolderPath = outputFolderPathSetting;
                }

                Console.WriteLine($"");
                Console.WriteLine("* Load target files.");
                var targetFiles = appSettingConfiguration.GetSection("TargetFiles:FileName").Get<List<string>>();

                if (targetFiles.Count == 0)
                {
                    Console.WriteLine("* There is no object. Terminate the process.");
                    Console.WriteLine("* Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                var sleepMilliSecond = 500;
                var needAdjustment = false;

                needAdjustment = VerifyNeedAdjustments(targetFiles, token);

                if (needAdjustment)
                {
                    sleepMilliSecond = 90 * 1000; // 90 sec
                }

                var notFoundList = new List<string>();
                Console.WriteLine("* The targets are as follows:");
                foreach (var item in targetFiles)
                {
                    Console.WriteLine($"** {item}");
                }

                Console.WriteLine($"");
                Console.WriteLine($"* Load {InputFileName}.");
                var packageDictionary = new Dictionary<string, string>();

                using (var sr = new StreamReader(Path.Combine(currentDirectoryPath, InputFileName)))
                {
                    while (!sr.EndOfStream)
                    {
                        var nameAndVersion = sr.ReadLine();
                        var name = nameAndVersion.Split("\t").First();
                        var version = nameAndVersion.Split("\t").Last();

                        if (packageDictionary.ContainsKey(name))
                        {
                            if (packageDictionary.TryGetValue(name, out var dictValue))
                            {
                                if (version.Equals(dictValue))
                                {
                                    Console.WriteLine($"** {name} {version} has already been added.");
                                    continue;
                                }
                            }
                        }

                        packageDictionary.Add(name, version);
                        notFoundList.Add(name);
                        Console.WriteLine($"** {name} {version}.");
                    }
                }

                if (packageDictionary.Count == 0)
                {
                    Console.WriteLine("* There is no package. Terminate the process.");
                    Console.WriteLine("* Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                needAdjustment = VerifyNeedAdjustments(packageDictionary.Select(r => r.Key).ToList(), token);

                if (needAdjustment)
                {
                    sleepMilliSecond = 90 * 1000; // 90 sec
                }

                foreach (var item in packageDictionary)
                {
                    var packageId = item.Key;
                    var packageVersion = item.Value;

                    Console.WriteLine($"");
                    Console.WriteLine($"*********************************************************");
                    Console.WriteLine($" {packageId} {packageVersion}");
                    Console.WriteLine($"*********************************************************");
                    Console.WriteLine($"");

                    Console.WriteLine($"* Load package manifest.");
                    var nuspecXml = XDocument.Load(nugetUrl + $"{packageId}/{packageVersion}/{packageId}.nuspec");
                    var nuspecNamespace = nuspecXml?.Root?.Name?.Namespace;

                    Console.WriteLine($"* Get project URL.");
                    var projectUrl = (string)nuspecXml.Root.Descendants(nuspecNamespace + "projectUrl").FirstOrDefault();
                    var repositoryUrl =
                        (string)nuspecXml.Root.Descendants(nuspecNamespace + "repository").FirstOrDefault()?.Attribute("url");

                    string repositoryUrlForApi = null;
                    string redirectedUrl = null;

                    if (string.IsNullOrEmpty(repositoryUrl) || !repositoryUrl.Contains("http"))
                    {
                        if (string.IsNullOrEmpty(projectUrl) || !projectUrl.Contains("http"))
                        {
                            Console.WriteLine($"** repository and project URL is null or empty or not http.");
                            continue;
                        }

                        var redirectedResponse = await client.GetAsync(projectUrl);
                        redirectedUrl = redirectedResponse.RequestMessage.RequestUri.ToString();
                    }
                    else
                    {
                        var redirectedResponse = await client.GetAsync(repositoryUrl);
                        redirectedUrl = redirectedResponse.EnsureSuccessStatusCode().RequestMessage.RequestUri.ToString();
                    }

                    if (!redirectedUrl.Contains("github.com"))
                    {
                        Console.WriteLine($"*** Repository URL :{redirectedUrl}.");
                        Console.WriteLine("*** Only github URL is supported. Skip the process.");

                        continue;
                    }

                    repositoryUrlForApi = redirectedUrl.Split("github.com").Last().Trim('/');

                    repositoryUrlForApi = RemoveEndsWith(repositoryUrlForApi, ".git");
                    repositoryUrlForApi = RemoveEndsWith(repositoryUrlForApi, "/wiki");

                    Console.WriteLine($"** Successfully retrieved the gitHub URL. {repositoryUrlForApi}");

                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

                    foreach(var file in targetFiles)
                    {
                        Thread.Sleep(sleepMilliSecond);

                        Console.WriteLine($"");
                        Console.WriteLine($"*** Attempt to retrieve {file}.");

                        var requestUrl = gitHubApiRepoUrl + repositoryUrlForApi + $"/contents/{file}";
                        Console.WriteLine($"*** Send Get request {requestUrl}.");

                        HttpResponseMessage response = await client.GetAsync(requestUrl);

                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                Console.WriteLine($"*** Response Status code :{response.StatusCode}.");
                                Console.WriteLine($"*** Processing is terminated due to an adjustment error. Please use a personal token.");
                                return;
                            }

                            Console.WriteLine($"*** Response Status code :{response.StatusCode}.");
                            Console.WriteLine($"*** Skip the process.");

                            continue;
                        }

                        Console.WriteLine($"");
                        Console.WriteLine("** Succeeded in retrieving the file.");

                        var contents = await JsonSerializer.DeserializeAsync<Contents>(await response.Content.ReadAsStreamAsync());

                        // Base64でエンコードされているのでデコード
                        var encodedContent = contents.Content;
                        string decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent));

                        // 結果を格納する
                        // パッケージ名のディレクトリを作成
                        var packageFolder = Directory.CreateDirectory(Path.Combine(outputFolderPath, packageId)).ToString();
                        File.WriteAllText(Path.Combine(packageFolder, $"{file}"), decodedContent);

                        Console.WriteLine($"");
                        Console.WriteLine($"** {packageId} license file created or overwrited.");
                        notFoundList.Remove(item.Key);

                        break;
                    }
                }

                Console.WriteLine($"");
                Console.WriteLine($"=========================================================");
                Console.WriteLine("* Completed! The following license files were not found.");

                foreach(var item in notFoundList)
                {
                    Console.WriteLine($"** {item}.");
                }

                Console.WriteLine($"");
                Console.WriteLine("* Press any key to exit...");
                Console.ReadKey();
            }
            catch(Exception ex)
            {
                Console.WriteLine($"An error has occurred. Message:{ex.Message}");
                Console.WriteLine("* Press any key to exit...");
                Console.ReadKey();
            }
        }

        public static string RemoveEndsWith(string inputString ,string removeString)
        {
            if (string.IsNullOrEmpty(inputString))
            {
                return null;
            }

            string returnString = inputString;

            if (inputString.EndsWith(removeString))
            {
                Console.WriteLine($"*** Remove \"{removeString}\" at the end.");
                returnString = inputString.Remove(inputString.Length - removeString.Length);
            }

            return returnString;
        }

        /// <summary>
        /// Check to see if any adjustments are needed.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="token"></param>
        public static bool VerifyNeedAdjustments(List<string> list, string token)
        {
            // 正直もう10ファイルくらいで場合によっては調整エラーになる
            if ((list.Count > 10) && (string.IsNullOrEmpty(token)))
            {
                Console.WriteLine("* Adjust the sense of the REST API call so that it does not exceed the rate.");
                return true;
            }

            return false;
        }
    }
}
