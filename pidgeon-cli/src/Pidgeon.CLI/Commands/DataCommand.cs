// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Command for managing clinical terminology data packages.
/// </summary>
public partial class DataCommand : CommandBuilderBase
{
    private readonly IDataPackageManager _packageManager;
    private readonly IServiceProvider _serviceProvider;

    public DataCommand(
        ILogger<DataCommand> logger,
        IConsoleOutput output,
        IDataPackageManager packageManager,
        IServiceProvider serviceProvider,
        FirstTimeUserService firstTimeUserService)
        : base(logger, output, firstTimeUserService)
    {
        _packageManager = packageManager ?? throw new ArgumentNullException(nameof(packageManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override Command CreateCommand()
    {
        var command = new Command("data", "Manage clinical terminology data packages");
        command.Add(CreateListCommand());
        command.Add(CreateInstallCommand());
        command.Add(CreateRemoveCommand());
        command.Add(CreateStatusCommand());
        AddCompositionCommands(command);
        return command;
    }

    // The private composition supplies maintainer/intelligence verbs from
    // DataCommand.DevTools.cs. The public composition omits that source file, so this call is
    // erased by the compiler and no hidden command can be activated by environment variables.
    partial void AddCompositionCommands(Command command);

    private Command CreateListCommand()
    {
        var command = new Command("list", "List available and installed data packages");

        SetInfoCommandAction(command, async (parseResult, cancellationToken) =>
        {
            // Agent-legible listing: one JSON array on stdout, no table art.
            if (Output.OutputFormat == "json")
            {
                var pkgs = await _packageManager.ListPackagesAsync();
                Output.WriteData(pkgs.Select(pkg => new
                {
                    name = pkg.Name,
                    dataType = pkg.DataType,
                    installed = pkg.IsInstalled,
                    recordCount = pkg.Manifest?.RecordCount,
                    version = pkg.Manifest?.Version,
                }).ToList());
                return 0;
            }

            // Show bundles first
            var bundles = _packageManager.ListBundles();
            if (bundles.Count > 0)
            {
                Output.WriteStatus("Bundles");
                Output.WriteStatus("");
                foreach (var b in bundles)
                {
                    Output.WriteData($"  {b.Name,-25} {b.DisplayName} ({b.Packages.Length} packages, {b.License} license)");
                }
                Output.WriteStatus("");
            }

            var packages = await _packageManager.ListPackagesAsync();

            Output.WriteStatus("Packages");
            Output.WriteStatus("");
            Output.WriteData($"{"Name",-25} {"DataType",-20} {"Status",-12} {"Records",-10} {"Version",-12}");
            Output.WriteData(new string('-', 79));

            foreach (var pkg in packages)
            {
                var status = pkg.IsInstalled ? "installed" : "available";
                var records = pkg.Manifest?.RecordCount.ToString("N0") ?? "-";
                var version = pkg.Manifest?.Version ?? "-";
                Output.WriteData($"{pkg.Name,-25} {pkg.DataType,-20} {status,-12} {records,-10} {version,-12}");
            }

            Output.WriteStatus("");
            var installed = packages.Count(p => p.IsInstalled);
            Output.WriteStatus($"{installed} installed, {packages.Count - installed} available");

            return 0;
        });

        return command;
    }

    private Command CreateInstallCommand()
    {
        var packageArg = new Argument<string?>("package")
        {
            Description = "Package name to install",
            Arity = ArgumentArity.ZeroOrOne
        };

        var allOption = CreateBooleanOption("--all", "-a", "Install all available packages");
        var acceptLicenseOption = CreateBooleanOption("--accept-license", "Accept required license terms (e.g., UMLS Metathesaurus License)");

        var command = new Command("install", "Install a data package")
        {
            packageArg, allOption, acceptLicenseOption
        };

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            var packageName = parseResult.GetValue(packageArg);
            var installAll = parseResult.GetValue(allOption);
            var acceptLicense = parseResult.GetValue(acceptLicenseOption);

            if (installAll)
            {
                var packages = await _packageManager.ListPackagesAsync();
                var available = packages.Where(p => !p.IsInstalled).ToList();

                if (available.Count == 0)
                {
                    Output.WriteStatus("All packages are already installed.");
                    return 0;
                }

                Output.WriteStatus($"Installing {available.Count} packages...");
                var failures = 0;

                foreach (var pkg in available)
                {
                    Output.WriteStatus($"  Installing {pkg.Name}...");
                    var result = await _packageManager.InstallPackageAsync(pkg.Name, acceptLicense);
                    if (result.IsSuccess)
                    {
                        Output.WriteSuccess($"  Installed {pkg.Name} ({result.Value.RecordCount:N0} records)");
                    }
                    else
                    {
                        Output.WriteError($"  Failed to install {pkg.Name}: {result.Error.Message}");
                        failures++;
                    }
                }

                Output.WriteStatus("");
                Output.WriteStatus($"Installed {available.Count - failures} of {available.Count} packages.");
                return failures > 0 ? 1 : 0;
            }

            if (string.IsNullOrEmpty(packageName))
            {
                Output.WriteError("Package name required. Usage:");
                Output.WriteError("  pidgeon data install <package-name>");
                Output.WriteError("  pidgeon data install --all");
                return 1;
            }

            // Check if this is a bundle for user-friendly progress output
            var bundlePackages = _packageManager.GetBundlePackages(packageName);
            if (bundlePackages != null)
            {
                var bundles = _packageManager.ListBundles();
                var bundleInfo = bundles.FirstOrDefault(b => string.Equals(b.Name, packageName, StringComparison.OrdinalIgnoreCase));
                var displayName = bundleInfo?.DisplayName ?? packageName;
                Output.WriteStatus($"Installing {displayName} ({bundlePackages.Length} packages)...");
            }
            else
            {
                Output.WriteStatus($"Installing {packageName}...");
            }

            var installResult = await _packageManager.InstallPackageAsync(packageName, acceptLicense);
            if (installResult.IsSuccess)
            {
                if (bundlePackages != null)
                {
                    var bundles = _packageManager.ListBundles();
                    var bundleInfo = bundles.FirstOrDefault(b => string.Equals(b.Name, packageName, StringComparison.OrdinalIgnoreCase));
                    var displayName = bundleInfo?.DisplayName ?? packageName;
                    Output.WriteSuccess($"{displayName} installed successfully ({installResult.Value.RecordCount:N0} records)");
                }
                else
                {
                    Output.WriteSuccess($"Installed {packageName} ({installResult.Value.RecordCount:N0} records)");
                }
                return 0;
            }

            Output.WriteError($"Failed to install {packageName}: {installResult.Error.Message}");
            return 1;
        });

        return command;
    }

    private Command CreateRemoveCommand()
    {
        var packageArg = new Argument<string>("package")
        {
            Description = "Package name to remove"
        };

        var command = new Command("remove", "Remove an installed data package")
        {
            packageArg
        };

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            var packageName = parseResult.GetValue(packageArg)!;

            Output.WriteStatus($"Removing {packageName}...");
            var result = await _packageManager.RemovePackageAsync(packageName);

            if (result.IsSuccess)
            {
                Output.WriteSuccess($"Removed {packageName}");
                return 0;
            }

            Output.WriteError($"Failed to remove {packageName}: {result.Error.Message}");
            return 1;
        });

        return command;
    }

    private Command CreateStatusCommand()
    {
        var command = new Command("status", "Show active data source status and record counts");

        SetInfoCommandAction(command, async (parseResult, cancellationToken) =>
        {
            var sources = await _packageManager.GetDataSourceStatusAsync();

            if (Output.OutputFormat == "json")
            {
                Output.WriteData(sources.Select(src => new
                {
                    dataType = src.DataType,
                    source = src.Source,
                    recordCount = src.RecordCount,
                }).ToList());
                return 0;
            }

            Output.WriteStatus("Data Source Status");
            Output.WriteStatus("");
            Output.WriteData($"{"DataType",-20} {"Source",-20} {"Records",-10}");
            Output.WriteData(new string('-', 50));

            var totalRecords = 0;
            foreach (var src in sources)
            {
                Output.WriteData($"{src.DataType,-20} {src.Source,-20} {src.RecordCount,-10:N0}");
                totalRecords += src.RecordCount;
            }

            Output.WriteStatus("");
            Output.WriteStatus($"Total: {totalRecords:N0} records across {sources.Count} data sources");

            return 0;
        });

        return command;
    }

}
