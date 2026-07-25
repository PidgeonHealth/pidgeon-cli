// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Commands;
using Pidgeon.CLI.Extensions;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using System.CommandLine;
using System.Reflection;

namespace Pidgeon.CLI;

/// <summary>
/// Entry point for the free-core public CLI build (<c>PidgeonCommunityComposition=true</c>).
/// Compiles exactly the community command catalog declared in
/// <c>config/public-compositions/community.yaml</c> — generate, validate, deident, data,
/// artifacts, lookup, find, path, run, session, completions — against the exact
/// <c>Pidgeon.Core</c> and <c>Pidgeon.Data.Baseline</c>
/// packages. No Flock/Loft/Migrate/Bridge reference, no reflection scan reaching outside this
/// assembly. <see cref="Program"/> (the all-product commercial host) is excluded from this
/// composition, so this is the only entry point compiled here. Bounded conform and capability
/// signaling remain separate follow-ups because their commercial implementations cross the
/// public policy boundary.
/// </summary>
internal class PublicProgram
{
    public static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected stdout */ }

        var (quiet, outputFormat, verbose) = ParseGlobalOptions(args);
        var host = CreateHostBuilder(args, quiet, outputFormat, verbose).Build();

        try
        {
            if (args.Contains("--version"))
            {
                var infoVersion = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                Console.WriteLine($"pidgeon {infoVersion ?? "0.0.0"}");
                return 0;
            }

            if (args.Length == 0)
            {
                var bannerService = host.Services.GetRequiredService<BannerService>();
                bannerService.DisplayBanner(showQuickStart: true);
                return 0;
            }

            var rootCommand = CreateRootCommand(host.Services);
            return await rootCommand.Parse(args).InvokeAsync();
        }
        catch (Exception ex)
        {
            var logger = host.Services.GetRequiredService<ILogger<PublicProgram>>();
            logger.LogError(ex, "An unhandled exception occurred");
            return 1;
        }
    }

    private static (bool quiet, string outputFormat, bool verbose) ParseGlobalOptions(string[] args)
    {
        bool quiet = false;
        string outputFormat = "text";
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--quiet" or "-q":
                    quiet = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--output-format" when i + 1 < args.Length:
                    outputFormat = args[i + 1];
                    i++;
                    break;
            }
        }

        return (quiet, outputFormat, verbose);
    }

    private static IHostBuilder CreateHostBuilder(string[] args, bool quiet, string outputFormat, bool verbose)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.SetMinimumLevel(verbose ? LogLevel.Information : LogLevel.Warning);
                logging.AddFilter(
                    "Pidgeon.Core.Infrastructure.Registry.StandardPluginRegistry",
                    verbose ? LogLevel.Information : LogLevel.Error);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddPidgeonCliCommunity();
                services.AddSingleton<IConsoleOutput>(_ => ConsoleOutput.Create(quiet, outputFormat));
                services.AddSingleton<BannerService>();
            });
    }

    private static RootCommand CreateRootCommand(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("Pidgeon Healthcare Interoperability Platform (community)")
        {
            Description = "Free-core CLI: generate, validate, deident, data, artifacts, lookup, find, path, run, session, completions"
        };

        var verboseOption = new Option<bool>("--verbose") { Description = "Enable verbose output" };
        var quietOption = new Option<bool>("--quiet", "-q") { Description = "Suppress status output (only emit data to stdout)" };
        var outputOption = new Option<string>("--output-format")
        {
            Description = "Stdout format: text|json (default: text). Subcommands may also accept their own --output for output paths.",
            DefaultValueFactory = _ => "text"
        };
        rootCommand.Add(verboseOption);
        rootCommand.Add(quietOption);
        rootCommand.Add(outputOption);

        var assembly = typeof(PublicProgram).Assembly;
        var commandTypes = assembly.GetTypes()
            .Where(type => type.IsClass &&
                          !type.IsAbstract &&
                          type.IsSubclassOf(typeof(CommandBuilderBase)))
            .ToList();

        foreach (var commandType in commandTypes)
        {
            var command = (CommandBuilderBase)serviceProvider.GetRequiredService(commandType);
            rootCommand.Add(command.CreateCommand());
        }

        return rootCommand;
    }
}
