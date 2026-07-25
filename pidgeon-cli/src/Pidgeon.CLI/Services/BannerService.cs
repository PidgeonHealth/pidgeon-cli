// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text;
using Pidgeon.CLI.Output;

namespace Pidgeon.CLI.Services;

/// <summary>
/// Provides ASCII art banner display for Pidgeon CLI startup.
/// Incorporates Pidgeon logo design with HL7 encoding character motifs.
/// </summary>
public class BannerService
{
    private readonly IConsoleOutput _output;

    private const string BannerArt = """

        ▓▓▓▓▓▓▓▓     ██████╗ ██╗██████╗  ██████╗ ███████╗ ██████╗ ███╗   ██╗     ▓▓▓▓▓▓▓▓
         ▒▒▒▒▒▒▒▒    ██╔══██╗██║██╔══██╗██╔════╝ ██╔════╝██╔═══██╗████╗  ██║    ▒▒▒▒▒▒▒▒
          ░░░░░░░░   ██████╔╝██║██║  ██║██║  ███╗█████╗  ██║   ██║██╔██╗ ██║   ░░░░░░░░
           ████████  ██╔═══╝ ██║██║  ██║██║   ██║██╔══╝  ██║   ██║██║╚██╗██║  ████████
                     ██║     ██║██████╔╝╚██████╔╝███████╗╚██████╔╝██║ ╚████║
                     ╚═╝     ╚═╝╚═════╝  ╚═════╝ ╚══════╝ ╚═════╝ ╚═╝  ╚═══╝

                     Healthcare Message Testing Platform
                     HL7 • FHIR • NCPDP • X12
                     "Confident interoperability testing without PHI risk"

        """;

    public BannerService(IConsoleOutput output)
    {
        _output = output;
    }

    /// <summary>
    /// Displays the Pidgeon banner with version and quick start information.
    /// </summary>
    /// <param name="showQuickStart">Whether to show quick start commands</param>
    public void DisplayBanner(bool showQuickStart = true)
    {
        _output.WriteStatus("");
        _output.WriteStatus(BannerArt);
        _output.WriteStatus("");

        // Version and build info
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "dev";
        var buildDate = GetBuildDate(assembly);

        _output.WriteStatus($"    Version {version} - Built {buildDate:yyyy-MM-dd}");
        _output.WriteStatus("");

        if (showQuickStart)
        {
            DisplayQuickStart();
        }
    }

    /// <summary>
    /// Displays compact quick start commands for immediate productivity.
    /// </summary>
    public void DisplayQuickStart()
    {
        _output.WriteStatus("    Quick Start Commands:");
        _output.WriteStatus("");
        _output.WriteStatus("    pidgeon generate ADT^A01             # Generate HL7 admission message");
        _output.WriteStatus("    pidgeon generate Patient --count 5   # Generate 5 FHIR patients");
        _output.WriteStatus("    pidgeon validate message.hl7         # Validate healthcare message");
        _output.WriteStatus("    pidgeon deident --in ./samples       # De-identify real data");
        _output.WriteStatus("");
        _output.WriteStatus("    More Commands:");
        _output.WriteStatus("    pidgeon --help                       # Full command reference");
        _output.WriteStatus("    pidgeon lookup MSH                   # HL7 standards reference");
        _output.WriteStatus("    pidgeon workflow wizard              # Guided testing scenarios");
        _output.WriteStatus("    pidgeon diff file1.hl7 file2.hl7    # Smart message comparison");
        _output.WriteStatus("");
    }

    /// <summary>
    /// Displays minimal banner for non-interactive scenarios.
    /// </summary>
    public void DisplayCompactBanner()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "dev";

        _output.WriteStatus($"Pidgeon Healthcare Platform v{version} | Testing without PHI nightmares");
        _output.WriteStatus("");
    }

    /// <summary>
    /// Displays first-time user welcome with enhanced guidance.
    /// </summary>
    public void DisplayWelcome()
    {
        DisplayBanner(showQuickStart: false);

        _output.WriteStatus("    Welcome to Pidgeon!");
        _output.WriteStatus("");
        _output.WriteStatus("    Pidgeon transforms healthcare interoperability testing by providing:");
        _output.WriteStatus("    - Synthetic data generation (HL7, FHIR, NCPDP)");
        _output.WriteStatus("    - HIPAA-compliant de-identification");
        _output.WriteStatus("    - Multi-standard validation & troubleshooting");
        _output.WriteStatus("    - Vendor pattern detection & smart configuration");
        _output.WriteStatus("");
        _output.WriteStatus("    Try These First:");
        _output.WriteStatus("");
        _output.WriteStatus("    pidgeon welcome                      # Interactive demo");
        _output.WriteStatus("    pidgeon --init                       # Machine setup");
        _output.WriteStatus("    pidgeon generate ADT^A01             # Generate test message");
        _output.WriteStatus("");
    }

    /// <summary>
    /// Gets the build date from assembly metadata.
    /// </summary>
    private static DateTime GetBuildDate(Assembly assembly)
    {
        // Try to get build date from assembly attributes
        var buildAttribute = assembly.GetCustomAttribute<AssemblyMetadataAttribute>()
            ?.Value;

        if (DateTime.TryParse(buildAttribute, out var buildDate))
        {
            return buildDate;
        }

        // Fallback to app directory for single-file deployments
        var appDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appDirectory) && Directory.Exists(appDirectory))
        {
            return Directory.GetCreationTime(appDirectory);
        }

        return DateTime.Now;
    }
}
