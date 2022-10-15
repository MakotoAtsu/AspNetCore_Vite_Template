using Microsoft.AspNetCore.SpaServices;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;

namespace Net6_Controller_And_VIte
{
    public static class ViteHelper
    {
        private static bool PlatformIsWindows => OperatingSystem.IsWindows();

        private static ILogger ViteLogger;


        /// <summary>
        /// Adds Connection to Vite Hosted VueApplication
        /// configured per <seealso cref="SpaOptions"/> on the <paramref name="spa"/>.
        /// NOTE: (this will create devcert.pfx and vite.config.js in your Vue Application on first run)
        /// </summary>
        /// <param name="port">Vite hosting port</param>
        /// <param name="sourcePath">Vite app source path</param>
        public static void UseViteDevelopmentServer(this ISpaBuilder spa, int? port = null, string sourcePath = null)
        {

            // throw error if node.js not installed.
            EnsureNodeJSAlreadyInstalled();

            // Default HostingPort
            if (!port.HasValue)
                port = 3000;

            spa.Options.DevServerPort = port.Value;

            if (!string.IsNullOrWhiteSpace(sourcePath))
                spa.Options.SourcePath = sourcePath;
            else if (string.IsNullOrWhiteSpace(spa.Options.SourcePath))
                throw new ArgumentNullException("ISpaBuilder.Options.SourcePath", "Must specific Spa Client App path");


            var devServerEndpoint = new Uri($"https://localhost:{spa.Options.DevServerPort}");
            var webHostEnvironment = spa.ApplicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            ViteLogger = spa.ApplicationBuilder.ApplicationServices.GetService<ILoggerFactory>()?.CreateLogger("Vite");

            // If port not in used , launch vite dev server
            if (!CheckPortInUsed(spa.Options.DevServerPort))
            {

                // export dev cert
                var spaFolder = Path.Combine(webHostEnvironment.ContentRootPath, spa.Options.SourcePath);
                if (!Directory.Exists(spaFolder))
                    throw new DirectoryNotFoundException(spaFolder);

                var viteConfigPath = GetViteConfigFile(spaFolder);

                var devCert = Path.Combine(spaFolder, "devcert.pfx");
                var serverOptionFile = Path.Combine(spaFolder, $"serverOption{new FileInfo(viteConfigPath).Extension}");


                // Check dev pfx exist
                if (!File.Exists(serverOptionFile) || !File.Exists(devCert))
                {
                    var pwd = CreateCertPfxKey(devCert);

                    // Create serverOption file
                    File.WriteAllText(serverOptionFile, BuildServerOption(devCert, pwd));
                    ViteLogger?.LogInformation($"Creating Vite config: {serverOptionFile}");

                    InjectionViteConfig(viteConfigPath, serverOptionFile);
                }


                EnsureNodeModuleAlreadyInstalled(spa.Options.SourcePath);

                // launch Vite development server
                RunDevServer(spa.Options.SourcePath, spa.Options.DevServerPort, spa.Options.StartupTimeout);
            }

            spa.UseProxyToSpaDevelopmentServer(devServerEndpoint);
        }

        /// <summary>
        /// Injection vite.config file to use serverOption file
        /// </summary>
        private static void InjectionViteConfig(string viteConfigPath, string serverOptionFile)
        {
            var optionFile = new FileInfo(serverOptionFile);
            var serverOption = optionFile.Name[..^optionFile.Extension.Length];
            var data = File.ReadAllLines(viteConfigPath).ToList();

            // Already injection
            if (data.Any(x => x.Contains($"./{serverOption}")))
                return;

            data.Insert(0, $"import serverOption from './{serverOption}'");

            var exportDefaultLine = data.FindIndex(x => x.Contains("export default"));
            if (exportDefaultLine == -1)
                return;

            data.Insert(exportDefaultLine + 1, "  server : serverOption,");

            File.WriteAllLines(viteConfigPath, data);
        }

        /// <summary>
        /// Get vite.config file Path (support .ts and .js)
        /// </summary>
        private static string GetViteConfigFile(string rootPath)
        {
            var configFile = Directory.GetFiles(rootPath)
                                      .Where(x =>
                                      {
                                          var file = new FileInfo(x);
                                          var fileName = file.Name[..^file.Extension.Length];
                                          return fileName.Equals("vite.config",
                                                                 StringComparison.OrdinalIgnoreCase);
                                      })
                                      .Single();

            return configFile;
        }

        /// <summary>
        /// Build Vite https server option
        /// </summary>
        private static string BuildServerOption(string certfile, string pass)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("export default {");
            sb.AppendLine($"https: {{ pfx: '{Path.GetFileName(certfile)}', passphrase: '{pass}' }}");
            sb.AppendLine("}");
            sb.AppendLine();

