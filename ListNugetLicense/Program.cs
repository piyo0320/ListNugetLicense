using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ListNugetLicense
{
    class Program
    {
        static readonly string appPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        static readonly string nugetUrl = "https://api.nuget.org/v3-flatcontainer/";
        static readonly string gitHubApiRepoUrl = "https://api.github.com/repos/";
        static readonly XNamespace nuspecNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

        // Get-Package | Select-Object Id, Version | Foreach-Object { "$($_.Id)`t$($_.Version)" }
        static readonly string InputFileName = "NugetPackageList.txt";

        static async Task Main(string[] args)
        {
            try
            {
                Console.WriteLine("- Loading the configuration file.");

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
                Console.WriteLine("- Check the proxy settings.");

                HttpClient client = null;
                var proxySetting = appSettingConfiguration.GetSection("Proxy");
                var proxyHost = proxySetting["Host"]?.ToString();

                if (!string.IsNullOrEmpty(proxyHost))
                {
                    Console.WriteLine("- Load the proxy settings.");

                    var proxyPort = proxySetting["Port"]?.ToString();
                    var proxyUser = proxySetting["UserName"]?.ToString();
                    var proxyPass = proxySetting["UserPassword"]?.ToString();

                    HttpClientHandler clientHandler = new HttpClientHandler();

                    clientHandler.Proxy = new WebProxy(proxyHost + ":" + proxyPort);
                    clientHandler.Proxy.Credentials = new NetworkCredential(proxyUser, proxyPass);
                    clientHandler.UseProxy = true;

                    Console.WriteLine("- Generate a client with proxy settings...");

                    client = new HttpClient(clientHandler);
                }
                else
                {
                    Console.WriteLine("- No proxy settings...");

                    client = new HttpClient();
                }

                var token = appSettingConfiguration.GetChildren().Where(r => r.Key == "GithubToken").Single().Value;

                client.DefaultRequestHeaders.Add("Authorization", $"token {token}");

                Console.WriteLine($"");
                Console.WriteLine("- Load target files.");
                var targetFiles = appSettingConfiguration.GetSection("TargetFiles:FileName").Get<List<string>>();

                if (targetFiles.Count == 0)
                {
                    Console.WriteLine("- There is no target. Terminate the process.");
                    Console.WriteLine("- Press any key to exit...");
                    Console.ReadKey();
                    return;
                }
                else if (targetFiles.Count > 60)
                {
                    Console.WriteLine("- Detected over 60 files..");
                }

                Console.WriteLine("- The targets are as follows:");
                foreach (var item in targetFiles)
                {
                    Console.WriteLine($"-- {item}");
                }

                Console.WriteLine($"");
                Console.WriteLine($"- Load {InputFileName}.");
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
                                    Console.WriteLine($"-- {name} {version} has already been added.");
                                    continue;
                                }
                            }
                        }

                        packageDictionary.Add(name, version);
                        Console.WriteLine($"-- {name} {version}.");
                    }
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

                    Console.WriteLine($"- Load package manifest.");
                    var nuspecXml = XDocument.Load(nugetUrl + $"{packageId}/{packageVersion}/{packageId}.nuspec");

                    Console.WriteLine($"- Get project URL.");
                    var projectUrl = (string)nuspecXml.Root.Descendants(nuspecNamespace + "projectUrl").FirstOrDefault();

                    string repositoryUrlForApi;
                    string repositoryUrl;
                    if (string.IsNullOrEmpty(projectUrl) || !projectUrl.Contains("github.com"))
                    {
                        Console.WriteLine($"-- Since the projectUrl does not contain the GitHub URL,");
                        Console.WriteLine($"     process will try to get it from the repository's url attribute.");

                        repositoryUrl =
                            (string)nuspecXml.Root.Descendants(nuspecNamespace + "repository").FirstOrDefault()?.Attribute("url");

                        if (string.IsNullOrEmpty(repositoryUrl) || !repositoryUrl.Contains("github.com"))
                        {
                            Console.WriteLine($"--- Repository URL :{repositoryUrl}.");
                            Console.WriteLine("--- Only github is supported. Skip the process.");
                            continue;
                        }

                        repositoryUrlForApi = repositoryUrl.Split("github.com").Last().Trim('/');
                    }
                    else
                    {
                        Console.WriteLine($"-- Since projectUrl holds the URL of GitHub, process will use it.");
                        repositoryUrlForApi = projectUrl.Split("github.com").Last().Trim('/');
                    }

                    if (repositoryUrlForApi.EndsWith(".git"))
                    {
                        Console.WriteLine($"--- Remove the .git at the end.");
                        repositoryUrlForApi.Remove(repositoryUrlForApi.Length - 4);
                    }

                    Console.WriteLine($"-- Successfully retrieved the gitHub URL. {repositoryUrlForApi}");

                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                    client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

                    foreach(var file in targetFiles)
                    {
                        Console.WriteLine($"");
                        Console.WriteLine($"--- Attempt to retrieve {file}.");

                        var requestUrl = gitHubApiRepoUrl + repositoryUrlForApi + $"/contents/{file}";
                        Console.WriteLine($"--- Send Get request {requestUrl}.");

                        HttpResponseMessage response = await client.GetAsync(requestUrl);

                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == HttpStatusCode.Forbidden)
                            {
                                Console.WriteLine($"--- Response Status code :{response.StatusCode}.");
                                Console.WriteLine($"--- Processing is terminated due to an adjustment error. Please use a personal token.");
                                return;
                            }


                            Console.WriteLine($"--- Response Status code :{response.StatusCode}.");
                            Console.WriteLine($"--- Skip the process.");
                            continue;
                        }

                        Console.WriteLine($"");
                        Console.WriteLine("-- Succeeded in retrieving the file.");

                        var contents = await JsonSerializer.DeserializeAsync<Contents>(await response.Content.ReadAsStreamAsync());

                        // Base64でエンコードされているのでデコード
                        var encodedContent = contents.Content;
                        string decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(encodedContent));

                        // 結果を格納する
                        // パッケージ名のディレクトリを作成
                        var packageFolder = Directory.CreateDirectory(Path.Combine(appPath, packageId)).ToString();
                        File.WriteAllText(Path.Combine(packageFolder, $"{file}"), decodedContent);

                        Console.WriteLine($"");
                        Console.WriteLine($"-- {packageId} license file created or overwrited.");

                        break;
                    }
                }

                Console.WriteLine($"");
                Console.WriteLine($"=========================================================");
                Console.WriteLine("- Completed! Press any key to exit...");
                Console.ReadKey();
            }
            catch(Exception ex)
            {
                //Console.WriteLine($"An error has occurred. Message:{ex.Message}");
                Console.WriteLine("- Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}
