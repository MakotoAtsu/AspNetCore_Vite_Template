using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ProjectTemplate
{
    public static class ViteHelper
    {
        // done message of 'npm run serve' command.
        private static string DoneMessage { get; } = "Dev server running at:";

        /// <summary>
        /// Adds Connection to Vite Hosted VueApplication
        /// configured per <seealso cref="SpaOptions"/> on the <paramref name="spa"/>.
        /// NOTE: (this will create devcert.pfx and vite.config.js in your Vue Application on first run)
        /// </summary>
        /// <param name="spa"></param>
        public static void UseViteDevelopmentServer(this ISpaBuilder spa, int? port = null)
        {
            // Default HostingPort
            if (spa.Options.DevServerPort == 0)
                spa.Options.DevServerPort = 3000;

            if (port.HasValue)
                spa.Options.DevServerPort = port.Value;

            if (string.IsNullOrWhiteSpace(spa.Options.SourcePath))
                throw new ArgumentNullException("ISpaBuilder.Options.SourcePath", "Must specific Spa Client App path");

            var devServerEndpoint = new Uri($"https://localhost:{spa.Options.DevServerPort}");
            var loggerFactory = spa.ApplicationBuilder.ApplicationServices.GetService<ILoggerFactory>();
            var webHostEnvironment = spa.ApplicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            var logger = loggerFactory.CreateLogger("Vue");

            // Is this already running
            bool IsRunning = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(x => x.Port)
                .Contains(spa.Options.DevServerPort);

            if (!IsRunning)
            {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                // export dev cert
                var spaFolder = Path.Combine(webHostEnvironment.ContentRootPath, spa.Options.SourcePath);
                if (!Directory.Exists(spaFolder))
                    throw new DirectoryNotFoundException(spaFolder);

                var viteConfigPath = GetViteConfigFile(spaFolder);

                var tempPfx = Path.Combine(spaFolder, "devcert.pfx");
                var serverOptionFile = Path.Combine(spaFolder, $"serverOption{new FileInfo(viteConfigPath).Extension}");


                // Check dev pfx exist
                if (!File.Exists(serverOptionFile) || !File.Exists(tempPfx))
                {
                    var pfxPassword = Guid.NewGuid().ToString("N");
                    logger.LogInformation($"Exporting dotnet dev cert to {tempPfx} for Vite");
                    logger.LogDebug($"Export password: {pfxPassword}");
                    var certExport = new ProcessStartInfo
                    {
                        FileName = isWindows ? "cmd" : "dotnet",
                        Arguments = $"{(isWindows ? "/c dotnet " : "")}dev-certs https -v -ep {tempPfx} -p {pfxPassword}",
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    };

                    var exportProcess = Process.Start(certExport);
                    exportProcess.WaitForExit();
                    if (exportProcess.ExitCode == 0)
                        logger.LogInformation(exportProcess.StandardOutput.ReadToEnd());
                    else
                        logger.LogError(exportProcess.StandardError.ReadToEnd());

                    // Create serverOption file
                    File.WriteAllText(serverOptionFile, BuildServerOption(tempPfx, pfxPassword));
                    logger.LogInformation($"Creating Vite config: {serverOptionFile}");

                    InjectionViteConfig(viteConfigPath, serverOptionFile);
                }

                // Check Node_Module exists
                if (!Directory.Exists(Path.Combine(spa.Options.SourcePath, "node_modules")))
                {
                    logger.LogWarning($"node_modules not found , run npm install...");
                    // Install node modules
                    var ps = Process.Start(new ProcessStartInfo()
                    {
                        FileName = isWindows ? "cmd" : "npm",
                        Arguments = $"{(isWindows ? "/c npm " : "")}install",
                        WorkingDirectory = spa.Options.SourcePath,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    });

                    ps.WaitForExit();
                    logger.LogWarning($"npm install done.");
                }

                // launch Vite development server
                var runningPort = port.HasValue ? $" -- --port {port.Value}" : string.Empty;
                var processInfo = new ProcessStartInfo
                {
                    FileName = isWindows ? "cmd" : "npm",
                    Arguments = $"{(isWindows ? "/c npm " : "")}run dev{runningPort}",
                    WorkingDirectory = spa.Options.SourcePath,
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
                        string line;
                        while ((line = process.StandardOutput.ReadLine()?.Trim()) != null)
                        {
                            if (!String.IsNullOrEmpty(line))
                            {
                                logger.LogInformation(line);
                                if (!tcs.Task.IsCompleted && line.Contains(DoneMessage, StringComparison.OrdinalIgnoreCase))
                                {
                                    tcs.SetResult(1);
                                }
                            }
                        }
                    }
                    catch (EndOfStreamException ex)
                    {
                        logger.LogError(ex.ToString());
                        tcs.SetException(new InvalidOperationException("'npm run dev' failed.", ex));
                    }
                });

                _ = Task.Run(() =>
                {
                    try
                    {
                        string line;
                        while ((line = process.StandardError.ReadLine()?.Trim()) != null)
                        {
                            logger.LogError(line);
                        }
                    }
                    catch (EndOfStreamException ex)
                    {
                        logger.LogError(ex.ToString());
                        tcs.SetException(new InvalidOperationException("'npm run dev' failed.", ex));
                    }
                });

                if (!tcs.Task.Wait(spa.Options.StartupTimeout))
                {
                    throw new TimeoutException();
                }
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
            sb.AppendLine("import { ServerOptions } from 'vite'");
            sb.AppendLine();
            sb.AppendLine("export default {");
            sb.AppendLine($"https: {{ pfx: '{Path.GetFileName(certfile)}', passphrase: '{pass}' }}");
            sb.AppendLine("}");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
