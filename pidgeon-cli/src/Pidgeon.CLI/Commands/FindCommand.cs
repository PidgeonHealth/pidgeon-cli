// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Interfaces.Search;
using Pidgeon.Core.Domain.Reference.Entities;
using Pidgeon.Core.Domain.Search;
using Pidgeon.CLI.Services;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Bidirectional search engine for healthcare message discovery and field location.
/// Provides field discovery, value search, pattern matching, and cross-standard mapping capabilities.
/// </summary>
public class FindCommand : CommandBuilderBase
{
    private readonly IStandardReferenceService _referenceService;
    private readonly IFieldDiscoveryService _fieldDiscoveryService;
    private readonly IMessageSearchService _messageSearchService;
    private readonly FirstTimeUserService _firstTimeUserService;

    public FindCommand(
        ILogger<FindCommand> logger,
        IConsoleOutput output,
        IStandardReferenceService referenceService,
        IFieldDiscoveryService fieldDiscoveryService,
        IMessageSearchService messageSearchService,
        FirstTimeUserService firstTimeUserService)
        : base(logger, output, firstTimeUserService)
    {
        _referenceService = referenceService;
        _fieldDiscoveryService = fieldDiscoveryService;
        _messageSearchService = messageSearchService;
        _firstTimeUserService = firstTimeUserService;
    }

