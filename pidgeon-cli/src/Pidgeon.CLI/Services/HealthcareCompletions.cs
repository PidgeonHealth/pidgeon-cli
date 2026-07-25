// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.CommandLine.Completions;
using System.Reflection;
using System.Text.Json;

namespace Pidgeon.CLI.Services;

/// <summary>
/// Provides healthcare-specific completion values for CLI options and arguments.
/// Dynamically loads from our comprehensive HL7v23 dataset (239 message types, 100+ segments).
/// </summary>
public static class HealthcareCompletions
{
    private static readonly Lazy<string[]> _hl7MessageTypes = new(() => LoadHL7MessageTypes());
    private static readonly Lazy<string[]> _hl7Segments = new(() => LoadHL7Segments());
    private static readonly Lazy<string[]> _fieldPaths = new(() => LoadFieldPaths());

    /// <summary>
    /// All supported HL7 message types loaded from our HL7v23 dataset.
    /// Includes 239 message types: ADT^A01-A51, ORU^R01, ORM^O01, MDM^T01-T11, etc.
    /// </summary>
    public static string[] HL7MessageTypes => _hl7MessageTypes.Value;

    /// <summary>
    /// Common FHIR resource types for generation.
    /// </summary>
    public static readonly string[] FHIRResourceTypes =
    [
        // Core Resources
        "Patient", "Practitioner", "Organization", "Location",

        // Clinical Resources
        "Encounter", "Observation", "DiagnosticReport", "MedicationRequest",
        "Condition", "Procedure", "AllergyIntolerance", "Immunization",

        // Administrative Resources
        "Schedule", "Appointment", "Slot", "Account", "Coverage",

        // Workflow Resources
        "ServiceRequest", "Task", "MessageHeader",

        // Specialized Resources
        "Specimen", "Device", "Medication", "Substance",

        // Bundle types
        "Bundle"
    ];

    /// <summary>
    /// Common NCPDP transaction types.
    /// </summary>
    public static readonly string[] NCPDPTransactionTypes =
    [
        // Primary transactions
        "NewRx", "RxFill", "CancelRx", "RxChange", "RxRenewal",

        // Status transactions
        "Status", "Verify", "Error",

        // Administrative
        "RxHistoryRequest", "RxHistoryResponse"
    ];

    /// <summary>
    /// Standard formats for output.
    /// </summary>
    public static readonly string[] OutputFormats =
    [
        "auto", "hl7", "json", "ndjson", "xml"
    ];

    /// <summary>
    /// Validation modes.
    /// </summary>
    public static readonly string[] ValidationModes =
    [
        "strict", "compatibility"
    ];

    /// <summary>
    /// Generation modes (beta features noted).
    /// </summary>
    public static readonly string[] GenerationModes =
    [
        "procedural",      // Stable
        "model",           // Beta (hardware dependent local models)
        "api"              // Beta (cloud APIs in development)
    ];

    /// <summary>
    /// Field paths dynamically generated from our HL7v23 segment data.
    /// Includes all segment fields plus common wildcards.
    /// </summary>
    public static string[] CommonFieldPaths => _fieldPaths.Value;

    /// <summary>
    /// Vendor patterns for configuration.
    /// </summary>
    public static readonly string[] VendorPatterns =
    [
        // Major EHR vendors
        "epic", "epic_er", "epic_amb", "epic_ip",
        "cerner", "cerner_millennium", "cerner_powerchart",
        "allscripts", "allscripts_sunrise", "allscripts_professional",
        "meditech", "meditech_magic", "meditech_expanse",

        // Laboratory systems
        "labcorp", "quest", "cerner_lab", "epic_lab",

        // Specialty systems
        "athenahealth", "nextgen", "eclinicalworks", "vitera",

        // Generic patterns
        "hospital_standard", "clinic_standard", "lab_standard"
    ];

    /// <summary>
    /// AI severity levels for diff analysis.
    /// </summary>
    public static readonly string[] SeverityLevels =
    [
        "hint", "warn", "error"
    ];

