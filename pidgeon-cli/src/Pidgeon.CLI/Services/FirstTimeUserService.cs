// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core;

namespace Pidgeon.CLI.Services;

/// <summary>
/// Manages the first-time user experience: first-run detection, auto-graduation to a
/// minimal config, and the welcome/init flows. The interactive on-device AI model wizard
/// lives in <see cref="AiModelSetupService"/> (optional here) so this service carries no
/// AI-provider dependency of its own.
/// </summary>
public class FirstTimeUserService(
    ILogger<FirstTimeUserService> logger,
    IAiModelSetupService? aiModelSetup = null)
{
    private readonly ILogger<FirstTimeUserService> _logger = logger;
    private readonly IAiModelSetupService? _aiModelSetup = aiModelSetup;
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pidgeon");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Checks if this is the first time the user is running Pidgeon.
    /// </summary>
    public async Task<bool> IsFirstTimeUserAsync()
    {
        await Task.Yield();
        var configFile = Path.Combine(_configPath, "pidgeon.config.json");
        return !File.Exists(configFile);
    }

    /// <summary>
    /// Creates a minimal default configuration for auto-graduation.
    /// Used when experienced users run productive commands without going through setup.
    /// </summary>
    public async Task<Result<bool>> CreateMinimalConfigAsync()
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(_configPath);

            var configFile = Path.Combine(_configPath, "pidgeon.config.json");

            // Create wise default configuration (git-style)
            var config = new
            {
                firstRun = DateTime.UtcNow,
                version = "1.0.0",
                setupType = "auto-graduated",
                telemetry = new { enabled = false },
                preferences = new
                {
                    defaultStandard = "hl7",
                    outputFormat = "console",
                    validationMode = "compatibility",
                    aiAssistance = "disabled"
                },
                generation = new
                {
                    defaultCount = 1,
                    includeOptionalFields = true,
                    seedStrategy = "random"
                },
                deidentification = new
                {
                    dateShiftDays = 30,
                    preserveFormat = true,
                    crossMessageConsistency = true
                },
                validation = new
                {
                    strictMode = false,
                    showWarnings = true,
                    stopOnFirstError = false
                }
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(configFile, json);

            _logger.LogInformation("Created minimal configuration for auto-graduation");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create minimal configuration");
            return Result<bool>.Failure($"Configuration creation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs welcome orientation - demo and introduction only (no configuration).
    /// </summary>
    public async Task<Result<bool>> RunWelcomeOrientationAsync()
    {
        try
        {
            ShowWelcomeBanner();
            
            Console.WriteLine("Let's see what Pidgeon can do...");
            Console.WriteLine();
            
            // Always run the demo for welcome
            await RunQuickDemoAsync();
            
            Console.WriteLine();
            Console.WriteLine("Ready to set up your machine? Run: pidgeon --init");
            Console.WriteLine("Need help? Run: pidgeon help");
            Console.WriteLine();
            
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during welcome orientation");
            return Result<bool>.Failure($"Welcome orientation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs machine initialization - configuration and setup.
    /// </summary>
    public async Task<Result<bool>> RunInitializationAsync()
    {
        try
        {
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  Pidgeon Machine Setup");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("Let's get your machine configured for productive use.");
            Console.WriteLine();
            
            // Initialize project structure first
            await InitializeProjectStructureAsync();
            
            // Set up AI models (optional)
            Console.WriteLine("Step 1: AI Model Setup");
            Console.WriteLine("───────────────────────");
            if (_aiModelSetup is not null)
            {
                await _aiModelSetup.RunModelSelectionWizardAsync();
            }
            else
            {
                Console.WriteLine("AI model setup is not available in this build.");
                Console.WriteLine("You can always run 'pidgeon ai download' later.");
            }

            // Save configuration
            await SaveFirstRunConfigAsync();
            
            Console.WriteLine();
            Console.WriteLine("✅ Machine setup complete!");
            Console.WriteLine();
            Console.WriteLine("Try these commands:");
            Console.WriteLine("  pidgeon generate ADT^A01         # Generate test message");
            Console.WriteLine("  pidgeon validate --file msg.hl7  # Validate a message");
            Console.WriteLine("  pidgeon welcome                  # See demo");
            Console.WriteLine();
            
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initialization");
            return Result<bool>.Failure($"Initialization failed: {ex.Message}");
        }
    }

    private static void ShowWelcomeBanner()
    {
        // Don't clear screen - respect user's terminal history
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  Welcome to Pidgeon Healthcare Platform");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("Generate, validate, and test HL7/FHIR messages");
        Console.WriteLine("without the compliance nightmare of real patient data.");
        Console.WriteLine();
        
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }


    private static async Task RunQuickDemoAsync()
    {
        await Task.Yield();
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine("  Quick Demo");
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("Generating an HL7 admission message...");
        Console.WriteLine();
        
        // Show the command
        Console.WriteLine("  $ pidgeon generate ADT^A01");
        Console.WriteLine();
        
        // Generate and show just a snippet
        var message = GenerateDemoMessage();
        var lines = message.Split('\n');
        
        // Show first 3 segments only
        Console.WriteLine("  " + lines[0]); // MSH
        Console.WriteLine("  " + lines[1]); // EVN
        Console.WriteLine("  " + lines[2]); // PID
        Console.WriteLine("  ...");
        Console.WriteLine();
        
        Console.WriteLine("✅ Generated realistic HL7 message");
        Console.WriteLine("   • Synthetic patient data (safe for testing)");
        Console.WriteLine("   • Standards-compliant format");
        Console.WriteLine();
        
        Console.WriteLine("Common commands:");
        Console.WriteLine("  pidgeon generate ADT^A01         # Generate admission");
        Console.WriteLine("  pidgeon validate --file msg.hl7  # Validate message");
        Console.WriteLine("  pidgeon deident --in ./real      # De-identify real data");
        Console.WriteLine();
    }

    private static async Task ShowDeIdentificationDemoAsync()
    {
        await Task.Yield();
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine("  De-identification");
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("Transform real messages into safe test data:");
        Console.WriteLine();
        Console.WriteLine("  $ pidgeon deident --in ./real --out ./safe");
        Console.WriteLine();
        Console.WriteLine("• Removes all PHI (HIPAA Safe Harbor)");
        Console.WriteLine("• Preserves message structure");
        Console.WriteLine("• Runs 100% on your device");
        Console.WriteLine();
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private static async Task RunGuidedTutorialAsync()
    {
        await Task.Yield();
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine("  Tutorial Coming Soon");
        Console.WriteLine("──────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("For now, explore these commands:");
        Console.WriteLine();
        Console.WriteLine("  pidgeon help              # Show all commands");
        Console.WriteLine("  pidgeon generate --help   # Generation options");
        Console.WriteLine("  pidgeon validate --help   # Validation options");
        Console.WriteLine();
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private async Task InitializeProjectStructureAsync()
    {
        await Task.Yield();
        try
        {
            // Create standard directory structure
            var directories = new[]
            {
                Path.Combine(_configPath, "models"),
                Path.Combine(_configPath, "configs"),
                Path.Combine(_configPath, "configs", "epic"),
                Path.Combine(_configPath, "configs", "cerner"),
                Path.Combine(_configPath, "configs", "allscripts"),
                Path.Combine(_configPath, "workflows"),
                Path.Combine(_configPath, "templates"),
            };

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
            }

            _logger.LogInformation("Initialized project structure at {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize project structure");
        }
    }

    private async Task SaveFirstRunConfigAsync()
    {
        try
        {
            var configFile = Path.Combine(_configPath, "pidgeon.config.json");
            var config = new
            {
                firstRun = DateTime.UtcNow,
                version = "1.0.0",
                telemetry = new { enabled = false },
                preferences = new
                {
                    defaultStandard = "hl7",
                    autoDetectModels = true,
                    colorOutput = true
                }
            };

            var json = JsonSerializer.Serialize(config, JsonOptions);

            await File.WriteAllTextAsync(configFile, json);
            _logger.LogInformation("Saved first-run configuration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save first-run configuration");
        }
    }

    private static string GenerateDemoMessage()
    {
        return @"MSH|^~\&|PIDGEON|HOSPITAL|RECEIVER|FACILITY|20250912154823||ADT^A01^ADT_A01|MSG12345|P|2.3
EVN|A01|20250912154823|||
PID|1||PAT123456^^^HOSPITAL^MR||DOE^JOHN^M||19850615|M||2106-3|123 MAIN ST^^ANYTOWN^CA^90210
PV1|1|I|ICU^201^A|E||||||SUR||||2|||DOC123^SMITH^JANE
DG1|1||I10^E11.9^Diabetes mellitus type 2||20250912|A";
    }

}