    public override Command CreateCommand()
    {
        var command = new Command("find", "🔍 Search engine for healthcare message discovery and field location");

        // Main search query argument (supports various formats)
        var queryArg = new Argument<string>("query")
        {
            Description = "Search query: field path, value, or semantic term (e.g., 'patient.mrn', 'M123456', 'medication')"
        };

        // Search mode options
        var fieldOption = CreateBooleanOption("--field", "-f", "Search for field paths/locations");
        var valueOption = CreateBooleanOption("--value", "-v", "Search for specific values in messages");
        var patternOption = CreateNullableOption("--pattern", "-p", "Pattern matching (wildcards: *, regex supported)");
        var mapOption = CreateBooleanOption("--map", "-m", "Show cross-standard field mappings");
        var semanticOption = CreateBooleanOption("--semantic", "-s", "Semantic search (find related clinical concepts)");

        // Scope and filtering options
        var standardOption = CreateNullableOption("--standard", "-t", "Limit to specific standard (hl7v23, fhir-r4, ncpdp)");
        var typeOption = CreateNullableOption("--type", "Message/resource type filter (e.g., ADT, Patient, NewRx)");
        var inOption = CreateNullableOption("--in", "-i", "Search within specific files/directories");

        // Output options
        var formatOption = CreateOptionalOption("--format", "Output format: table|json|paths", "table");
        var limitOption = CreateIntegerOption("--limit", "-l", "Maximum number of results", 20);
        var detailedOption = CreateBooleanOption("--detailed", "-d", "Show detailed field information");

        command.Add(queryArg);
        command.Add(fieldOption);
        command.Add(valueOption);
        command.Add(patternOption);
        command.Add(mapOption);
        command.Add(semanticOption);
        command.Add(standardOption);
        command.Add(typeOption);
        command.Add(inOption);
        command.Add(formatOption);
        command.Add(limitOption);
        command.Add(detailedOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            try
            {
                var query = parseResult.GetValue(queryArg)!;
                var isField = parseResult.GetValue(fieldOption);
                var isValue = parseResult.GetValue(valueOption);
                var pattern = parseResult.GetValue(patternOption);
                var isMap = parseResult.GetValue(mapOption);
                var isSemantic = parseResult.GetValue(semanticOption);
                var standard = parseResult.GetValue(standardOption);
                var messageType = parseResult.GetValue(typeOption);
                var searchPath = parseResult.GetValue(inOption);
                var format = parseResult.GetValue(formatOption)!;
                var limit = parseResult.GetValue(limitOption);
                var detailed = parseResult.GetValue(detailedOption);

                // Determine search mode (auto-detect if not explicit)
                var searchMode = DetermineSearchMode(query, isField, isValue, pattern, isMap, isSemantic);

                Output.WriteStatus($"Pidgeon Find - {GetSearchModeDescription(searchMode)}");
                Output.WriteStatus("----------------------------------------------------");
                Output.WriteStatus($"Query: {query}");
                if (!string.IsNullOrEmpty(standard))
                    Output.WriteStatus($"Standard: {standard.ToUpperInvariant()}");
                if (!string.IsNullOrEmpty(messageType))
                    Output.WriteStatus($"Message Type: {messageType}");
                Output.WriteStatus("");

                return await ExecuteSearchAsync(searchMode, query, standard, messageType, searchPath, format, limit, detailed, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during find execution");
                Output.WriteError($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private SearchMode DetermineSearchMode(string query, bool isField, bool isValue, string? pattern, bool isMap, bool isSemantic)
    {
        // Explicit mode flags take precedence
        if (isMap) return SearchMode.CrossStandardMapping;
        if (isSemantic) return SearchMode.Semantic;
        if (isValue) return SearchMode.Value;
        if (isField) return SearchMode.Field;
        if (!string.IsNullOrEmpty(pattern)) return SearchMode.Pattern;

        // Auto-detect based on query characteristics
        if (query.Contains('.') || query.Contains('-') || query.Contains('['))
            return SearchMode.Field; // Looks like a field path

        if (query.Length > 2 && (query.All(char.IsDigit) ||
                                 query.Contains('-') && query.Split('-').All(part => part.All(char.IsDigit)) ||
                                 DateTime.TryParse(query, out _)))
            return SearchMode.Value; // Looks like an ID or date

        return SearchMode.Semantic; // Default to semantic search for natural language
    }

    private string GetSearchModeDescription(SearchMode mode) => mode switch
    {
        SearchMode.Field => "Field Path Discovery",
        SearchMode.Value => "Value Location Search",
        SearchMode.Pattern => "Pattern Matching",
        SearchMode.CrossStandardMapping => "Cross-Standard Field Mapping",
        SearchMode.Semantic => "Semantic Clinical Concept Search",
        _ => "Multi-Modal Search"
    };

    private async Task<int> ExecuteSearchAsync(
        SearchMode searchMode,
        string query,
        string? standard,
        string? messageType,
        string? searchPath,
        string format,
        int limit,
        bool detailed,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = searchMode switch
            {
                SearchMode.Field => await FindFieldLocationsAsync(query, standard, messageType, cancellationToken),
                SearchMode.Value => await FindValueLocationsAsync(query, searchPath, standard, cancellationToken),
                SearchMode.Pattern => await FindByPatternAsync(query, standard, cancellationToken),
                SearchMode.CrossStandardMapping => await FindCrossStandardMappingAsync(query, cancellationToken),
                SearchMode.Semantic => await FindSemanticMatchesAsync(query, standard, messageType, cancellationToken),
                _ => throw new ArgumentException($"Unsupported search mode: {searchMode}")
            };

            if (results.Count == 0)
            {
                Output.WriteStatus("No results found.");
                Output.WriteStatus("");
                await ShowSearchSuggestionsAsync(query, searchMode);
                return 0;
            }

            DisplayResults(results, format, limit, detailed);
            return 0;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Search execution failed for mode {SearchMode} with query {Query}", searchMode, query);
            Output.WriteError($"Search failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<List<FindResult>> FindFieldLocationsAsync(string fieldPath, string? standard, string? messageType, CancellationToken cancellationToken)
    {
        var results = new List<FindResult>();

        // Use real field discovery service
        var searchResult = await _fieldDiscoveryService.FindBySemanticPathAsync(fieldPath, cancellationToken);

        foreach (var location in searchResult.Locations)
        {
            // Filter by standard if specified
            if (!string.IsNullOrEmpty(standard) &&
                !location.Standard.Equals(standard, StringComparison.OrdinalIgnoreCase))
                continue;

            // Filter by message type if specified
            if (!string.IsNullOrEmpty(messageType) &&
                !location.MessageType.Contains(messageType, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new FindResult
            {
                Standard = location.Standard,
                Location = location.Path,
                Description = location.Description,
                MessageType = location.MessageType,
                FieldName = ExtractFieldName(location.Path, location.Description),
                DataType = location.DataType,
                Usage = location.Usage,
                Example = location.Examples.FirstOrDefault() ?? ""
            });
        }

        // If no results from semantic search, try direct lookup
        if (results.Count == 0 && !string.IsNullOrEmpty(fieldPath))
        {
            var directResult = await _referenceService.LookupAsync(fieldPath, cancellationToken);
            if (directResult.IsSuccess)
            {
                results.Add(new FindResult
                {
                    Standard = directResult.Value.Standard,
                    Location = directResult.Value.Path,
                    Description = directResult.Value.Description,
                    MessageType = ExtractMessageType(directResult.Value),
                    FieldName = directResult.Value.Name,
                    DataType = directResult.Value.DataType,
                    Usage = directResult.Value.Usage,
                    Example = directResult.Value.Examples.FirstOrDefault() ?? ""
                });
            }
        }

        return results;
    }

    private string ExtractFieldName(string path, string description)
    {
        // Try to extract a meaningful name from the description
        if (!string.IsNullOrEmpty(description))
        {
            // Take the first part before any dash or colon
            var parts = description.Split(new[] { '-', ':' }, StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
                return parts[0];
        }

        // Fall back to the last component of the path
        var pathParts = path.Split('.');
        return pathParts.Length > 0 ? pathParts[^1] : path;
    }

    private string ExtractMessageType(StandardElement element)
    {
        // For HL7, use segment name
        if (element.Standard.StartsWith("hl7", StringComparison.OrdinalIgnoreCase))
        {
            var parts = element.Path.Split('.');
            if (parts.Length > 0)
                return parts[0] + " segment";
        }
        // For FHIR, use resource type
        else if (element.Standard.StartsWith("fhir", StringComparison.OrdinalIgnoreCase))
        {
            var parts = element.Path.Split('.');
            if (parts.Length > 0)
                return parts[0] + " resource";
        }

        return "Message";
    }

    private async Task<List<FindResult>> FindValueLocationsAsync(string value, string? searchPath, string? standard, CancellationToken cancellationToken)
    {
        var results = new List<FindResult>();

        // Use real message search service
        List<ValueLocation> locations;

        if (!string.IsNullOrEmpty(searchPath))
        {
            if (Directory.Exists(searchPath))
            {
                locations = await _messageSearchService.FindValueInDirectoryAsync(value, searchPath, cancellationToken);
            }
            else
            {
                locations = await _messageSearchService.FindValueInFilesAsync(value, searchPath, cancellationToken);
            }
        }
        else
        {
            // Search in current directory by default
            locations = await _messageSearchService.FindValueInDirectoryAsync(value, Environment.CurrentDirectory, cancellationToken);
        }

        foreach (var location in locations)
        {
            // Filter by standard if specified
            if (!string.IsNullOrEmpty(standard) &&
                !location.Standard.Equals(standard, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new FindResult
            {
                Standard = location.Standard,
                Location = $"{location.FileName}:{location.FieldPath}",
                Description = $"Found value '{value}' in {location.FieldDescription}",
                MessageType = location.MessageType,
                FieldName = location.FieldDescription,
                DataType = location.DataType,
                Usage = "Found",
                Example = value
            });
        }

        return results;
    }

    private async Task<List<FindResult>> FindByPatternAsync(string pattern, string? standard, CancellationToken cancellationToken)
    {
        var results = new List<FindResult>();

        // Use real pattern matching
        var searchResult = await _fieldDiscoveryService.FindByPatternAsync(pattern, PatternType.Wildcard, cancellationToken);

        foreach (var location in searchResult.Locations)
        {
            // Filter by standard if specified
            if (!string.IsNullOrEmpty(standard) &&
                !location.Standard.Equals(standard, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new FindResult
            {
                Standard = location.Standard,
                Location = location.Path,
                Description = location.Description,
                MessageType = location.MessageType,
                FieldName = ExtractFieldName(location.Path, location.Description),
                DataType = location.DataType,
                Usage = location.Usage,
                Example = location.Examples.FirstOrDefault() ?? ""
            });
        }

        return results;
    }

    private async Task<List<FindResult>> FindCrossStandardMappingAsync(string fieldPath, CancellationToken cancellationToken)
    {
        var results = new List<FindResult>();

        // Use real cross-standard mapping service
        var mapping = await _fieldDiscoveryService.MapAcrossStandardsAsync(fieldPath, cancellationToken);

        // Add source field
        if (mapping.SourceField != null && !string.IsNullOrEmpty(mapping.SourceField.Path))
        {
            results.Add(new FindResult
            {
                Standard = mapping.SourceField.Standard,
                Location = mapping.SourceField.Path,
                Description = mapping.SourceField.Description,
                MessageType = mapping.SourceField.MessageType,
                FieldName = ExtractFieldName(mapping.SourceField.Path, mapping.SourceField.Description),
                DataType = mapping.SourceField.DataType,
                Usage = mapping.SourceField.Usage,
                MappingType = "Source",
                Example = mapping.SourceField.Examples.FirstOrDefault() ?? ""
            });
        }

        // Add mapped fields
        foreach (var mappedField in mapping.TargetFields)
        {
            results.Add(new FindResult
            {
                Standard = mappedField.Standard,
                Location = mappedField.Path,
                Description = mappedField.Description,
                MessageType = mappedField.MessageType,
                FieldName = ExtractFieldName(mappedField.Path, mappedField.Description),
                DataType = mappedField.DataType,
                Usage = mappedField.Usage,
                MappingType = $"{mappedField.Type} ({mappedField.Confidence:P0})",
                Example = mappedField.Examples.FirstOrDefault() ?? ""
            });
        }

        return results;
    }

    private async Task<List<FindResult>> FindSemanticMatchesAsync(string query, string? standard, string? messageType, CancellationToken cancellationToken)
    {
        var results = new List<FindResult>();

        // Use real semantic search
        var searchResult = await _fieldDiscoveryService.FindBySemanticAsync(query, messageType, cancellationToken);

        foreach (var location in searchResult.Locations)
        {
            // Filter by standard if specified
            if (!string.IsNullOrEmpty(standard) &&
                !location.Standard.Equals(standard, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new FindResult
            {
                Standard = location.Standard,
                Location = location.Path,
                Description = location.Description,
                MessageType = location.MessageType,
                FieldName = ExtractFieldName(location.Path, location.Description),
                DataType = location.DataType,
                Usage = location.Usage,
                SemanticMatch = $"Matches '{query}' concept",
                Example = location.Examples.FirstOrDefault() ?? ""
            });
        }

        return results;
    }

    private async Task ShowSearchSuggestionsAsync(string query, SearchMode searchMode)
    {
        Output.WriteStatus("Suggestions:");

        switch (searchMode)
        {
            case SearchMode.Field:
                Output.WriteStatus("  - Try broader field names: 'patient', 'encounter', 'medication'");
                Output.WriteStatus("  - Use standard prefixes: 'PID.', 'Patient.', 'NewRx.'");
                Output.WriteStatus("  - Check spelling and use dots/hyphens for paths");
                break;

            case SearchMode.Value:
                Output.WriteStatus("  - Ensure the value exists in your sample files");
                Output.WriteStatus("  - Try searching in specific directories with --in");
                Output.WriteStatus("  - Use quotes for values with spaces");
                break;

            case SearchMode.Semantic:
                Output.WriteStatus("  - Try related terms: 'patient name', 'birth date', 'medical record'");
                Output.WriteStatus("  - Use clinical terminology: 'diagnosis', 'procedure', 'allergy'");
                Output.WriteStatus("  - Be more specific: 'prescription medication' vs 'medication'");
                break;

            default:
                Output.WriteStatus("  - Try different search modes: --field, --value, --map");
                Output.WriteStatus("  - Use --pattern for wildcard searches");
                Output.WriteStatus("  - Check available standards with 'pidgeon lookup --help'");
                break;
        }

        Output.WriteStatus("");
        Output.WriteStatus("Related commands:");
        Output.WriteStatus("  pidgeon lookup <field>     - Detailed field information");
        Output.WriteStatus("  pidgeon path list          - Browse available field paths");
        Output.WriteStatus("  pidgeon config analyze     - Infer patterns from your data");

        await Task.CompletedTask;
    }

    private void DisplayResults(List<FindResult> results, string format, int limit, bool detailed)
    {
        var limitedResults = results.Take(limit).ToList();

        Output.WriteStatus($"Found {results.Count} result(s) (showing {limitedResults.Count}):");
        Output.WriteStatus("");

        switch (format.ToLower())
        {
            case "json":
                DisplayJsonResults(limitedResults);
                break;
            case "paths":
                DisplayPathResults(limitedResults);
                break;
            default:
                DisplayTableResults(limitedResults, detailed);
                break;
        }

        if (results.Count > limit)
        {
            Output.WriteStatus($"... and {results.Count - limit} more results");
            Output.WriteStatus($"Use --limit {results.Count} to see all results");
        }
    }

    private void DisplayJsonResults(List<FindResult> results)
    {
        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        };
        var json = System.Text.Json.JsonSerializer.Serialize(results, jsonOptions);
        Output.WriteData(json);
    }

    private void DisplayPathResults(List<FindResult> results)
    {
        foreach (var result in results)
        {
            Output.WriteData(result.Location);
        }
    }

    private void DisplayTableResults(List<FindResult> results, bool detailed)
    {
        // Group by standard for better organization
        var groupedResults = results.GroupBy(r => r.Standard);

        foreach (var group in groupedResults)
        {
            Output.WriteStatus($"  {group.Key}");
            Output.WriteStatus(new string('-', 50));

            foreach (var result in group)
            {
                Output.WriteStatus($"  {result.Location}");
                Output.WriteStatus($"   {result.Description}");

                if (detailed)
                {
                    Output.WriteStatus($"   Message Type: {result.MessageType}");
                    Output.WriteStatus($"   Data Type: {result.DataType}");
                    Output.WriteStatus($"   Usage: {result.Usage}");

                    if (!string.IsNullOrEmpty(result.Example))
                        Output.WriteStatus($"   Example: {result.Example}");

                    if (!string.IsNullOrEmpty(result.MappingType))
                        Output.WriteStatus($"   Mapping: {result.MappingType}");

                    if (!string.IsNullOrEmpty(result.SemanticMatch))
                        Output.WriteStatus($"   Match: {result.SemanticMatch}");
                }

                Output.WriteStatus("");
            }
        }
    }

    private enum SearchMode
    {
        Field,
        Value,
        Pattern,
        CrossStandardMapping,
        Semantic
    }

    private class FindResult
    {
        public string Standard { get; set; } = "";
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public string MessageType { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string DataType { get; set; } = "";
        public string Usage { get; set; } = "";
        public string Example { get; set; } = "";
        public string MappingType { get; set; } = "";
        public string SemanticMatch { get; set; } = "";
    }
}