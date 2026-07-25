// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Rendering helpers for <see cref="LookupCommand"/>. Text mode writes human-readable status;
/// when --output-format json (or --format json) is set, the resolved StandardElement(s) are
/// emitted as a structured object on stdout so an agent can parse the lookup result.
/// </summary>
public partial class LookupCommand
{
    private static readonly System.Text.Json.JsonSerializerOptions LookupJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private bool WantsJson(string? format)
        => Output.OutputFormat == "json" || string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private void WriteJson<T>(T value)
    {
        if (Output.OutputFormat == "json")
        {
            Output.WriteData(value!);
            return;
        }

        // The command-local --format option cannot mutate the root IConsoleOutput
        // instance, so serialize explicitly when it alone selected JSON.
        Output.WriteData(System.Text.Json.JsonSerializer.Serialize(value, LookupJsonOptions));
    }

    private void DisplayElement(
        global::Pidgeon.Core.Domain.Reference.Entities.StandardElement element,
        string? format,
        bool showExamples,
        bool showCrossRef)
    {
        if (WantsJson(format)) { WriteJson(element); return; }

        Output.WriteStatus($"{element.Path}");
        Output.WriteStatus($"   {element.Name}");
        Output.WriteStatus("");

        Output.WriteStatus($"Definition:");
        Output.WriteStatus($"   Standard: {element.Standard} ({element.Version})");
        Output.WriteStatus($"   Data Type: {element.DataType}");
        Output.WriteStatus($"   Usage: {GetUsageDescription(element.Usage)}");
        if (element.MaxLength.HasValue)
        {
            Output.WriteStatus($"   Max Length: {element.MaxLength}");
        }
        if (element.Position.HasValue)
        {
            Output.WriteStatus($"   Position: {element.Position}");
        }
        if (!string.IsNullOrEmpty(element.Repeatability))
        {
            Output.WriteStatus($"   Repeatability: {element.Repeatability}");
        }
        if (!string.IsNullOrEmpty(element.TableReference))
        {
            Output.WriteStatus($"   Table: {element.TableReference}");
            Output.WriteStatus($"   Valid Values: -> pidgeon lookup {element.TableReference}");
        }
        Output.WriteStatus("");

        Output.WriteStatus($"Description:");
        Output.WriteStatus($"   {element.Description}");
        Output.WriteStatus("");

        if (element.Examples.Any())
        {
            Output.WriteStatus($"Examples:");
            foreach (var example in element.Examples.Take(3))
            {
                Output.WriteStatus($"   - {example}");
            }
            Output.WriteStatus("");
        }

        if (element.ValidValues.Any())
        {
            Output.WriteStatus($"Valid Values:");
            foreach (var validValue in element.ValidValues.Take(5))
            {
                var deprecated = validValue.IsDeprecated ? " (deprecated)" : "";
                Output.WriteStatus($"   {validValue.Code.PadRight(5)} - {validValue.Description}{deprecated}");
            }
            Output.WriteStatus("");
        }

        if (showExamples && element.VendorVariations.Any())
        {
            Output.WriteStatus($"Vendor Variations:");
            foreach (var vendor in element.VendorVariations.Take(2))
            {
                Output.WriteStatus($"   {vendor.Vendor}:");
                Output.WriteStatus($"     {vendor.Note}");
                if (vendor.Examples.Any())
                {
                    Output.WriteStatus($"     Examples: {string.Join(", ", vendor.Examples.Take(2))}");
                }
            }
            Output.WriteStatus("");
        }

        if (showCrossRef && element.CrossReferences.Any())
        {
            Output.WriteStatus($"Cross-References:");
            foreach (var crossRef in element.CrossReferences.Take(3))
            {
                Output.WriteStatus($"   {crossRef.Standard}: {crossRef.Path} ({crossRef.MappingType})");
                if (!string.IsNullOrEmpty(crossRef.Notes))
                {
                    Output.WriteStatus($"     Note: {crossRef.Notes}");
                }
            }
            Output.WriteStatus("");
        }
    }

    private void DisplaySearchResults(IReadOnlyList<global::Pidgeon.Core.Domain.Reference.Entities.StandardElement> elements, string? format)
    {
        if (WantsJson(format)) { WriteJson(elements); return; }

        Output.WriteStatus($"Found {elements.Count} result(s):");
        Output.WriteStatus("");

        foreach (var element in elements.Take(10))
        {
            Output.WriteStatus($"  {element.Path.PadRight(15)} {element.Name}");
            Output.WriteStatus($"   {element.Standard} | {element.DataType} | {GetUsageDescription(element.Usage)}");
            Output.WriteStatus($"   {TruncateDescription(element.Description, 80)}");
            Output.WriteStatus("");
        }

        if (elements.Count > 10)
        {
            Output.WriteStatus($"... and {elements.Count - 10} more results");
            Output.WriteStatus("Use more specific search terms to narrow results");
            Output.WriteStatus("");
        }
    }

    private void DisplayElementList(IReadOnlyList<global::Pidgeon.Core.Domain.Reference.Entities.StandardElement> elements, string? format)
    {
        if (WantsJson(format)) { WriteJson(elements); return; }

        foreach (var element in elements)
        {
            Output.WriteStatus($"  {element.Path.PadRight(15)} {element.Name}");
            Output.WriteStatus($"   {GetUsageDescription(element.Usage)} | {element.DataType}");
            if (!string.IsNullOrEmpty(element.Description) && element.Description.Length > 0)
            {
                Output.WriteStatus($"   {TruncateDescription(element.Description, 80)}");
            }
            Output.WriteStatus("");
        }
    }

    private static string GetUsageDescription(string usage)
    {
        return usage switch
        {
            "R" => "Required",
            "O" => "Optional",
            "C" => "Conditional",
            "X" => "Not used",
            _ => usage
        };
    }

    private static string TruncateDescription(string description, int maxLength)
    {
        if (description.Length <= maxLength)
            return description;

        return description.Substring(0, maxLength - 3) + "...";
    }
}