            return sb.ToString();
        }

        private static bool CheckPortInUsed(int port)
            => IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(x => x.Port)
                .Contains(port);

        /// <summary>
        /// if 'node_module' not exist than run 'npm install'
        /// </summary>
        private static void EnsureNodeModuleAlreadyInstalled(string sourcePath)
        {
            // Check Node_Module exists
            if (!Directory.Exists(Path.Combine(sourcePath, "node_modules")))
            {
                ViteLogger?.LogWarning($"node_modules not found , run npm install...");

                // Install node modules
                var ps = Process.Start(new ProcessStartInfo()
                {
                    FileName = PlatformIsWindows ? "cmd" : "npm",
                    Arguments = $"{(PlatformIsWindows ? "/c npm " : "")}install",
                    WorkingDirectory = sourcePath,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });

                ps.WaitForExit();
                ViteLogger?.LogWarning($"npm install done.");
            }
        }

        /// <summary>
        /// Throw exception if 'node --version' catch error
        /// </summary>
        /// <exception cref="Exception"></exception>
        private static void EnsureNodeJSAlreadyInstalled()
        {
            var ps = Process.Start(new ProcessStartInfo()
            {
                FileName = PlatformIsWindows ? "cmd" : "node",
                Arguments = $"{(PlatformIsWindows ? "/c node " : "")}--version",
                //WorkingDirectory = /*SourcePath*/,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });

            ps.WaitForExit();

            if (ps.ExitCode == 0)
                return;

            throw new Exception("Node.js is required to build and run this project. To continue, please install Node.js from https://nodejs.org/, and then restart your command prompt or IDE.");

        }

        /// <summary>
        /// Create pfx key and return password
        /// </summary>
        private static string CreateCertPfxKey(string fileName)
        {
            var pfxPassword = Guid.NewGuid().ToString("N");
            ViteLogger?.LogInformation($"Exporting dotnet dev cert to {fileName} for Vite");
            ViteLogger?.LogDebug($"Export password: {pfxPassword}");
            var certExport = new ProcessStartInfo
            {
                FileName = PlatformIsWindows ? "cmd" : "dotnet",
                Arguments = $"{(PlatformIsWindows ? "/c dotnet " : "")}dev-certs https -v -ep {fileName} -p {pfxPassword}",
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            var exportProcess = Process.Start(certExport);
            exportProcess.WaitForExit();
            if (exportProcess.ExitCode == 0)
                ViteLogger?.LogInformation(exportProcess.StandardOutput.ReadToEnd());
            else
                ViteLogger?.LogError(exportProcess.StandardError.ReadToEnd());

            return pfxPassword;
        }

        private static void RunDevServer(string sourcePath, int port, TimeSpan timeout)
        {
            var runningPort = $" -- --port {port}";
            var processInfo = new ProcessStartInfo
            {
                FileName = PlatformIsWindows ? "cmd" : "npm",
                Arguments = $"{(PlatformIsWindows ? "/c npm " : "")}run dev{runningPort}",
                WorkingDirectory = sourcePath,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            var process = Process.Start(processInfo);
            var tcs = new TaskCompletionSource<int>();

            _ = Task.Run(() =>
            {
                try
                {
                    string? line;
                    while ((line = process?.StandardOutput.ReadLine()?.Trim()) != null)
                    {
                        // Wait for done message
                        if (!string.IsNullOrEmpty(line))
                        {
                            ViteLogger?.LogInformation(line);
                            if (!tcs.Task.IsCompleted && line.Contains("VITE", StringComparison.OrdinalIgnoreCase))
                                if (line.Contains("ready in", StringComparison.OrdinalIgnoreCase) || // for VITE v3
                                    line.Contains("Dev server running at:", StringComparison.OrdinalIgnoreCase)) // for VITE v2
                                {
                                    tcs.SetResult(1);
                                }
                        }
                    }
                }
                catch (EndOfStreamException ex)
                {
                    ViteLogger?.LogError(ex.ToString());
                    tcs.SetException(new InvalidOperationException("'npm run dev' failed.", ex));
                }
            });

            _ = Task.Run(() =>
            {
                try
                {
                    string? line;
                    while ((line = process?.StandardError.ReadLine()?.Trim()) != null)
                    {
                        ViteLogger?.LogError(line);
                    }
                }
                catch (EndOfStreamException ex)
                {
                    ViteLogger?.LogError(ex.ToString());
                    tcs.SetException(new InvalidOperationException("'npm run dev' failed.", ex));
                }
            });

            if (!tcs.Task.Wait(timeout))
            {
                throw new TimeoutException();
            }
        }
    }
}