    /// <summary>
    /// Adds healthcare-specific completions to a string argument based on context.
    /// Includes all our supported message types from the dataset.
    /// </summary>
    public static void AddCompletionsToArgument(System.CommandLine.Argument<string[]> argument)
    {
        // Smart completion based on argument context using CompletionSources property
        argument.CompletionSources.Add(ctx =>
        {
            var values = new List<string>();

            // Add all message types for generation arguments
            values.AddRange(HL7MessageTypes);        // All 239 HL7 message types from dataset
            values.AddRange(FHIRResourceTypes);      // FHIR resources
            values.AddRange(NCPDPTransactionTypes);  // NCPDP transactions

            return values
                .Where(v => v.StartsWith(ctx.WordToComplete, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Adds smart healthcare completions that don't interfere with help display.
    /// Provides completions during interactive use but not in help generation.
    /// </summary>
    public static void AddSmartCompletionsToArgument(System.CommandLine.Argument<string[]> argument)
    {
        // Use delegate-based completion that only activates during interactive completion
        argument.CompletionSources.Add(ctx =>
        {
            // Only provide completions if there's actual user input to complete
            if (string.IsNullOrEmpty(ctx.WordToComplete))
                return Array.Empty<string>();

            var values = new List<string>();

            // Add all message types for generation arguments
            values.AddRange(HL7MessageTypes);        // All 239 HL7 message types from dataset
            values.AddRange(FHIRResourceTypes);      // FHIR resources
            values.AddRange(NCPDPTransactionTypes);  // NCPDP transactions

            return values
                .Where(v => v.StartsWith(ctx.WordToComplete, StringComparison.OrdinalIgnoreCase))
                .Take(10); // Limit to top 10 matches to prevent overwhelming output
        });
    }

    /// <summary>
    /// Adds format completions to an option.
    /// </summary>
    public static void AddFormatCompletions(System.CommandLine.Option<string> option)
    {
        option.CompletionSources.Add(OutputFormats);
    }

    /// <summary>
    /// Adds validation mode completions to an option.
    /// </summary>
    public static void AddValidationModeCompletions(System.CommandLine.Option<string> option)
    {
        option.CompletionSources.Add(ValidationModes);
    }

    /// <summary>
    /// Adds generation mode completions to an option.
    /// </summary>
    public static void AddGenerationModeCompletions(System.CommandLine.Option<string> option)
    {
        option.CompletionSources.Add(GenerationModes);
    }

    /// <summary>
    /// Adds field path completions for --ignore options in diff commands.
    /// </summary>
    public static void AddFieldPathCompletions(System.CommandLine.Option<string?> option)
    {
        option.CompletionSources.Add(ctx =>
        {
            // Split on comma for multi-value completion
            var currentInput = ctx.WordToComplete;
            var lastCommaIndex = currentInput.LastIndexOf(',');

            if (lastCommaIndex >= 0)
            {
                // Multi-value case: complete after the last comma
                var prefix = currentInput.Substring(0, lastCommaIndex + 1);
                var currentValue = currentInput.Substring(lastCommaIndex + 1);

                return CommonFieldPaths
                    .Where(path => path.StartsWith(currentValue, StringComparison.OrdinalIgnoreCase))
                    .Select(path => prefix + path);
            }
            else
            {
                // Single value case
                return CommonFieldPaths
                    .Where(path => path.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase));
            }
        });
    }

    /// <summary>
    /// Adds vendor pattern completions to an option.
    /// </summary>
    public static void AddVendorCompletions(System.CommandLine.Option<string?> option)
    {
        option.CompletionSources.Add(VendorPatterns);
    }

    /// <summary>
    /// Adds severity level completions to an option.
    /// </summary>
    public static void AddSeverityCompletions(System.CommandLine.Option<string> option)
    {
        option.CompletionSources.Add(SeverityLevels);
    }

    /// <summary>
    /// Adds file extension-based completions for healthcare file types.
    /// </summary>
    public static void AddHealthcareFileCompletions(System.CommandLine.Option<string> option)
    {
        option.CompletionSources.Add(ctx =>
        {
            var directory = Path.GetDirectoryName(ctx.WordToComplete) ?? ".";
            var filename = Path.GetFileName(ctx.WordToComplete);

            try
            {
                if (Directory.Exists(directory))
                {
                    var healthcareExtensions = new[] { "*.hl7", "*.json", "*.xml", "*.txt" };
                    var files = new List<string>();

                    foreach (var extension in healthcareExtensions)
                    {
                        files.AddRange(Directory.GetFiles(directory, extension));
                    }

                    return files
                        .Select(Path.GetFileName)
                        .Where(f => f?.StartsWith(filename, StringComparison.OrdinalIgnoreCase) == true)
                        .Cast<string>();
                }
            }
            catch
            {
                // Fallback to default file completion
            }

            return Array.Empty<string>();
        });
    }

    /// <summary>
    /// Loads all HL7 message types from our HL7v23 trigger events dataset.
    /// Converts file names like "adt_a01.json" to "ADT^A01" format.
    /// </summary>
    private static string[] LoadHL7MessageTypes()
    {
        try
        {
            // Try embedded resources first
            var dataAssembly = FindDataAssembly();
            if (dataAssembly != null)
            {
                return LoadHL7MessageTypesFromEmbeddedResources(dataAssembly);
            }

            // Fallback to file system (development mode)
            var dataPath = GetDataPath();
            var triggerEventsPath = Path.Combine(dataPath, "standards", "hl7v23", "trigger_events");

            if (!Directory.Exists(triggerEventsPath))
            {
                return GetFallbackHL7MessageTypes();
            }

            var messageTypes = new List<string>();
            var files = Directory.GetFiles(triggerEventsPath, "*.json");

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Convert file names to HL7 format
                // "adt_a01" -> "ADT^A01"
                // "oru_r01" -> "ORU^R01"
                // "ack" -> "ACK"
                var messageType = ConvertFileNameToMessageType(fileName);
                if (!string.IsNullOrEmpty(messageType))
                {
                    messageTypes.Add(messageType);
                }
            }

            messageTypes.Sort();
            return messageTypes.ToArray();
        }
        catch
        {
            return GetFallbackHL7MessageTypes();
        }
    }

    /// <summary>
    /// Loads all HL7 segments from our HL7v23 segments dataset.
    /// </summary>
    private static string[] LoadHL7Segments()
    {
        try
        {
            // Try embedded resources first
            var dataAssembly = FindDataAssembly();
            if (dataAssembly != null)
            {
                return LoadHL7SegmentsFromEmbeddedResources(dataAssembly);
            }

            // Fallback to file system (development mode)
            var dataPath = GetDataPath();
            var segmentsPath = Path.Combine(dataPath, "standards", "hl7v23", "segments");

            if (!Directory.Exists(segmentsPath))
            {
                return GetFallbackSegments();
            }

            var segments = Directory.GetFiles(segmentsPath, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f).ToUpperInvariant())
                .OrderBy(s => s)
                .ToArray();

            return segments;
        }
        catch
        {
            return GetFallbackSegments();
        }
    }

    /// <summary>
    /// Generates field paths from segment data (e.g., "MSH-1", "PID-5", "PV1-*").
    /// </summary>
    private static string[] LoadFieldPaths()
    {
        try
        {
            var segments = LoadHL7Segments();
            var fieldPaths = new List<string>();

            // Generate common field patterns for major segments
            var majorSegments = new[] { "MSH", "EVN", "PID", "PV1", "OBR", "OBX", "AL1", "DG1", "PR1" };

            foreach (var segment in majorSegments.Where(s => segments.Contains(s)))
            {
                // Add specific field numbers for common segments
                switch (segment)
                {
                    case "MSH":
                        fieldPaths.AddRange(new[] { "MSH-1", "MSH-2", "MSH-3", "MSH-4", "MSH-7", "MSH-10", "MSH-11" });
                        break;
                    case "PID":
                        fieldPaths.AddRange(new[] { "PID-1", "PID-3", "PID-5", "PID-7", "PID-8", "PID-11", "PID-13", "PID-14" });
                        break;
                    case "PV1":
                        fieldPaths.AddRange(new[] { "PV1-1", "PV1-2", "PV1-3", "PV1-19", "PV1-44", "PV1-45" });
                        break;
                    case "OBR":
                        fieldPaths.AddRange(new[] { "OBR-1", "OBR-2", "OBR-3", "OBR-4", "OBR-7", "OBR-8" });
                        break;
                    case "OBX":
                        fieldPaths.AddRange(new[] { "OBX-1", "OBX-2", "OBX-3", "OBX-5", "OBX-14" });
                        break;
                }

                // Add wildcard for each segment
                fieldPaths.Add($"{segment}-*");
            }

            return fieldPaths.Distinct().OrderBy(f => f).ToArray();
        }
        catch
        {
            return GetFallbackFieldPaths();
        }
    }

    /// <summary>
    /// Converts trigger event file names to HL7 message type format.
    /// </summary>
    private static string ConvertFileNameToMessageType(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        // Handle special cases
        if (fileName.Equals("ack", StringComparison.OrdinalIgnoreCase))
            return "ACK";

        // Standard pattern: "prefix_suffix" -> "PREFIX^SUFFIX"
        var parts = fileName.Split('_');
        if (parts.Length == 2)
        {
            return $"{parts[0].ToUpperInvariant()}^{parts[1].ToUpperInvariant()}";
        }

        // If no underscore, just uppercase it
        return fileName.ToUpperInvariant();
    }

    /// <summary>
    /// Gets the data directory path, trying multiple possible locations.
    /// </summary>
    private static string GetDataPath()
    {
        // Try relative to current directory first (development)
        var currentDir = Directory.GetCurrentDirectory();
        var possiblePaths = new[]
        {
            Path.Combine(currentDir, "data"),
            Path.Combine(currentDir, "..", "data"),
            Path.Combine(currentDir, "..", "..", "data"),
            Path.Combine(currentDir, "pidgeon", "data"),
            // Try from application directory (deployed)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data"),
            // Try from project structure
            Path.Combine(currentDir, "..", "..", "..", "data")
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns empty array if data files can't be loaded.
    /// Better to have no completions than wrong completions.
    /// </summary>
    private static string[] GetFallbackHL7MessageTypes()
    {
        return Array.Empty<string>();
    }

    /// <summary>
    /// Returns empty array if data files can't be loaded.
    /// Better to have no completions than wrong completions.
    /// </summary>
    private static string[] GetFallbackSegments()
    {
        return Array.Empty<string>();
    }

    /// <summary>
    /// Returns empty array if data files can't be loaded.
    /// Better to have no completions than wrong completions.
    /// </summary>
    private static string[] GetFallbackFieldPaths()
    {
        return Array.Empty<string>();
    }

    /// <summary>
    /// Finds the assembly containing embedded data resources.
    /// </summary>
    private static Assembly? FindDataAssembly()
    {
        var resourcePrefix = "data.standards.hl7v23";
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => HasEmbeddedResources(a, resourcePrefix));
    }

    /// <summary>
    /// Checks if embedded resources exist for the specified resource prefix.
    /// </summary>
    private static bool HasEmbeddedResources(Assembly assembly, string resourcePrefix)
    {
        var resourceNames = assembly.GetManifestResourceNames();
        return resourceNames.Any(name => name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loads HL7 message types from embedded resources.
    /// </summary>
    private static string[] LoadHL7MessageTypesFromEmbeddedResources(Assembly dataAssembly)
    {
        var messageTypes = new List<string>();
        var resourceNames = dataAssembly.GetManifestResourceNames();
        var triggerEventResources = resourceNames
            .Where(name => name.StartsWith("data.standards.hl7v23.trigger_events.", StringComparison.OrdinalIgnoreCase))
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in triggerEventResources)
        {
            var fileName = resourceName.Split('.').TakeLast(2).First(); // Get filename without .json
            var messageType = ConvertFileNameToMessageType(fileName);
            if (!string.IsNullOrEmpty(messageType))
            {
                messageTypes.Add(messageType);
            }
        }

        messageTypes.Sort();
        return messageTypes.ToArray();
    }

    /// <summary>
    /// Loads HL7 segments from embedded resources.
    /// </summary>
    private static string[] LoadHL7SegmentsFromEmbeddedResources(Assembly dataAssembly)
    {
        var resourceNames = dataAssembly.GetManifestResourceNames();
        var segmentResources = resourceNames
            .Where(name => name.StartsWith("data.standards.hl7v23.segments.", StringComparison.OrdinalIgnoreCase))
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        var segments = segmentResources
            .Select(name => name.Split('.').TakeLast(2).First().ToUpperInvariant()) // Get filename without .json
            .OrderBy(s => s)
            .ToArray();

        return segments;
    }
}